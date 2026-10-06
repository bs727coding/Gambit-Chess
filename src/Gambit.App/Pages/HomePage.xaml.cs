using Gambit.App.Controls;
using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Core.Board;
using Gambit.Core.Notation;
using Gambit.Core.Puzzles;
using Gambit.Engine.Bots;
using Gambit.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gambit.App.Pages;

/// <summary>
/// The start page: your game in progress (or the suggested next bot) and today's puzzle, each on a
/// small board, shortcuts to puzzles, lessons and analysis, your progress and recent games.
/// </summary>
public sealed partial class HomePage : Page
{
    private BotProfile? _nextBot;

    public HomePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        PlayerProfile p = App.Profile.Profile;
        int hour = DateTime.Now.Hour;
        string part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        Greeting.Text = $"{part}, {p.Name}";
        SubGreeting.Text = p.GamesPlayed > 0
            ? "Ready for another game?"
            : p.Experience == ExperienceLevel.NewToChess && p.Lessons.Count == 0
                ? "Welcome to Gambit. New to chess? The Basics lessons show how every piece moves."
                : "Welcome to Gambit. Start with a bot at your level, or explore a position on the analysis board.";

        UpdateService.Instance.Changed += ShowUpdate;
        ShowUpdate();
        ShowNextGame(p);
        ShowDaily(p.Puzzles);

        PuzzleProfile pz = p.Puzzles;
        PuzzleCardText.Text = pz.Attempts == 0 ? "Rated tactics and Puzzle Rush" : $"Rating {Math.Round(pz.Rating):0} · {pz.Solved:N0} solved";
        int due = App.Profile.DueReviews().Count;
        var lessons = Gambit.Core.Lessons.LessonCatalog.AllLessons.ToList();
        LessonsCardText.Text = due == 0
            ? $"{lessons.Count(l => p.Lessons.ContainsKey(l.Key))} of {lessons.Count} completed"
            : due == 1 ? "1 opening line to review today" : $"{due} opening lines to review today";

        StatGames.Text = p.GamesPlayed.ToString();
        StatRecord.Text = $"{p.Wins} · {p.Losses} · {p.Draws}";
        BotProfile? best = BotRoster.Bots.Where(b => App.Profile.RecordAgainst(b.Id).Wins > 0).LastOrDefault();
        StatBestBot.Text = best == null ? "—" : $"{best.Name} ({best.RatingText})";
        StatStreak.Text = p.BestWinStreak.ToString();

