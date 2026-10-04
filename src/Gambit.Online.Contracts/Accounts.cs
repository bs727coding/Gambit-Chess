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
public sealed record ServerInfoDto(string Name, int ProtocolVersion, bool InviteOnly);

/// <summary>Body of a failed account request: a message to show the player.</summary>
public sealed record ApiError(string Message);
