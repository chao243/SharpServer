using MagicOnion;

namespace SharpServer.Common.RpcClient;

public interface IRpcClientManager<T> where T : class, IService<T>
{
    Task<T> GetClientAsync();
    Task<TResult> ExecuteAsync<TResult>(Func<T, Task<TResult>> operation, int maxRetries = 3);
    Task ExecuteAsync(Func<T, Task> operation, int maxRetries = 3);
    void Dispose();
}

public class RpcClientOptions
{
    public string ServiceName { get; set; } = string.Empty;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 3;
    public int MaxConnectionsPerService { get; set; } = 10;
    public bool EnableCircuitBreaker { get; set; } = true;
    public TimeSpan CircuitBreakerTimeout { get; set; } = TimeSpan.FromMinutes(1);
}