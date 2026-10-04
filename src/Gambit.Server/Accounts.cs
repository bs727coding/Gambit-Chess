using System.Security.Claims;
using System.Text.Encodings.Web;
using Gambit.Online;
using Gambit.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Gambit.Server;

/// <summary>
/// Signs requests in by session key: "Authorization: Bearer ..." or, on the game hub only,
/// ?access_token=... (how SignalR's WebSocket transport sends it).
/// </summary>
public sealed class SessionAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AccountStore accounts)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "GambitSession";
    public const string AdminRole = "admin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (SessionToken(Request) is not string token) return Task.FromResult(AuthenticateResult.NoResult());
        if (accounts.FindSession(token) is not Account account) return Task.FromResult(AuthenticateResult.Fail("Unknown or expired session."));

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, account.Id), new(ClaimTypes.Name, account.Username) };
        if (account.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    public static string? SessionToken(HttpRequest request)
    {
        string header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return header["Bearer ".Length..].Trim();
        if (request.Path.StartsWithSegments(OnlineProtocol.HubPath) && request.Query["access_token"].ToString() is { Length: > 0 } query) return query;
        return null;
    }
}

/// <summary>The account endpoints (<see cref="AccountApi"/>).</summary>
public static class AccountEndpoints
{
    public static void MapAccountApi(this WebApplication app, ServerOptions options)
    {
        app.MapGet(AccountApi.Info, () => new ServerInfoDto("Gambit server", OnlineProtocol.Version, options.SignUps == SignUpMode.Invite));

        // Signing in and up: rate-limited per address on top of the per-name lockout in AccountStore.
        app.MapPost(AccountApi.Register, (RegisterRequest req, AccountStore accounts, ILogger<AccountStore> log) =>
        {
            AccountResult r = accounts.Register(req.Username, req.Password, req.InviteCode);
            if (r.Account is not Account account) return Results.BadRequest(new ApiError(r.Error ?? "Sign-up failed."));
            log.LogInformation("New account {Name} ({Id})", account.Username, account.Id);
            return Results.Ok(new SessionDto(accounts.CreateSession(account, req.Device), ToDto(account)));
        }).RequireRateLimiting("account");

        app.MapPost(AccountApi.SignIn, (SignInRequest req, AccountStore accounts) =>
        {
            AccountResult r = accounts.SignIn(req.Username, req.Password);
            if (r.Account is not Account account) return Results.Json(new ApiError(r.Error ?? "Sign-in failed."), statusCode: StatusCodes.Status401Unauthorized);
            return Results.Ok(new SessionDto(accounts.CreateSession(account, req.Device), ToDto(account)));
        }).RequireRateLimiting("account");

        app.MapPost(AccountApi.SignOut, (HttpRequest http, AccountStore accounts) =>
        {
            accounts.EndSession(SessionAuthHandler.SessionToken(http));
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet(AccountApi.Me, (ClaimsPrincipal user, AccountStore accounts) =>
            accounts.FindById(Id(user)) is Account account ? Results.Ok(ToDto(account)) : Results.Unauthorized()).RequireAuthorization();

        app.MapPost(AccountApi.Password, (ChangePasswordRequest req, ClaimsPrincipal user, HttpRequest http, AccountStore accounts) =>
        {
            if (accounts.FindById(Id(user)) is not Account account) return Results.Unauthorized();
            string? error = accounts.ChangePassword(account, req.CurrentPassword, req.NewPassword, SessionAuthHandler.SessionToken(http)!);
            return error == null ? Results.NoContent() : Results.BadRequest(new ApiError(error));
        }).RequireAuthorization().RequireRateLimiting("account");

        app.MapPost(AccountApi.Invites, (ClaimsPrincipal user, AccountStore accounts) =>
        {
            if (accounts.FindById(Id(user)) is not Account account) return Results.Unauthorized();
            (Invite? invite, string? error) = accounts.CreateMemberInvite(account);
            return invite != null ? Results.Ok(ToDto(invite)) : Results.BadRequest(new ApiError(error ?? "Couldn't create an invite."));
        }).RequireAuthorization();

        app.MapGet(AccountApi.Invites, (ClaimsPrincipal user, AccountStore accounts) =>
            accounts.OpenInvites(Id(user)).Select(ToDto).ToList()).RequireAuthorization();

        // A fresh copy of the whole database for an admin to keep off the server.
        app.MapGet(AccountApi.Backup, (ServerDatabase db, ClaimsPrincipal user, ILogger<ServerDatabase> log) =>
        {
            string temp = Path.Combine(Path.GetTempPath(), $"gambit-download-{Guid.NewGuid():N}.db");
            db.BackupTo(temp);
            log.LogInformation("Database downloaded by {Admin}", user.Identity?.Name);
            var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.DeleteOnClose);
            return Results.File(stream, "application/vnd.sqlite3", $"gambit-{DateTime.UtcNow:yyyy-MM-dd-HHmm}.db");
        }).RequireAuthorization(policy => policy.RequireRole(SessionAuthHandler.AdminRole));
    }

    private static string Id(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    public static AccountDto ToDto(Account a) => new(a.Id, a.Username, a.IsAdmin, a.CreatedAt);

    public static InviteDto ToDto(Invite i) => new(i.Code, i.ExpiresAt, i.UsesLeft);
}
