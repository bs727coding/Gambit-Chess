using Windows.System;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Text;
using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Online;
using Gambit.Online.Client;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace Gambit.App.Pages;

/// <summary>Online lobby: sign in to a server, quick pairing by time control, friend challenges, invites.</summary>
public sealed partial class OnlinePage : Page
{
    private static readonly string[] TimeControls = ["1+0", "2+1", "3+0", "3+2", "5+0", "5+3", "10+0", "10+5", "15+10", "30+0"];
    private OnlineService Online => OnlineService.Instance;
    private bool _creatingAccount;

    public OnlinePage()
    {
        InitializeComponent();
        Ui.StretchTiles(SeekGrid, 100);
        foreach (string tc in TimeControls)
        {
            var button = new Button
            {
                Content = Label(tc),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 0, 8, 8),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Seek {tc}");
            button.Click += async (_, _) => await SeekAsync(tc);
            SeekGrid.Children.Add(button);
            ChallengeTime.Items.Add(tc);
        }
        ChallengeTime.SelectedItem = "10+0";
    }

    private static object Label(string tc)
    {
        TimeControlDto dto = OnlineService.ParseTimeControl(tc);
        string category = new Core.Games.TimeControl(TimeSpan.FromSeconds(dto.InitialSeconds), TimeSpan.FromSeconds(dto.IncrementSeconds)).Category.ToString();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = tc.Replace("+", " | "), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = category, FontSize = 11, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center });
        return stack;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ServerBox.Text = App.Settings.Current.OnlineServerUrl;
        Online.StateChanged += OnStateChanged;
        Online.StatsChanged += OnStatsChanged;
        Online.GameOpened += OnGameOpened;
        Online.NoticeReceived += OnNotice;
        Online.FriendsChanged += ShowFriends;
        ShowFriends();
        UpdateState(Online.Client.State);
        if (Online.Client.Stats is LobbyStatsDto stats) OnStatsChanged(stats);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Online.StateChanged -= OnStateChanged;
        Online.StatsChanged -= OnStatsChanged;
        Online.GameOpened -= OnGameOpened;
        Online.NoticeReceived -= OnNotice;
        Online.FriendsChanged -= ShowFriends;
    }

    private void OnNotice(string message)
    {
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
    }

    private void OnStateChanged(OnlineState state) => UpdateState(state);

    private void OnStatsChanged(LobbyStatsDto s) =>
        StatsText.Text = $"· {s.PlayersOnline} online · {s.GamesInProgress} game{(s.GamesInProgress == 1 ? "" : "s")} in progress";

    private void OnGameOpened()
    {
        SeekingPanel.Visibility = Visibility.Collapsed;
        CodePanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateState(OnlineState state)
    {
        string? savedName = OnlineCredentials.Get(ServerBox.Text.Trim())?.Username;
        (string text, string color) = state switch
        {
            OnlineState.Connected => ($"Signed in as {Online.Client.Me?.Name ?? savedName}" +
                                      (Online.Client.Me is PlayerDto me ? $" ({me.Rating}{(me.Provisional ? "?" : "")})" : ""), "#2E7D32"),
            OnlineState.Connecting => ("Connecting…", "#F7C045"),
            OnlineState.Reconnecting => ("Connection lost — reconnecting…", "#F7C045"),
            _ => (savedName != null ? $"Not connected · signed in as {savedName}" : "Not connected", "#8A8A8A"),
        };
        StatusText.Text = text;
        StatusDot.Fill = new SolidColorBrush(Ui.ParseColor(color));
        bool connected = state == OnlineState.Connected;
        LobbyGrid.IsHitTestVisible = WatchCard.IsHitTestVisible = FriendsCard.IsHitTestVisible = connected;
        LobbyGrid.Opacity = WatchCard.Opacity = FriendsCard.Opacity = connected ? 1 : 0.45;
        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        ConnectButton.IsEnabled = state is OnlineState.Connected or OnlineState.Disconnected;
        SignedInPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ServerBackupButton.Visibility = connected && Online.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (connected) AccountPanel.Visibility = Visibility.Collapsed;
        else InvitePanel.Visibility = Visibility.Collapsed;
        if (!connected)
        {
            StatsText.Text = "";
            LiveGamesList.Children.Clear();
            NoGamesText.Visibility = Visibility.Visible;
        }
        else
        {
            _ = RefreshGamesAsync();
        }
    }

    // ------------------------------------------------------------------ spectating

    private async void RefreshGames_Click(object sender, RoutedEventArgs e) => await RefreshGamesAsync();

    private async Task RefreshGamesAsync()
    {
        if (!Online.IsConnected) return;
        try
        {
            IReadOnlyList<LiveGameDto> games = await Online.Client.ListGamesAsync();
            LiveGamesList.Children.Clear();
            foreach (LiveGameDto g in games) LiveGamesList.Children.Add(LiveGameRow(g));
            NoGamesText.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private FrameworkElement LiveGameRow(LiveGameDto g)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = $"{g.White.Name} ({g.White.Rating}) vs {g.Black.Name} ({g.Black.Rating})",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        string watching = g.Spectators > 0 ? $" · {g.Spectators} watching" : "";
        text.Children.Add(new TextBlock { Text = $"{g.TimeControl.Key.Replace("+", " | ")} · move {g.Plies / 2 + 1}{watching}", FontSize = 12, Opacity = 0.7 });
        row.Children.Add(text);

        var watch = new Button { Content = "Watch", VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(watch, $"Watch {g.White.Name} vs {g.Black.Name}");
        watch.Click += async (_, _) => await WatchAsync(g.GameId);
        Grid.SetColumn(watch, 1);
        row.Children.Add(watch);
        return row;
    }

    private async Task WatchAsync(string gameId)
    {
        if (!EnsureConnected()) return;
        try
        {
            if (await Online.Client.WatchAsync(gameId) is GameStartDto game) Online.Open(game);
            else
            {
                ShowError("That game has already finished.");
                await RefreshGamesAsync();
            }
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        if (Online.IsConnected)
        {
            await Online.DisconnectAsync();
            return;
        }
        if (ServerUrl() is not string url) return;
        try
        {
            await Online.ConnectAsync(url);
        }
        catch (OnlineAccountException ex) when (ex.SignInRequired)
        {
            await ShowAccountPanelAsync(url, ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't connect: {ex.Message}");
        }
    }

    /// <summary>The server address from the box, or null (with an error shown) if it isn't one.</summary>
    private string? ServerUrl()
    {
        string url = ServerBox.Text.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https") return url;
        ShowError("Enter a server address like http://192.168.1.20:5080 or https://chess.example.com.");
        return null;
    }

    // ------------------------------------------------------------------ account

    /// <summary>Shows the sign-in form (keeping "create an account" only if the form was already showing it).</summary>
    private async Task ShowAccountPanelAsync(string url, string message)
    {
        SetAccountMode(AccountPanel.Visibility == Visibility.Visible && _creatingAccount, message);
        AccountPanel.Visibility = Visibility.Visible;
        InsecureBar.IsOpen = ServerAddress.IsUnencryptedOverInternet(url);
        UsernameBox.Text = OnlineCredentials.Get(url)?.Username ?? UsernameBox.Text;
        UsernameBox.Focus(FocusState.Programmatic);
        try
        {
            ServerInfoDto info = await AccountClient.GetInfoAsync(url);
            InviteBox.Header = info.InviteOnly ? "Invite code" : "Invite code (optional)";
        }
        catch (OnlineAccountException ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private void SetAccountMode(bool create, string? message = null)
    {
        _creatingAccount = create;
        AccountTitle.Text = create ? "Create an account" : "Sign in";
        AccountHint.Text = message ?? (create
            ? "Pick a username (3 to 20 letters, digits, - or _) and a password of 8 or more characters."
            : "Sign in with your username and password for this server.");
        InviteBox.Visibility = ConfirmInput.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        AccountButton.Content = create ? "Create account" : "Sign in";
        SwitchModeButton.Content = create ? "Already have an account? Sign in" : "New here? Create an account";
    }

    private void SwitchMode_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        SetAccountMode(!_creatingAccount);
    }

    private void AccountField_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        Account_Click(sender, new RoutedEventArgs());
    }

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        if (ServerUrl() is not string url) return;
        string username = UsernameBox.Text.Trim(), password = PasswordInput.Password;
        if (username.Length == 0 || password.Length == 0)
        {
            ShowError("Enter your username and password.");
            return;
        }
        if (_creatingAccount && password != ConfirmInput.Password)
        {
            ShowError("The two passwords don't match.");
            return;
        }

        AccountButton.IsEnabled = false;
        try
        {
            if (_creatingAccount) await Online.RegisterAsync(url, username, password, InviteBox.Text.Trim());
            else await Online.SignInAsync(url, username, password);
            PasswordInput.Password = ConfirmInput.Password = InviteBox.Text = "";
            AccountPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
        finally
        {
            AccountButton.IsEnabled = true;
        }
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        string url = Online.Client.ServerUrl ?? ServerBox.Text.Trim();
        await Online.SignOutAsync(url);
        UpdateState(Online.Client.State);
        await ShowAccountPanelAsync(url, "Signed out. Sign in again whenever you like.");
    }

    private async void Invite_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;
        try
        {
            InviteDto invite = await Online.CreateInviteAsync(Online.Client.ServerUrl ?? ServerBox.Text.Trim());
            InviteCodeText.Text = invite.Code;
            InviteNote.Text = (invite.ExpiresAt is DateTimeOffset expires ? $"Works once, until {expires.LocalDateTime:MMMM d}. " : "Works once. ")
                + "Your friend enters it under Create an account, with this server's address.";
            InvitePanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    /// <summary>Admins: saves a copy of the server's whole database on this PC (a backup that survives losing the server).</summary>
    private async void ServerBackup_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = DoneBar.IsOpen = false;
        string url = Online.Client.ServerUrl ?? ServerBox.Text.Trim();
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Gambit server backup {DateTime.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add("Server database", new List<string> { ".db" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Window));
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file == null) return;
        try
        {
            await Online.DownloadServerBackupAsync(url, file.Path);
            Log.Info("Server database downloaded");
            DoneBar.Message = $"Saved a copy of the server's database to {file.Path}.";
            DoneBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    // ------------------------------------------------------------------ friends

    private void ShowFriends()
    {
        FriendsList.Children.Clear();
        FriendsDto? f = Online.Friends;
        bool none = f == null || f.Friends.Count + f.Incoming.Count + f.Outgoing.Count == 0;
        NoFriendsText.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        if (f == null) return;
        foreach (string name in f.Incoming)
            FriendsList.Children.Add(FriendRow(name, "wants to be your friend", null,
                ("Accept", () => AddFriendAsync(name)), ("Decline", () => RemoveFriendAsync(name))));
        foreach (FriendDto friend in f.Friends)
            FriendsList.Children.Add(FriendRow(friend.Username, friend.Status switch { "online" => "Online", "playing" => "Playing a game", _ => "Offline" },
                friend.Status, friend.Status == "online" ? ("Challenge", () => ChallengeFriendAsync(friend.Username)) : null,
                ("Remove", () => ConfirmRemoveAsync(friend.Username))));
        foreach (string name in f.Outgoing)
            FriendsList.Children.Add(FriendRow(name, "hasn't answered yet", null, null, ("Cancel", () => RemoveFriendAsync(name))));
    }

    /// <summary>A friend (or request) with its status and up to two buttons.</summary>
    private static Grid FriendRow(string name, string status, string? presence,
        (string Label, Func<Task> Run)? first, (string Label, Func<Task> Run)? second)
    {
        var grid = new Grid { ColumnSpacing = 10, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string dot = presence switch { "online" => "#2E7D32", "playing" => "#EF6C00", "offline" => "#8A8A8A", _ => "#5C6BC0" };
        grid.Children.Add(new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Fill = Ui.Brush(dot) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = status, Opacity = 0.7, FontSize = 12 });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        int column = 2;
        foreach (var action in new[] { first, second })
        {
            if (action is (string label, Func<Task> run))
            {
                var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(button, $"{label} {name}");
                button.Click += async (_, _) => await run();
                Grid.SetColumn(button, column);
                grid.Children.Add(button);
            }
            column++;
        }
        return grid;
    }

    private async void AddFriend_Click(object sender, RoutedEventArgs e) => await AddFriendAsync(FriendNameBox.Text.Trim());

    private async void FriendNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await AddFriendAsync(FriendNameBox.Text.Trim());
    }

    private async Task AddFriendAsync(string name)
    {
        if (name.Length == 0) return;
        try
        {
            await Online.AddFriendAsync(name);
            FriendNameBox.Text = "";
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private async Task RemoveFriendAsync(string name)
    {
        try
        {
            await Online.RemoveFriendAsync(name);
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private async Task ConfirmRemoveAsync(string name)
    {
        var dialog = new ContentDialog
        {
            Title = $"Remove {name} from your friends?",
            Content = "You can add each other again any time.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await Dialogs.ShowAsync(dialog, XamlRoot) == ContentDialogResult.Primary) await RemoveFriendAsync(name);
    }

    private async Task ChallengeFriendAsync(string name)
    {
        string tc = ChallengeTime.SelectedItem as string ?? "10+0";
        string color = ChallengeColor.SelectedIndex switch { 1 => "white", 2 => "black", _ => "random" };
        try
        {
            await Online.ChallengeFriendAsync(name, tc, color);
            DoneBar.Message = $"Challenge sent to {name} ({tc.Replace("+", " | ")}). The game starts when they accept.";
            DoneBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private void CopyInvite_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(InviteCodeText.Text);
        Clipboard.SetContent(package);
    }

    private async Task SeekAsync(string tc)
    {
        if (!EnsureConnected()) return;
        try
        {
            App.Settings.Current.LastOnlineTimeControl = tc;
            SeekingText.Text = $"Looking for an opponent ({tc.Replace("+", " | ")})…";
            SeekingPanel.Visibility = Visibility.Visible;
            await Online.Client.SeekAsync(OnlineService.ParseTimeControl(tc));
        }
        catch (Exception ex)
        {
            SeekingPanel.Visibility = Visibility.Collapsed;
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private async void CancelSeek_Click(object sender, RoutedEventArgs e)
    {
        SeekingPanel.Visibility = Visibility.Collapsed;
        try
        {
            await Online.Client.CancelSeekAsync();
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private async void CreateChallenge_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;
        try
        {
            string tc = ChallengeTime.SelectedItem as string ?? "10+0";
            string color = ChallengeColor.SelectedIndex switch { 1 => "white", 2 => "black", _ => "random" };
            ChallengeDto ch = await Online.Client.CreateChallengeAsync(OnlineService.ParseTimeControl(tc), color);
            CodeText.Text = ch.Code;
            CodePanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(CodeText.Text);
        Clipboard.SetContent(package);
    }

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;
        try
        {
            if (!await Online.Client.AcceptChallengeAsync(JoinBox.Text.Trim()))
                ShowError("That code isn't valid (or the game was already taken).");
        }
        catch (Exception ex)
        {
            ShowError(OnlineClient.ServerMessage(ex));
        }
    }

    /// <summary>Clears old errors and refuses lobby actions until the connection is up.</summary>
    private bool EnsureConnected()
    {
        ErrorBar.IsOpen = false;
        if (Online.IsConnected) return true;
        ShowError("Connect to a server first.");
        return false;
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
