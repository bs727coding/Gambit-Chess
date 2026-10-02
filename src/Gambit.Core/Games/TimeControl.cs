using Gambit.Core.Board;

namespace Gambit.Core.Games;

public enum TimeCategory
{
    Unlimited,
    Bullet,
    Blitz,
    Rapid,
    Classical,
}

/// <summary>Base time + Fischer increment. <see cref="Unlimited"/> means no clock.</summary>
public readonly record struct TimeControl(TimeSpan Initial, TimeSpan Increment)
{
    public static readonly TimeControl Unlimited = new(TimeSpan.Zero, TimeSpan.Zero);

    public static TimeControl Minutes(double minutes, double incrementSeconds = 0) =>
        new(TimeSpan.FromMinutes(minutes), TimeSpan.FromSeconds(incrementSeconds));

    public bool IsUnlimited => Initial <= TimeSpan.Zero;

    /// <summary>Lichess-style categories by estimated duration (initial + 40 × increment).</summary>
    public TimeCategory Category
    {
        get
        {
            if (IsUnlimited) return TimeCategory.Unlimited;
            double est = Initial.TotalSeconds + 40 * Increment.TotalSeconds;
            return est switch
            {
                < 180 => TimeCategory.Bullet,
                < 480 => TimeCategory.Blitz,
                < 1500 => TimeCategory.Rapid,
                _ => TimeCategory.Classical,
            };
        }
    }

    /// <summary>"10 min", "3 | 2", "Unlimited".</summary>
    public string DisplayName
    {
        get
        {
            if (IsUnlimited) return "Unlimited";
            string baseText = Initial.TotalMinutes >= 1
                ? $"{Initial.TotalMinutes:0.##}"
                : $"{Initial.TotalSeconds:0}s";
            return Increment > TimeSpan.Zero ? $"{baseText} | {Increment.TotalSeconds:0}" : $"{baseText} min";
        }
    }

    /// <summary>PGN TimeControl tag value, e.g. "600+5" or "-".</summary>
    public string PgnTag => IsUnlimited ? "-" : $"{(int)Initial.TotalSeconds}+{(int)Increment.TotalSeconds}";

    public override string ToString() => DisplayName;
}

/// <summary>
/// A two-sided chess clock with Fischer increment. Time comes from a <see cref="TimeProvider"/> so it
/// can be faked in tests and mirrored from a server later.
/// </summary>
public sealed class ChessClock
{
    private readonly TimeProvider _time;
    private readonly TimeSpan[] _remaining = new TimeSpan[2];
    private long _turnStarted;

    public ChessClock(TimeControl control, TimeProvider? time = null)
    {
        Control = control;
        _time = time ?? TimeProvider.System;
        _remaining[0] = _remaining[1] = control.Initial;
    }

    public TimeControl Control { get; }

    /// <summary>The side whose clock is running, or null when stopped.</summary>
    public Color? Running { get; private set; }

    public TimeSpan Remaining(Color side)
    {
        TimeSpan t = _remaining[(int)side];
        if (Running == side) t -= _time.GetElapsedTime(_turnStarted);
        return t < TimeSpan.Zero ? TimeSpan.Zero : t;
    }

    /// <summary>Time spent on the current turn so far.</summary>
    public TimeSpan CurrentTurnElapsed => Running is null ? TimeSpan.Zero : _time.GetElapsedTime(_turnStarted);

    public bool IsFlagged(Color side) => Remaining(side) <= TimeSpan.Zero;

    public void Start(Color side)
    {
        Running = side;
        _turnStarted = _time.GetTimestamp();
    }

    /// <summary>
    /// Ends the running side's turn: deducts the elapsed time, adds the increment, and starts the
    /// opponent's clock. Returns how long the move took.
    /// </summary>
    public TimeSpan Switch()
    {
        if (Running is not Color side) return TimeSpan.Zero;
        TimeSpan spent = _time.GetElapsedTime(_turnStarted);
        _remaining[(int)side] = _remaining[(int)side] - spent + Control.Increment;
        if (_remaining[(int)side] < TimeSpan.Zero) _remaining[(int)side] = TimeSpan.Zero;
        Start(side.Opposite());
        return spent;
    }

    public void Stop()
    {
        if (Running is Color side)
        {
            _remaining[(int)side] -= _time.GetElapsedTime(_turnStarted);
            if (_remaining[(int)side] < TimeSpan.Zero) _remaining[(int)side] = TimeSpan.Zero;
        }
        Running = null;
    }

    /// <summary>Overwrite both clocks (used for takebacks and for syncing with a server).</summary>
    public void Set(TimeSpan white, TimeSpan black)
    {
        _remaining[0] = white;
        _remaining[1] = black;
        if (Running is not null) _turnStarted = _time.GetTimestamp();
    }
}
