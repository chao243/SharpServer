using System.Collections.Concurrent;
using SharpServer.Common.ServiceRegistry;

namespace SharpServer.Common.LoadBalancing;

public class RoundRobinLoadBalancer : ILoadBalancer
{
    private readonly ConcurrentDictionary<string, int> _counters = new();
    private readonly ConcurrentDictionary<string, ServiceHealth> _healthMap = new();

    public Task<ServiceInfo?> SelectServiceAsync(List<ServiceInfo> services)
    {
        if (!services.Any())
            return Task.FromResult<ServiceInfo?>(null);

        // Filter healthy services
        var healthyServices = services.Where(IsHealthy).ToList();
        if (!healthyServices.Any())
            return Task.FromResult<ServiceInfo?>(null);

        var serviceName = healthyServices.First().ServiceName;
        var counter = _counters.AddOrUpdate(serviceName, 0, (_, current) => (current + 1) % healthyServices.Count);
        
        return Task.FromResult<ServiceInfo?>(healthyServices[counter]);
    }

    public void RecordSuccess(ServiceInfo service)
    {
        _healthMap.AddOrUpdate(service.ServiceId, 
            new ServiceHealth { SuccessCount = 1, FailureCount = 0 },
            (_, health) => 
            {
                health.SuccessCount++;
                health.LastSuccess = DateTime.UtcNow;
                return health;
            });
    }

    public void RecordFailure(ServiceInfo service)
    {
        _healthMap.AddOrUpdate(service.ServiceId,
            new ServiceHealth { SuccessCount = 0, FailureCount = 1 },
            (_, health) =>
            {
                health.FailureCount++;
                health.LastFailure = DateTime.UtcNow;
                return health;
            });
    }

    private bool IsHealthy(ServiceInfo service)
    {
        if (!_healthMap.TryGetValue(service.ServiceId, out var health))
            return true; // New service is considered healthy

        // Circuit breaker logic: if failure rate > 50% in last minute, mark as unhealthy
        var recentFailures = health.FailureCount;
        var recentSuccesses = health.SuccessCount;
        var totalRequests = recentFailures + recentSuccesses;

        if (totalRequests < 5)
            return true; // Not enough data

        var failureRate = (double)recentFailures / totalRequests;
        return failureRate < 0.5;
    }

    private class ServiceHealth
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public DateTime LastSuccess { get; set; }
        public DateTime LastFailure { get; set; }
    }
}