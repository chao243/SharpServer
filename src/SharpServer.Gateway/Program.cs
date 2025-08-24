using SharpServer.Gateway.Services;
using SharpServer.Protocol;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register MagicOnion client
var gameServerAddress = builder.Configuration.GetConnectionString("GameServer") ?? "https://localhost:7144";
builder.Services.AddSingleton(new GameServiceClient(gameServerAddress));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Game API endpoints
app.MapGet("/api/players/{playerId:int}", async (int playerId, GameServiceClient client) =>
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
})
.WithName("GetPlayerInfo");

app.MapGet("/api/games/{gameId:int}", async (int gameId, GameServiceClient client) =>
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
})
.WithName("GetGameState");

app.MapPost("/api/games", async (CreateGameRequest request, GameServiceClient client) =>
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
})
.WithName("CreateGame");

app.MapPost("/api/games/{gameId:int}/join", async (int gameId, JoinGameRequest request, GameServiceClient client) =>
{
    try
    {
        request.GameId = gameId; // Ensure gameId from route matches request
        var response = await client.JoinGameAsync(request);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error joining game: {ex.Message}");
    }
})
.WithName("JoinGame");

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/hello", () => "hello")
    .WithName("Hello");

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

public partial class Program { }
