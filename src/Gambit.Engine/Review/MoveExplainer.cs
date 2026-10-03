using Gambit.Core.Board;
using Gambit.Core.Notation;
using Gambit.Engine.Search;

namespace Gambit.Engine.Review;

/// <summary>
/// Plain-language reasons for review verdicts ("This allows mate in 2", "The knight on g4 can simply
/// be taken", "Qxf7# was mate in one"), derived from the engine's best move before the played move
/// and its best reply after it. Scores are raw search scores from the side to move's point of view.
/// </summary>
public static class MoveExplainer
{
    /// <param name="cls">The verdict on the move.</param>
    /// <param name="before">Position before the move (the mover to play).</param>
    /// <param name="played">The move that was played.</param>
    /// <param name="best">Engine's best move in <paramref name="before"/>; <paramref name="bestScore"/> is the mover's view.</param>
    /// <param name="after">Position after the move (the opponent to play).</param>
    /// <param name="reply">Engine's best reply in <paramref name="after"/>; <paramref name="replyScore"/> is the opponent's view.</param>
    public static string? Explain(MoveClass cls, Position before, Move played, Move best, int bestScore, Position after, Move reply, int replyScore)
    {
        switch (cls)
        {
            case MoveClass.Brilliant:
                return $"A sound sacrifice: the {Name(before.PieceAt(played.From).Type())} can be taken, but it pays off.";
            case MoveClass.Great:
                return "The only move that keeps the advantage.";
            case MoveClass.Miss:
                // The point of a miss is the chance that went by.
                return Missed(before, best, bestScore, cls) ?? Punishment(after, played, reply, replyScore);
            case MoveClass.Inaccuracy or MoveClass.Mistake or MoveClass.Blunder:
                return Punishment(after, played, reply, replyScore) ?? Missed(before, best, bestScore, cls);
            default:
                return null;
        }
    }

    /// <summary>What the opponent can do now: mate, win material, or fork.</summary>
    private static string? Punishment(Position after, Move played, Move reply, int replyScore)
    {
        if (reply.IsNone) return null;
        string san = San.Format(after, reply);
        if (Searcher.IsMateScore(replyScore) && replyScore > 0)
        {
            int n = Searcher.MateIn(replyScore);
            return n == 1 ? $"This allows mate: {san}." : $"This allows mate in {n}, starting with {san}.";
        }
        if (reply.IsCapture && Searcher.See(after, reply) >= 100)
        {
            int victimSquare = reply.IsEnPassant ? reply.To + (after.SideToMove == Color.White ? -8 : 8) : reply.To;
            PieceType victim = after.PieceAt(victimSquare).Type();
            if (victim == PieceType.Pawn) return $"This loses a pawn: {san}.";
            string where = Square.Name(victimSquare);
            return victimSquare == played.To
                ? $"The {Name(victim)} on {where} can simply be taken: {san}."
                : $"This leaves the {Name(victim)} on {where} hanging: {san}.";
        }
        if (IsFork(after, reply)) return $"This allows a fork: {san}.";
        return null;
    }

    /// <summary>What the mover could have done instead.</summary>
    private static string? Missed(Position before, Move best, int bestScore, MoveClass cls)
    {
        if (best.IsNone) return null;
        string san = San.Format(before, best);
        if (Searcher.IsMateScore(bestScore) && bestScore > 0)
        {
            int n = Searcher.MateIn(bestScore);
            return n == 1 ? $"{san} was mate in one." : $"{san} started a mate in {n}.";
        }
        if (best.IsCapture && !best.IsEnPassant && Searcher.See(before, best) >= 200)
            return $"{san} would have won the {Name(before.PieceAt(best.To).Type())} on {Square.Name(best.To)}.";
        if (IsFork(before, best)) return $"{san} would have forked two pieces.";
        return cls == MoveClass.Inaccuracy ? $"{san} was more accurate." : $"{san} was much stronger.";
    }

    /// <summary>The moved piece attacks two or more enemy pieces that it outranks or that are undefended (a king counts).</summary>
    public static bool IsFork(Position pos, Move move)
    {
        Position p = pos.Clone();
        Color us = p.SideToMove, them = us.Opposite();
        p.MakeMove(move);
        PieceType type = p.PieceAt(move.To).Type();
        ulong attacks = type == PieceType.Pawn ? Attacks.Pawn(us, move.To) : Attacks.Of(type, move.To, p.Occupied);
        int value = type == PieceType.King ? 0 : type.NominalValue();
        int targets = 0;
        foreach (int sq in Bitboard.Squares(attacks & p.Pieces(them)))
        {
            PieceType victim = p.PieceAt(sq).Type();
            if (victim == PieceType.Pawn) continue;
            if (victim == PieceType.King || victim.NominalValue() > value || !p.IsAttacked(sq, them)) targets++;
        }
        return targets >= 2;
    }

    private static string Name(PieceType type) => type.ToString().ToLowerInvariant();
}
