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
/// Connection to a Gambit server. Wraps the SignalR hub with typed calls and events (raised on the
/// SignalR thread — consumers marshal to their UI thread). Reconnects automatically and re-registers,
/// so the server can resume a game in progress.
/// </summary>
public sealed class OnlineClient : IAsyncDisposable
{
    private HubConnection? _hub;
    private string _name = "Guest";

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

    public async Task ConnectAsync(string serverUrl, string token, string name, CancellationToken ct = default)
    {
        await DisconnectAsync();
        _name = name;
        ServerUrl = serverUrl.TrimEnd('/');
        string url = $"{ServerUrl}{OnlineProtocol.HubPath}?token={Uri.EscapeDataString(token)}";
        _hub = new HubConnectionBuilder()
            .WithUrl(url)
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

        _hub.Reconnecting += _ =>
        {
            SetState(OnlineState.Reconnecting);
            return Task.CompletedTask;
        };
        _hub.Reconnected += async _ =>
        {
            SetState(OnlineState.Connected);
            await _hub.InvokeAsync<PlayerDto>(nameof(IGameServer.Hello), _name, OnlineProtocol.Version);
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
            Me = await _hub.InvokeAsync<PlayerDto>(nameof(IGameServer.Hello), name, OnlineProtocol.Version, ct);
            SetState(OnlineState.Connected);
        }
        catch
        {
            SetState(OnlineState.Disconnected);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
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

    private void SetState(OnlineState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
