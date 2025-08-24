using Grpc.Net.Client;
using MagicOnion.Client;
using SharpServer.Protocol;

namespace SharpServer.Gateway.Services;

public class GameServiceClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly IGameService _client;

    public GameServiceClient(string address)
    {
        _channel = GrpcChannel.ForAddress(address);
        _client = MagicOnionClient.Create<IGameService>(_channel);
    }

    public async Task<PlayerInfoResponse> GetPlayerInfoAsync(int playerId)
    {
        return await _client.GetPlayerInfo(playerId);
    }

    public async Task<GameStateResponse> GetGameStateAsync(int gameId)
    {
        return await _client.GetGameState(gameId);
    }

    public async Task<CreateGameResponse> CreateGameAsync(CreateGameRequest request)
    {
        return await _client.CreateGame(request);
    }

    public async Task<JoinGameResponse> JoinGameAsync(JoinGameRequest request)
    {
        return await _client.JoinGame(request);
    }

    public void Dispose()
    {
        _channel?.Dispose();
    }
}