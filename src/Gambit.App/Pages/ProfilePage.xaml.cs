using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Engine.Bots;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Gambit.App.Pages;

public sealed partial class ProfilePage : Page
{
    public ProfilePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Build();
    }

    private void Build()
    {
        PlayerProfile p = App.Profile.Profile;
        AvatarHost.Children.Clear();
        AvatarHost.Children.Add(Ui.Avatar(p.Name.Length > 0 ? p.Name[..1].ToUpperInvariant() : "?", "#0F6CBD", 80));
        NameText.Text = p.Name;
        SinceText.Text = $"Playing since {p.Created:MMMM d, yyyy}";

        GamesText.Text = p.GamesPlayed.ToString();
        WinRateText.Text = p.GamesPlayed == 0 ? "—" : $"{100.0 * p.Wins / p.GamesPlayed:0}%";
        RecordText.Text = $"{p.Wins} · {p.Losses} · {p.Draws}";
        StreakText.Text = $"{p.CurrentWinStreak} ({p.BestWinStreak})";

        BotRecords.Children.Clear();
        foreach (BotProfile bot in BotRoster.Bots) BotRecords.Children.Add(BotRow(bot, App.Profile.RecordAgainst(bot.Id)));

        History.Children.Clear();
        if (p.RecentGames.Count == 0)
        {
            History.Children.Add(new TextBlock { Text = "No games yet.", Opacity = 0.7 });
        }
        foreach (GameRecord g in p.RecentGames.Take(30)) History.Children.Add(GameRows.Create(g));
    }

    private static Grid BotRow(BotProfile bot, BotRecord rec)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 6, 8, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

        row.Children.Add(Ui.Avatar(bot.Monogram, bot.Color, 30));

        var name = new TextBlock { Text = $"{bot.Name}  ({bot.RatingText})", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        // Proportional win/draw/loss bar.
        var bar = new Grid { Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Background = Ui.NeutralFill(30) };
        if (rec.Games > 0)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(rec.Wins, 0.0001), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(rec.Draws, 0.0001), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(rec.Losses, 0.0001), GridUnitType.Star) });
            AddSegment(bar, 0, ColorHelper.FromArgb(255, 67, 160, 71));
            AddSegment(bar, 1, ColorHelper.FromArgb(255, 158, 158, 158));
            AddSegment(bar, 2, ColorHelper.FromArgb(255, 229, 57, 53));
        }
        Grid.SetColumn(bar, 2);
        row.Children.Add(bar);

        var text = new TextBlock
        {
            Text = rec.Games == 0 ? "Not played" : $"{rec.Wins}W  {rec.Draws}D  {rec.Losses}L",
            Opacity = 0.75,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 3);
        row.Children.Add(text);
        return row;
    }

    private static void AddSegment(Grid bar, int column, Windows.UI.Color color)
    {
        var r = new Border { Background = new SolidColorBrush(color) };
        Grid.SetColumn(r, column);
        bar.Children.Add(r);
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { Text = App.Profile.Profile.Name, MaxLength = 24, SelectionStart = 0 };
        var dialog = new ContentDialog
        {
            Title = "Display name",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await Dialogs.ShowAsync(dialog, XamlRoot) == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            App.Profile.Profile.Name = box.Text.Trim();
            App.Profile.Save();
            Build();
        }
    }
}
