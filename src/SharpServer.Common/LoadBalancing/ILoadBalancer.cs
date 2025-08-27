using SharpServer.Common.ServiceRegistry;

namespace SharpServer.Common.LoadBalancing;

public interface ILoadBalancer
{
    Task<ServiceInfo?> SelectServiceAsync(List<ServiceInfo> services);
    void RecordSuccess(ServiceInfo service);
    void RecordFailure(ServiceInfo service);
}

public enum LoadBalancingStrategy
{
    RoundRobin,
    Random,
    WeightedRoundRobin,
    LeastConnections,
    ConsistentHash
}