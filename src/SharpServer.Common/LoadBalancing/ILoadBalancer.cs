using System.Collections.Generic;
using System.Threading;
using SharpServer.Common.ServiceRegistry;

namespace SharpServer.Common.LoadBalancing;

public interface ILoadBalancer
{
    Task<ServiceInfo?> SelectServiceAsync(string serviceName, IReadOnlyList<ServiceInfo> services, string? affinityKey = null, CancellationToken cancellationToken = default);
    void RecordSuccess(string serviceId);
    void RecordFailure(string serviceId, Exception? exception = null);
}

public enum LoadBalancingStrategy
{
    RoundRobin,
    Random,
    WeightedRoundRobin,
    LeastConnections,
    ConsistentHash
}
