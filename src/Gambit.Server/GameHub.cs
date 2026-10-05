using System.Security.Claims;
using Gambit.Core.Games;
using Gambit.Online;
using Gambit.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Gambit.Server;

/// <summary>
/// SignalR endpoint for signed-in accounts (session key from <see cref="Gambit.Online.AccountApi"/>);
/// all game logic lives in <see cref="GameManager"/>.
/// </summary>
[Authorize]
public sealed class GameHub(PlayerRegistry players, GameManager games, AccountStore accounts, FriendStore friendships, FriendNotifier friends)
    : Hub<IGameClient>, IGameServer
{
    private Player Me => players.ByConnection(Context.ConnectionId) ?? throw new HubException("Say Hello first.");

    public async Task<PlayerDto> Hello(string name, int protocolVersion)
    {
        if (protocolVersion < OnlineProtocol.Version)
            throw new HubException("This server runs a newer version of Gambit. Update Gambit to play online (Settings → Updates).");
        if (protocolVersion > OnlineProtocol.Version)
            throw new HubException("This server runs an older version of Gambit than yours. Ask whoever runs it to update the server.");
        string accountId = Context.UserIdentifier ?? throw new HubException("Please sign in again.");
        string username = Context.User?.FindFirstValue(ClaimTypes.Name) ?? "Player";

        Player p = players.Connect(accountId, username, Context.ConnectionId, Context.Abort, out string? replaced);
        if (replaced != null && replaced != Context.ConnectionId)
            await Clients.Client(replaced).Notice("You signed in to Gambit somewhere else, so this window was disconnected from the game.");
        PlayerDto me = players.ToDto(p, TimeCategory.Blitz);
        await Clients.Caller.Welcome(me, games.Stats());
        await games.OnConnectedAsync(p);
        await friends.FriendsOfAsync(p.PublicId); // now online
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

    public async Task<ChallengeDto> ChallengeFriend(string username, TimeControlDto timeControl, string color)
    {
        Player me = Me;
        if (accounts.Find(username) is not Account other || !friendships.AreFriends(me.PublicId, other.Id))
            throw new HubException("You can challenge your friends this way. Add them as a friend first.");
        if (players.ById(other.Id) is not { ConnectionId: string conn }) throw new HubException($"{other.Username} isn't online right now.");
        if (games.IsPlaying(other.Id)) throw new HubException($"{other.Username} is playing a game right now.");
        if (games.IsPlaying(me.PublicId)) throw new HubException("Finish your game first.");
        ChallengeDto challenge = games.CreateChallenge(me, timeControl, color, other.Id);
        await Clients.Client(conn).ChallengeReceived(challenge);
        return challenge;
    }

    public Task DeclineChallenge(string code) => games.DeclineChallengeAsync(Me, code);

    public Task SendChat(string gameId, string text) => games.SendChatAsync(Me, gameId, text);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        games.RemoveSpectator(Context.ConnectionId);
        if (players.Disconnect(Context.ConnectionId) is Player p)
        {
            await games.OnDisconnectedAsync(p);
            if (!p.Connected) await friends.FriendsOfAsync(p.PublicId); // now offline
        }
        await base.OnDisconnectedAsync(exception);
    }
}
