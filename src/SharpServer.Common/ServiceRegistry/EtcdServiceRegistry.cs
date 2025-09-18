using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DotNetEtcd;
using Etcdserverpb;

namespace SharpServer.Common.ServiceRegistry;

public class EtcdServiceRegistry : IServiceRegistry, IDisposable
{
    private readonly EtcdClient _client;
    private readonly string _prefix;
    private readonly ConcurrentDictionary<string, long> _leases = new();

    public EtcdServiceRegistry(EtcdClient client, string keyPrefix = "service_registry")
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _prefix = keyPrefix.TrimEnd('/');
    }

    public async Task RegisterServiceAsync(ServiceInfo serviceInfo, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(serviceInfo);

        serviceInfo.LastHeartbeat = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(serviceInfo);
        var seconds = Math.Max(1, (long)Math.Ceiling(ttl.TotalSeconds));

        var lease = await _client.LeaseGrantAsync(seconds);
        var leaseId = lease.ID;
        _leases[serviceInfo.ServiceId] = leaseId;

        await _client.PutAsync(BuildServiceKey(serviceInfo.ServiceName, serviceInfo.ServiceId), payload, leaseId);
        await _client.PutAsync(BuildIndexKey(serviceInfo.ServiceId), serviceInfo.ServiceName, leaseId);
    }

    public async Task UnregisterServiceAsync(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            return;
        }

        if (_leases.TryRemove(serviceId, out var leaseId))
        {
            try
            {
                await _client.LeaseRevokeAsync(leaseId);
            }
            catch
            {
                // 如果连接已断开，撤销租约失败并不影响删除键
            }
        }

        var indexKey = BuildIndexKey(serviceId);
        var indexResponse = await _client.GetAsync(indexKey);
        var serviceName = indexResponse.Kvs.FirstOrDefault()?.Value.ToStringUtf8();
        if (string.IsNullOrEmpty(serviceName))
        {
            await _client.DeleteAsync(indexKey);
            return;
        }

        var serviceKey = BuildServiceKey(serviceName, serviceId);
        await Task.WhenAll(
            _client.DeleteAsync(serviceKey),
            _client.DeleteAsync(indexKey));
    }

    public async Task<List<ServiceInfo>> DiscoverServicesAsync(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return new List<ServiceInfo>();
        }

        var prefix = BuildServicePrefix(serviceName);
        var rangeEnd = CreateRangeEnd(prefix);
        var response = await _client.GetRangeAsync(prefix, rangeEnd);

        if (response.Kvs.Count == 0)
        {
            return new List<ServiceInfo>();
        }

        var services = new List<ServiceInfo>(response.Kvs.Count);
        foreach (var kv in response.Kvs)
        {
            var info = Deserialize(kv.Value.ToStringUtf8());
            if (info.Status == ServiceStatus.Up)
            {
                services.Add(info);
            }
        }

        return services;
    }

    public async Task<ServiceInfo?> GetServiceAsync(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            return null;
        }

        var indexKey = BuildIndexKey(serviceId);
        var indexResponse = await _client.GetAsync(indexKey);
        var serviceName = indexResponse.Kvs.FirstOrDefault()?.Value.ToStringUtf8();
        if (string.IsNullOrEmpty(serviceName))
        {
            return null;
        }

        var serviceKey = BuildServiceKey(serviceName, serviceId);
        var response = await _client.GetAsync(serviceKey);
        var payload = response.Kvs.FirstOrDefault()?.Value.ToStringUtf8();
        return string.IsNullOrEmpty(payload) ? null : Deserialize(payload);
    }

    public async Task RefreshServiceAsync(string serviceId, TimeSpan ttl)
    {
        var service = await GetServiceAsync(serviceId);
        if (service == null)
        {
            return;
        }

        await RegisterServiceAsync(service, ttl);
    }

    private string BuildServicePrefix(string serviceName) => $"{_prefix}/service/{serviceName}/";
    private string BuildServiceKey(string serviceName, string serviceId) => $"{_prefix}/service/{serviceName}/{serviceId}";
    private string BuildIndexKey(string serviceId) => $"{_prefix}/index/{serviceId}";

    private static string CreateRangeEnd(string prefix)
    {
        var bytes = Encoding.UTF8.GetBytes(prefix);
        for (var i = bytes.Length - 1; i >= 0; i--)
        {
            if (bytes[i] < 0xFF)
            {
                bytes[i]++;
                return Encoding.UTF8.GetString(bytes, 0, i + 1);
            }
        }

        return prefix + "\0";
    }

    private static ServiceInfo Deserialize(string payload)
    {
        var info = JsonSerializer.Deserialize<ServiceInfo>(payload);
        if (info == null)
        {
            throw new InvalidOperationException("无法从 Etcd 载入服务信息。");
        }

        return info;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
