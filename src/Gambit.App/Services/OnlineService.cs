using Microsoft.UI.Xaml.Controls;
using Gambit.App.Helpers;
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
        Client.Welcomed += (_, _) => _queue.TryEnqueue(() =>
        {
            _ = RefreshFriendsAsync();
            _ = SyncGamesAsync();
        });
        Client.StateChanged += s =>
        {
            if (s == OnlineState.Disconnected) _queue.TryEnqueue(() =>
            {
                Friends = null;
                FriendsChanged?.Invoke();
            });
        };
        Client.FriendsChanged += () => _queue.TryEnqueue(() => _ = RefreshFriendsAsync());
        Client.ChallengeReceived += c => _queue.TryEnqueue(() => _ = AnswerChallengeAsync(c));
        Client.ChallengeDeclined += (_, by) => _queue.TryEnqueue(() => NoticeReceived?.Invoke($"{by} declined your challenge."));
        Client.Rejected += ex => _queue.TryEnqueue(() =>
        {
            if (ex is OnlineVersionException version) SetVersionProblem(version);
            else NoticeReceived?.Invoke(OnlineClient.ServerMessage(ex));
        });
    }

    /// <summary>Create on the UI thread (first access happens from a page).</summary>
    public static OnlineService Instance => _instance ??= new OnlineService();

    public OnlineClient Client { get; } = new();

    public bool IsConnected => Client.State == OnlineState.Connected;

    /// <summary>The signed-in account is one of the server's admins (it can download the server's database).</summary>
    public bool IsAdmin { get; private set; }

    /// <summary>The server runs another version of Gambit (found by the last connection attempt), or null.</summary>
    public OnlineVersionException? VersionProblem { get; private set; }

    /// <summary>Raised when <see cref="VersionProblem"/> changes.</summary>
    public event Action? VersionProblemChanged;

    private void SetVersionProblem(OnlineVersionException? problem)
    {
        VersionProblem = problem;
        // This copy is the older one: look for the update now, so Home and the Online page can offer it.
        if (problem?.UpdateRequired == true && UpdateService.Instance.Available == null) _ = UpdateService.Instance.CheckAsync();
        VersionProblemChanged?.Invoke();
    }

    public event Action<OnlineState>? StateChanged;
    public event Action<LobbyStatsDto>? StatsChanged;
    public event Action<string>? NoticeReceived;

    /// <summary>Raised just before navigating to a new online game (lets the lobby reset its UI).</summary>
    public event Action? GameOpened;

    /// <summary>Your friends and open requests on the connected server, or null when not known.</summary>
    public FriendsDto? Friends { get; private set; }

    public event Action? FriendsChanged;

    /// <summary>
    /// Connects with the sign-in saved for this server. Throws <see cref="OnlineAccountException"/>
    /// (SignInRequired) when there is none or the server no longer accepts it, and
    /// <see cref="OnlineVersionException"/> when the server runs another version of Gambit.
    /// </summary>
    public async Task ConnectAsync(string serverUrl)
    {
        App.Settings.Current.OnlineServerUrl = serverUrl;
        VersionProblem = null; // this attempt says whether there still is one
        try
        {
            // First, because signing in is no use until this copy and the server run the same version.
            if (await OnlineClient.VersionProblemAsync(serverUrl) is OnlineVersionException version) throw version;
            if (OnlineCredentials.Get(serverUrl) is not OnlineCredentials.SavedSignIn saved)
                throw new OnlineAccountException("Sign in, or create an account, to play on this server.", signInRequired: true);
            IsAdmin = (await AccountClient.GetAccountAsync(serverUrl, saved.Token)).IsAdmin;
            await Client.ConnectAsync(serverUrl, saved.Token);
            SetVersionProblem(null);
        }
        catch (OnlineVersionException ex)
        {
            SetVersionProblem(ex);
            throw;
        }
        catch (OnlineAccountException ex) when (ex.SignInRequired)
        {
            OnlineCredentials.Remove(serverUrl);
            throw;
        }
    }

    /// <summary>Saves a copy of the server's database to <paramref name="file"/> (admins only).</summary>
    public async Task DownloadServerBackupAsync(string serverUrl, string file)
    {
        if (OnlineCredentials.Get(serverUrl) is not OnlineCredentials.SavedSignIn saved)
            throw new OnlineAccountException("Sign in first.", signInRequired: true);
        try
        {
            await using FileStream stream = File.Create(file);
            await AccountClient.DownloadBackupAsync(serverUrl, saved.Token, stream);
        }
        catch
        {
            File.Delete(file); // no half-written copies
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

    // ------------------------------------------------------------------ friends

    /// <summary>The connected server and the saved session key for it, or null.</summary>
    private (string Url, string Token)? Session =>
        Client.ServerUrl is string url && OnlineCredentials.Get(url) is OnlineCredentials.SavedSignIn saved ? (url, saved.Token) : null;

    public async Task RefreshFriendsAsync()
    {
        if (Session is not (string url, string token)) return;
        try
        {
            Friends = await AccountClient.GetFriendsAsync(url, token);
            FriendsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Info($"Friends not loaded: {ex.Message}");
        }
    }

    /// <summary>Asks someone to be friends, or accepts their request. Throws with the server's reason.</summary>
    public async Task AddFriendAsync(string username)
    {
        if (Session is not (string url, string token)) throw new OnlineAccountException("Sign in first.", signInRequired: true);
        Friends = await AccountClient.AddFriendAsync(url, token, username);
        FriendsChanged?.Invoke();
    }

    /// <summary>Removes a friend, declines their request or withdraws yours.</summary>
    public async Task RemoveFriendAsync(string username)
    {
        if (Session is not (string url, string token)) throw new OnlineAccountException("Sign in first.", signInRequired: true);
        Friends = await AccountClient.RemoveFriendAsync(url, token, username);
        FriendsChanged?.Invoke();
    }

    public Task ChallengeFriendAsync(string username, string timeControl, string color) =>
        Client.ChallengeFriendAsync(username, ParseTimeControl(timeControl), color);

    /// <summary>A friend's challenge: asks the user (any page), or declines when they're already playing online.</summary>
    private async Task AnswerChallengeAsync(ChallengeDto c)
    {
        try
        {
            if (GamePage.HasActiveOnlineGame)
            {
                await Client.DeclineChallengeAsync(c.Code);
                return;
            }
            string colors = c.Color switch { "white" => "you play Black", "black" => "you play White", _ => "colors at random" };
            var dialog = new ContentDialog
            {
                Title = $"{c.Creator.Name} challenges you",
                Content = $"{c.TimeControl.Key.Replace("+", " | ")} · {colors}",
                PrimaryButtonText = "Play",
                CloseButtonText = "Decline",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await Dialogs.ShowAsync(dialog, App.Window.Content?.XamlRoot) == ContentDialogResult.Primary)
            {
                if (!await Client.AcceptChallengeAsync(c.Code)) NoticeReceived?.Invoke($"{c.Creator.Name}'s challenge isn't open anymore.");
            }
            else
            {
                await Client.DeclineChallengeAsync(c.Code);
            }
        }
        catch (Exception ex)
        {
            NoticeReceived?.Invoke(OnlineClient.ServerMessage(ex));
        }
    }

    /// <summary>Adds your online games from other PCs to the game list (see ProfileService.ImportOnlineGames).</summary>
    public async Task SyncGamesAsync()
    {
        if (Session is not (string url, string token) || Client.Me is not PlayerDto me) return;
        try
        {
            List<GameRecordDto> games = await AccountClient.GetGamesAsync(url, token, 100);
            int added = App.Profile.ImportOnlineGames(games, me.Name);
            if (added > 0) Log.Info($"Added {added} online game(s) from the server to the game list");
        }
        catch (Exception ex)
        {
            Log.Info($"Online games not synced: {ex.Message}");
        }
    }

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
