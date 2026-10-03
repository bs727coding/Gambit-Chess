using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Engine.Bots;
using Gambit.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Gambit.App.Pages;

public sealed partial class HomePage : Page
{
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

        ContinueBar.IsOpen = GamePage.HasActiveGame;

        BotProfile next = App.Profile.NextBot();
        PlayCardText.Text = p.GamesPlayed > 0
            ? $"Suggested next: {next.Name} ({next.RatingText})."
            : next == BotRoster.Bots[0]
                ? $"Start with {next.Name} ({next.RatingText}) — a gentle first opponent."
                : $"Start with {next.Name} ({next.RatingText}) — about your level.";

        PuzzleProfile pz = p.Puzzles;
        PuzzleCardText.Text = pz.Attempts == 0
            ? "Rated tactics, Puzzle Rush and a daily puzzle."
            : $"Puzzle rating {Math.Round(pz.Rating):0} · {pz.Solved} solved. Keep the streak going!";
        int due = App.Profile.DueReviews().Count;
        LessonsCardText.Text = due == 0
            ? "Interactive lessons: the basics, checkmates, tactics, openings and endgames."
            : due == 1 ? "1 opening line to review today." : $"{due} opening lines to review today.";
        StatGames.Text = p.GamesPlayed.ToString();
        StatRecord.Text = $"{p.Wins} · {p.Losses} · {p.Draws}";
        BotProfile? best = BotRoster.Bots.Where(b => App.Profile.RecordAgainst(b.Id).Wins > 0).LastOrDefault();
        StatBestBot.Text = best == null ? "—" : $"{best.Name} ({best.RatingText})";
        StatStreak.Text = p.BestWinStreak.ToString();

        BuildRecentGames(p);
    }

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

    private void Continue_Click(object sender, RoutedEventArgs e) => App.Window.Navigate(typeof(GamePage), null, "play");

    private void PlayCard_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Current.LastBotId = App.Profile.NextBot().Id;
        App.Window.NavigateTo("play", PlayPage.ChooseParameter);
    }

    private void Puzzles_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("puzzles");
    private void Learn_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("learn");
    private void Analysis_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("analysis");
}
