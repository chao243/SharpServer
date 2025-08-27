using System.Security.Cryptography;
using System.Text;
using SharpServer.Common.ServiceRegistry;

namespace SharpServer.Common.LoadBalancing;

public class ConsistentHashLoadBalancer : ILoadBalancer
{
    private readonly SortedDictionary<uint, ServiceInfo> _ring = new();
    private readonly int _virtualNodes = 150;

    public Task<ServiceInfo?> SelectServiceAsync(List<ServiceInfo> services)
    {
        if (!services.Any())
            return Task.FromResult<ServiceInfo?>(null);

        UpdateRing(services);

        if (!_ring.Any())
            return Task.FromResult<ServiceInfo?>(null);

        // For load balancing without specific key, use current timestamp
        var key = DateTime.UtcNow.Ticks.ToString();
        return Task.FromResult<ServiceInfo?>(SelectByKey(key));
    }

    public ServiceInfo? SelectByKey(string key)
    {
        if (!_ring.Any())
            return null;

        var hash = ComputeHash(key);
        
        // Find the first service with hash >= key hash
        var node = _ring.FirstOrDefault(kvp => kvp.Key >= hash);
        
        // If not found, wrap around to the first service
        return node.Key == 0 ? _ring.First().Value : node.Value;
    }

    public void RecordSuccess(ServiceInfo service)
    {
        // Consistent hash doesn't need to track success/failure for selection
        // But you could implement health tracking here if needed
    }

    public void RecordFailure(ServiceInfo service)
    {
        // Remove failed service from ring temporarily
        // You might want to implement more sophisticated health tracking
    }

    private void UpdateRing(List<ServiceInfo> services)
    {
        _ring.Clear();
        
        foreach (var service in services)
        {
            for (int i = 0; i < _virtualNodes; i++)
            {
                var virtualKey = $"{service.ServiceId}:{i}";
                var hash = ComputeHash(virtualKey);
                _ring[hash] = service;
            }
        }
    }

    private uint ComputeHash(string input)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToUInt32(hash, 0);
    }
}