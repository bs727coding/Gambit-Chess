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
    public ProfilePage()
    {
        InitializeComponent();
        Ui.StretchTiles(AchievementGrid, 250);
    }

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

        BuildPuzzles(p.Puzzles);
        BuildOpenings(p);
        BuildAchievements();
    }

    private void BuildPuzzles(PuzzleProfile pz)
    {
        var (rank, _, _) = PuzzleService.Rank(pz.Rating);
        PuzzleRatingText.Text = $"{Math.Round(pz.Rating):0}{(pz.Deviation > 110 ? "?" : "")} · {rank}";
        PuzzleStatsText.Text = pz.Attempts == 0
            ? "No puzzles attempted yet."
            : $"{pz.Solved:N0} solved of {pz.Attempts:N0} ({100.0 * pz.Solved / pz.Attempts:0}%) · best streak {pz.BestStreak}";
        RushStatsText.Text = $"Rush best: 3 min {pz.BestRush3} · 5 min {pz.BestRush5} · Survival {pz.BestSurvival} · Daily streak {pz.DailyStreak}";
        PuzzleChart.SetValues(pz.History.TakeLast(150).Select(h => h.Rating).ToList());
    }

    private void BuildOpenings(PlayerProfile p)
    {
        OpeningsList.Children.Clear();
        var groups = p.RecentGames.Where(g => g.Opening != null && g.PlayerColor != "both")
            .GroupBy(g => g.Opening!)
            .OrderByDescending(g => g.Count())
            .Take(8)
            .ToList();
        if (groups.Count == 0)
        {
            OpeningsList.Children.Add(new TextBlock { Text = "Play a few games to see which openings you play most.", Opacity = 0.7, Margin = new Thickness(8) });
            return;
        }
        foreach (var grp in groups)
        {
            int w = grp.Count(g => g.Outcome == "win"), d = grp.Count(g => g.Outcome == "draw"), l = grp.Count(g => g.Outcome == "loss");
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 6, 8, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = grp.Key, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            string games = grp.Count() == 1 ? "1 game" : $"{grp.Count()} games";
            var stats = new TextBlock { Text = $"{games} · {w}W · {l}L · {d}D", Opacity = 0.75 };
            Grid.SetColumn(stats, 1);
            row.Children.Add(stats);
            OpeningsList.Children.Add(row);
        }
    }

    private void BuildAchievements()
    {
        AchievementService svc = AchievementService.Instance;
        var all = svc.All;
        AchievementSummary.Text = $"{svc.UnlockedCount} of {all.Count} unlocked";
        AchievementGrid.Children.Clear();
        foreach (AchievementDef a in all.OrderByDescending(a => svc.IsUnlocked(a.Id)).ThenBy(a => a.Category))
        {
            bool unlocked = svc.IsUnlocked(a.Id);
            string color = !unlocked ? "#7A7A7A" : a.Tier switch
            {
                AchievementTier.Gold => "#D4A017",
                AchievementTier.Silver => "#8E9AA6",
                _ => "#B87333",
            };
            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(6), Background = Ui.NeutralFill(18), Opacity = unlocked ? 1 : 0.55 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Ui.Brush(color),
                Child = new FontIcon { Glyph = unlocked ? a.Glyph : "\uE72E", FontSize = 17, Foreground = new SolidColorBrush(Colors.White) },
            });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = a.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = a.Description, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            ToolTipService.SetToolTip(grid, unlocked ? $"Unlocked {App.Profile.Profile.Achievements[a.Id]:MMM d, yyyy}" : $"Locked · {a.Tier} · {a.Category}");
            AchievementGrid.Children.Add(grid);
        }
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
            Text = rec.Games == 0 ? "Not played" : $"{rec.Wins}W · {rec.Losses}L · {rec.Draws}D",
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
