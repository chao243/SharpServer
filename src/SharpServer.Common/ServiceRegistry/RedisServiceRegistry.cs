using System.Text.Json;
using StackExchange.Redis;

namespace SharpServer.Common.ServiceRegistry;

public class RedisServiceRegistry : IServiceRegistry, IDisposable
{
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisServiceRegistry(IConnectionMultiplexer redis, string keyPrefix = "service_registry")
    {
        _redis = redis;
        _database = redis.GetDatabase();
        _keyPrefix = keyPrefix;
    }

    public async Task RegisterServiceAsync(ServiceInfo serviceInfo, TimeSpan ttl)
    {
        var key = $"{_keyPrefix}:services:{serviceInfo.ServiceName}:{serviceInfo.ServiceId}";
        var value = JsonSerializer.Serialize(serviceInfo);
        
        await _database.StringSetAsync(key, value, ttl);
        
        // Add to service list
        var serviceListKey = $"{_keyPrefix}:service_lists:{serviceInfo.ServiceName}";
        await _database.SetAddAsync(serviceListKey, serviceInfo.ServiceId);
    }

    public async Task UnregisterServiceAsync(string serviceId)
    {
        // Find and remove from all service lists
        var pattern = $"{_keyPrefix}:services:*:{serviceId}";
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern);
        
        foreach (var key in keys)
        {
            var serviceInfo = await GetServiceFromKeyAsync(key);
            if (serviceInfo != null)
            {
                var serviceListKey = $"{_keyPrefix}:service_lists:{serviceInfo.ServiceName}";
                await _database.SetRemoveAsync(serviceListKey, serviceId);
            }
            
            await _database.KeyDeleteAsync(key);
        }
    }

    public async Task<List<ServiceInfo>> DiscoverServicesAsync(string serviceName)
    {
        var serviceListKey = $"{_keyPrefix}:service_lists:{serviceName}";
        var serviceIds = await _database.SetMembersAsync(serviceListKey);
        
        var services = new List<ServiceInfo>();
        
        foreach (var serviceId in serviceIds)
        {
            var key = $"{_keyPrefix}:services:{serviceName}:{serviceId}";
            var serviceInfo = await GetServiceFromKeyAsync(key);
            
            if (serviceInfo != null && serviceInfo.Status == ServiceStatus.Up)
            {
                services.Add(serviceInfo);
            }
            else
            {
                // Clean up expired service
                await _database.SetRemoveAsync(serviceListKey, serviceId);
            }
        }
        
        return services;
    }

    public async Task<ServiceInfo?> GetServiceAsync(string serviceId)
    {
        var pattern = $"{_keyPrefix}:services:*:{serviceId}";
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern);
        
        var key = keys.FirstOrDefault();
        return key != default(RedisKey) ? await GetServiceFromKeyAsync(key) : null;
    }

    public async Task RefreshServiceAsync(string serviceId, TimeSpan ttl)
    {
        var serviceInfo = await GetServiceAsync(serviceId);
        if (serviceInfo != null)
        {
            serviceInfo.LastHeartbeat = DateTime.UtcNow;
            await RegisterServiceAsync(serviceInfo, ttl);
        }
    }

    private async Task<ServiceInfo?> GetServiceFromKeyAsync(RedisKey key)
    {
        var value = await _database.StringGetAsync(key);
        if (!value.HasValue)
            return null;
        
        try
        {
            return JsonSerializer.Deserialize<ServiceInfo>(value.ToString());
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}