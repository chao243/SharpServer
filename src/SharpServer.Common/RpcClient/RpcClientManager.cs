using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;
using Microsoft.Extensions.Logging;
using SharpServer.Common.LoadBalancing;
using SharpServer.Common.ServiceRegistry;

namespace SharpServer.Common.RpcClient;

public class RpcClientManager<T> : IRpcClientManager<T>, IDisposable where T : class, IService<T>
{
    private readonly IServiceRegistry _serviceRegistry;
    private readonly ILoadBalancer _loadBalancer;
    private readonly ILogger<RpcClientManager<T>> _logger;
    private readonly RpcClientOptions _options;
    private readonly ConcurrentDictionary<string, ClientPool> _clientPools = new();
    private readonly Timer _healthCheckTimer;

    public RpcClientManager(
        IServiceRegistry serviceRegistry,
        ILoadBalancer loadBalancer,
        ILogger<RpcClientManager<T>> logger,
        RpcClientOptions options)
    {
        _serviceRegistry = serviceRegistry;
        _loadBalancer = loadBalancer;
        _logger = logger;
        _options = options;
        
        // Start health check timer
        _healthCheckTimer = new Timer(HealthCheck, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task<T> GetClientAsync()
    {
        var services = await _serviceRegistry.DiscoverServicesAsync(_options.ServiceName);
        var selectedService = await _loadBalancer.SelectServiceAsync(services);
        
        if (selectedService == null)
        {
            throw new InvalidOperationException($"No available services found for {_options.ServiceName}");
        }

        var pool = _clientPools.GetOrAdd(selectedService.ServiceId, 
            _ => new ClientPool(selectedService, _options));
        
        return await pool.GetClientAsync();
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<T, Task<TResult>> operation, int maxRetries = 3)
    {
        Exception? lastException = null;
        
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var client = await GetClientAsync();
                var result = await operation(client);
                
                // Record success for load balancer
                var services = await _serviceRegistry.DiscoverServicesAsync(_options.ServiceName);
                var currentService = services.FirstOrDefault(s => s.ServiceId == GetServiceIdFromClient(client));
                if (currentService != null)
                {
                    _loadBalancer.RecordSuccess(currentService);
                }
                
                return result;
            }
            catch (RpcException ex) when (IsRetryable(ex) && attempt < maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(ex, "RPC call failed (attempt {Attempt}/{MaxRetries})", attempt + 1, maxRetries + 1);
                
                // Record failure for load balancer
                var services = await _serviceRegistry.DiscoverServicesAsync(_options.ServiceName);
                var failedService = services.FirstOrDefault();
                if (failedService != null)
                {
                    _loadBalancer.RecordFailure(failedService);
                }
                
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100)); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Non-retryable RPC error");
                throw;
            }
        }
        
        throw lastException ?? new InvalidOperationException("RPC operation failed after all retries");
    }

    public async Task ExecuteAsync(Func<T, Task> operation, int maxRetries = 3)
    {
        await ExecuteAsync(async client =>
        {
            await operation(client);
            return true; // Dummy return value
        }, maxRetries);
    }

    private bool IsRetryable(RpcException ex)
    {
        return ex.StatusCode switch
        {
            StatusCode.Unavailable => true,
            StatusCode.DeadlineExceeded => true,
            StatusCode.ResourceExhausted => true,
            StatusCode.Aborted => true,
            StatusCode.Internal => true,
            _ => false
        };
    }

    private string GetServiceIdFromClient(T client)
    {
        // This is a simplified approach. In practice, you'd need to track
        // which service each client is connected to.
        return "unknown";
    }

    private async void HealthCheck(object? state)
    {
        try
        {
            var services = await _serviceRegistry.DiscoverServicesAsync(_options.ServiceName);
            
            // Remove pools for services that are no longer available
            var activeServiceIds = services.Select(s => s.ServiceId).ToHashSet();
            var poolsToRemove = _clientPools.Keys.Where(id => !activeServiceIds.Contains(id)).ToList();
            
            foreach (var serviceId in poolsToRemove)
            {
                if (_clientPools.TryRemove(serviceId, out var pool))
                {
                    pool.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
        }
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        
        foreach (var pool in _clientPools.Values)
        {
            pool.Dispose();
        }
        
        _clientPools.Clear();
    }

    private class ClientPool : IDisposable
    {
        private readonly ServiceInfo _serviceInfo;
        private readonly RpcClientOptions _options;
        private readonly ConcurrentQueue<ClientWrapper> _clients = new();
        private readonly SemaphoreSlim _semaphore;
        private int _clientCount;

        public ClientPool(ServiceInfo serviceInfo, RpcClientOptions options)
        {
            _serviceInfo = serviceInfo;
            _options = options;
            _semaphore = new SemaphoreSlim(options.MaxConnectionsPerService, options.MaxConnectionsPerService);
        }

        public async Task<T> GetClientAsync()
        {
            await _semaphore.WaitAsync();
            
            try
            {
                if (_clients.TryDequeue(out var wrapper) && wrapper.IsHealthy)
                {
                    return wrapper.Client;
                }
                
                // Create new client
                var channel = GrpcChannel.ForAddress(_serviceInfo.GetFullAddress());
                var client = MagicOnionClient.Create<T>(channel);
                
                Interlocked.Increment(ref _clientCount);
                
                return client;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void ReturnClient(T client)
        {
            if (_clientCount <= _options.MaxConnectionsPerService)
            {
                _clients.Enqueue(new ClientWrapper(client));
            }
        }

        public void Dispose()
        {
            while (_clients.TryDequeue(out var wrapper))
            {
                wrapper.Dispose();
            }
            
            _semaphore?.Dispose();
        }

        private class ClientWrapper : IDisposable
        {
            public T Client { get; }
            public DateTime LastUsed { get; private set; }

            public bool IsHealthy => DateTime.UtcNow - LastUsed < TimeSpan.FromMinutes(5);

            public ClientWrapper(T client)
            {
                Client = client;
                LastUsed = DateTime.UtcNow;
            }

            public void Dispose()
            {
                if (Client is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}