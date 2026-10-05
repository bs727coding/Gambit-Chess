using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Engine.Bots;

namespace Gambit.ViewModels;

/// <summary>Parameters for starting a game from the Play page.</summary>
public sealed record GameSetup(BotProfile? Bot, Color HumanColor, TimeControl TimeControl, bool AllowTakebacks, string? StartFen = null)
{
    /// <summary>An online game (the session is a RemoteGameSession).</summary>
    public bool IsOnline { get; init; }

    /// <summary>Watching someone else's online game: no moves, nothing recorded in the profile.</summary>
    public bool IsSpectating { get; init; }

    public bool IsHotSeat => Bot == null && !IsOnline;

    /// <summary>Pass and play: the players' names (null = "White" / "Black").</summary>
    public string? WhiteName { get; init; }

    /// <inheritdoc cref="WhiteName"/>
    public string? BlackName { get; init; }

    /// <summary>
    /// A game from a set-up position (analysis board → Play from here) is practice: it is kept in the
    /// game archive but left out of the player's record, bot records and achievements.
    /// </summary>
    public bool IsPractice => StartFen != null;

    /// <summary>The result is shown neutrally ("White wins") instead of "You won".</summary>
    public bool IsNeutralView => IsHotSeat || IsSpectating;
}

/// <summary>An unfinished game saved to disk so it survives closing the app.</summary>
public sealed class SavedGame
{
    public string? BotId { get; set; }
    public string HumanColor { get; set; } = "white";
    public double InitialSeconds { get; set; }
    public double IncrementSeconds { get; set; }
    public bool AllowTakebacks { get; set; } = true;
    public string? StartFen { get; set; }
    public string? WhiteName { get; set; }
    public string? BlackName { get; set; }
    public List<string> Moves { get; set; } = [];
    public double? WhiteMs { get; set; }
    public double? BlackMs { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;

    public GameSetup ToSetup()
    {
        BotProfile? bot = BotId == null ? null : BotRoster.Bots.FirstOrDefault(b => b.Id == BotId);
        var tc = new TimeControl(TimeSpan.FromSeconds(InitialSeconds), TimeSpan.FromSeconds(IncrementSeconds));
        return new GameSetup(bot, HumanColor == "black" ? Color.Black : Color.White, tc, AllowTakebacks, StartFen)
        {
            WhiteName = WhiteName,
            BlackName = BlackName,
        };
    }

    public static SavedGame From(GameSetup setup, Game game, ChessClock? clock) => new()
    {
        BotId = setup.Bot?.Id,
        HumanColor = setup.HumanColor == Color.Black ? "black" : "white",
        InitialSeconds = setup.TimeControl.Initial.TotalSeconds,
        IncrementSeconds = setup.TimeControl.Increment.TotalSeconds,
        AllowTakebacks = setup.AllowTakebacks,
        StartFen = game.StartsFromStandardPosition ? null : game.StartFen,
        WhiteName = setup.WhiteName,
        BlackName = setup.BlackName,
        Moves = game.Moves.Select(m => m.Uci).ToList(),
        WhiteMs = clock?.Remaining(Color.White).TotalMilliseconds,
        BlackMs = clock?.Remaining(Color.Black).TotalMilliseconds,
    };
}
