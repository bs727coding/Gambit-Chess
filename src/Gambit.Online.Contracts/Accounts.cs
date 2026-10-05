namespace Gambit.Online;

/// <summary>
/// Account endpoints (plain HTTP + JSON, next to the game hub). Sign-in returns a session key; the app
/// sends it as "Authorization: Bearer ..." here and to the hub (SignalR puts it in ?access_token=).
/// </summary>
public static class AccountApi
{
    /// <summary>GET: <see cref="ServerInfoDto"/> (no sign-in needed).</summary>
    public const string Info = "/api/info";

    /// <summary>POST <see cref="RegisterRequest"/> → <see cref="SessionDto"/>, or 400 + <see cref="ApiError"/>.</summary>
    public const string Register = "/api/register";

    /// <summary>POST <see cref="SignInRequest"/> → <see cref="SessionDto"/>, or 401 + <see cref="ApiError"/>.</summary>
    public const string SignIn = "/api/signin";

    /// <summary>POST (signed in): ends this session.</summary>
    public const string SignOut = "/api/signout";

    /// <summary>GET (signed in): <see cref="AccountDto"/>.</summary>
    public const string Me = "/api/me";

    /// <summary>POST <see cref="ChangePasswordRequest"/> (signed in): other devices are signed out.</summary>
    public const string Password = "/api/password";

    /// <summary>POST (signed in): a new <see cref="InviteDto"/> for a friend. GET: your unused invites.</summary>
    public const string Invites = "/api/invites";

    /// <summary>GET (admins): a copy of the server's database (a SQLite file), made on the spot.</summary>
    public const string Backup = "/api/admin/backup";

    /// <summary>GET (signed in): <see cref="FriendsDto"/>. POST <see cref="FriendRequest"/>: ask someone, or accept their request.</summary>
    public const string Friends = "/api/friends";

    /// <summary>POST <see cref="FriendRequest"/>: remove a friend, decline their request, or withdraw yours.</summary>
    public const string RemoveFriend = "/api/friends/remove";

    /// <summary>GET (signed in) ?count=50&amp;before=ISO-time: your finished games, newest first (<see cref="GameRecordDto"/>).</summary>
    public const string Games = "/api/games";
}

public sealed record RegisterRequest(string Username, string Password, string? InviteCode, string? Device);

public sealed record SignInRequest(string Username, string Password, string? Device);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>A signed-in session: the key the app keeps (secret), and whose it is.</summary>
public sealed record SessionDto(string Token, AccountDto Account);

public sealed record AccountDto(string Id, string Username, bool IsAdmin, DateTimeOffset CreatedAt);

/// <summary>An invite code ("ABCD-EFGH") that lets one more person create an account.</summary>
public sealed record InviteDto(string Code, DateTimeOffset? ExpiresAt, int UsesLeft);

/// <summary>What the server is and allows (shown before signing in).</summary>
public sealed record ServerInfoDto(string Name, int ProtocolVersion, bool InviteOnly, string? Version = null);

/// <summary>Body of a failed account request: a message to show the player.</summary>
public sealed record ApiError(string Message);

public sealed record FriendRequest(string Username);

/// <summary>A friend and what they're doing: "online", "playing" or "offline".</summary>
public sealed record FriendDto(string Id, string Username, string Status);

/// <summary>Your friends, the requests waiting for you, and the ones you sent (usernames).</summary>
public sealed record FriendsDto(IReadOnlyList<FriendDto> Friends, IReadOnlyList<string> Incoming, IReadOnlyList<string> Outgoing);

/// <summary>A finished game of yours, with its PGN.</summary>
public sealed record GameRecordDto(string Id, string White, string Black, string TimeControl, string Result, string Termination,
    bool Rated, int? WhiteChange, int? BlackChange, DateTimeOffset EndedAt, string Pgn);
