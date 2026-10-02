using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;

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
    public GameRecord RecordGame(Game game, string opponent, string? botId, int? opponentRating, Color? playerColor, TimeControl timeControl)
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
        if (playerColor is not null && game.Termination != Termination.Aborted)
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
