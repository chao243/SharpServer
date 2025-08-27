using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SharpServer.Common.ServiceRegistry;

public class ServiceRegistrationOptions
{
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceId { get; set; } = Guid.NewGuid().ToString();
    public string Address { get; set; } = "localhost";
    public int Port { get; set; }
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RegistrationTtl { get; set; } = TimeSpan.FromMinutes(2);
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class ServiceRegistrationService : BackgroundService
{
    private readonly IServiceRegistry _serviceRegistry;
    private readonly ILogger<ServiceRegistrationService> _logger;
    private readonly ServiceRegistrationOptions _options;
    private ServiceInfo? _serviceInfo;

    public ServiceRegistrationService(
        IServiceRegistry serviceRegistry,
        ILogger<ServiceRegistrationService> logger,
        IOptions<ServiceRegistrationOptions> options)
    {
        _serviceRegistry = serviceRegistry;
        _logger = logger;
        _options = options.Value;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _serviceInfo = new ServiceInfo
        {
            ServiceId = _options.ServiceId,
            ServiceName = _options.ServiceName,
            Address = _options.Address,
            Port = _options.Port,
            Metadata = _options.Metadata,
            Status = ServiceStatus.Up
        };

        await RegisterServiceAsync();
        _logger.LogInformation("Service {ServiceName} registered with ID {ServiceId}", 
            _options.ServiceName, _options.ServiceId);

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_serviceInfo != null)
        {
            await _serviceRegistry.UnregisterServiceAsync(_serviceInfo.ServiceId);
            _logger.LogInformation("Service {ServiceName} unregistered", _options.ServiceName);
        }

        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HeartbeatInterval, stoppingToken);
                
                if (_serviceInfo != null)
                {
                    await _serviceRegistry.RefreshServiceAsync(_serviceInfo.ServiceId, _options.RegistrationTtl);
                    _logger.LogDebug("Service {ServiceName} heartbeat sent", _options.ServiceName);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during service heartbeat");
            }
        }
    }

    private async Task RegisterServiceAsync()
    {
        if (_serviceInfo != null)
        {
            await _serviceRegistry.RegisterServiceAsync(_serviceInfo, _options.RegistrationTtl);
        }
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceRegistration(
        this IServiceCollection services,
        Action<ServiceRegistrationOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddHostedService<ServiceRegistrationService>();
        return services;
    }
}