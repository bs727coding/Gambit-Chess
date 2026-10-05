using Gambit.Core.Openings;
using Gambit.Online;
using System.Text.Json.Serialization;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Engine.Bots;
using Gambit.ViewModels;

namespace Gambit.App.Services;

/// <summary>One finished game in the local archive.</summary>
public sealed class GameRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.Now;
    public string Opponent { get; set; } = "";
    public string? BotId { get; set; }
    public int? OpponentRating { get; set; }

    /// <summary>"white", "black" or "both" (hot-seat).</summary>
    public string PlayerColor { get; set; } = "white";

    /// <summary>"win", "loss" or "draw" from the local player's point of view ("draw" for hot-seat).</summary>
    public string Outcome { get; set; } = "draw";

    public string Result { get; set; } = "*";
    public string Termination { get; set; } = "";
    public int Moves { get; set; }
    public string TimeControl { get; set; } = "-";
    public string? Opening { get; set; }
    public string PgnFile { get; set; } = "";

    /// <summary>Played from a set-up position: listed, but not counted in the record or statistics.</summary>
    public bool Practice { get; set; }

    /// <summary>The game's id on the online server (online games), so the server's copy isn't added twice.</summary>
    public string? OnlineId { get; set; }

    /// <summary>
    /// The PGN file, or null if it is gone. Profiles store full paths, so a profile restored from a
    /// backup made on another computer finds its games by file name in this data folder.
    /// </summary>
    [JsonIgnore]
    public string? PgnPath
    {
        get
        {
            if (PgnFile.Length == 0) return null;
            if (File.Exists(PgnFile)) return PgnFile;
            string local = Path.Combine(AppPaths.Games, Path.GetFileName(PgnFile));
            return File.Exists(local) ? local : null;
        }
    }
}

/// <summary>Per-bot record for the stats page and bot unlock progression.</summary>
public sealed class BotRecord
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int Games => Wins + Losses + Draws;
}

public sealed class PlayerProfile
{
    public string Name { get; set; } = Environment.UserName is { Length: > 0 } n ? n : "You";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int CurrentWinStreak { get; set; }
    public int BestWinStreak { get; set; }
    public Dictionary<string, BotRecord> Bots { get; set; } = [];
    public List<GameRecord> RecentGames { get; set; } = [];
    public PuzzleProfile Puzzles { get; set; } = new();
    public Dictionary<string, LessonResult> Lessons { get; set; } = [];
    public Dictionary<string, DateTimeOffset> Achievements { get; set; } = [];

    /// <summary>Finished or skipped the welcome screen.</summary>
    public bool Onboarded { get; set; }

    /// <summary>The level chosen on the welcome screen (null if skipped, or a profile from before it).</summary>
    public ExperienceLevel? Experience { get; set; }

    /// <summary>Opening review schedule, by line id ("openings/italian#2").</summary>
    public Dictionary<string, ReviewState> Reviews { get; set; } = [];
}

