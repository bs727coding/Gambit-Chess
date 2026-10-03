using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Engine.Bots;
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
    public string RatingText => Profile.IsMaxStrength ? "Max strength" : $"{Profile.Rating}";
    public SolidColorBrush AvatarBrush { get; } = Ui.Brush(profile.Color);
    public string RecordText => record.Games == 0 ? "Not played yet" : $"{record.Wins}W · {record.Losses}L · {record.Draws}D";
    public Visibility BeatenVisibility => record.Wins > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Used by screen readers (UI Automation name of the grid item).</summary>
    public override string ToString() => $"{Name}, {RatingText}";
}

public sealed partial class PlayPage : Page
{
    /// <summary>Navigation parameter that forces the opponent picker even if a game is in progress.</summary>
    public const string ChooseParameter = "choose";

    private static readonly (string Key, string Label, TimeControl Control)[] TimeControls =
    [
        ("unlimited", "Unlimited — no clock", TimeControl.Unlimited),
        ("1+0", "1 min · Bullet", TimeControl.Minutes(1)),
        ("3+2", "3 | 2 · Blitz", TimeControl.Minutes(3, 2)),
        ("5+0", "5 min · Blitz", TimeControl.Minutes(5)),
        ("10+0", "10 min · Rapid", TimeControl.Minutes(10)),
        ("15+10", "15 | 10 · Rapid", TimeControl.Minutes(15, 10)),
        ("30+0", "30 min · Classical", TimeControl.Minutes(30)),
    ];

    private List<BotCardModel> _cards = [];
    private string _color = "white";
    private bool _loading;

    public PlayPage()
    {
        InitializeComponent();
        foreach (var tc in TimeControls) TimeControlBox.Items.Add(tc.Label);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loading = true;
        AppSettings s = App.Settings.Current;

        _cards = BotRoster.Bots.Select(b => new BotCardModel(b, App.Profile.RecordAgainst(b.Id))).ToList();
        BotGrid.ItemsSource = _cards;
        BotCardModel selected = _cards.FirstOrDefault(c => c.Profile.Id == s.LastBotId) ?? _cards[0];
        BotGrid.SelectedItem = selected;
        ShowDetails(selected);

        _color = s.LastColor is "white" or "black" or "random" ? s.LastColor : "white";
        UpdateColorToggles();
        int tcIndex = Array.FindIndex(TimeControls, t => t.Key == s.LastTimeControl);
        TimeControlBox.SelectedIndex = tcIndex < 0 ? 0 : tcIndex;
        TakebacksSwitch.IsOn = s.AllowTakebacks;

        ResumeBar.IsOpen = GamePage.HasActiveGame;
        _loading = false;
    }

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
        DetailAvatar.Children.Add(Ui.Avatar(b.Monogram, b.Color, 72));
        DetailName.Text = b.Name;
        DetailRating.Text = b.IsMaxStrength ? "Full engine strength" : $"Rating ≈ {b.Rating}";
        // A style only shows in play for bots that choose among several candidate moves.
        DetailStyle.Text = b.Style == BotStyle.Balanced || b.Candidates <= 1 || b.Temperature <= 0 ? "" : b.Style switch
        {
            BotStyle.Aggressive => "Aggressive style: loves checks and attacks",
            BotStyle.Solid => "Solid style: safety first",
            _ => "Tricky style: likes to set threats",
        };
        DetailStyle.Visibility = DetailStyle.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailTagline.Text = $"“{b.Tagline}”";
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

    private TimeControl SelectedTimeControl =>
        TimeControlBox.SelectedIndex >= 0 ? TimeControls[TimeControlBox.SelectedIndex].Control : TimeControl.Unlimited;

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (BotGrid.SelectedItem is not BotCardModel card) return;
        Color human = _color switch
        {
            "black" => Color.Black,
            "random" => Random.Shared.Next(2) == 0 ? Color.White : Color.Black,
            _ => Color.White,
        };
        var setup = new GameSetup(card.Profile, human, SelectedTimeControl, TakebacksSwitch.IsOn);
        App.Window.Navigate(typeof(GamePage), setup, "play");
    }

    private void HotSeat_Click(object sender, RoutedEventArgs e)
    {
        var setup = new GameSetup(null, Color.White, SelectedTimeControl, AllowTakebacks: true);
        App.Window.Navigate(typeof(GamePage), setup, "play");
    }

    private void Resume_Click(object sender, RoutedEventArgs e) => App.Window.Navigate(typeof(GamePage), null, "play");

    private void Online_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("online");
}
