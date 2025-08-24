using MagicOnion;
using MessagePack;

namespace SharpServer.Protocol;

public interface IGameService : IService<IGameService>
{
    UnaryResult<PlayerInfoResponse> GetPlayerInfo(int playerId);
    UnaryResult<GameStateResponse> GetGameState(int gameId);
    UnaryResult<CreateGameResponse> CreateGame(CreateGameRequest request);
    UnaryResult<JoinGameResponse> JoinGame(JoinGameRequest request);
}

[MessagePackObject]
public class PlayerInfoResponse
{
    [Key(0)]
    public int PlayerId { get; set; }
    
    [Key(1)]
    public string PlayerName { get; set; } = string.Empty;
    
    [Key(2)]
    public int Level { get; set; }
    
    [Key(3)]
    public int Experience { get; set; }
}

[MessagePackObject]
public class GameStateResponse
{
    [Key(0)]
    public int GameId { get; set; }
    
    [Key(1)]
    public string GameName { get; set; } = string.Empty;
    
    [Key(2)]
    public List<int> PlayerIds { get; set; } = new();
    
    [Key(3)]
    public GameStatus Status { get; set; }
}

[MessagePackObject]
public class CreateGameRequest
{
    [Key(0)]
    public string GameName { get; set; } = string.Empty;
    
    [Key(1)]
    public int MaxPlayers { get; set; }
    
    [Key(2)]
    public int CreatorPlayerId { get; set; }
}

[MessagePackObject]
public class CreateGameResponse
{
    [Key(0)]
    public int GameId { get; set; }
    
    [Key(1)]
    public bool Success { get; set; }
    
    [Key(2)]
    public string ErrorMessage { get; set; } = string.Empty;
}

[MessagePackObject]
public class JoinGameRequest
{
    [Key(0)]
    public int GameId { get; set; }
    
    [Key(1)]
    public int PlayerId { get; set; }
}

[MessagePackObject]
public class JoinGameResponse
{
    [Key(0)]
    public bool Success { get; set; }
    
    [Key(1)]
    public string ErrorMessage { get; set; } = string.Empty;
    
    [Key(2)]
    public GameStateResponse? GameState { get; set; }
}

public enum GameStatus
{
    Waiting = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}