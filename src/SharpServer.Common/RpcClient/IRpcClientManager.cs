using System;
using System.Net.Http;
using System.Threading;
using MagicOnion;

namespace SharpServer.Common.RpcClient;

public interface IRpcClientManager<T> where T : class, IService<T>
{
    Task<TResult> ExecuteAsync<TResult>(Func<T, Task<TResult>> operation, string? affinityKey = null, int? maxRetries = null, CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<T, Task> operation, string? affinityKey = null, int? maxRetries = null, CancellationToken cancellationToken = default);
}

public class RpcClientOptions
{
    public string ServiceName { get; set; } = string.Empty;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 3;
    public int MaxConnectionsPerService { get; set; } = 10;
    public bool EnableTls { get; set; } = true;
    public Func<HttpMessageHandler?>? HttpHandlerFactory { get; set; }
        = null;
    public RetryBackoffOptions RetryBackoff { get; set; } = RetryBackoffOptions.Default;
}

public record RetryBackoffOptions(double Multiplier = 2.0, int MaxExponent = 5, double BaseMilliseconds = 100, double MaxMilliseconds = 10_000)
{
    public static RetryBackoffOptions Default { get; } = new();
}
