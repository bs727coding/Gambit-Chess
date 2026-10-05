using System.Security.Claims;
using Gambit.Online;
using Gambit.Server.Data;
using Microsoft.AspNetCore.SignalR;

namespace Gambit.Server;

/// <summary>Tells players their friend list changed (a request, an answer, a friend came online or started a game).</summary>
public sealed class FriendNotifier(IHubContext<GameHub, IGameClient> hub, PlayerRegistry players, FriendStore friends)
{
    /// <summary>Notifies the connected friends of <paramref name="playerId"/> (and both sides of pending requests).</summary>
    public Task FriendsOfAsync(string playerId) => PlayersAsync(friends.Related(playerId));

    public async Task PlayersAsync(IEnumerable<string> playerIds)
    {
        foreach (string id in playerIds.Distinct())
            if (players.ById(id)?.ConnectionId is string conn) await hub.Clients.Client(conn).FriendsChanged();
    }
}

/// <summary>The friends and game history endpoints (<see cref="AccountApi.Friends"/>, <see cref="AccountApi.Games"/>).</summary>
public static class SocialEndpoints
{
    public static void MapSocialApi(this WebApplication app)
    {
        app.MapGet(AccountApi.Friends, (ClaimsPrincipal user, FriendStore friends, PlayerRegistry players, GameManager games) =>
            List(Id(user), friends, players, games)).RequireAuthorization();

        // Asking someone (or saying yes to their request): they hear about it at once if they're online.
        app.MapPost(AccountApi.Friends, async (FriendRequest req, ClaimsPrincipal user, AccountStore accounts, FriendStore friends,
            PlayerRegistry players, GameManager games, FriendNotifier notify) =>
        {
            string me = Id(user);
            if (friends.Request(me, req.Username ?? "") is string error) return Results.BadRequest(new ApiError(error));
            if (accounts.Find(req.Username) is Account other) await notify.PlayersAsync([other.Id]);
            return Results.Ok(List(me, friends, players, games));
        }).RequireAuthorization().RequireRateLimiting("account");

        app.MapPost(AccountApi.RemoveFriend, async (FriendRequest req, ClaimsPrincipal user, AccountStore accounts, FriendStore friends,
            PlayerRegistry players, GameManager games, FriendNotifier notify) =>
        {
            string me = Id(user);
            if (friends.Remove(me, req.Username ?? "") && accounts.Find(req.Username) is Account other) await notify.PlayersAsync([other.Id]);
            return Results.Ok(List(me, friends, players, games));
        }).RequireAuthorization();

        app.MapGet(AccountApi.Games, (ClaimsPrincipal user, GameArchive archive, int? count, DateTimeOffset? before) =>
            archive.History(Id(user), before, Math.Clamp(count ?? 50, 1, 200))).RequireAuthorization();
    }

    private static FriendsDto List(string me, FriendStore friends, PlayerRegistry players, GameManager games)
    {
        var (list, incoming, outgoing) = friends.List(me);
        return new FriendsDto([.. list.Select(f => new FriendDto(f.Id, f.Name, Status(f.Id, players, games)))], incoming, outgoing);
    }

    /// <summary>"online", "playing" or "offline".</summary>
    public static string Status(string playerId, PlayerRegistry players, GameManager games) =>
        players.ById(playerId) is not { Connected: true } ? "offline" : games.IsPlaying(playerId) ? "playing" : "online";

    private static string Id(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
}
