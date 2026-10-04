using System.Threading.RateLimiting;
using Gambit.Online;
using Gambit.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;

namespace Gambit.Server;

/// <summary>Builds the server app (used by Program and by in-process integration tests).</summary>
public static class ServerHost
{
    /// <summary>Settings from the "Gambit" configuration section, with GAMBIT_DATA overriding the data folder.</summary>
    public static ServerOptions LoadOptions(IConfiguration configuration)
    {
        var options = new ServerOptions();
        configuration.GetSection("Gambit").Bind(options);
        if (Environment.GetEnvironmentVariable("GAMBIT_DATA") is { Length: > 0 } dataDir) options.DataDirectory = dataDir;
        return options;
    }

    public static WebApplication CreateApp(string[] args, ServerOptions? overrideOptions = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        ServerOptions options = overrideOptions ?? LoadOptions(builder.Configuration);
        Directory.CreateDirectory(options.DataDirectory);
        string? restored = ServerDatabase.ApplyPendingRestore(options.DataDirectory);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ServerDatabase>();
        builder.Services.AddSingleton<AccountStore>();
        builder.Services.AddSingleton<RatingStore>();
        builder.Services.AddSingleton<GameArchive>();
        builder.Services.AddSingleton<PlayerRegistry>();
        builder.Services.AddSingleton<Conduct>();
        builder.Services.AddSingleton<GameManager>();
        builder.Services.AddHostedService<GameClockService>();
        builder.Services.AddSingleton<AccountWatch>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AccountWatch>());
        builder.Services.AddSingleton<DatabaseBackups>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DatabaseBackups>());
        builder.Services.AddSingleton<CallRateLimitFilter>();
        builder.Services.AddAuthentication(SessionAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthHandler>(SessionAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR(o =>
        {
            o.KeepAliveInterval = TimeSpan.FromSeconds(10);
            o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            o.MaximumReceiveMessageSize = 32 * 1024;
            o.AddFilter<CallRateLimitFilter>();
        });

        // Requests per client IP. Behind a reverse proxy (most cloud hosts) set
        // ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so the client's real address is used.
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("connect", http => PerAddress(http, options.ConnectionsPerMinute));
            o.AddPolicy("account", http => PerAddress(http, options.AccountRequestsPerMinute));
        });

        WebApplication app = builder.Build();
        if (restored != null) app.Logger.LogWarning("{Message}", restored);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<GameHub>(OnlineProtocol.HubPath).RequireRateLimiting("connect");
        app.MapAccountApi(options);
        app.MapReleases(options);
        app.MapGet("/health", () => Results.Ok("ok"));
        app.MapGet("/", (GameManager games) =>
        {
            LobbyStatsDto s = games.Stats();
            return Results.Text($"Gambit server · protocol v{OnlineProtocol.Version} · {s.PlayersOnline} online · {s.GamesInProgress} games in progress");
        });

        // A brand-new server has no accounts: print the code that creates the first one (an admin).
        if (app.Services.GetRequiredService<AccountStore>().OwnerInvite() is Invite owner)
        {
            app.Logger.LogWarning("No accounts yet. In Gambit, choose Online > Create account and use invite code {Code} " +
                "for your own account (it becomes the admin).", owner.Code);
        }
        return app;
    }

    private static RateLimitPartition<string> PerAddress(HttpContext http, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
