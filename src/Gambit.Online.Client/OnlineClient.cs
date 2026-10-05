using System.Net;
using Microsoft.AspNetCore.SignalR.Client;

namespace Gambit.Online.Client;

public enum OnlineState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>
/// Connection to a Gambit server as a signed-in account (session key from <see cref="AccountClient"/>).
/// Wraps the SignalR hub with typed calls and events (raised on the SignalR thread — consumers marshal
/// to their UI thread). Reconnects automatically and re-registers, so the server can resume a game in progress.
/// </summary>
public sealed class OnlineClient : IAsyncDisposable
{
    private HubConnection? _hub;
    private readonly HashSet<string> _watching = [];

    public OnlineState State { get; private set; } = OnlineState.Disconnected;
    public PlayerDto? Me { get; private set; }
    public LobbyStatsDto? Stats { get; private set; }
    public string? ServerUrl { get; private set; }

    public event Action<OnlineState>? StateChanged;
    public event Action<PlayerDto, LobbyStatsDto>? Welcomed;
    public event Action<GameStartDto>? GameStarted;
    public event Action<MoveDto>? MovePlayed;
    public event Action<GameOverDto>? GameOver;
    public event Action<string, string>? DrawOffered;
    public event Action<string>? DrawDeclined;
    public event Action<string, bool, int>? OpponentConnection;
    public event Action<GameStartDto>? Resync;
    public event Action<string>? Notice;
    public event Action<string>? RematchOffered;
    public event Action<string, bool>? RematchDeclined;

    /// <summary>The friend list changed on the server: fetch it again (AccountClient.GetFriendsAsync).</summary>
    public event Action? FriendsChanged;

    /// <summary>A friend challenges you: AcceptChallengeAsync(code) or DeclineChallengeAsync(code).</summary>
    public event Action<ChallengeDto>? ChallengeReceived;

    /// <summary>A challenge you sent a friend was turned down: (code, their name).</summary>
    public event Action<string, string>? ChallengeDeclined;

    public event Action<ChatDto>? ChatMessage;

    /// <summary>
    /// The server turned down a reconnection, for instance because it was updated to a newer version
    /// meanwhile; the client has disconnected. An <see cref="OnlineVersionException"/> says which side
    /// needs updating.
    /// </summary>
    public event Action<Exception>? Rejected;

