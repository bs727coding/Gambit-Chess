namespace Gambit.Core.Board;

/// <summary>
/// Premove rules: while the opponent is to move, a player may queue a move by squares. It is checked
/// for legality only once it is their turn (the opponent's reply may open a line or offer a capture).
/// </summary>
public static class Premoves
{
    /// <summary>
    /// Squares the piece on <paramref name="from"/> may premove to: everything it could reach on an
    /// otherwise empty board, plus pawn pushes and castling while the right exists.
    /// </summary>
    public static ulong Targets(Position pos, int from)
    {
        Piece p = pos.PieceAt(from);
        if (p == Piece.None) return 0;
        Color c = p.Color();
        ulong here = Bitboard.Of(from);
        ulong targets = p.Type() switch
        {
            PieceType.Knight => Attacks.Knight(from),
            PieceType.Bishop => Attacks.Bishop(from, 0),
            PieceType.Rook => Attacks.Rook(from, 0),
            PieceType.Queen => Attacks.Queen(from, 0),
            PieceType.King => Attacks.King(from),
            PieceType.Pawn => Attacks.Pawn(c, from) | Bitboard.Forward(here, c),
            _ => 0,
        };
        if (p.Type() == PieceType.Pawn && Square.Rank(from) == (c == Color.White ? 1 : 6))
            targets |= Bitboard.Forward(Bitboard.Forward(here, c), c);
        if (p.Type() == PieceType.King && from == (c == Color.White ? 4 : 60))
        {
            if (pos.Castling.HasFlag(c == Color.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide)) targets |= Bitboard.Of(from + 2);
            if (pos.Castling.HasFlag(c == Color.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide)) targets |= Bitboard.Of(from - 2);
        }
        return targets;
    }

    /// <summary>The legal move matching a queued premove in the current position (promotions become queens), or <see cref="Move.None"/>.</summary>
    public static Move Resolve(Position pos, int from, int to)
    {
        Move chosen = Move.None;
        foreach (Move m in MoveGenerator.LegalMoves(pos))
        {
            if (m.From != from || m.To != to) continue;
            if (chosen.IsNone || m.PromotionType == PieceType.Queen) chosen = m;
        }
        return chosen;
    }
}
