using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace SharpServer.Common.ServiceRegistry;

public class ServiceRegistrationOptions
{
    public string ServiceName { get; set; } = string.Empty;
    public string ServiceId { get; set; } = Guid.NewGuid().ToString();
    public string Address { get; set; } = "localhost";
    public int Port { get; set; }
    public string Scheme { get; set; } = Uri.UriSchemeHttp;
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RegistrationTtl { get; set; } = TimeSpan.FromMinutes(2);
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class ServiceRegistrationService : BackgroundService
{
    private readonly IServiceRegistry _serviceRegistry;
    private readonly ILogger<ServiceRegistrationService> _logger;
    private readonly ServiceRegistrationOptions _options;
    private readonly IServer _server;
    private ServiceInfo? _serviceInfo;

    public ServiceRegistrationService(
        IServiceRegistry serviceRegistry,
        ILogger<ServiceRegistrationService> logger,
        IOptions<ServiceRegistrationOptions> options,
        IServer server)
    {
        _serviceRegistry = serviceRegistry;
        _logger = logger;
        _options = options.Value;
        _server = server;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        NormalizeEndpointFromServer();

        _serviceInfo = new ServiceInfo
        {
            ServiceId = _options.ServiceId,
            ServiceName = _options.ServiceName,
            Address = _options.Address,
            Port = _options.Port,
            Scheme = _options.Scheme,
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

    private void NormalizeEndpointFromServer()
    {
        if (!string.IsNullOrWhiteSpace(_options.Address) && _options.Port != 0 && !string.IsNullOrWhiteSpace(_options.Scheme))
        {
            return;
        }

        var addressesFeature = _server.Features.Get<IServerAddressesFeature>();
        var address = addressesFeature?.Addresses.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Address))
        {
            _options.Address = uri.Host;
        }

        if (_options.Port == 0)
        {
            _options.Port = uri.Port;
        }

        if (string.IsNullOrWhiteSpace(_options.Scheme))
        {
            _options.Scheme = uri.Scheme;
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
