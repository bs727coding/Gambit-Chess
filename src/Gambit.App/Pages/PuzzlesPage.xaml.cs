using Gambit.App.Services;
using Gambit.Core.Puzzles;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gambit.App.Pages;

/// <summary>Puzzle hub: rating and rank, rated puzzles, daily puzzle, Puzzle Rush, theme and opening practice.</summary>
public sealed partial class PuzzlesPage : Page
{
    public PuzzlesPage()
    {
        InitializeComponent();
        Helpers.Ui.StretchTiles(OpeningGrid, 200);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        PuzzleService svc = PuzzleService.Instance;
        PuzzleProfile p = svc.Profile;
        int total = svc.All.Count;

        Subtitle.Text = total == 0
            ? "The puzzle collection is not installed in this build."
            : $"{total:N0} puzzles, from one-move tactics to deep combinations.";

        var (rank, floor, next) = PuzzleService.Rank(p.Rating);
        RankName.Text = $"{rank} rank";
        RankGlyph.Text = rank switch
        {
            "Pawn" => "♟",
            "Knight" => "♞",
            "Bishop" => "♝",
            "Rook" => "♜",
            "Queen" => "♛",
            _ => "♚",
        };
        RatingText.Text = $"{Math.Round(p.Rating):0}{(p.Deviation > 110 ? "?" : "")}";
        RankProgress.Value = Math.Clamp((p.Rating - floor) / Math.Max(1, next - floor), 0, 1);
        RankHint.Text = rank == "Grandmaster" ? "Top rank reached." : $"{Math.Max(0, next - (int)p.Rating)} points to the next rank";
        SolvedText.Text = p.Solved.ToString("N0");
        SuccessText.Text = p.Attempts == 0 ? "—" : $"{100.0 * p.Solved / p.Attempts:0}%";
        StreakText.Text = $"{p.CurrentStreak} ({p.BestStreak})";

        string today = DateTime.Now.ToString("yyyy-MM-dd");
        bool doneToday = p.LastDailyDate == today;
        DailyText.Text = doneToday
            ? $"Done for today. Daily streak: {p.DailyStreak} day{(p.DailyStreak == 1 ? "" : "s")}."
            : $"A new puzzle every day. Current streak: {p.DailyStreak} day{(p.DailyStreak == 1 ? "" : "s")}.";
        DailyButton.Content = doneToday ? "Play it again" : "Solve today's puzzle";

        Best3.Text = p.BestRush3 > 0 ? $"Best {p.BestRush3}" : "—";
        Best5.Text = p.BestRush5 > 0 ? $"Best {p.BestRush5}" : "—";
        BestSurvival.Text = p.BestSurvival > 0 ? $"Best {p.BestSurvival}" : "—";

        BuildThemes(p);
        BuildOpenings(p);
        SourceNote.Text = "Puzzles come from the Lichess puzzle database (CC0): positions from real games, each rated by how players fared with it.";
    }

    private void BuildThemes(PuzzleProfile p)
    {
        ThemeGroupsPanel.Children.Clear();
        foreach ((string title, string[] themes) in PuzzleThemes.PracticeGroups)
        {
            var tiles = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 200, ItemHeight = 70 };
            foreach (string theme in themes)
            {
                int count = PuzzleCatalog.CountWithTheme(theme);
                if (count > 0) tiles.Children.Add(ThemeTile(theme, count, p));
            }
            if (tiles.Children.Count == 0) continue;
            var group = new StackPanel { Spacing = 8 };
            group.Children.Add(new TextBlock { Text = title, FontSize = 13, Opacity = 0.75 });
            group.Children.Add(tiles);
            Helpers.Ui.StretchTiles(tiles, 200);
            ThemeGroupsPanel.Children.Add(group);
        }
    }

    /// <summary>The openings with enough puzzles, most common first.</summary>
    private void BuildOpenings(PuzzleProfile p)
    {
        OpeningGrid.Children.Clear();
        foreach ((string name, int count) in PuzzleCatalog.Openings(minimum: 50))
            OpeningGrid.Children.Add(Tile(name, count, p.Openings.GetValueOrDefault(name), new PuzzleRequest(PuzzleMode.Opening, Opening: name)));
        OpeningSection.Visibility = OpeningGrid.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Button ThemeTile(string theme, int count, PuzzleProfile p)
    {
        p.Themes.TryGetValue(theme, out ThemeStat? stat);
        return Tile(PuzzleThemes.Name(theme), count, stat, new PuzzleRequest(PuzzleMode.Theme, theme));
    }

    private static Button Tile(string title, int count, ThemeStat? stat, PuzzleRequest request)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string puzzles = count == 1 ? "1 puzzle" : $"{count:N0} puzzles";
        stack.Children.Add(new TextBlock
        {
            Text = stat is { Attempts: > 0 }
                ? $"{puzzles} · {100.0 * stat.Solved / stat.Attempts:0}% solved"
                : puzzles,
            FontSize = 12,
            Opacity = 0.7,
        });
        var button = new Button
        {
            Content = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(14, 8, 14, 8),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Practice {title}");
        button.Click += (_, _) => Start(request);
        return button;
    }

    private static void Start(PuzzleRequest request) => App.Window.Navigate(typeof(PuzzleSolvePage), request, "puzzles");

    private void Rated_Click(object sender, RoutedEventArgs e) => Start(new PuzzleRequest(PuzzleMode.Rated));
    private void Daily_Click(object sender, RoutedEventArgs e) => Start(new PuzzleRequest(PuzzleMode.Daily));
    private void Rush3_Click(object sender, RoutedEventArgs e) => Start(new PuzzleRequest(PuzzleMode.Rush3));
    private void Rush5_Click(object sender, RoutedEventArgs e) => Start(new PuzzleRequest(PuzzleMode.Rush5));
    private void Survival_Click(object sender, RoutedEventArgs e) => Start(new PuzzleRequest(PuzzleMode.Survival));
}
