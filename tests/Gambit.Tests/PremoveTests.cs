using Gambit.Core.Board;

namespace Gambit.Tests;

public sealed class PremoveTests
{
    private static int Sq(string name) => Square.Parse(name);

    private static ulong Squares(params string[] names) => names.Aggregate(0UL, (bb, n) => bb | Bitboard.Of(Sq(n)));

    [Fact]
    public void Targets_look_through_pieces_because_the_reply_may_open_lines()
    {
        Position start = Position.Start(); // White to move, so these are Black premoves
        ulong bishop = Premoves.Targets(start, Sq("c8"));
        Assert.True(Bitboard.Contains(bishop, Sq("h3")));
        Assert.True(Bitboard.Contains(bishop, Sq("a6")));
        Assert.Equal(Squares("e6", "e5", "d6", "f6"), Premoves.Targets(start, Sq("e7")));
        Assert.Equal(Squares("a6", "c6", "d7"), Premoves.Targets(start, Sq("b8")));

        ulong king = Premoves.Targets(start, Sq("e8"));
        Assert.True(Bitboard.Contains(king, Sq("g8")) && Bitboard.Contains(king, Sq("c8")));
        Position noCastling = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w - - 0 1");
        Assert.False(Bitboard.Contains(Premoves.Targets(noCastling, Sq("e8")), Sq("g8")));
        Assert.Equal(0UL, Premoves.Targets(start, Sq("e4"))); // empty square
    }

    [Fact]
    public void Resolve_plays_a_premove_only_if_it_became_legal()
    {
        // 1.e4 d5: White's premove exd5 is now a legal capture; e4e5 is too; e4d3 never is.
        Position pos = Position.FromFen("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2");
        Assert.Equal("e4d5", Premoves.Resolve(pos, Sq("e4"), Sq("d5")).ToUci());
        Assert.Equal("e4e5", Premoves.Resolve(pos, Sq("e4"), Sq("e5")).ToUci());
        Assert.True(Premoves.Resolve(pos, Sq("e4"), Sq("d3")).IsNone);
        Assert.Equal("f1b5", Premoves.Resolve(pos, Sq("f1"), Sq("b5")).ToUci()); // the e-pawn cleared the way
        Assert.True(Premoves.Resolve(pos, Sq("d1"), Sq("d5")).IsNone); // blocked by the d2 pawn

        // A premoved promotion becomes a queen.
        Position promo = Position.FromFen("8/4P3/8/8/8/8/k7/4K3 w - - 0 1");
        Assert.Equal("e7e8q", Premoves.Resolve(promo, Sq("e7"), Sq("e8")).ToUci());
    }
}
