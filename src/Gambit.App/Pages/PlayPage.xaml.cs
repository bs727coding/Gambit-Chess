using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Engine.Bots;
using Gambit.Online;
using Gambit.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Gambit.App.Pages;

/// <summary>Item shown in the bot grid.</summary>
public sealed class BotCardModel(BotProfile profile, BotRecord record)
{
    public BotProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Monogram => Profile.Monogram;
    public string RatingText => Profile.IsMaxStrength ? "Full strength" : $"{Profile.Rating}";
    public SolidColorBrush AvatarBrush { get; } = Ui.Brush(profile.Color);
    public string RecordText => record.Games == 0 ? "Not played yet" : $"{record.Wins}W · {record.Losses}L · {record.Draws}D";
    public Visibility BeatenVisibility => record.Wins > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Used by screen readers (UI Automation name of the grid item).</summary>
    public override string ToString() => $"{Name}, {RatingText}";
}

/// <summary>A group of bots in the grid: beginners, club players, experts, full strength.</summary>
public sealed class BotTier(string title, string range, IEnumerable<BotCardModel> bots) : List<BotCardModel>(bots)
{
    public string Title { get; } = title;
    public string Range { get; } = range;
}

/// <summary>
/// Starting a game: a bot (grouped by level, with the side, time control and takebacks), pass and play
/// on this PC (names, time control, board turning, takebacks), or the way to the online lobby.
/// </summary>
public sealed partial class PlayPage : Page
{
    /// <summary>Navigation parameter that forces the opponent picker even if a game is in progress.</summary>
    public const string ChooseParameter = "choose";

    private static IReadOnlyList<(string Key, string Label, TimeControl Control)> TimeControls => TimeControlChoices.All;

    private List<BotCardModel> _cards = [];
    private string _color = "white";
    private bool _loading;

    public PlayPage()
    {
        InitializeComponent();
        foreach (var tc in TimeControls)
        {
            TimeControlBox.Items.Add(tc.Label);
            PassTimeControlBox.Items.Add(tc.Label);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loading = true;
        AppSettings s = App.Settings.Current;

        _cards = BotRoster.Bots.Select(b => new BotCardModel(b, App.Profile.RecordAgainst(b.Id))).ToList();
        BotTiers.Source = new List<BotTier>
        {
            new("Beginner", "250 to 800", _cards.Where(c => !c.Profile.IsMaxStrength && c.Profile.Rating <= 800)),
            new("Club player", "1000 to 1600", _cards.Where(c => !c.Profile.IsMaxStrength && c.Profile.Rating is > 800 and <= 1600)),
            new("Expert", "1800 to 2400", _cards.Where(c => !c.Profile.IsMaxStrength && c.Profile.Rating > 1600)),
            new("Full strength", "the engine at full power", _cards.Where(c => c.Profile.IsMaxStrength)),
        }.Where(t => t.Count > 0).ToList();
        BotGrid.ItemsSource = BotTiers.View;
        BotCardModel selected = _cards.FirstOrDefault(c => c.Profile.Id == s.LastBotId) ?? _cards[0];
        BotGrid.SelectedItem = selected;
        ShowDetails(selected);

        _color = s.LastColor is "white" or "black" or "random" ? s.LastColor : "white";
        UpdateColorToggles();
        TimeControlBox.SelectedIndex = TimeControlChoices.IndexOf(s.LastTimeControl);
        TakebacksSwitch.IsOn = s.AllowTakebacks;

        WhiteNameBox.Text = s.LastWhiteName.Length > 0 ? s.LastWhiteName : App.Profile.Profile.Name;
        BlackNameBox.Text = s.LastBlackName.Length > 0 ? s.LastBlackName : "Guest";
        PassTimeControlBox.SelectedIndex = TimeControlChoices.IndexOf(s.PassAndPlayTimeControl);
        FlipSwitch.IsOn = s.PassAndPlayFlip;
        PassTakebacksSwitch.IsOn = s.PassAndPlayTakebacks;

        ShowOnlineStatus();
        ModeTabs.SelectedItem = s.LastPlayMode switch
        {
            "passandplay" => PassAndPlayTab,
            "online" => OnlineTab,
            _ => ComputerTab,
        };
        ShowMode();

        ResumeBar.IsOpen = GamePage.HasActiveGame;
        _loading = false;
    }

    // ------------------------------------------------------------------ modes

    private void ModeTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ShowMode();
        if (!_loading && sender.SelectedItem?.Tag is string mode) App.Settings.Current.LastPlayMode = mode;
    }

    private void ShowMode()
    {
        object? tab = ModeTabs.SelectedItem;
        ComputerView.Visibility = tab == ComputerTab || tab == null ? Visibility.Visible : Visibility.Collapsed;
        PassAndPlayView.Visibility = tab == PassAndPlayTab ? Visibility.Visible : Visibility.Collapsed;
        OnlineView.Visibility = tab == OnlineTab ? Visibility.Visible : Visibility.Collapsed;
        if (tab == OnlineTab) ShowOnlineStatus();
    }

    // ------------------------------------------------------------------ computer

