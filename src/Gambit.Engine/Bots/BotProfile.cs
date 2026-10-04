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
/// Strength comes from search limits (depth/nodes/time), from how it picks among the engine's
/// top candidate moves (noise + softmax temperature) and from occasional oversights.
/// </summary>
public sealed record BotProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// Playing strength on the Chess.com rapid scale: the bot makes blunders, mistakes and
    /// inaccuracies about as often as real players of this rating (docs/BOT-CALIBRATION.md).
    /// </summary>
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

    /// <summary>
    /// Probability of an oversight: the move is chosen by how the board looks right after it, without
    /// checking the opponent's replies. Like a person who doesn't look at what the opponent can do
    /// next, the bot may leave a piece hanging or grab a defended pawn with its queen.
    /// </summary>
    public double OversightChance { get; init; }

    /// <summary>How many plies the bot follows the opening book (0 = never).</summary>
    public int BookDepth { get; init; } = 12;

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
