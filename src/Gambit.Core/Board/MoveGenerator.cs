using System.Runtime.CompilerServices;

namespace Gambit.Core.Board;

public enum GenType
{
    /// <summary>Every legal move.</summary>
    All,

    /// <summary>Legal captures (incl. en passant) and promotions — for quiescence search.</summary>
    Noisy,
}

/// <summary>
/// Fully legal move generation using check-evasion masks and pin rays (no make/unmake needed to
/// filter). Verified by perft in the test suite.
/// </summary>
public static class MoveGenerator
{
    public const int MaxMoves = 256;

    public static int Generate(Position pos, Span<Move> moves, GenType type = GenType.All)
    {
        int n = 0;
        bool noisy = type == GenType.Noisy;
        Color us = pos.SideToMove, them = us.Opposite();
        ulong ours = pos.Pieces(us), theirs = pos.Pieces(them), occ = ours | theirs;
        int ksq = pos.KingSquare(us);
        ulong checkers = pos.Checkers;

        // ---- King
        ulong kingTargets = Attacks.King(ksq) & ~ours;
        if (noisy) kingTargets &= theirs;
        ulong occWithoutKing = occ ^ (1UL << ksq);
        while (kingTargets != 0)
        {
            int to = Bitboard.PopLsb(ref kingTargets);
            if (!pos.IsAttacked(to, them, occWithoutKing))
                moves[n++] = new Move(ksq, to, Bitboard.Contains(theirs, to) ? MoveFlag.Capture : MoveFlag.Quiet);
        }

        // Double check: only the king can move.
        if (Bitboard.MoreThanOne(checkers)) return n;

        ulong evasionMask = checkers == 0
            ? Bitboard.All
            : Attacks.Between(ksq, Bitboard.Lsb(checkers)) | checkers;
        ulong pinned = pos.PinnedPieces(us);

        // ---- Pawns
        n = GeneratePawnMoves(pos, moves, n, noisy, us, theirs, occ, ksq, checkers, evasionMask, pinned);

        // ---- Pieces
        ulong pieceTargets = ~ours & evasionMask;
        if (noisy) pieceTargets &= theirs;

        ulong knights = pos.Pieces(us, PieceType.Knight) & ~pinned; // a pinned knight can never move
        while (knights != 0)
        {
            int from = Bitboard.PopLsb(ref knights);
            n = AddMoves(moves, n, from, Attacks.Knight(from) & pieceTargets, theirs);
        }

        ulong diagonal = pos.Pieces(us, PieceType.Bishop, PieceType.Queen);
        while (diagonal != 0)
        {
            int from = Bitboard.PopLsb(ref diagonal);
            ulong t = Attacks.Bishop(from, occ) & pieceTargets;
            if (Bitboard.Contains(pinned, from)) t &= Attacks.Line(ksq, from);
            n = AddMoves(moves, n, from, t, theirs);
        }

        ulong orthogonal = pos.Pieces(us, PieceType.Rook, PieceType.Queen);
        while (orthogonal != 0)
        {
            int from = Bitboard.PopLsb(ref orthogonal);
            ulong t = Attacks.Rook(from, occ) & pieceTargets;
            if (Bitboard.Contains(pinned, from)) t &= Attacks.Line(ksq, from);
            n = AddMoves(moves, n, from, t, theirs);
        }

        // ---- Castling
        if (!noisy && checkers == 0 && pos.Castling != CastlingRights.None)
        {
            if (us == Color.White)
            {
                if ((pos.Castling & CastlingRights.WhiteKingSide) != 0
                    && (occ & (Bitboard.Of(Square.F1) | Bitboard.Of(Square.G1))) == 0
                    && !pos.IsAttacked(Square.F1, them, occ) && !pos.IsAttacked(Square.G1, them, occ))
                    moves[n++] = new Move(Square.E1, Square.G1, MoveFlag.KingCastle);
                if ((pos.Castling & CastlingRights.WhiteQueenSide) != 0
                    && (occ & (Bitboard.Of(Square.B1) | Bitboard.Of(Square.C1) | Bitboard.Of(Square.D1))) == 0
                    && !pos.IsAttacked(Square.D1, them, occ) && !pos.IsAttacked(Square.C1, them, occ))
                    moves[n++] = new Move(Square.E1, Square.C1, MoveFlag.QueenCastle);
            }
            else
            {
                if ((pos.Castling & CastlingRights.BlackKingSide) != 0
                    && (occ & (Bitboard.Of(Square.F8) | Bitboard.Of(Square.G8))) == 0
                    && !pos.IsAttacked(Square.F8, them, occ) && !pos.IsAttacked(Square.G8, them, occ))
                    moves[n++] = new Move(Square.E8, Square.G8, MoveFlag.KingCastle);
                if ((pos.Castling & CastlingRights.BlackQueenSide) != 0
                    && (occ & (Bitboard.Of(Square.B8) | Bitboard.Of(Square.C8) | Bitboard.Of(Square.D8))) == 0
                    && !pos.IsAttacked(Square.D8, them, occ) && !pos.IsAttacked(Square.C8, them, occ))
                    moves[n++] = new Move(Square.E8, Square.C8, MoveFlag.QueenCastle);
            }
        }

        return n;
    }

