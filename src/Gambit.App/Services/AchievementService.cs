using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Lessons;
using Gambit.Engine.Bots;
using Gambit.Engine.Review;

namespace Gambit.App.Services;

public enum AchievementTier
{
    Bronze,
    Silver,
    Gold,
}

/// <summary>One achievement. <see cref="Check"/> tests the cumulative profile; event-based ones are unlocked explicitly.</summary>
public sealed record AchievementDef(string Id, string Title, string Description, string Glyph, string Category, AchievementTier Tier,
    Func<PlayerProfile, bool>? Check = null);

/// <summary>Defines all achievements, detects unlocks after games/puzzles/lessons/reviews and announces them.</summary>
public sealed class AchievementService
{
    public static AchievementService Instance { get; } = new();

    public event Action<AchievementDef>? Unlocked;

    public IReadOnlyList<AchievementDef> All { get; } = Build();

    private static PuzzleProfile Pz(PlayerProfile p) => p.Puzzles;

    private static List<AchievementDef> Build()
    {
        var list = new List<AchievementDef>
        {
            // Games
            new("first-game", "First moves", "Finish your first game.", "", "Games", AchievementTier.Bronze, p => p.GamesPlayed >= 1),
            new("first-win", "Victory!", "Win your first game.", "", "Games", AchievementTier.Bronze, p => p.Wins >= 1),
            new("games-10", "Regular", "Play 10 games.", "", "Games", AchievementTier.Bronze, p => p.GamesPlayed >= 10),
            new("games-50", "Dedicated", "Play 50 games.", "", "Games", AchievementTier.Silver, p => p.GamesPlayed >= 50),
            new("games-200", "Lifer", "Play 200 games.", "", "Games", AchievementTier.Gold, p => p.GamesPlayed >= 200),
            new("streak-3", "Hat trick", "Win 3 games in a row.", "", "Games", AchievementTier.Bronze, p => p.BestWinStreak >= 3),
            new("streak-5", "On fire", "Win 5 games in a row.", "", "Games", AchievementTier.Silver, p => p.BestWinStreak >= 5),
            new("streak-10", "Unstoppable", "Win 10 games in a row.", "", "Games", AchievementTier.Gold, p => p.BestWinStreak >= 10),
            new("checkmate", "Checkmate!", "Win a game by checkmate.", "", "Games", AchievementTier.Bronze),
            new("quick-mate", "Lightning strike", "Checkmate your opponent in 20 moves or fewer.", "", "Games", AchievementTier.Silver),
            new("draw", "Peace treaty", "Draw a game.", "", "Games", AchievementTier.Bronze, p => p.Draws >= 1),
            new("promotion", "Promotion", "Promote a pawn in a game.", "", "Games", AchievementTier.Bronze),
            new("en-passant", "En passant!", "Capture en passant in a game.", "", "Games", AchievementTier.Silver),
            new("underdog", "Giant slayer", "Beat a bot rated 2000 or higher.", "", "Games", AchievementTier.Gold),
            new("timed-win", "Beat the clock", "Win a timed game.", "", "Games", AchievementTier.Bronze),

            // Puzzles
            new("puzzle-1", "Puzzler", "Solve your first puzzle.", "", "Puzzles", AchievementTier.Bronze, p => Pz(p).Solved >= 1),
            new("puzzle-50", "Tactician", "Solve 50 puzzles.", "", "Puzzles", AchievementTier.Silver, p => Pz(p).Solved >= 50),
            new("puzzle-250", "Puzzle master", "Solve 250 puzzles.", "", "Puzzles", AchievementTier.Gold, p => Pz(p).Solved >= 250),
            new("puzzle-streak-10", "In the zone", "Solve 10 puzzles in a row.", "", "Puzzles", AchievementTier.Silver, p => Pz(p).BestStreak >= 10),
            new("puzzle-1300", "Sharp eye", "Reach a puzzle rating of 1300.", "", "Puzzles", AchievementTier.Bronze, p => Pz(p).Rating >= 1300 && Pz(p).Deviation < 150),
            new("puzzle-1600", "Eagle eye", "Reach a puzzle rating of 1600.", "", "Puzzles", AchievementTier.Silver, p => Pz(p).Rating >= 1600 && Pz(p).Deviation < 150),
            new("puzzle-2000", "All-seeing", "Reach a puzzle rating of 2000.", "", "Puzzles", AchievementTier.Gold, p => Pz(p).Rating >= 2000 && Pz(p).Deviation < 150),
            new("rush-10", "Rush hour", "Score 10 in Puzzle Rush (3 or 5 min).", "", "Puzzles", AchievementTier.Bronze, p => Math.Max(Pz(p).BestRush3, Pz(p).BestRush5) >= 10),
            new("rush-25", "Speed demon", "Score 25 in Puzzle Rush (3 or 5 min).", "", "Puzzles", AchievementTier.Gold, p => Math.Max(Pz(p).BestRush3, Pz(p).BestRush5) >= 25),
            new("survival-15", "Survivor", "Score 15 in Puzzle Survival.", "", "Puzzles", AchievementTier.Silver, p => Pz(p).BestSurvival >= 15),
            new("daily-7", "Daily habit", "Solve the daily puzzle 7 days in a row.", "", "Puzzles", AchievementTier.Silver, p => Pz(p).DailyStreak >= 7),

            // Lessons
            new("lesson-1", "Student", "Complete your first lesson.", "", "Lessons", AchievementTier.Bronze, p => p.Lessons.Count >= 1),
            new("perfect-lesson", "Perfectionist", "Complete a lesson without mistakes.", "", "Lessons", AchievementTier.Bronze, p => p.Lessons.Values.Any(l => l.Stars == 3)),
            new("all-lessons", "Graduate", "Complete every lesson.", "", "Lessons", AchievementTier.Gold,
                p => LessonCatalog.AllLessons.All(l => p.Lessons.ContainsKey(l.Key))),

            // Review
            new("review", "Self-improvement", "Review one of your games.", "", "Review", AchievementTier.Bronze),
            new("accuracy-90", "Precision", "Win a game with at least 90% accuracy.", "", "Review", AchievementTier.Gold),
            new("brilliant", "Brilliant!", "Play a brilliant move.", "", "Review", AchievementTier.Gold),
            new("analyst", "Analyst", "Explore a position on the analysis board.", "", "Review", AchievementTier.Bronze),
        };

        // One achievement per bot.
        foreach (BotProfile bot in BotRoster.Bots)
        {
            AchievementTier tier = bot.Rating < 1000 ? AchievementTier.Bronze : bot.Rating < 1800 ? AchievementTier.Silver : AchievementTier.Gold;
            string id = bot.Id;
            list.Add(new AchievementDef($"beat-{id}", $"Beat {bot.Name}", $"Win a game against {bot.Name} ({bot.RatingText}).", "", "Bots", tier,
                p => p.Bots.TryGetValue(id, out BotRecord? r) && r.Wins > 0));
        }

        // Course completion.
        foreach (Course course in LessonCatalog.Courses)
        {
            string id = course.Id;
            list.Add(new AchievementDef($"course-{id}", $"{course.Title} complete", $"Finish every lesson in {course.Title}.", course.Glyph, "Lessons", AchievementTier.Silver,
                p => LessonCatalog.Courses.First(c => c.Id == id).Lessons.All(l => p.Lessons.ContainsKey(l.Key))));
        }
        return list;
    }

