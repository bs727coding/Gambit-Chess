using Gambit.Core.Board;
using Gambit.Engine.Search;

namespace Gambit.Engine.Bots;

/// <summary>
/// Personality for bots that pick among their top candidate moves: a small bonus (centipawns) added
/// to a candidate's score before sampling. Candidates are already among the engine's best moves, so a
/// style changes the flavour of play, not how many pieces the bot gives away.
/// </summary>
public static class BotStyles
{
    public static int Bonus(BotStyle style, Position pos, Move move)
    {
        if (style == BotStyle.Balanced) return 0;
        Color us = pos.SideToMove, them = us.Opposite();
        Position after = pos.Clone();
        after.MakeMove(move);
        PieceType mover = after.PieceAt(move.To).Type();
        ulong attacks = mover == PieceType.Pawn ? Attacks.Pawn(us, move.To) : Attacks.Of(mover, move.To, after.Occupied);
        bool check = after.InCheck;
        int bonus = 0;

        switch (style)
        {
            case BotStyle.Aggressive:
                // Checks, captures and pressure on the enemy king.
                if (check) bonus += 25;
                if (move.IsCapture) bonus += 10;
                int king = after.KingSquare(them);
                bonus += 6 * Bitboard.Count(attacks & (Attacks.King(king) | Bitboard.Of(king)));
                break;

            case BotStyle.Solid:
                // King safety first, simplify with even trades, keep the pawn shelter intact.
                if (move.IsCastle) bonus += 30;
                if (move.IsCapture && Searcher.See(pos, move) == 0) bonus += 10;
                if (pos.PieceAt(move.From).Type() == PieceType.Pawn && IsShelterPawn(pos, move.From, us)) bonus -= 20;
                break;

            case BotStyle.Tricky:
                // Create threats: hit undefended pieces or pieces worth more than the attacker.
                if (check) bonus += 10;
                int value = mover == PieceType.King ? 100 : mover.NominalValue();
                foreach (int sq in Bitboard.Squares(attacks & after.Pieces(them)))
                {
                    PieceType victim = after.PieceAt(sq).Type();
                    if (victim is PieceType.Pawn or PieceType.King) continue;
                    if (victim.NominalValue() > value || !after.IsAttacked(sq, them)) bonus += 15;
                }
                break;
        }
        return bonus;
    }

    /// <summary>A pawn on the king's file or a neighbouring one, at most two ranks in front of a castled-looking king.</summary>
    private static bool IsShelterPawn(Position pos, int pawnSquare, Color us)
    {
        int king = pos.KingSquare(us);
        if (Square.RelativeRank(king, us) != 0 || Square.File(king) is 3 or 4) return false; // only after castling
        int ahead = Square.RelativeRank(pawnSquare, us);
        return Math.Abs(Square.File(pawnSquare) - Square.File(king)) <= 1 && ahead is 1 or 2;
    }
}
