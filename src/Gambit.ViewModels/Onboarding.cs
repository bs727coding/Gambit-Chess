using Gambit.Engine.Bots;

namespace Gambit.ViewModels;

/// <summary>How much chess the player has played, asked once at first launch.</summary>
public enum ExperienceLevel
{
    NewToChess,
    Beginner,
    Casual,
    Club,
    Strong,
}

/// <summary>One choice on the welcome screen and what it sets up.</summary>
/// <param name="BotId">The first bot to suggest (roughly the player's strength).</param>
/// <param name="PuzzleRating">Starting puzzle rating; it still moves fast for the first few dozen puzzles.</param>
public sealed record ExperienceOption(ExperienceLevel Level, string Title, string Description, string BotId, int PuzzleRating)
{
    public BotProfile Bot => BotRoster.Get(BotId);
}

/// <summary>First-run setup: the player's name and level pick a first opponent and a starting puzzle rating.</summary>
public static class Onboarding
{
    public static IReadOnlyList<ExperienceOption> Options { get; } =
    [
        new(ExperienceLevel.NewToChess, "New to chess", "I'm still learning how the pieces move.", "acorn", 600),
        new(ExperienceLevel.Beginner, "Beginner", "I know the rules but haven't played much.", "clover", 900),
        new(ExperienceLevel.Casual, "Casual player", "I play now and then and know some tactics.", "ember", 1200),
        new(ExperienceLevel.Club, "Club player", "I play regularly and know my openings.", "gale", 1600),
        new(ExperienceLevel.Strong, "Strong player", "I'm rated 1800 or more.", "iris", 2000),
    ];

    public static ExperienceOption For(ExperienceLevel level) => Options.First(o => o.Level == level);

    /// <summary>
    /// Whether to show the welcome screen: never finished it, and nothing played yet. Profiles from
    /// before onboarding existed (with games, puzzles or lessons) skip it.
    /// </summary>
    public static bool IsNeeded(bool onboarded, int gamesPlayed, int puzzleAttempts, int lessonsDone) =>
        !onboarded && gamesPlayed == 0 && puzzleAttempts == 0 && lessonsDone == 0;

    /// <summary>One line on what the choice sets up, shown under the options.</summary>
    public static string Summary(ExperienceOption option) => option.Level == ExperienceLevel.NewToChess
        ? $"We'll suggest the Basics lessons first, then {option.Bot.Name} ({option.Bot.RatingText}), a gentle first opponent. Puzzles start at {option.PuzzleRating}."
        : $"Your first suggested opponent is {option.Bot.Name} ({option.Bot.RatingText}), and puzzles start at {option.PuzzleRating}. Both adjust as you play.";

    /// <summary>
    /// The bot to suggest next: the weakest one the player hasn't beaten, no weaker than the one
    /// their level started them on.
    /// </summary>
    public static BotProfile NextBot(ExperienceLevel? level, Func<string, bool> beaten)
    {
        int floor = level is ExperienceLevel l ? For(l).Bot.Rating : 0;
        return BotRoster.Bots.FirstOrDefault(b => b.Rating >= floor && !beaten(b.Id)) ?? BotRoster.Bots[^1];
    }
}
