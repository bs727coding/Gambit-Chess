namespace Gambit.Engine.Bots;

public enum BotStyle
{
    Balanced,
    Aggressive,
    Solid,
    Tricky,
}

/// <summary>
/// A named computer opponent: identity/personality plus the knobs that set its strength.
/// Strength comes from search limits (depth/nodes/time) and from how it picks among the engine's
/// top candidate moves (noise + softmax temperature + occasional random moves).
/// </summary>
public sealed record BotProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Approximate playing strength (estimated; calibrated by self-play in a later session).</summary>
    public required int Rating { get; init; }

    public required string Tagline { get; init; }
    public string Bio { get; init; } = "";

    /// <summary>Avatar accent color (#RRGGBB) and a short monogram/emoji shown in the avatar circle.</summary>
    public string Color { get; init; } = "#5B8DEF";
    public string Monogram { get; init; } = "?";

    public BotStyle Style { get; init; } = BotStyle.Balanced;

    // ---- strength knobs
    public int MaxDepth { get; init; } = 64;
    public long MaxNodes { get; init; } = long.MaxValue;

    /// <summary>Think-time budget for untimed games (ms).</summary>
    public int ThinkTimeMs { get; init; } = 1000;

    /// <summary>Use the game clock for time management when the game is timed.</summary>
    public bool UsesClock { get; init; }

    /// <summary>How many top candidate moves are scored (int.MaxValue = all legal moves).</summary>
    public int Candidates { get; init; } = 1;

    /// <summary>Softmax temperature in centipawns; 0 = always the best move.</summary>
    public double Temperature { get; init; }

    /// <summary>Standard deviation (cp) of noise added to candidate scores.</summary>
    public double EvalNoise { get; init; }

    /// <summary>Probability of playing a uniformly random legal move.</summary>
    public double RandomMoveChance { get; init; }

    // ---- feel
    public int MinMoveDelayMs { get; init; } = 350;
    public int MaxMoveDelayMs { get; init; } = 1100;

    public IReadOnlyList<string> Greetings { get; init; } = [];
    public IReadOnlyList<string> WinLines { get; init; } = [];
    public IReadOnlyList<string> LossLines { get; init; } = [];
    public IReadOnlyList<string> DrawLines { get; init; } = [];

    /// <summary>Unlimited-strength bots are labelled "Max" instead of a number.</summary>
    public bool IsMaxStrength { get; init; }

    public string RatingText => IsMaxStrength ? "Max" : Rating.ToString();
}
