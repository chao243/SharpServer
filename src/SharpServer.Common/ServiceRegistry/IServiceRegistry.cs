namespace SharpServer.Common.ServiceRegistry;

public interface IServiceRegistry
{
    Task RegisterServiceAsync(ServiceInfo serviceInfo, TimeSpan ttl);
    Task UnregisterServiceAsync(string serviceId);
    Task<List<ServiceInfo>> DiscoverServicesAsync(string serviceName);
    Task<ServiceInfo?> GetServiceAsync(string serviceId);
    Task RefreshServiceAsync(string serviceId, TimeSpan ttl);
}

public class ServiceInfo
{
    public string ServiceId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Version { get; set; } = "1.0";
    public Dictionary<string, string> Metadata { get; set; } = new();
    public ServiceStatus Status { get; set; } = ServiceStatus.Up;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    public string GetFullAddress() => $"https://{Address}:{Port}";
}

public enum ServiceStatus
{
    Up,
    Down,
    Maintenance
}