using Gambit.Core.Puzzles;
using Gambit.Core.Rating;

namespace Gambit.App.Services;

/// <summary>The player's puzzle progress (stored inside profile.json).</summary>
public sealed class PuzzleProfile
{
    public double Rating { get; set; } = 1000;
    public double Deviation { get; set; } = 350;
    public double Volatility { get; set; } = 0.06;
    public int Attempts { get; set; }
    public int Solved { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
    public int BestRush3 { get; set; }
    public int BestRush5 { get; set; }
    public int BestSurvival { get; set; }
    public string? LastDailyDate { get; set; }
    public int DailyStreak { get; set; }
    public HashSet<string> Seen { get; set; } = [];
    public Dictionary<string, ThemeStat> Themes { get; set; } = [];
    public List<RatingPoint> History { get; set; } = [];

    public Glicko2Rating Glicko => new(Rating, Deviation, Volatility);
}

public sealed class ThemeStat
{
    public int Attempts { get; set; }
    public int Solved { get; set; }
}

public sealed record RatingPoint(DateTimeOffset At, int Rating);

public enum PuzzleMode
{
    Rated,
    Daily,
    Theme,
    Rush3,
    Rush5,
    Survival,
}

/// <summary>Picks puzzles, rates attempts with Glicko-2 and tracks streaks, Rush bests and themes.</summary>
public sealed class PuzzleService
{
    private static readonly Glicko2Rating PuzzleRd = new(1500, 80, 0.06);
    private readonly Random _rng = new();

    public static PuzzleService Instance { get; } = new();

    public PuzzleProfile Profile => App.Profile.Profile.Puzzles;

    public IReadOnlyList<Puzzle> All => PuzzleCatalog.All;

    /// <summary>Puzzle ranks by rating, like belts — shown on the Puzzles hub.</summary>
    public static (string Name, int Floor, int Next) Rank(double rating) => rating switch
    {
        < 600 => ("Pawn", 0, 600),
        < 900 => ("Knight", 600, 900),
        < 1200 => ("Bishop", 900, 1200),
        < 1500 => ("Rook", 1200, 1500),
        < 1800 => ("Queen", 1500, 1800),
        < 2100 => ("King", 1800, 2100),
        _ => ("Grandmaster", 2100, 2700),
    };

    /// <summary>An unseen puzzle close to the player's rating (window widens if needed).</summary>
    public Puzzle? NextRated(string? theme = null)
    {
        var pool = All.Where(p => theme == null || p.HasTheme(theme)).ToList();
        if (pool.Count == 0) return null;
        double target = Profile.Rating;
        foreach (int window in new[] { 100, 200, 350, 600, 1200, 5000 })
        {
            var candidates = pool.Where(p => Math.Abs(p.Rating - target) <= window && !Profile.Seen.Contains(p.Id)).ToList();
            if (candidates.Count > 0) return candidates[_rng.Next(candidates.Count)];
        }
        // Everything seen: allow repeats.
        return pool.OrderBy(p => Math.Abs(p.Rating - target)).Take(20).OrderBy(_ => _rng.Next()).First();
    }

    /// <summary>The same puzzle for everyone on a given date.</summary>
    public Puzzle? Daily(DateOnly date)
    {
        var pool = All.Where(p => p.Rating is >= 900 and <= 1900 && p.SolutionLength >= 2).ToList();
        if (pool.Count == 0) pool = All.ToList();
        if (pool.Count == 0) return null;
        long h = (long)date.DayNumber * 2654435761L;
        return pool[(int)(Math.Abs(h) % pool.Count)];
    }

    /// <summary>Rush ladder: ascending difficulty, starting easy.</summary>
    public List<Puzzle> RushLadder(int count = 120)
    {
        var sorted = All.OrderBy(p => p.Rating).ToList();
        if (sorted.Count == 0) return [];
        var ladder = new List<Puzzle>();
        var used = new HashSet<string>();
        double rating = 500;
        for (int i = 0; i < count && used.Count < sorted.Count; i++)
        {
            double target = rating;
            var near = sorted.Where(p => Math.Abs(p.Rating - target) < 120 && !used.Contains(p.Id)).ToList();
            // Past the hardest puzzles, keep serving the hardest ones left (not the easiest).
            Puzzle pick = near.Count > 0 ? near[_rng.Next(near.Count)] : sorted.Last(p => !used.Contains(p.Id));
            ladder.Add(pick);
            used.Add(pick.Id);
            rating += i < 10 ? 60 : 35;
        }
        return ladder;
    }

    /// <summary>Records a rated attempt; returns the rating change.</summary>
    public int RecordRated(Puzzle puzzle, bool solved)
    {
        PuzzleProfile p = Profile;
        double before = p.Rating;
        Glicko2Rating updated = Glicko2.Update(p.Glicko, PuzzleRd with { Rating = puzzle.Rating }, solved ? 1 : 0,
            minDeviation: 60, maxDeviation: 350);
        p.Rating = updated.Rating;
        p.Deviation = updated.Deviation;
        p.Volatility = updated.Volatility;
        p.History.Add(new RatingPoint(DateTimeOffset.Now, (int)Math.Round(p.Rating)));
        if (p.History.Count > 2000) p.History.RemoveRange(0, p.History.Count - 2000);
        RecordCommon(puzzle, solved);
        return (int)Math.Round(p.Rating - before);
    }

    /// <summary>Records an unrated attempt (Puzzle Rush, Survival, the daily puzzle).</summary>
    public void RecordUnrated(Puzzle puzzle, bool solved) => RecordCommon(puzzle, solved);

    public void RecordDaily(DateOnly date, bool solved)
    {
        PuzzleProfile p = Profile;
        string today = date.ToString("yyyy-MM-dd");
        if (p.LastDailyDate == today) return;
        string yesterday = date.AddDays(-1).ToString("yyyy-MM-dd");
        p.DailyStreak = solved ? (p.LastDailyDate == yesterday ? p.DailyStreak + 1 : 1) : 0;
        p.LastDailyDate = today;
        App.Profile.Save();
        AchievementService.Instance.CheckProfile();
    }

    public bool RecordRush(PuzzleMode mode, int score)
    {
        PuzzleProfile p = Profile;
        bool best = false;
        switch (mode)
        {
            case PuzzleMode.Rush3 when score > p.BestRush3:
                p.BestRush3 = score;
                best = true;
                break;
            case PuzzleMode.Rush5 when score > p.BestRush5:
                p.BestRush5 = score;
                best = true;
                break;
            case PuzzleMode.Survival when score > p.BestSurvival:
                p.BestSurvival = score;
                best = true;
                break;
        }
        App.Profile.Save();
        AchievementService.Instance.CheckProfile();
        return best;
    }

    private void RecordCommon(Puzzle puzzle, bool solved)
    {
        PuzzleProfile p = Profile;
        p.Attempts++;
        p.Seen.Add(puzzle.Id);
        if (solved)
        {
            p.Solved++;
            p.CurrentStreak++;
            p.BestStreak = Math.Max(p.BestStreak, p.CurrentStreak);
        }
        else
        {
            p.CurrentStreak = 0;
        }
        foreach (string theme in puzzle.Themes)
        {
            if (!p.Themes.TryGetValue(theme, out ThemeStat? stat)) p.Themes[theme] = stat = new ThemeStat();
            stat.Attempts++;
            if (solved) stat.Solved++;
        }
        App.Profile.Save();
        AchievementService.Instance.CheckProfile();
    }
}