/// <summary>Best result for one lesson (key = "course/lesson").</summary>
public sealed class LessonResult
{
    public int Stars { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>Owns the local player profile: results, per-bot records and the PGN archive.</summary>
public sealed class ProfileService
{
    private const int MaxRecent = 200;

    public ProfileService() => Profile = JsonStore.Load<PlayerProfile>(AppPaths.Profile);

    public PlayerProfile Profile { get; }

    public event EventHandler? Changed;

    /// <summary>Records a finished game and stores its PGN. Returns the stored record.</summary>
    public GameRecord RecordGame(Game game, string opponent, string? botId, int? opponentRating, Color? playerColor, TimeControl timeControl,
        bool practice = false, string? onlineId = null)
    {
        string outcome = playerColor is not Color pc || game.Result == GameResult.Draw
            ? "draw"
            : game.Winner == pc ? "win" : "loss";

        var record = new GameRecord
        {
            Opponent = opponent,
            BotId = botId,
            OpponentRating = opponentRating,
            PlayerColor = playerColor switch { Color.White => "white", Color.Black => "black", _ => "both" },
            Outcome = outcome,
            Result = game.ResultString,
            Termination = game.ResultDescription,
            Moves = (game.Moves.Count + 1) / 2,
            TimeControl = timeControl.DisplayName,
            Opening = game.Tags.GetValueOrDefault("Opening"),
            Practice = practice,
            OnlineId = onlineId,
        };

        try
        {
            string file = Path.Combine(AppPaths.Games, $"{record.PlayedAt:yyyyMMdd-HHmmss}-{record.Id[..6]}.pgn");
            File.WriteAllText(file, Pgn.Write(game));
            record.PgnFile = file;
        }
        catch (Exception ex)
        {
            Log.Warn($"Saving PGN failed: {ex.Message}");
        }

        PlayerProfile p = Profile;
        if (playerColor is not null && !practice && game.Termination != Termination.Aborted)
        {
            p.GamesPlayed++;
            switch (outcome)
            {
                case "win":
                    p.Wins++;
                    p.CurrentWinStreak++;
                    p.BestWinStreak = Math.Max(p.BestWinStreak, p.CurrentWinStreak);
                    break;
                case "loss":
                    p.Losses++;
                    p.CurrentWinStreak = 0;
                    break;
                default:
                    p.Draws++;
                    break;
            }

            if (botId != null)
            {
                if (!p.Bots.TryGetValue(botId, out BotRecord? br)) p.Bots[botId] = br = new BotRecord();
                if (outcome == "win") br.Wins++;
                else if (outcome == "loss") br.Losses++;
                else br.Draws++;
            }
        }

        p.RecentGames.Insert(0, record);
        if (p.RecentGames.Count > MaxRecent) p.RecentGames.RemoveRange(MaxRecent, p.RecentGames.Count - MaxRecent);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return record;
    }

    /// <summary>
    /// Adds the online games the server has and this PC doesn't (played on another PC) to the game list
    /// and archive. They're listed, not counted: the record and statistics stay as played here.
    /// Returns how many were added.
    /// </summary>
    public int ImportOnlineGames(IEnumerable<GameRecordDto> server, string myName)
    {
        PlayerProfile p = Profile;
        var local = p.RecentGames.Where(g => g.BotId == null && g.PlayerColor != "both")
            .Select(g => new OnlineHistory.LocalGame(g.OnlineId, g.Opponent, g.PlayedAt)).ToList();
        int added = 0;
        foreach (GameRecordDto g in OnlineHistory.Missing(server, local, myName))
        {
            if (OnlineHistory.ToGame(g) is not Game game) continue;
            Color me = OnlineHistory.PlayedWhite(g, myName) ? Color.White : Color.Black;
            if (OpeningBook.Identify(game) is Opening opening) game.Tags["Opening"] = opening.Name;
            var record = new GameRecord
            {
                PlayedAt = g.EndedAt,
                Opponent = OnlineHistory.Opponent(g, myName),
                PlayerColor = me == Color.White ? "white" : "black",
                Outcome = game.Result == GameResult.Draw ? "draw" : game.Winner == me ? "win" : "loss",
                Result = game.ResultString,
                Termination = game.ResultDescription,
                Moves = (game.Moves.Count + 1) / 2,
                TimeControl = OnlineHistory.ParseTimeControl(g.TimeControl).DisplayName,
                Opening = game.Tags.GetValueOrDefault("Opening"),
                OnlineId = g.Id,
            };
            try
            {
                string file = Path.Combine(AppPaths.Games, $"{record.PlayedAt.ToLocalTime():yyyyMMdd-HHmmss}-{record.Id[..6]}.pgn");
                File.WriteAllText(file, g.Pgn);
                record.PgnFile = file;
            }
            catch (Exception ex)
            {
                Log.Warn($"Saving PGN failed: {ex.Message}");
            }
            p.RecentGames.Add(record);
            added++;
        }
        if (added == 0) return 0;
        p.RecentGames = [.. p.RecentGames.OrderByDescending(r => r.PlayedAt).Take(MaxRecent)];
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>A new profile that hasn't seen the welcome screen yet.</summary>
    public bool NeedsOnboarding =>
        Onboarding.IsNeeded(Profile.Onboarded, Profile.GamesPlayed, Profile.Puzzles.Attempts, Profile.Lessons.Count);

    /// <summary>Applies the welcome screen: the name, and the level's starting puzzle rating (null = skipped).</summary>
    public void CompleteOnboarding(string? name, ExperienceOption? option)
    {
        PlayerProfile p = Profile;
        if (!string.IsNullOrWhiteSpace(name)) p.Name = name.Trim();
        if (option != null)
        {
            p.Experience = option.Level;
            if (p.Puzzles.Attempts == 0) p.Puzzles.Rating = option.PuzzleRating;
        }
        p.Onboarded = true;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>This profile's opening review schedule: lines of finished opening lessons, soonest first.</summary>
    public IReadOnlyList<(ReviewLine Line, DateOnly Due)> ReviewSchedule() => OpeningReview.Schedule(Profile.Reviews, LessonFinishedOn);

    /// <summary>Opening lines due for review today.</summary>
    public IReadOnlyList<ReviewLine> DueReviews() => OpeningReview.Due(Profile.Reviews, LessonFinishedOn, DateOnly.FromDateTime(DateTime.Now));

    private DateOnly? LessonFinishedOn(string key) =>
        Profile.Lessons.TryGetValue(key, out LessonResult? r) ? DateOnly.FromDateTime(r.CompletedAt.LocalDateTime) : null;

    /// <summary>The bot to suggest next: the weakest unbeaten one at or above the player's starting level.</summary>
    public BotProfile NextBot() => Onboarding.NextBot(Profile.Experience, id => RecordAgainst(id).Wins > 0);

    public BotRecord RecordAgainst(string botId) =>
        Profile.Bots.TryGetValue(botId, out BotRecord? r) ? r : new BotRecord();

    public void Save()
    {
        try
        {
            JsonStore.Save(AppPaths.Profile, Profile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Saving profile failed: {ex.Message}");
        }
    }
}
