using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace SharpServer.Common.ServiceRegistry;

public class RedisServiceRegistry : IServiceRegistry
{
    private readonly IDatabase _database;
    private readonly string _keyPrefix;

    public RedisServiceRegistry(IConnectionMultiplexer redis, string keyPrefix = "service_registry")
    {
        _database = redis.GetDatabase();
        _keyPrefix = keyPrefix.TrimEnd(':');
    }

    public async Task RegisterServiceAsync(ServiceInfo serviceInfo, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(serviceInfo);

        serviceInfo.LastHeartbeat = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(serviceInfo);

        var serviceKey = BuildServiceKey(serviceInfo.ServiceName, serviceInfo.ServiceId);
        var listKey = BuildServiceListKey(serviceInfo.ServiceName);
        var indexKey = BuildServiceIndexKey(serviceInfo.ServiceId);

        var tasks = new Task[]
        {
            _database.StringSetAsync(serviceKey, payload, ttl),
            _database.SetAddAsync(listKey, serviceInfo.ServiceId),
            _database.StringSetAsync(indexKey, serviceInfo.ServiceName, ttl)
        };

        await Task.WhenAll(tasks);
    }

    public async Task UnregisterServiceAsync(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            return;
        }

        var indexKey = BuildServiceIndexKey(serviceId);
        var serviceNameValue = await _database.StringGetAsync(indexKey);
        if (!serviceNameValue.HasValue)
        {
            await _database.KeyDeleteAsync(indexKey);
            return;
        }

        var serviceName = serviceNameValue.ToString();
        var serviceKey = BuildServiceKey(serviceName, serviceId);
        var listKey = BuildServiceListKey(serviceName);

        await Task.WhenAll(
            _database.KeyDeleteAsync(serviceKey),
            _database.SetRemoveAsync(listKey, serviceId),
            _database.KeyDeleteAsync(indexKey));
    }

    public async Task<List<ServiceInfo>> DiscoverServicesAsync(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return new List<ServiceInfo>();
        }

        var listKey = BuildServiceListKey(serviceName);
        var serviceIds = await _database.SetMembersAsync(listKey);
        if (serviceIds.Length == 0)
        {
            return new List<ServiceInfo>();
        }

        var services = new List<ServiceInfo>(serviceIds.Length);
        foreach (var serviceId in serviceIds)
        {
            var serviceKey = BuildServiceKey(serviceName, serviceId!);
            var value = await _database.StringGetAsync(serviceKey);
            if (!value.HasValue)
            {
                await _database.SetRemoveAsync(listKey, serviceId);
                continue;
            }

            var serviceInfo = Deserialize(value!);
            if (serviceInfo.Status == ServiceStatus.Up)
            {
                services.Add(serviceInfo);
            }
            else
            {
                await _database.SetRemoveAsync(listKey, serviceId);
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

        var indexKey = BuildServiceIndexKey(serviceId);
        var serviceNameValue = await _database.StringGetAsync(indexKey);
        if (!serviceNameValue.HasValue)
        {
            return null;
        }

        var serviceName = serviceNameValue.ToString();
        var serviceKey = BuildServiceKey(serviceName, serviceId);
        var payload = await _database.StringGetAsync(serviceKey);
        return payload.HasValue ? Deserialize(payload!) : null;
    }

    public async Task RefreshServiceAsync(string serviceId, TimeSpan ttl)
    {
        var serviceInfo = await GetServiceAsync(serviceId);
        if (serviceInfo == null)
        {
            return;
        }

        serviceInfo.LastHeartbeat = DateTime.UtcNow;
        await RegisterServiceAsync(serviceInfo, ttl);
    }

    private string BuildServiceKey(string serviceName, string serviceId) => $"{_keyPrefix}:service:{serviceName}:{serviceId}";
    private string BuildServiceListKey(string serviceName) => $"{_keyPrefix}:list:{serviceName}";
    private string BuildServiceIndexKey(string serviceId) => $"{_keyPrefix}:index:{serviceId}";

    private static ServiceInfo Deserialize(string payload)
    {
        var info = JsonSerializer.Deserialize<ServiceInfo>(payload);
        if (info == null)
        {
            throw new InvalidOperationException("无法从 Redis 载入服务信息。");
        }

        return info;
    }
}
