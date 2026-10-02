using Gambit.App.Helpers;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Engine.Bots;

namespace Gambit.App.Services;

/// <summary>An unfinished game saved to disk so it survives closing the app.</summary>
public sealed class SavedGame
{
    public string? BotId { get; set; }
    public string HumanColor { get; set; } = "white";
    public double InitialSeconds { get; set; }
    public double IncrementSeconds { get; set; }
    public bool AllowTakebacks { get; set; } = true;
    public string? StartFen { get; set; }
    public List<string> Moves { get; set; } = [];
    public double? WhiteMs { get; set; }
    public double? BlackMs { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;

    public GameSetup ToSetup()
    {
        BotProfile? bot = BotId == null ? null : BotRoster.Bots.FirstOrDefault(b => b.Id == BotId);
        var tc = new TimeControl(TimeSpan.FromSeconds(InitialSeconds), TimeSpan.FromSeconds(IncrementSeconds));
        return new GameSetup(bot, HumanColor == "black" ? Color.Black : Color.White, tc, AllowTakebacks, StartFen);
    }
}

/// <summary>Persists the game in progress (current-game.json) after every move.</summary>
public static class ActiveGameStore
{
    private static string FilePath => Path.Combine(AppPaths.Root, "current-game.json");

    public static bool Exists => File.Exists(FilePath);

    public static void Save(GameSetup setup, Game game, ChessClock? clock)
    {
        try
        {
            var saved = new SavedGame
            {
                BotId = setup.Bot?.Id,
                HumanColor = setup.HumanColor == Color.Black ? "black" : "white",
                InitialSeconds = setup.TimeControl.Initial.TotalSeconds,
                IncrementSeconds = setup.TimeControl.Increment.TotalSeconds,
                AllowTakebacks = setup.AllowTakebacks,
                StartFen = game.StartsFromStandardPosition ? null : game.StartFen,
                Moves = game.Moves.Select(m => m.Uci).ToList(),
                WhiteMs = clock?.Remaining(Color.White).TotalMilliseconds,
                BlackMs = clock?.Remaining(Color.Black).TotalMilliseconds,
            };
            JsonStore.Save(FilePath, saved);
        }
        catch (Exception ex)
        {
            Log.Warn($"Saving the current game failed: {ex.Message}");
        }
    }

    public static SavedGame? Load()
    {
        if (!Exists) return null;
        SavedGame saved = JsonStore.Load<SavedGame>(FilePath);
        return saved.Moves.Count == 0 && saved.BotId == null ? null : saved;
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Log.Warn($"Clearing the saved game failed: {ex.Message}");
        }
    }
}
