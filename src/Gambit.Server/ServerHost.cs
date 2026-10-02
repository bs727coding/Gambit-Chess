using Gambit.Online;

namespace Gambit.Server;

/// <summary>Builds the server app (used by Program and by in-process integration tests).</summary>
public static class ServerHost
{
    public static WebApplication CreateApp(string[] args, ServerOptions? overrideOptions = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        ServerOptions options = overrideOptions ?? new ServerOptions();
        if (overrideOptions == null)
        {
            builder.Configuration.GetSection("Gambit").Bind(options);
            if (Environment.GetEnvironmentVariable("GAMBIT_DATA") is { Length: > 0 } dataDir) options.DataDirectory = dataDir;
        }
        Directory.CreateDirectory(options.DataDirectory);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<RatingStore>();
        builder.Services.AddSingleton<PlayerRegistry>();
        builder.Services.AddSingleton<GameManager>();
        builder.Services.AddHostedService<GameClockService>();
        builder.Services.AddSignalR(o =>
        {
            o.KeepAliveInterval = TimeSpan.FromSeconds(10);
            o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            o.MaximumReceiveMessageSize = 32 * 1024;
        });

        WebApplication app = builder.Build();
        app.MapHub<GameHub>(OnlineProtocol.HubPath);
        app.MapGet("/health", () => Results.Ok("ok"));
        app.MapGet("/", (GameManager games) =>
        {
            LobbyStatsDto s = games.Stats();
            return Results.Text($"Gambit server · protocol v{OnlineProtocol.Version} · {s.PlayersOnline} online · {s.GamesInProgress} games in progress");
        });
        return app;
    }
}
