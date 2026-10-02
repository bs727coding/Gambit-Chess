using Gambit.Core.Board;

namespace Gambit.Engine.Search;

/// <summary>When to stop searching. Any combination of depth, nodes and time may be given.</summary>
public sealed record SearchLimits
{
    public int MaxDepth { get; init; } = Searcher.MaxPly - 1;
    public long MaxNodes { get; init; } = long.MaxValue;

    /// <summary>Don't start a new iteration after this much time.</summary>
    public TimeSpan? SoftTime { get; init; }

    /// <summary>Abort the search outright after this much time.</summary>
    public TimeSpan? HardTime { get; init; }

    /// <summary>Number of best lines to compute (analysis + weakened bots).</summary>
    public int MultiPv { get; init; } = 1;

    /// <summary>Restrict the root to these moves (null = all legal moves).</summary>
    public IReadOnlyCollection<Move>? SearchMoves { get; init; }

    public static SearchLimits Depth(int depth) => new() { MaxDepth = depth };

    public static SearchLimits Time(TimeSpan time) => new() { SoftTime = time * 0.6, HardTime = time };

    public static SearchLimits Infinite => new();

    /// <summary>Time allocation from a game clock.</summary>
    public static SearchLimits ForClock(TimeSpan remaining, TimeSpan increment, int maxDepth = Searcher.MaxPly - 1)
    {
        double rem = Math.Max(0, remaining.TotalMilliseconds - 50); // safety margin
        double inc = increment.TotalMilliseconds;
        double soft = Math.Max(20, rem / 32 + inc * 0.75);
        double hard = Math.Max(30, Math.Min(rem * 0.3, soft * 3.5));
        soft = Math.Min(soft, hard);
        return new SearchLimits
        {
            MaxDepth = maxDepth,
            SoftTime = TimeSpan.FromMilliseconds(soft),
            HardTime = TimeSpan.FromMilliseconds(hard),
        };
    }
}

/// <summary>One principal variation: a root move, its score and the expected continuation.</summary>
public sealed record PvLine(Move Move, int Score, int Depth, IReadOnlyList<Move> Pv)
{
    public bool IsMate => Searcher.IsMateScore(Score);

    /// <summary>Moves to mate (positive = side to move mates, negative = gets mated); 0 if not a mate score.</summary>
    public int MateIn => Searcher.MateIn(Score);

    /// <summary>"+1.25", "-0.40", "M3", "-M2".</summary>
    public string ScoreText => Searcher.FormatScore(Score);
}

/// <summary>Progress report after each completed iteration.</summary>
public sealed record SearchInfo(int Depth, int SelDepth, long Nodes, TimeSpan Elapsed, IReadOnlyList<PvLine> Lines)
{
    public long NodesPerSecond => Elapsed.TotalSeconds > 0 ? (long)(Nodes / Elapsed.TotalSeconds) : 0;
    public PvLine? Best => Lines.Count > 0 ? Lines[0] : null;
}

public sealed record SearchResult(Move BestMove, int Score, int Depth, long Nodes, TimeSpan Elapsed, IReadOnlyList<PvLine> Lines)
{
    public bool IsMate => Searcher.IsMateScore(Score);
    public long NodesPerSecond => Elapsed.TotalSeconds > 0 ? (long)(Nodes / Elapsed.TotalSeconds) : 0;
}