        BuildRecentGames(p);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        UpdateService.Instance.Changed -= ShowUpdate;
    }

    // ------------------------------------------------------------------ next game

    /// <summary>The game in progress, or else the suggested next bot, on a small board.</summary>
    private void ShowNextGame(PlayerProfile p)
    {
        NextGameAvatar.Children.Clear();
        if (GamePage.HasActiveGame)
        {
            _nextBot = null;
            SavedGame? saved = GamePage.ActiveGameSnapshot();
            (Position pos, Move last) = Replay(saved);
            bool humanIsBlack = saved?.HumanColor == "black";
            ShowBoard(NextGameBoardHost, pos, last, humanIsBlack);
            NextGameLabel.Text = "Your game in progress";
            if (saved?.BotId is string id)
            {
                BotProfile bot = BotRoster.Get(id);
                NextGameAvatar.Children.Add(Ui.Avatar(bot.Monogram, bot.Color, 36));
                NextGameTitle.Text = $"vs {bot.Name}";
            }
            else
            {
                NextGameAvatar.Children.Add(new FontIcon { Glyph = "", FontSize = 20 });
                NextGameTitle.Text = saved == null ? "Game in progress" : $"{saved.WhiteName ?? "White"} vs {saved.BlackName ?? "Black"}";
            }
            NextGameDetail.Text = $"{pos.SideToMove.Name()} to move · move {pos.FullmoveNumber}";
            NextGameButton.Content = "Continue";
            ChooseButton.Visibility = Visibility.Collapsed;
            return;
        }

        BotProfile next = App.Profile.NextBot();
        _nextBot = next;
        ShowBoard(NextGameBoardHost, Position.Start(), Move.None, flipped: false);
        NextGameLabel.Text = p.GamesPlayed > 0 ? "Suggested next" : "Your first opponent";
        NextGameAvatar.Children.Add(Ui.Avatar(next.Monogram, next.Color, 36));
        NextGameTitle.Text = $"{next.Name} · {next.RatingText}";
        AppSettings s = App.Settings.Current;
        string side = s.LastColor switch { "black" => "Black", "random" => "a random color", _ => "White" };
        NextGameDetail.Text = $"{TimeControlChoices.All[TimeControlChoices.IndexOf(s.LastTimeControl)].Label} · you play {side}";
        NextGameButton.Content = $"Play {next.Name}";
        ChooseButton.Visibility = Visibility.Visible;
    }

    /// <summary>The saved game's position and last move (the start position when there is none to read).</summary>
    private static (Position Position, Move Last) Replay(SavedGame? saved)
    {
        Position pos = saved?.StartFen is string fen && Fen.TryParse(fen, out Position? start, out _) && start != null ? start : Position.Start();
        Move last = Move.None;
        foreach (string uci in saved?.Moves ?? [])
        {
            try
            {
                last = Uci.Parse(pos, uci);
                pos.MakeMove(last);
            }
            catch (Exception)
            {
                break; // a damaged file: show what could be read
            }
        }
        return (pos, last);
    }

    private static void ShowBoard(Grid host, Position pos, Move last, bool flipped)
    {
        var board = new ChessBoardControl { Interaction = BoardInteraction.None, IsHitTestVisible = false };
        board.ApplyUserSettings();
        board.ShowCoordinates = false;
        board.Flipped = flipped;
        board.SetPosition(pos, last);
        host.Children.Clear();
        host.Children.Add(board);
    }

    private void NextGame_Click(object sender, RoutedEventArgs e)
    {
        if (_nextBot is BotProfile bot) PlayPage.StartBotGame(bot);
        else App.Window.Navigate(typeof(GamePage), null, "play");
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Current.LastPlayMode = "computer";
        App.Window.NavigateTo("play", PlayPage.ChooseParameter);
    }

    // ------------------------------------------------------------------ daily puzzle

    private void ShowDaily(PuzzleProfile pz)
    {
        Puzzle? daily = PuzzleService.Instance.Daily(DateOnly.FromDateTime(DateTime.Now));
        bool done = pz.LastDailyDate == DateTime.Now.ToString("yyyy-MM-dd");
        string streak = pz.DailyStreak > 0 ? $" · streak {pz.DailyStreak} day{(pz.DailyStreak == 1 ? "" : "s")}" : "";
        if (daily == null)
        {
            DailyTitle.Text = "No puzzle today";
            DailyDetail.Text = "";
            DailyButton.Visibility = Visibility.Collapsed;
            return;
        }
        Position start = daily.StartPosition();
        var setup = Position.FromFen(daily.Fen);
        ShowBoard(DailyBoardHost, start, Uci.Parse(setup, daily.Moves[0]), flipped: daily.SolverColor == Color.Black);
        DailyTitle.Text = done ? "Solved today" : $"{daily.SolverColor.Name()} to play";
        DailyDetail.Text = done ? $"Come back tomorrow{streak}" : $"Rated about {daily.Rating}{streak}";
        DailyButton.Content = done ? "Play it again" : "Solve";
    }

    private void Daily_Click(object sender, RoutedEventArgs e) =>
        App.Window.Navigate(typeof(PuzzleSolvePage), new PuzzleRequest(PuzzleMode.Daily), "puzzles");

    // ------------------------------------------------------------------ the rest

    private void BuildRecentGames(PlayerProfile p)
    {
        RecentList.Children.Clear();
        if (p.RecentGames.Count == 0)
        {
            RecentList.Children.Add(new TextBlock
            {
                Text = "No games yet. Your finished games will show up here.",
                Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
            });
            return;
        }
        foreach (GameRecord g in p.RecentGames.Take(6)) RecentList.Children.Add(GameRows.Create(g));
    }

    private void ShowUpdate()
    {
        UpdateBar.IsOpen = UpdateService.Instance.Available != null;
        UpdateBar.Message = $"Gambit {UpdateService.Instance.AvailableVersion} is ready to install.";
        UpdateBarButton.IsEnabled = true;
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateService.WaitReason is string wait)
        {
            UpdateBar.Message = wait;
            return;
        }
        UpdateBarButton.IsEnabled = false;
        try
        {
            await UpdateService.Instance.UpdateAndRestartAsync(percent => DispatcherQueue.TryEnqueue(() => UpdateBar.Message = $"Downloading the update… {percent}%"));
        }
        catch (Exception ex)
        {
            Log.Warn($"Update failed: {ex.Message}");
            UpdateBar.Message = $"The update didn't work: {ex.Message}";
            UpdateBarButton.IsEnabled = true;
        }
    }

    private void Puzzles_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("puzzles");
    private void Learn_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("learn");
    private void Analysis_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("analysis");
}