    private void BotGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BotGrid.SelectedItem is not BotCardModel card) return;
        ShowDetails(card);
        if (!_loading) App.Settings.Current.LastBotId = card.Profile.Id;
    }

    private void ShowDetails(BotCardModel card)
    {
        BotProfile b = card.Profile;
        DetailAvatar.Children.Clear();
        DetailAvatar.Children.Add(Ui.Avatar(b.Monogram, b.Color, 48));
        DetailName.Text = b.Name;
        // A style only shows in play for bots that choose among several candidate moves.
        string style = b.Style == BotStyle.Balanced || b.Candidates <= 1 || b.Temperature <= 0 ? "" : b.Style switch
        {
            BotStyle.Aggressive => " · aggressive",
            BotStyle.Solid => " · solid",
            _ => " · tricky",
        };
        DetailRating.Text = (b.IsMaxStrength ? "Full engine strength" : $"Rated about {b.Rating}") + style;
        DetailBio.Text = b.Bio;
        DetailRecord.Text = card.RecordText == "Not played yet" ? "You haven't played this bot yet." : $"Your record: {card.RecordText}";
        PlayButtonText.Text = $"Play {b.Name}";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PlayButton, $"Play {b.Name}");
    }

    private void ColorToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton t && t.Tag is string tag) _color = tag;
        UpdateColorToggles();
        App.Settings.Current.LastColor = _color;
    }

    private void UpdateColorToggles()
    {
        WhiteToggle.IsChecked = _color == "white";
        RandomToggle.IsChecked = _color == "random";
        BlackToggle.IsChecked = _color == "black";
    }

    private void TimeControlBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && TimeControlBox.SelectedIndex >= 0)
            App.Settings.Current.LastTimeControl = TimeControls[TimeControlBox.SelectedIndex].Key;
    }

    private void TakebacksSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) App.Settings.Current.AllowTakebacks = TakebacksSwitch.IsOn;
    }

    private static TimeControl ChosenTimeControl(ComboBox box) =>
        box.SelectedIndex >= 0 ? TimeControls[box.SelectedIndex].Control : TimeControl.Unlimited;

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (BotGrid.SelectedItem is not BotCardModel card) return;
        Color human = _color switch
        {
            "black" => Color.Black,
            "random" => Random.Shared.Next(2) == 0 ? Color.White : Color.Black,
            _ => Color.White,
        };
        var setup = new GameSetup(card.Profile, human, ChosenTimeControl(TimeControlBox), TakebacksSwitch.IsOn);
        App.Window.Navigate(typeof(GamePage), setup, "play");
    }

    /// <summary>Starts a game against <paramref name="bot"/> with the side, time control and takebacks last chosen here.</summary>
    public static void StartBotGame(BotProfile bot)
    {
        AppSettings s = App.Settings.Current;
        Color human = s.LastColor switch
        {
            "black" => Color.Black,
            "random" => Random.Shared.Next(2) == 0 ? Color.White : Color.Black,
            _ => Color.White,
        };
        s.LastBotId = bot.Id;
        TimeControl tc = TimeControls[TimeControlChoices.IndexOf(s.LastTimeControl)].Control;
        App.Window.Navigate(typeof(GamePage), new GameSetup(bot, human, tc, s.AllowTakebacks), "play");
    }

    // ------------------------------------------------------------------ pass and play

    private void SwapNames_Click(object sender, RoutedEventArgs e) =>
        (WhiteNameBox.Text, BlackNameBox.Text) = (BlackNameBox.Text, WhiteNameBox.Text);

    private void StartPassAndPlay_Click(object sender, RoutedEventArgs e)
    {
        AppSettings s = App.Settings.Current;
        string white = Clean(WhiteNameBox.Text, "White"), black = Clean(BlackNameBox.Text, "Black");
        s.LastWhiteName = white;
        s.LastBlackName = black;
        s.PassAndPlayFlip = FlipSwitch.IsOn;
        s.PassAndPlayTakebacks = PassTakebacksSwitch.IsOn;
        if (PassTimeControlBox.SelectedIndex >= 0) s.PassAndPlayTimeControl = TimeControls[PassTimeControlBox.SelectedIndex].Key;
        var setup = new GameSetup(null, Color.White, ChosenTimeControl(PassTimeControlBox), PassTakebacksSwitch.IsOn) { WhiteName = white, BlackName = black };
        App.Window.Navigate(typeof(GamePage), setup, "play");
    }

    private static string Clean(string text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

    // ------------------------------------------------------------------ online

    private void ShowOnlineStatus()
    {
        OnlineService online = OnlineService.Instance;
        string server = App.Settings.Current.OnlineServerUrl;
        bool connected = online.IsConnected;
        OnlineDot.Fill = Ui.Brush(connected ? "#2E7D32" : "#8A8A8A");
        string? saved = OnlineCredentials.Get(server)?.Username;
        OnlineStatusText.Text = connected
            ? $"Signed in as {online.Client.Me?.Name ?? saved}"
            : saved != null ? $"Not connected · signed in as {saved}" : "Not connected";
        OnlineServerText.Text = connected && online.Client.Stats is LobbyStatsDto stats
            ? $"{server} · {stats.PlayersOnline} online · {stats.GamesInProgress} game{(stats.GamesInProgress == 1 ? "" : "s")} in progress"
            : server;
    }

    private void Resume_Click(object sender, RoutedEventArgs e) => App.Window.Navigate(typeof(GamePage), null, "play");

    private void Online_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("online");
}
