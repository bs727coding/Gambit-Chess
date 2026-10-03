using System.Diagnostics;
using Gambit.Core.Board;

namespace Gambit.Engine.Search;

/// <summary>
/// A fixed search benchmark: the same positions searched to the same depth, each from a fresh
/// searcher, on one thread. The total node count is a signature of the search and evaluation:
/// a pure speed-up leaves it unchanged, anything else changes how the engine (and the bots) play.
/// Run it with <c>dotnet run -c Release tools/bench.cs</c>; a test pins the signature.
/// </summary>
public static class Bench
{
    /// <summary>Openings, middlegames, endgames and tactics (several from Stockfish's bench set).</summary>
    public static readonly string[] Positions =
    [
        Position.StartFen,
        "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
        "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4",
        "4rrk1/pp1n3p/3q2pQ/2p1pb2/2PP4/2P3N1/P2B2PP/4RRK1 b - - 7 19",
        "rq3rk1/ppp2ppp/1bnpb3/3N2B1/3NP3/7P/PPPQ1PP1/2KR3R w - - 7 14",
        "r1bq1r1k/1pp1n1pp/1p1p4/4p2Q/4Pp2/1BNP4/PPP2PPP/3R1RK1 w - - 2 14",
        "r3r1k1/2p2ppp/p1p1bn2/8/1q2P3/2NPQN2/PPP3PP/R4RK1 b - - 2 15",
        "r1bbk1nr/pp3p1p/2n5/1N4p1/2Np1B2/8/PPP2PPP/2KR1B1R w kq - 0 13",
        "8/8/1p2k1p1/3p3p/1p1P1P1P/1P2PK2/8/8 w - - 3 54",
        "6k1/6p1/6Pp/ppp5/3pn2P/1P3K2/1PP2P2/3N4 b - - 0 1",
        "3b4/5kp1/1p1p1p1p/pP1PpP1P/P1P1P3/3KN3/8/8 w - - 0 1",
        "2K5/p7/7P/5pR1/8/5k2/r7/8 w - - 0 4",
    ];

    /// <summary>Searches each position to <paramref name="depth"/>; reports one line per position.</summary>
    public static (long Nodes, TimeSpan Elapsed) Run(IReadOnlyList<string> fens, int depth, Action<string>? report = null)
    {
        long nodes = 0;
        var searcher = new Searcher(16);
        var sw = Stopwatch.StartNew();
        foreach (string fen in fens)
        {
            searcher.Reset(); // as good as a new searcher
            SearchResult r = searcher.Search(Position.FromFen(fen), SearchLimits.Depth(depth));
            nodes += r.Nodes;
            report?.Invoke($"{r.Nodes,12:N0}  {r.BestMove,-6} {Searcher.FormatScore(r.Score),7}  {fen}");
        }
        return (nodes, sw.Elapsed);
    }
}
