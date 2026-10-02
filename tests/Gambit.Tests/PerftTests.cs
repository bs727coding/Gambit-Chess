using Gambit.Core.Board;

namespace Gambit.Tests;

/// <summary>Move-generator correctness against the standard perft suite (chessprogramming.org).</summary>
public class PerftTests
{
    public const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";

    [Theory]
    [InlineData(Position.StartFen, 1, 20L)]
    [InlineData(Position.StartFen, 2, 400L)]
    [InlineData(Position.StartFen, 3, 8_902L)]
    [InlineData(Position.StartFen, 4, 197_281L)]
    [InlineData(Position.StartFen, 5, 4_865_609L)]
    [InlineData(Kiwipete, 1, 48L)]
    [InlineData(Kiwipete, 2, 2_039L)]
    [InlineData(Kiwipete, 3, 97_862L)]
    [InlineData(Kiwipete, 4, 4_085_603L)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 1, 14L)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 3, 2_812L)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 5, 674_624L)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 6, 11_030_083L)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 1, 6L)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9_467L)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 4, 422_333L)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 1, 44L)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62_379L)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 4, 2_103_487L)]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 1, 46L)]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 3, 89_890L)]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 4, 3_894_594L)]
    public void Perft_matches_reference(string fen, int depth, long expected)
    {
        var pos = Position.FromFen(fen);
        Assert.Equal(expected, MoveGenerator.Perft(pos, depth));
        Assert.Equal(fen, pos.ToFen()); // make/unmake restores everything
    }

    [Fact]
    public void Make_unmake_restores_zobrist_key()
    {
        var pos = Position.FromFen(Kiwipete);
        ulong key = pos.Key;
        Walk(pos, 3);
        Assert.Equal(key, pos.Key);

        static void Walk(Position p, int depth)
        {
            if (depth == 0) return;
            foreach (Move m in MoveGenerator.LegalMoves(p))
            {
                p.MakeMove(m);
                // Incremental key must equal a key computed from scratch.
                Assert.Equal(Position.FromFen(p.ToFen()).Key, p.Key);
                Walk(p, depth - 1);
                p.UnmakeMove();
            }
        }
    }

    [Theory]
    [InlineData("8/8/8/8/k2Pp2Q/8/8/3K4 b - d3 0 1", "e4d3", false)] // ep would expose king along rank
    [InlineData("8/8/3k4/8/2pP4/8/8/3K4 b - d3 0 1", "c4d3", true)]
    [InlineData("8/8/8/2k5/3Pp3/8/8/3K4 b - d3 0 1", "e4d3", true)]   // ep captures the checking pawn
    public void En_passant_legality(string fen, string uci, bool legal)
    {
        var pos = Position.FromFen(fen);
        Assert.Equal(legal, MoveGenerator.LegalMoves(pos).Any(m => m.ToUci() == uci));
    }
}
