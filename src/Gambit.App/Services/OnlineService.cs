using Gambit.App.Pages;
using Gambit.Online;
using Gambit.Online.Client;
using Microsoft.UI.Dispatching;

namespace Gambit.App.Services;

/// <summary>
/// App-wide connection to a Gambit server. Marshals client events to the UI thread and opens the
/// game page (with a <see cref="RemoteGameSession"/>) whenever the server starts or resumes a game.
/// </summary>
public sealed class OnlineService
{
    private static OnlineService? _instance;
    private readonly DispatcherQueue _queue;
    private RemoteGameSession? _current;

    private OnlineService()
    {
        _queue = DispatcherQueue.GetForCurrentThread();
        Client.StateChanged += s => _queue.TryEnqueue(() => StateChanged?.Invoke(s));
        Client.Welcomed += (_, stats) => _queue.TryEnqueue(() => StatsChanged?.Invoke(stats));
        Client.GameStarted += g => _queue.TryEnqueue(() => OpenGame(g));
        Client.Resync += g => _queue.TryEnqueue(() =>
        {
            // A resync for a game we don't have open (e.g. after restarting the app) opens it.
            if (_current?.GameId != g.GameId) OpenGame(g);
        });
        Client.Notice += m => _queue.TryEnqueue(() => NoticeReceived?.Invoke(m));
    }

    /// <summary>Create on the UI thread (first access happens from a page).</summary>
    public static OnlineService Instance => _instance ??= new OnlineService();

    public OnlineClient Client { get; } = new();

    public bool IsConnected => Client.State == OnlineState.Connected;

    public event Action<OnlineState>? StateChanged;
    public event Action<LobbyStatsDto>? StatsChanged;
    public event Action<string>? NoticeReceived;

    /// <summary>Raised just before navigating to a new online game (lets the lobby reset its UI).</summary>
    public event Action? GameOpened;

    /// <summary>
    /// Connects with the sign-in saved for this server. Throws <see cref="OnlineAccountException"/>
    /// (SignInRequired) when there is none or the server no longer accepts it.
    /// </summary>
    public async Task ConnectAsync(string serverUrl)
    {
        App.Settings.Current.OnlineServerUrl = serverUrl;
        if (OnlineCredentials.Get(serverUrl) is not OnlineCredentials.SavedSignIn saved)
            throw new OnlineAccountException("Sign in, or create an account, to play on this server.", signInRequired: true);
        try
        {
            await Client.ConnectAsync(serverUrl, saved.Token);
        }
        catch (OnlineAccountException ex) when (ex.SignInRequired)
        {
            OnlineCredentials.Remove(serverUrl);
            throw;
        }
    }

    public async Task SignInAsync(string serverUrl, string username, string password)
    {
        SessionDto session = await AccountClient.SignInAsync(serverUrl, username, password, Device);
        OnlineCredentials.Save(serverUrl, session.Account.Username, session.Token);
        await ConnectAsync(serverUrl);
    }

    public async Task RegisterAsync(string serverUrl, string username, string password, string? inviteCode)
    {
        SessionDto session = await AccountClient.RegisterAsync(serverUrl, username, password, inviteCode, Device);
        OnlineCredentials.Save(serverUrl, session.Account.Username, session.Token);
        await ConnectAsync(serverUrl);
    }

    /// <summary>Ends the session on the server, forgets it here and disconnects.</summary>
    public async Task SignOutAsync(string serverUrl)
    {
        if (OnlineCredentials.Get(serverUrl) is OnlineCredentials.SavedSignIn saved) await AccountClient.SignOutAsync(serverUrl, saved.Token);
        OnlineCredentials.Remove(serverUrl);
        await Client.DisconnectAsync();
    }

    /// <summary>A one-use invite code for a friend (signed-in players only).</summary>
    public Task<InviteDto> CreateInviteAsync(string serverUrl) =>
        OnlineCredentials.Get(serverUrl) is OnlineCredentials.SavedSignIn saved
            ? AccountClient.CreateInviteAsync(serverUrl, saved.Token)
            : throw new OnlineAccountException("Sign in first.", signInRequired: true);

    public Task DisconnectAsync() => Client.DisconnectAsync();

    /// <summary>How this PC shows up among an account's sign-ins on the server.</summary>
    private static string Device => $"Gambit on {Environment.MachineName}";

    /// <summary>Shows a game the server sent (a player's own game, or one being watched). Call on the UI thread.</summary>
    public void Open(GameStartDto start) => OpenGame(start);

    private void OpenGame(GameStartDto start)
    {
        GameOpened?.Invoke();
        _current = new RemoteGameSession(Client, start);
        App.Window.Navigate(typeof(GamePage), _current, "online");
    }

    /// <summary>Parses "3+2" style keys.</summary>
    public static TimeControlDto ParseTimeControl(string key)
    {
        string[] p = key.Split('+');
        int minutes = int.TryParse(p[0], out int m) ? m : 3;
        int inc = p.Length > 1 && int.TryParse(p[1], out int i) ? i : 0;
        return new TimeControlDto(minutes * 60, inc);
    }
}
