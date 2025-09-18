using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SharpServer.Common.ServiceRegistry;

namespace SharpServer.Common.LoadBalancing;

public class ConsistentHashLoadBalancer : ILoadBalancer
{
    private readonly ConcurrentDictionary<string, RingState> _rings = new();
    private readonly int _virtualNodeCount;

    public ConsistentHashLoadBalancer(int virtualNodeCount = 160)
    {
        _virtualNodeCount = virtualNodeCount;
    }

    public Task<ServiceInfo?> SelectServiceAsync(string serviceName, IReadOnlyList<ServiceInfo> services, string? affinityKey = null, CancellationToken cancellationToken = default)
    {
        if (services.Count == 0)
        {
            return Task.FromResult<ServiceInfo?>(null);
        }

        var state = _rings.GetOrAdd(serviceName, _ => new RingState());
        EnsureRingIsCurrent(state, services);

        ServiceInfo? selected;
        lock (state.SyncRoot)
        {
            if (state.Nodes.Count == 0)
            {
                return Task.FromResult<ServiceInfo?>(null);
            }

            var effectiveKey = affinityKey ?? GenerateFallbackKey();
            var hash = ComputeHash(effectiveKey);
            var index = FindNodeIndex(state.Nodes, hash);
            selected = state.Nodes[index].Value;
        }

        return Task.FromResult<ServiceInfo?>(selected);
    }

    public void RecordSuccess(string serviceId)
    {
        // Consistent hash目前不依赖成功率调整，可在此扩展健康度反馈
    }

    public void RecordFailure(string serviceId, Exception? exception = null)
    {
        // 预留健康度反馈扩展点，例如结合熔断器剔除节点
    }

    private void EnsureRingIsCurrent(RingState state, IReadOnlyList<ServiceInfo> services)
    {
        var signature = BuildSignature(services);

        lock (state.SyncRoot)
        {
            if (signature == state.Signature)
            {
                return;
            }

            var nodes = BuildNodes(services);
            state.Update(nodes, signature);
        }
    }

    private List<KeyValuePair<uint, ServiceInfo>> BuildNodes(IReadOnlyList<ServiceInfo> services)
    {
        var nodes = new List<KeyValuePair<uint, ServiceInfo>>();
        var occupied = new HashSet<uint>();

        foreach (var service in services)
        {
            if (service.Status != ServiceStatus.Up)
            {
                continue;
            }

            for (var index = 0; index < _virtualNodeCount; index++)
            {
                var virtualKey = $"{service.ServiceId}:{service.Address}:{service.Port}:{index}";
                var hash = ComputeHash(virtualKey);

                while (!occupied.Add(hash))
                {
                    hash++;
                }

                nodes.Add(new KeyValuePair<uint, ServiceInfo>(hash, service));
            }
        }

        nodes.Sort((left, right) => left.Key.CompareTo(right.Key));
        return nodes;
    }

    private static uint ComputeHash(string input)
    {
        using var sha1 = SHA1.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha1.ComputeHash(bytes);
        return BitConverter.ToUInt32(hash, 0);
    }

    private static string GenerateFallbackKey()
    {
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexString(buffer);
    }

    private static string BuildSignature(IReadOnlyList<ServiceInfo> services)
    {
        return string.Join('|', services
            .Where(s => s.Status == ServiceStatus.Up)
            .OrderBy(s => s.ServiceId)
            .Select(s => $"{s.ServiceId}:{s.Address}:{s.Port}:{s.Scheme}:{s.Version}")
        );
    }

    private static int FindNodeIndex(IReadOnlyList<KeyValuePair<uint, ServiceInfo>> nodes, uint hash)
    {
        var low = 0;
        var high = nodes.Count - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var midHash = nodes[mid].Key;

            if (midHash == hash)
            {
                return mid;
            }

            if (midHash < hash)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low < nodes.Count ? low : 0;
    }

    private sealed class RingState
    {
        public object SyncRoot { get; } = new();
        public IReadOnlyList<KeyValuePair<uint, ServiceInfo>> Nodes { get; private set; } = Array.Empty<KeyValuePair<uint, ServiceInfo>>();
        public string Signature { get; private set; } = string.Empty;

        public void Update(IReadOnlyList<KeyValuePair<uint, ServiceInfo>> nodes, string signature)
        {
            Nodes = nodes;
            Signature = signature;
        }
    }
}
