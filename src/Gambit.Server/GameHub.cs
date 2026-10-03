using Gambit.Core.Games;
using Gambit.Online;
using Microsoft.AspNetCore.SignalR;

namespace Gambit.Server;

/// <summary>
/// SignalR endpoint. Clients identify with a secret token in the query string (?token=...), which acts
/// as a guest account; all game logic lives in <see cref="GameManager"/>.
/// </summary>
public sealed class GameHub(PlayerRegistry players, GameManager games) : Hub<IGameClient>, IGameServer
{
    private Player Me => players.ByConnection(Context.ConnectionId) ?? throw new HubException("Say Hello first.");

    public async Task<PlayerDto> Hello(string name, int protocolVersion)
    {
        if (protocolVersion != OnlineProtocol.Version) throw new HubException("Please update Gambit to play online.");
        string token = Context.GetHttpContext()?.Request.Query["token"].ToString() ?? "";
        if (token.Length < 16) throw new HubException("Missing player token.");

        Player p = players.Connect(token, Context.ConnectionId, name);
        PlayerDto me = players.ToDto(p, TimeCategory.Blitz);
        await Clients.Caller.Welcome(me, games.Stats());
        await games.OnConnectedAsync(p);
        return me;
    }

    public Task Seek(TimeControlDto timeControl) => games.SeekAsync(Me, timeControl);

    public Task CancelSeek()
    {
        games.CancelSeek(Me);
        return Task.CompletedTask;
    }

    public Task<ChallengeDto> CreateChallenge(TimeControlDto timeControl, string color) =>
        Task.FromResult(games.CreateChallenge(Me, timeControl, color));

    public Task<bool> AcceptChallenge(string code) => games.AcceptChallengeAsync(Me, code);

    public Task<bool> MakeMove(string gameId, int ply, string uci) => games.MakeMoveAsync(Me, gameId, ply, uci);

    public Task Resign(string gameId) => games.ResignAsync(Me, gameId);

    public Task OfferDraw(string gameId) => games.OfferDrawAsync(Me, gameId);

    public Task RespondToDraw(string gameId, bool accept) => games.RespondToDrawAsync(Me, gameId, accept);

    public Task<bool> Rejoin(string gameId) => games.RejoinAsync(Me, gameId);

    public Task OfferRematch(string gameId) => games.OfferRematchAsync(Me, gameId);

    public Task DeclineRematch(string gameId) => games.DeclineRematchAsync(Me, gameId);

    public Task<IReadOnlyList<LiveGameDto>> ListGames() => Task.FromResult(games.ListGames());

    public Task<GameStartDto?> Watch(string gameId) => games.WatchAsync(Me, Context.ConnectionId, gameId);

    public Task Unwatch(string gameId) => games.UnwatchAsync(Context.ConnectionId, gameId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        games.RemoveSpectator(Context.ConnectionId);
        if (players.Disconnect(Context.ConnectionId) is Player p) await games.OnDisconnectedAsync(p);
        await base.OnDisconnectedAsync(exception);
    }
}
