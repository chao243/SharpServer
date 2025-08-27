using MagicOnion.Server;
using StackExchange.Redis;
using SharpServer.Common.ServiceRegistry;

var builder = WebApplication.CreateBuilder(args);

// Add MagicOnion
builder.Services.AddGrpc();
builder.Services.AddMagicOnion();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure Redis connection
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<IServiceRegistry, RedisServiceRegistry>();

// Configure service registration
var serverUrl = builder.Configuration.GetSection("Server");
var address = serverUrl["Address"] ?? "localhost";
var port = int.Parse(serverUrl["Port"] ?? "7144");

builder.Services.AddServiceRegistration(options =>
{
    options.ServiceName = "GameServer";
    options.Address = address;
    options.Port = port;
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.RegistrationTtl = TimeSpan.FromMinutes(2);
    options.Metadata["version"] = "1.0";
    options.Metadata["environment"] = builder.Environment.EnvironmentName;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map MagicOnion services
app.MapMagicOnionService();

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