    /// <summary>
    /// Connects with a session key. Throws <see cref="OnlineAccountException"/> (SignInRequired) when the
    /// server no longer accepts it, and <see cref="OnlineVersionException"/> when it runs another version
    /// of the online protocol.
    /// </summary>
    public async Task ConnectAsync(string serverUrl, string sessionToken, CancellationToken ct = default)
    {
        await DisconnectAsync();
        ServerUrl = serverUrl.TrimEnd('/');
        if (await VersionProblemAsync(ServerUrl, ct) is OnlineVersionException version) throw version;
        _hub = new HubConnectionBuilder()
            .WithUrl($"{ServerUrl}{OnlineProtocol.HubPath}", o => o.AccessTokenProvider = () => Task.FromResult<string?>(sessionToken))
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15)])
            .Build();

        _hub.On<PlayerDto, LobbyStatsDto>(nameof(IGameClient.Welcome), (me, stats) =>
        {
            Me = me;
            Stats = stats;
            Welcomed?.Invoke(me, stats);
        });
        _hub.On<GameStartDto>(nameof(IGameClient.GameStarted), g => GameStarted?.Invoke(g));
        _hub.On<MoveDto>(nameof(IGameClient.MovePlayed), m => MovePlayed?.Invoke(m));
        _hub.On<GameOverDto>(nameof(IGameClient.GameOver), r => GameOver?.Invoke(r));
        _hub.On<string, string>(nameof(IGameClient.DrawOffered), (id, by) => DrawOffered?.Invoke(id, by));
        _hub.On<string>(nameof(IGameClient.DrawDeclined), id => DrawDeclined?.Invoke(id));
        _hub.On<string, bool, int>(nameof(IGameClient.OpponentConnection), (id, c, s) => OpponentConnection?.Invoke(id, c, s));
        _hub.On<GameStartDto>(nameof(IGameClient.Resync), g => Resync?.Invoke(g));
        _hub.On<string>(nameof(IGameClient.Notice), m => Notice?.Invoke(m));
        _hub.On<string>(nameof(IGameClient.RematchOffered), id => RematchOffered?.Invoke(id));
        _hub.On<string, bool>(nameof(IGameClient.RematchDeclined), (id, unavailable) => RematchDeclined?.Invoke(id, unavailable));
        _hub.On(nameof(IGameClient.FriendsChanged), () => FriendsChanged?.Invoke());
        _hub.On<ChallengeDto>(nameof(IGameClient.ChallengeReceived), c => ChallengeReceived?.Invoke(c));
        _hub.On<string, string>(nameof(IGameClient.ChallengeDeclined), (code, by) => ChallengeDeclined?.Invoke(code, by));
        _hub.On<ChatDto>(nameof(IGameClient.ChatMessage), m => ChatMessage?.Invoke(m));

        _hub.Reconnecting += _ =>
        {
            SetState(OnlineState.Reconnecting);
            return Task.CompletedTask;
        };
        _hub.Reconnected += async _ =>
        {
            try
            {
                await _hub.InvokeAsync<PlayerDto>(nameof(IGameServer.Hello), "", OnlineProtocol.Version);
            }
            catch (Exception ex)
            {
                // Turned down, e.g. the server came back updated: stop retrying and say why.
                Exception reason = await VersionProblemAsync(ServerUrl!, CancellationToken.None) ?? ex;
                await DisconnectAsync();
                Rejected?.Invoke(reason);
                return;
            }
            SetState(OnlineState.Connected);
            // A new connection isn't in any spectator group yet: watch again and catch up.
            string[] watching;
            lock (_watching) watching = [.. _watching];
            foreach (string id in watching)
                if (await _hub.InvokeAsync<GameStartDto?>(nameof(IGameServer.Watch), id) is GameStartDto game) Resync?.Invoke(game);
        };
        _hub.Closed += _ =>
        {
            SetState(OnlineState.Disconnected);
            return Task.CompletedTask;
        };

        SetState(OnlineState.Connecting);
        try
        {
            await _hub.StartAsync(ct);
            Me = await _hub.InvokeAsync<PlayerDto>(nameof(IGameServer.Hello), "", OnlineProtocol.Version, ct);
            SetState(OnlineState.Connected);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            await DisconnectAsync();
            throw new OnlineAccountException("Please sign in again.", signInRequired: true);
        }
        catch
        {
            SetState(OnlineState.Disconnected);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        lock (_watching) _watching.Clear();
        if (_hub == null) return;
        HubConnection hub = _hub;
        _hub = null;
        try
        {
            await hub.DisposeAsync();
        }
        catch
        {
            // Already gone.
        }
        SetState(OnlineState.Disconnected);
    }

    private HubConnection Hub => _hub is { State: HubConnectionState.Connected } h ? h : throw new InvalidOperationException("Not connected to the server.");

    public Task SeekAsync(TimeControlDto tc) => Hub.InvokeAsync(nameof(IGameServer.Seek), tc);
    public Task CancelSeekAsync() => Hub.InvokeAsync(nameof(IGameServer.CancelSeek));
    public Task<ChallengeDto> CreateChallengeAsync(TimeControlDto tc, string color) => Hub.InvokeAsync<ChallengeDto>(nameof(IGameServer.CreateChallenge), tc, color);
    public Task<bool> AcceptChallengeAsync(string code) => Hub.InvokeAsync<bool>(nameof(IGameServer.AcceptChallenge), code);
    public Task<bool> MakeMoveAsync(string gameId, int ply, string uci) => Hub.InvokeAsync<bool>(nameof(IGameServer.MakeMove), gameId, ply, uci);
    public Task ResignAsync(string gameId) => Hub.InvokeAsync(nameof(IGameServer.Resign), gameId);
    public Task OfferDrawAsync(string gameId) => Hub.InvokeAsync(nameof(IGameServer.OfferDraw), gameId);
    public Task RespondToDrawAsync(string gameId, bool accept) => Hub.InvokeAsync(nameof(IGameServer.RespondToDraw), gameId, accept);
    public Task<bool> RejoinAsync(string gameId) => Hub.InvokeAsync<bool>(nameof(IGameServer.Rejoin), gameId);
    public Task OfferRematchAsync(string gameId) => Hub.InvokeAsync(nameof(IGameServer.OfferRematch), gameId);
    public Task DeclineRematchAsync(string gameId) => Hub.InvokeAsync(nameof(IGameServer.DeclineRematch), gameId);
    public Task<IReadOnlyList<LiveGameDto>> ListGamesAsync() => Hub.InvokeAsync<IReadOnlyList<LiveGameDto>>(nameof(IGameServer.ListGames));
    public Task<ChallengeDto> ChallengeFriendAsync(string username, TimeControlDto tc, string color) =>
        Hub.InvokeAsync<ChallengeDto>(nameof(IGameServer.ChallengeFriend), username, tc, color);
    public Task DeclineChallengeAsync(string code) => Hub.InvokeAsync(nameof(IGameServer.DeclineChallenge), code);
    public Task SendChatAsync(string gameId, string text) => Hub.InvokeAsync(nameof(IGameServer.SendChat), gameId, text);

    /// <summary>Starts watching a game (null if it's no longer running). Watched games survive reconnects.</summary>
    public async Task<GameStartDto?> WatchAsync(string gameId)
    {
        GameStartDto? game = await Hub.InvokeAsync<GameStartDto?>(nameof(IGameServer.Watch), gameId);
        if (game?.YourColor == "spectator") lock (_watching) _watching.Add(gameId);
        return game;
    }

    public async Task UnwatchAsync(string gameId)
    {
        lock (_watching) _watching.Remove(gameId);
        if (_hub is { State: HubConnectionState.Connected } hub) await hub.InvokeAsync(nameof(IGameServer.Unwatch), gameId);
    }

    /// <summary>
    /// Asks the server which protocol version it speaks (no sign-in needed): an exception saying which
    /// side needs updating, or null when they match or the server can't tell (connecting then says why).
    /// </summary>
    public static async Task<OnlineVersionException?> VersionProblemAsync(string serverUrl, CancellationToken ct = default)
    {
        try
        {
            return VersionProblem(await AccountClient.GetInfoAsync(serverUrl, ct));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Which side needs updating when <paramref name="server"/> speaks another protocol version than this app, or null.</summary>
    public static OnlineVersionException? VersionProblem(ServerInfoDto server)
    {
        string version = server.Version is { Length: > 0 } v ? $" ({v})" : "";
        if (server.ProtocolVersion > OnlineProtocol.Version)
            return new OnlineVersionException($"This server runs a newer version of Gambit{version}. Update Gambit to play online.", updateRequired: true);
        if (server.ProtocolVersion < OnlineProtocol.Version)
            return new OnlineVersionException($"This server runs an older version of Gambit{version} than yours. Ask whoever runs it to update the server.", updateRequired: false);
        return null;
    }

    /// <summary>The server's own words from a failed hub call ("... HubException: message"), else the exception's message.</summary>
    public static string ServerMessage(Exception ex)
    {
        const string marker = "HubException: ";
        int i = ex.Message.IndexOf(marker, StringComparison.Ordinal);
        return i >= 0 ? ex.Message[(i + marker.Length)..] : ex.Message;
    }

    private void SetState(OnlineState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
