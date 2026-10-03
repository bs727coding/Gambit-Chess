using Gambit.Core.Board;
using Gambit.Core.Notation;
using Gambit.Engine.Bots;

namespace Gambit.Tests;

public sealed class BotStyleTests
{
    private static int Bonus(BotStyle style, string fen, string uci)
    {
        Position pos = Position.FromFen(fen);
        Move move = Uci.Parse(pos, uci);
        Assert.False(move.IsNone, uci);
        return BotStyles.Bonus(style, pos, move);
    }

    [Fact]
    public void Aggressive_bots_like_checks_and_king_pressure()
    {
        const string fen = "4k3/8/8/8/8/8/8/R3K3 w - - 0 1";
        Assert.True(Bonus(BotStyle.Aggressive, fen, "a1a8") > Bonus(BotStyle.Aggressive, fen, "a1a2")); // Ra8+ vs a quiet move
        Assert.Equal(0, Bonus(BotStyle.Balanced, fen, "a1a8"));
    }

    [Fact]
    public void Solid_bots_castle_and_keep_their_pawn_shelter()
    {
        Assert.True(Bonus(BotStyle.Solid, "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1", "e1g1") > 0);
        const string castled = "4k3/8/8/8/8/8/5PPP/6K1 w - - 0 1";
        Assert.True(Bonus(BotStyle.Solid, castled, "g2g4") < 0);  // weakens the king
        Assert.Equal(0, Bonus(BotStyle.Solid, castled, "g1f1"));
    }

    [Fact]
    public void Tricky_bots_create_threats()
    {
        const string fen = "4k3/8/8/3r4/8/8/8/1N2K3 w - - 0 1";
        Assert.True(Bonus(BotStyle.Tricky, fen, "b1c3") > 0);     // the knight hits the loose rook
        Assert.Equal(0, Bonus(BotStyle.Tricky, fen, "e1f2"));
    }
}
