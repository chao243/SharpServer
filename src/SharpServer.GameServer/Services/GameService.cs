using MagicOnion;
using MagicOnion.Server;
using SharpServer.Protocol;

namespace SharpServer.GameServer.Services;

public class GameService : ServiceBase<IGameService>, IGameService
{
    private static readonly Dictionary<int, PlayerInfoResponse> Players = new()
    {
        { 1, new PlayerInfoResponse { PlayerId = 1, PlayerName = "Alice", Level = 10, Experience = 1500 } },
        { 2, new PlayerInfoResponse { PlayerId = 2, PlayerName = "Bob", Level = 8, Experience = 1200 } }
    };

    private static readonly Dictionary<int, GameStateResponse> Games = new();
    private static int _nextGameId = 1;

    public async UnaryResult<PlayerInfoResponse> GetPlayerInfo(int playerId)
    {
        await Task.Delay(10); // Simulate some async work
        
        if (Players.TryGetValue(playerId, out var player))
        {
            return player;
        }
        
        throw new InvalidOperationException($"Player with ID {playerId} not found");
    }

    public async UnaryResult<GameStateResponse> GetGameState(int gameId)
    {
        await Task.Delay(10); // Simulate some async work
        
        if (Games.TryGetValue(gameId, out var game))
        {
            return game;
        }
        
        throw new InvalidOperationException($"Game with ID {gameId} not found");
    }

    public async UnaryResult<CreateGameResponse> CreateGame(CreateGameRequest request)
    {
        await Task.Delay(50); // Simulate some async work
        
        try
        {
            var gameId = _nextGameId++;
            var game = new GameStateResponse
            {
                GameId = gameId,
                GameName = request.GameName,
                PlayerIds = new List<int> { request.CreatorPlayerId },
                Status = GameStatus.Waiting
            };
            
            Games[gameId] = game;
            
            return new CreateGameResponse
            {
                GameId = gameId,
                Success = true,
                ErrorMessage = string.Empty
            };
        }
        catch (Exception ex)
        {
            return new CreateGameResponse
            {
                GameId = 0,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async UnaryResult<JoinGameResponse> JoinGame(JoinGameRequest request)
    {
        await Task.Delay(30); // Simulate some async work
        
        try
        {
            if (!Games.TryGetValue(request.GameId, out var game))
            {
                return new JoinGameResponse
                {
                    Success = false,
                    ErrorMessage = "Game not found",
                    GameState = null
                };
            }
            
            if (game.PlayerIds.Contains(request.PlayerId))
            {
                return new JoinGameResponse
                {
                    Success = false,
                    ErrorMessage = "Player already in game",
                    GameState = game
                };
            }
            
            game.PlayerIds.Add(request.PlayerId);
            
            return new JoinGameResponse
            {
                Success = true,
                ErrorMessage = string.Empty,
                GameState = game
            };
        }
        catch (Exception ex)
        {
            return new JoinGameResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
                GameState = null
            };
        }
    }
}