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

/// <summary>Online lobby: connect to a server, quick pairing by time control, friend challenges.</summary>
public sealed partial class OnlinePage : Page
{
    private static readonly string[] TimeControls = ["1+0", "2+1", "3+0", "3+2", "5+0", "5+3", "10+0", "10+5", "15+10", "30+0"];
    private OnlineService Online => OnlineService.Instance;

    public OnlinePage()
    {
        InitializeComponent();
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
        UpdateState(Online.Client.State);
        if (Online.Client.Stats is LobbyStatsDto stats) OnStatsChanged(stats);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Online.StateChanged -= OnStateChanged;
        Online.StatsChanged -= OnStatsChanged;
        Online.GameOpened -= OnGameOpened;
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
        (string text, string color) = state switch
        {
            OnlineState.Connected => ($"Connected as {Online.Client.Me?.Name ?? App.Profile.Profile.Name}" +
                                      (Online.Client.Me is PlayerDto me ? $" ({me.Rating}{(me.Provisional ? "?" : "")})" : ""), "#2E7D32"),
            OnlineState.Connecting => ("Connecting…", "#F7C045"),
            OnlineState.Reconnecting => ("Connection lost — reconnecting…", "#F7C045"),
            _ => ("Not connected", "#8A8A8A"),
        };
        StatusText.Text = text;
        StatusDot.Fill = new SolidColorBrush(Ui.ParseColor(color));
        bool connected = state == OnlineState.Connected;
        LobbyGrid.IsHitTestVisible = WatchCard.IsHitTestVisible = connected;
        LobbyGrid.Opacity = WatchCard.Opacity = connected ? 1 : 0.45;
        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        ConnectButton.IsEnabled = state is OnlineState.Connected or OnlineState.Disconnected;
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
            ShowError(ex.Message);
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
            ShowError(ex.Message);
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
        string url = ServerBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            ShowError("Enter a server address like http://192.168.1.20:5080 or https://chess.example.com.");
            return;
        }
        try
        {
            await Online.ConnectAsync(url);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't connect: {ex.Message}");
        }
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
            ShowError(ex.Message);
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
            ShowError(ex.Message);
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
            ShowError(ex.Message);
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
            ShowError(ex.Message);
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