    private static int GeneratePawnMoves(Position pos, Span<Move> moves, int n, bool noisy, Color us, ulong theirs,
        ulong occ, int ksq, ulong checkers, ulong evasionMask, ulong pinned)
    {
        int up = us == Color.White ? 8 : -8;
        int startRank = us == Color.White ? 1 : 6;
        int promoRank = us == Color.White ? 7 : 0;
        int ep = pos.EnPassantSquare;
        ulong pawns = pos.Pieces(us, PieceType.Pawn);

        while (pawns != 0)
        {
            int from = Bitboard.PopLsb(ref pawns);
            ulong allowed = evasionMask;
            if (Bitboard.Contains(pinned, from)) allowed &= Attacks.Line(ksq, from);

            // Pushes
            int to = from + up;
            if (!Bitboard.Contains(occ, to))
            {
                if (Square.Rank(to) == promoRank)
                {
                    if (Bitboard.Contains(allowed, to)) n = AddPromotions(moves, n, from, to, capture: false);
                }
                else if (!noisy)
                {
                    if (Bitboard.Contains(allowed, to)) moves[n++] = new Move(from, to);
                    if (Square.Rank(from) == startRank)
                    {
                        int to2 = to + up;
                        if (!Bitboard.Contains(occ, to2) && Bitboard.Contains(allowed, to2))
                            moves[n++] = new Move(from, to2, MoveFlag.DoublePawnPush);
                    }
                }
            }

            // Captures
            ulong caps = Attacks.Pawn(us, from) & theirs & allowed;
            while (caps != 0)
            {
                int c = Bitboard.PopLsb(ref caps);
                if (Square.Rank(c) == promoRank) n = AddPromotions(moves, n, from, c, capture: true);
                else moves[n++] = new Move(from, c, MoveFlag.Capture);
            }

            // En passant: rare, so verify with a full occupancy test (handles pins along the rank
            // and capturing a checking pawn).
            if (ep != Square.None && Bitboard.Contains(Attacks.Pawn(us, from), ep))
            {
                int capSq = ep - up;
                ulong capBb = Bitboard.Of(capSq);
                ulong occAfter = (occ ^ Bitboard.Of(from) ^ capBb) | Bitboard.Of(ep);
                ulong attackers = pos.AttackersTo(ksq, occAfter) & theirs & ~capBb;
                if (attackers == 0) moves[n++] = new Move(from, ep, MoveFlag.EnPassant);
            }
        }

        _ = checkers;
        return n;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AddMoves(Span<Move> moves, int n, int from, ulong targets, ulong theirs)
    {
        while (targets != 0)
        {
            int to = Bitboard.PopLsb(ref targets);
            moves[n++] = new Move(from, to, Bitboard.Contains(theirs, to) ? MoveFlag.Capture : MoveFlag.Quiet);
        }
        return n;
    }

    private static int AddPromotions(Span<Move> moves, int n, int from, int to, bool capture)
    {
        moves[n++] = new Move(from, to, Move.PromotionFlag(PieceType.Queen, capture));
        moves[n++] = new Move(from, to, Move.PromotionFlag(PieceType.Knight, capture));
        moves[n++] = new Move(from, to, Move.PromotionFlag(PieceType.Rook, capture));
        moves[n++] = new Move(from, to, Move.PromotionFlag(PieceType.Bishop, capture));
        return n;
    }

    /// <summary>All legal moves as a list (convenient for UI code; the engine uses spans).</summary>
    public static List<Move> LegalMoves(Position pos)
    {
        Span<Move> buffer = stackalloc Move[MaxMoves];
        int n = Generate(pos, buffer);
        var list = new List<Move>(n);
        for (int i = 0; i < n; i++) list.Add(buffer[i]);
        return list;
    }

    public static bool HasLegalMove(Position pos)
    {
        Span<Move> buffer = stackalloc Move[MaxMoves];
        return Generate(pos, buffer) > 0;
    }

    public static bool IsLegal(Position pos, Move move)
    {
        if (move.IsNone) return false;
        Span<Move> buffer = stackalloc Move[MaxMoves];
        int n = Generate(pos, buffer);
        for (int i = 0; i < n; i++)
            if (buffer[i] == move) return true;
        return false;
    }

    /// <summary>Counts leaf nodes of the legal move tree — the standard move-generator correctness test.</summary>
    public static long Perft(Position pos, int depth)
    {
        if (depth <= 0) return 1;
        Span<Move> moves = stackalloc Move[MaxMoves];
        int n = Generate(pos, moves);
        if (depth == 1) return n;
        long total = 0;
        for (int i = 0; i < n; i++)
        {
            pos.MakeMove(moves[i]);
            total += Perft(pos, depth - 1);
            pos.UnmakeMove();
        }
        return total;
    }

    /// <summary>Perft split by root move ("divide"), for debugging generator bugs.</summary>
    public static List<(Move move, long nodes)> Divide(Position pos, int depth)
    {
        var result = new List<(Move, long)>();
        foreach (Move m in LegalMoves(pos))
        {
            pos.MakeMove(m);
            result.Add((m, Perft(pos, depth - 1)));
            pos.UnmakeMove();
        }
        return result;
    }
}
