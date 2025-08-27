using SharpServer.Common.LoadBalancing;
using SharpServer.Common.RpcClient;
using SharpServer.Common.ServiceRegistry;
using SharpServer.Protocol;

namespace SharpServer.Gateway.Services;

public class EnhancedGameServiceClient : IDisposable
{
    private readonly IRpcClientManager<IGameService> _clientManager;

    public EnhancedGameServiceClient(IRpcClientManager<IGameService> clientManager)
    {
        _clientManager = clientManager;
    }

    public async Task<PlayerInfoResponse> GetPlayerInfoAsync(int playerId)
    {
        return await _clientManager.ExecuteAsync(async client => 
            await client.GetPlayerInfo(playerId));
    }

    public async Task<GameStateResponse> GetGameStateAsync(int gameId)
    {
        return await _clientManager.ExecuteAsync(async client => 
            await client.GetGameState(gameId));
    }

    public async Task<CreateGameResponse> CreateGameAsync(CreateGameRequest request)
    {
        return await _clientManager.ExecuteAsync(async client => 
            await client.CreateGame(request));
    }

    public async Task<JoinGameResponse> JoinGameAsync(JoinGameRequest request)
    {
        return await _clientManager.ExecuteAsync(async client => 
            await client.JoinGame(request));
    }

    public void Dispose()
    {
        _clientManager?.Dispose();
    }
}