using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpServer.Common.LoadBalancing;
using SharpServer.Common.ServiceRegistry;

namespace SharpServer.Common.RpcClient;

public sealed class RpcClientManager<T> : IRpcClientManager<T>, IDisposable where T : class, IService<T>
{
    private readonly IServiceRegistry _serviceRegistry;
    private readonly ILoadBalancer _loadBalancer;
    private readonly ILogger<RpcClientManager<T>> _logger;
    private readonly IOptionsMonitor<RpcClientOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, ClientPool> _clientPools = new();
    private readonly Timer _reconcileTimer;

    public RpcClientManager(
        IServiceRegistry serviceRegistry,
        ILoadBalancer loadBalancer,
        ILogger<RpcClientManager<T>> logger,
        IOptionsMonitor<RpcClientOptions> optionsMonitor)
    {
        _serviceRegistry = serviceRegistry;
        _loadBalancer = loadBalancer;
        _logger = logger;
        _optionsMonitor = optionsMonitor;

        var interval = TimeSpan.FromSeconds(30);
        _reconcileTimer = new Timer(OnReconcile, null, interval, interval);
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<T, Task<TResult>> operation, string? affinityKey = null, int? maxRetries = null, CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var retryLimit = maxRetries ?? options.MaxRetries;
        Exception? lastException = null;

        for (var attempt = 0; attempt <= retryLimit; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lease = await AcquireClientAsync(options.ServiceName, affinityKey, cancellationToken);
            try
            {
                var result = await operation(lease.Client);
                _loadBalancer.RecordSuccess(lease.ServiceId);
                lease.ReturnToPool();
                return result;
            }
            catch (RpcException ex) when (ShouldRetry(ex.StatusCode) && attempt < retryLimit)
            {
                lastException = ex;
                _logger.LogWarning(ex, "RPC 调用失败，已重试 {Attempt}/{Max}", attempt + 1, retryLimit + 1);
                _loadBalancer.RecordFailure(lease.ServiceId, ex);
                lease.Discard();
                await Task.Delay(ComputeBackoff(attempt, options.RetryBackoff), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                lease.Discard();
                throw;
            }
            catch (Exception ex)
            {
                lease.Discard();
                _logger.LogError(ex, "RPC 调用出现不可重试异常");
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException("RPC 调用在所有重试后仍失败");
    }

    public Task ExecuteAsync(Func<T, Task> operation, string? affinityKey = null, int? maxRetries = null, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async client =>
        {
            await operation(client);
            return true;
        }, affinityKey, maxRetries, cancellationToken);
    }

    public void Dispose()
    {
        _reconcileTimer.Dispose();

        foreach (var pool in _clientPools.Values)
        {
            pool.Dispose();
        }

        _clientPools.Clear();
    }

    private async Task<ClientLease> AcquireClientAsync(string serviceName, string? affinityKey, CancellationToken cancellationToken)
    {
        var services = await _serviceRegistry.DiscoverServicesAsync(serviceName);
        var selected = await _loadBalancer.SelectServiceAsync(serviceName, services, affinityKey, cancellationToken);

        if (selected == null)
        {
            throw new InvalidOperationException($"未找到可用的服务实例：{serviceName}");
        }

        var options = _optionsMonitor.CurrentValue;
        var pool = _clientPools.AddOrUpdate(
            selected.ServiceId,
            _ => new ClientPool(selected, options, _logger),
            (_, existing) => existing.WithLatestConfiguration(selected, options));

        var wrapper = await pool.RentAsync(cancellationToken);
        return new ClientLease(selected.ServiceId, pool, wrapper);
    }

    private void OnReconcile(object? state)
    {
        try
        {
            var options = _optionsMonitor.CurrentValue;
            var services = _serviceRegistry.DiscoverServicesAsync(options.ServiceName).GetAwaiter().GetResult();
            var activeServiceIds = services.Select(s => s.ServiceId).ToHashSet();

            foreach (var entry in _clientPools)
            {
                if (!activeServiceIds.Contains(entry.Key))
                {
                    if (_clientPools.TryRemove(entry.Key, out var pool))
                    {
                        pool.Dispose();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步 RPC 客户端池失败");
        }
    }

    private static bool ShouldRetry(StatusCode statusCode) => statusCode switch
    {
        StatusCode.Unavailable => true,
        StatusCode.DeadlineExceeded => true,
        StatusCode.ResourceExhausted => true,
        StatusCode.Aborted => true,
        StatusCode.Internal => true,
        _ => false
    };

    private static TimeSpan ComputeBackoff(int attempt, RetryBackoffOptions backoff)
    {
        var exponent = Math.Min(attempt, backoff.MaxExponent);
        var delayMs = backoff.BaseMilliseconds * Math.Pow(backoff.Multiplier, exponent);
        delayMs = Math.Min(delayMs, backoff.MaxMilliseconds);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private sealed class ClientLease
    {
        private readonly ClientPool _pool;
        private readonly ClientWrapper _wrapper;
        private bool _returned;

        public string ServiceId { get; }
        public T Client => _wrapper.Client;

        public ClientLease(string serviceId, ClientPool pool, ClientWrapper wrapper)
        {
            ServiceId = serviceId;
            _pool = pool;
            _wrapper = wrapper;
        }

        public void ReturnToPool()
        {
            if (_returned)
            {
                return;
            }

            _returned = true;
            _pool.Return(_wrapper);
        }

        public void Discard()
        {
            if (_returned)
            {
                return;
            }

            _returned = true;
            _pool.Discard(_wrapper);
        }
    }

    private sealed class ClientPool : IDisposable
    {
        private readonly object _syncRoot = new();
        private ServiceInfo _serviceInfo;
        private RpcClientOptions _options;
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<ClientWrapper> _queue = new();
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public ClientPool(ServiceInfo serviceInfo, RpcClientOptions options, ILogger logger)
        {
            _serviceInfo = serviceInfo;
            _options = options;
            _logger = logger;
            _semaphore = new SemaphoreSlim(options.MaxConnectionsPerService, options.MaxConnectionsPerService);
        }

        public ClientPool WithLatestConfiguration(ServiceInfo serviceInfo, RpcClientOptions options)
        {
            lock (_syncRoot)
            {
                _serviceInfo = serviceInfo;
                _options = options;
            }

            return this;
        }

        public async Task<ClientWrapper> RentAsync(CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            while (_queue.TryDequeue(out var wrapper))
            {
                if (wrapper.IsHealthy)
                {
                    return wrapper;
                }

                wrapper.Dispose();
            }

            return CreateWrapper();
        }

        public void Return(ClientWrapper wrapper)
        {
            wrapper.Touch();
            _queue.Enqueue(wrapper);
            _semaphore.Release();
        }

        public void Discard(ClientWrapper wrapper)
        {
            wrapper.Dispose();
            _semaphore.Release();
        }

        private ClientWrapper CreateWrapper()
        {
            try
            {
                var address = new Uri(_serviceInfo.GetUri());
                var useTls = string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && _options.EnableTls;

                var channelOptions = new GrpcChannelOptions
                {
                    HttpHandler = _options.HttpHandlerFactory?.Invoke(),
                    Credentials = useTls ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure
                };

                if (!useTls && string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    address = new UriBuilder(address) { Scheme = Uri.UriSchemeHttp }.Uri;
                }

                var channel = GrpcChannel.ForAddress(address, channelOptions);
                var client = MagicOnionClient.Create<T>(channel);
                return new ClientWrapper(channel, client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 gRPC 客户端失败，地址：{Address}", _serviceInfo.GetUri());
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            while (_queue.TryDequeue(out var wrapper))
            {
                wrapper.Dispose();
            }

            _semaphore.Dispose();
        }
    }

    private sealed class ClientWrapper : IDisposable
    {
        private readonly GrpcChannel _channel;
        public T Client { get; }
        private DateTime _lastUsed;

        public ClientWrapper(GrpcChannel channel, T client)
        {
            _channel = channel;
            Client = client;
            _lastUsed = DateTime.UtcNow;
        }

        public bool IsHealthy => DateTime.UtcNow - _lastUsed < TimeSpan.FromMinutes(5);

        public void Touch()
        {
            _lastUsed = DateTime.UtcNow;
        }

        public void Dispose()
        {
            if (Client is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _channel.Dispose();
        }
    }
}
