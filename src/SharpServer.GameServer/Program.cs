using DotNetEtcd;
using MagicOnion.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using SharpServer.Common.ServiceRegistry;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// gRPC 服务配置
builder.Services.AddGrpc();
builder.Services.AddMagicOnion();
builder.Services.AddOpenApi();

// 服务发现配置
var registrySection = builder.Configuration.GetSection("ServiceRegistry");
var registryProvider = registrySection.GetValue("Provider", "Redis");
var registryKeyPrefix = registrySection.GetValue("KeyPrefix", "sharpserver");

if (string.Equals(registryProvider, "Etcd", StringComparison.OrdinalIgnoreCase))
{
    var endpoint = registrySection.GetSection("Etcd").GetValue("Endpoint", "http://localhost:2379");
    builder.Services.TryAddSingleton(_ => new EtcdClient(endpoint));
    builder.Services.AddSingleton<IServiceRegistry>(sp =>
        new EtcdServiceRegistry(sp.GetRequiredService<EtcdClient>(), registryKeyPrefix));
}
else
{
    var redisConnectionString = registrySection.GetSection("Redis").GetValue(
        "ConnectionString",
        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");

    builder.Services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
    builder.Services.AddSingleton<IServiceRegistry>(sp =>
        new RedisServiceRegistry(sp.GetRequiredService<IConnectionMultiplexer>(), registryKeyPrefix));
}

// 服务注册信息
var serverSection = builder.Configuration.GetSection("Server");
var advertiseAddress = serverSection.GetValue("Address", "localhost");
var advertisePort = serverSection.GetValue("Port", 7144);
var advertiseScheme = serverSection.GetValue("Scheme", Uri.UriSchemeHttp);

builder.Services.AddServiceRegistration(options =>
{
    options.ServiceName = "GameServer";
    options.Address = advertiseAddress;
    options.Port = advertisePort;
    options.Scheme = advertiseScheme;
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.RegistrationTtl = TimeSpan.FromMinutes(2);
    options.Metadata["version"] = "1.0";
    options.Metadata["environment"] = builder.Environment.EnvironmentName;
});

// Kestrel 监听配置（确保 gRPC 使用 HTTP/2）
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(advertisePort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
        if (string.Equals(advertiseScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            listenOptions.UseHttps();
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapMagicOnionService();

if (string.Equals(advertiseScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        )).ToArray();
    return forecast;
}).WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