    public bool IsUnlocked(string id) => App.Profile.Profile.Achievements.ContainsKey(id);

    public int UnlockedCount => App.Profile.Profile.Achievements.Count;

    /// <summary>Re-checks every profile-based achievement (call after any progress).</summary>
    public void CheckProfile()
    {
        PlayerProfile p = App.Profile.Profile;
        foreach (AchievementDef a in All)
            if (a.Check != null && !IsUnlocked(a.Id) && SafeCheck(a, p)) Unlock(a.Id);
    }

    /// <summary>Game-specific achievements, then profile ones.</summary>
    public void OnGameFinished(Game game, Color? humanColor, BotProfile? bot, TimeControl timeControl)
    {
        if (humanColor is Color me)
        {
            bool won = game.Winner == me;
            if (won && game.Termination == Termination.Checkmate)
            {
                Unlock("checkmate");
                if (game.Moves.Count <= 40) Unlock("quick-mate");
            }
            if (won && bot is { Rating: >= 2000 }) Unlock("underdog");
            if (won && !timeControl.IsUnlimited) Unlock("timed-win");
            if (game.Moves.Any(m => m.Side == me && m.Move.IsPromotion)) Unlock("promotion");
            if (game.Moves.Any(m => m.Side == me && m.Move.IsEnPassant)) Unlock("en-passant");
        }
        CheckProfile();
    }

    public void OnReview(GameReview review, Game game, Color perspective)
    {
        Unlock("review");
        if (review.Moves.Any(m => m.Side == perspective && m.Class == MoveClass.Brilliant)) Unlock("brilliant");
        double accuracy = perspective == Color.White ? review.WhiteAccuracy : review.BlackAccuracy;
        if (game.Winner == perspective && accuracy >= 90) Unlock("accuracy-90");
    }

    public void Unlock(string id)
    {
        if (IsUnlocked(id)) return;
        AchievementDef? def = All.FirstOrDefault(a => a.Id == id);
        if (def == null) return;
        App.Profile.Profile.Achievements[id] = DateTimeOffset.Now;
        App.Profile.Save();
        Unlocked?.Invoke(def);
    }

    private static bool SafeCheck(AchievementDef a, PlayerProfile p)
    {
        try
        {
            return a.Check!(p);
        }
        catch
        {
            return false;
        }
    }
}
