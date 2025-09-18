using System.Net.Http;
using DotNetEtcd;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using SharpServer.Common.LoadBalancing;
using SharpServer.Common.RpcClient;
using SharpServer.Common.ServiceRegistry;
using SharpServer.Gateway.Services;
using SharpServer.Protocol;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// 服务注册与发现配置
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

// 依赖注册
builder.Services.AddOpenApi();
builder.Services.AddSingleton<ILoadBalancer, ConsistentHashLoadBalancer>();

builder.Services.Configure<RpcClientOptions>(options =>
{
    options.ServiceName = "GameServer";
    options.MaxRetries = 3;
    options.MaxConnectionsPerService = 32;
    options.RetryBackoff = new RetryBackoffOptions(BaseMilliseconds: 100, Multiplier: 2.0, MaxExponent: 5, MaxMilliseconds: 5_000);
    options.HttpHandlerFactory = () => new SocketsHttpHandler
    {
        EnableMultipleHttp2Connections = true,
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
    };
});

builder.Services.AddSingleton<IRpcClientManager<IGameService>, RpcClientManager<IGameService>>();
builder.Services.AddSingleton<EnhancedGameServiceClient>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/players/{playerId:int}", async (int playerId, EnhancedGameServiceClient client) =>
{
    try
    {
        var player = await client.GetPlayerInfoAsync(playerId);
        return Results.Ok(player);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting player info: {ex.Message}");
    }
}).WithName("GetPlayerInfo");

app.MapGet("/api/games/{gameId:int}", async (int gameId, EnhancedGameServiceClient client) =>
{
    try
    {
        var game = await client.GetGameStateAsync(gameId);
        return Results.Ok(game);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting game state: {ex.Message}");
    }
}).WithName("GetGameState");

app.MapPost("/api/games", async (CreateGameRequest request, EnhancedGameServiceClient client) =>
{
    try
    {
        var response = await client.CreateGameAsync(request);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error creating game: {ex.Message}");
    }
}).WithName("CreateGame");

app.MapPost("/api/games/{gameId:int}/join", async (int gameId, JoinGameRequest request, EnhancedGameServiceClient client) =>
{
    try
    {
        request.GameId = gameId;
        var response = await client.JoinGameAsync(request);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error joining game: {ex.Message}");
    }
}).WithName("JoinGame");

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/hello", () => "hello").WithName("Hello");

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

public partial class Program { }
