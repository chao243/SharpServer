using SharpServer.Common.RpcClient;
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
        return await _clientManager.ExecuteAsync(
            async client => await client.GetPlayerInfo(playerId),
            affinityKey: playerId.ToString());
    }

    public async Task<GameStateResponse> GetGameStateAsync(int gameId)
    {
        return await _clientManager.ExecuteAsync(
            async client => await client.GetGameState(gameId),
            affinityKey: gameId.ToString());
    }

    public async Task<CreateGameResponse> CreateGameAsync(CreateGameRequest request)
    {
        var affinity = request.CreatorPlayerId != 0 ? request.CreatorPlayerId.ToString() : null;
        return await _clientManager.ExecuteAsync(
            async client => await client.CreateGame(request),
            affinityKey: affinity);
    }

    public async Task<JoinGameResponse> JoinGameAsync(JoinGameRequest request)
    {
        return await _clientManager.ExecuteAsync(
            async client => await client.JoinGame(request),
            affinityKey: request.GameId.ToString());
    }

    public void Dispose()
    {
        if (_clientManager is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
