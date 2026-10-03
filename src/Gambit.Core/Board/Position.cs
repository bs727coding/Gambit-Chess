using System.Runtime.CompilerServices;
using System.Text;

namespace Gambit.Core.Board;

/// <summary>
/// A mutable chess position: bitboards + mailbox, side to move, castling, en passant, clocks and an
/// incremental Zobrist key. Moves are applied with <see cref="MakeMove"/> and reverted with
/// <see cref="UnmakeMove"/>; the undo stack doubles as the repetition history.
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // Stored inline (no array objects): read millions of times a second by the engine.
    private TypeBitboards _byType;   // by PieceType (0 and 7 unused)
    private ColorBitboards _byColor; // by Color
    private Mailbox _board;          // the piece on each square
    private StateInfo[] _states = new StateInfo[512];
    private int _stateCount;

    private static readonly CastlingRights[] CastleMask = BuildCastleMask();

    public Color SideToMove { get; private set; }
    public CastlingRights Castling { get; private set; }

    /// <summary>En-passant target square, only set when an enemy pawn could actually capture there.</summary>
    public int EnPassantSquare { get; private set; } = Square.None;

    public int HalfmoveClock { get; private set; }
    public int FullmoveNumber { get; private set; } = 1;
    public ulong Key { get; private set; }

    /// <summary>Enemy pieces giving check to the side to move.</summary>
    public ulong Checkers { get; private set; }

    public bool InCheck => Checkers != 0;

    /// <summary>Number of moves made since this position object was set up (depth of the undo stack).</summary>
    public int Ply => _stateCount;

    private struct StateInfo
    {
        public Move Move;
        public Piece Captured;
        public CastlingRights Castling;
        public int EnPassant;
        public int HalfmoveClock;
        public ulong Key;
        public ulong Checkers;
    }

    public Position() { }

    private Position(Position other)
    {
        _byType = other._byType;
        _byColor = other._byColor;
        _board = other._board;
        _states = (StateInfo[])other._states.Clone();
        _stateCount = other._stateCount;
        SideToMove = other.SideToMove;
        Castling = other.Castling;
        EnPassantSquare = other.EnPassantSquare;
        HalfmoveClock = other.HalfmoveClock;
        FullmoveNumber = other.FullmoveNumber;
        Key = other.Key;
        Checkers = other.Checkers;
    }

    /// <summary>Deep copy including the move history (so repetitions are still detected).</summary>
    public Position Clone() => new(this);

    public static Position FromFen(string fen) => Notation.Fen.Parse(fen);

    public static Position Start() => Notation.Fen.Parse(StartFen);

    public string ToFen() => Notation.Fen.Format(this);

    // ------------------------------------------------------------------ queries

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Piece PieceAt(int sq) => _board[sq];

    public ulong Occupied
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ColorBb(0) | ColorBb(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Pieces(Color c) => ColorBb((int)c);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Pieces(PieceType t) => TypeBb((int)t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Pieces(Color c, PieceType t) => TypeBb((int)t) & ColorBb((int)c);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Pieces(Color c, PieceType t1, PieceType t2) => (TypeBb((int)t1) | TypeBb((int)t2)) & ColorBb((int)c);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int KingSquare(Color c) => Bitboard.Lsb(TypeBb((int)PieceType.King) & ColorBb((int)c));

    public int Count(Color c, PieceType t) => Bitboard.Count(Pieces(c, t));

    /// <summary>All pieces of both colors attacking <paramref name="sq"/> given an occupancy.</summary>
    public ulong AttackersTo(int sq, ulong occupied) =>
        (Attacks.Pawn(Color.White, sq) & Pieces(Color.Black, PieceType.Pawn))
        | (Attacks.Pawn(Color.Black, sq) & Pieces(Color.White, PieceType.Pawn))
        | (Attacks.Knight(sq) & TypeBb((int)PieceType.Knight))
        | (Attacks.King(sq) & TypeBb((int)PieceType.King))
        | (Attacks.Bishop(sq, occupied) & (TypeBb((int)PieceType.Bishop) | TypeBb((int)PieceType.Queen)))
        | (Attacks.Rook(sq, occupied) & (TypeBb((int)PieceType.Rook) | TypeBb((int)PieceType.Queen)));

    public ulong AttackersTo(int sq) => AttackersTo(sq, Occupied);

    /// <summary>Is <paramref name="sq"/> attacked by any piece of color <paramref name="by"/>?</summary>
    public bool IsAttacked(int sq, Color by, ulong occupied)
    {
        if ((Attacks.Pawn(by.Opposite(), sq) & Pieces(by, PieceType.Pawn)) != 0) return true;
        if ((Attacks.Knight(sq) & Pieces(by, PieceType.Knight)) != 0) return true;
        if ((Attacks.King(sq) & Pieces(by, PieceType.King)) != 0) return true;
        if ((Attacks.Bishop(sq, occupied) & Pieces(by, PieceType.Bishop, PieceType.Queen)) != 0) return true;
        return (Attacks.Rook(sq, occupied) & Pieces(by, PieceType.Rook, PieceType.Queen)) != 0;
    }

    public bool IsAttacked(int sq, Color by) => IsAttacked(sq, by, Occupied);

    /// <summary>Pieces of <paramref name="c"/> that are absolutely pinned to their own king.</summary>
    public ulong PinnedPieces(Color c)
    {
        int ksq = KingSquare(c);
        Color them = c.Opposite();
        ulong snipers = (Attacks.Rook(ksq, 0) & Pieces(them, PieceType.Rook, PieceType.Queen))
                      | (Attacks.Bishop(ksq, 0) & Pieces(them, PieceType.Bishop, PieceType.Queen));
        ulong occ = Occupied, pinned = 0;
        while (snipers != 0)
        {
            int s = Bitboard.PopLsb(ref snipers);
            ulong between = Attacks.Between(ksq, s) & occ;
            if (between != 0 && !Bitboard.MoreThanOne(between) && (between & Pieces(c)) != 0)
                pinned |= between;
        }
        return pinned;
    }

    /// <summary>True if <paramref name="c"/> has any knight, bishop, rook or queen.</summary>
    public bool HasNonPawnMaterial(Color c) =>
        (Pieces(c) & ~(Pieces(PieceType.Pawn) | Pieces(PieceType.King))) != 0;

    /// <summary>The last move made on this position object, or <see cref="Move.None"/>.</summary>
    public Move LastMove => _stateCount > 0 ? _states[_stateCount - 1].Move : Move.None;

    /// <summary>The piece captured by the last move (if any).</summary>
    public Piece LastCaptured => _stateCount > 0 ? _states[_stateCount - 1].Captured : Piece.None;

    /// <summary>
    /// How many earlier positions in the reversible history are identical to the current one
    /// (same pieces, side to move, castling and en-passant rights). 2 or more = threefold repetition.
    /// </summary>
    public int RepetitionCount()
    {
        int count = 0;
        int end = Math.Min(HalfmoveClock, _stateCount);
        for (int i = 4; i <= end; i += 2)
            if (_states[_stateCount - i].Key == Key) count++;
        return count;
    }

    /// <summary>Fast check used by search: has the current position occurred before at all?</summary>
    public bool IsRepetition()
    {
        int end = Math.Min(HalfmoveClock, _stateCount);
        for (int i = 4; i <= end; i += 2)
            if (_states[_stateCount - i].Key == Key) return true;
        return false;
    }

    /// <summary>Neither side can possibly deliver mate (FIDE dead position for the common cases).</summary>
    public bool IsInsufficientMaterial()
    {
        if ((Pieces(PieceType.Pawn) | Pieces(PieceType.Rook) | Pieces(PieceType.Queen)) != 0) return false;
        ulong minors = Pieces(PieceType.Knight) | Pieces(PieceType.Bishop);
        if (Bitboard.Count(minors) <= 1) return true;
        if (Pieces(PieceType.Knight) == 0)
        {
            ulong b = Pieces(PieceType.Bishop);
            return (b & Bitboard.LightSquares) == 0 || (b & Bitboard.DarkSquares) == 0;
        }
        return false;
    }

    /// <summary>Can <paramref name="c"/> ever checkmate (used for "timeout vs insufficient material")?</summary>
    public bool HasMatingMaterial(Color c)
    {
        if (Pieces(c, PieceType.Pawn, PieceType.Rook) != 0 || Pieces(c, PieceType.Queen) != 0) return true;
        return Bitboard.Count(Pieces(c, PieceType.Knight, PieceType.Bishop)) >= 2;
    }

    // ------------------------------------------------------------------ make / unmake

    public void MakeMove(Move m)
    {
        if (_stateCount == _states.Length) Array.Resize(ref _states, _states.Length * 2);
        ref StateInfo st = ref _states[_stateCount++];
        st.Move = m;
        st.Captured = Piece.None;
        st.Castling = Castling;
        st.EnPassant = EnPassantSquare;
        st.HalfmoveClock = HalfmoveClock;
        st.Key = Key;
        st.Checkers = Checkers;

        int from = m.From, to = m.To;
        Piece piece = BoardAt(from);
        Color us = SideToMove, them = us.Opposite();
        ulong key = Key;

        if (EnPassantSquare != Square.None)
        {
            key ^= Zobrist.EnPassant(EnPassantSquare);
            EnPassantSquare = Square.None;
        }

        HalfmoveClock++;
        MoveFlag flag = m.Flag;

        if (flag is MoveFlag.KingCastle or MoveFlag.QueenCastle)
        {
            int rookFrom = flag == MoveFlag.KingCastle ? to + 1 : to - 2;
            int rookTo = flag == MoveFlag.KingCastle ? to - 1 : to + 1;
            Piece rook = BoardAt(rookFrom);
            MovePieceRaw(from, to, piece);
            MovePieceRaw(rookFrom, rookTo, rook);
            key ^= Zobrist.Piece(piece, from) ^ Zobrist.Piece(piece, to)
                 ^ Zobrist.Piece(rook, rookFrom) ^ Zobrist.Piece(rook, rookTo);
        }
        else
        {
            if (m.IsCapture)
            {
                int capSq = flag == MoveFlag.EnPassant ? (us == Color.White ? to - 8 : to + 8) : to;
                Piece captured = BoardAt(capSq);
                st.Captured = captured;
                RemovePieceRaw(capSq, captured);
                key ^= Zobrist.Piece(captured, capSq);
                HalfmoveClock = 0;
            }

            MovePieceRaw(from, to, piece);
            key ^= Zobrist.Piece(piece, from) ^ Zobrist.Piece(piece, to);

            if (piece.Type() == PieceType.Pawn)
            {
                HalfmoveClock = 0;
                if (flag == MoveFlag.DoublePawnPush)
                {
                    int epSq = (from + to) >> 1;
                    if ((Attacks.Pawn(us, epSq) & Pieces(them, PieceType.Pawn)) != 0)
                    {
                        EnPassantSquare = epSq;
                        key ^= Zobrist.EnPassant(epSq);
                    }
                }
                else if (m.IsPromotion)
                {
                    Piece promo = m.PromotionType.Of(us);
                    RemovePieceRaw(to, piece);
                    AddPieceRaw(to, promo);
                    key ^= Zobrist.Piece(piece, to) ^ Zobrist.Piece(promo, to);
                }
            }
        }

        CastlingRights rights = Castling & CastleMask[from] & CastleMask[to];
        if (rights != Castling)
        {
            key ^= Zobrist.Castling(Castling) ^ Zobrist.Castling(rights);
            Castling = rights;
        }

        if (us == Color.Black) FullmoveNumber++;
        SideToMove = them;
        Key = key ^ Zobrist.SideToMove;
        ulong theirKing = Pieces(them, PieceType.King);
        Checkers = theirKing == 0 ? 0 : AttackersTo(Bitboard.Lsb(theirKing), Occupied) & Pieces(us);
    }

    public void UnmakeMove()
    {
        ref StateInfo st = ref _states[--_stateCount];
        Move m = st.Move;
        SideToMove = SideToMove.Opposite();
        Color us = SideToMove;
        if (us == Color.Black) FullmoveNumber--;

        int from = m.From, to = m.To;
        MoveFlag flag = m.Flag;

        if (flag is MoveFlag.KingCastle or MoveFlag.QueenCastle)
        {
            int rookFrom = flag == MoveFlag.KingCastle ? to + 1 : to - 2;
            int rookTo = flag == MoveFlag.KingCastle ? to - 1 : to + 1;
            MovePieceRaw(to, from, BoardAt(to));
            MovePieceRaw(rookTo, rookFrom, BoardAt(rookTo));
        }
        else
        {
            if (m.IsPromotion)
            {
                RemovePieceRaw(to, BoardAt(to));
                AddPieceRaw(to, PieceType.Pawn.Of(us));
            }

            MovePieceRaw(to, from, BoardAt(to));

            if (st.Captured != Piece.None)
            {
                int capSq = flag == MoveFlag.EnPassant ? (us == Color.White ? to - 8 : to + 8) : to;
                AddPieceRaw(capSq, st.Captured);
            }
        }

        Castling = st.Castling;
        EnPassantSquare = st.EnPassant;
        HalfmoveClock = st.HalfmoveClock;
        Key = st.Key;
        Checkers = st.Checkers;
    }

    /// <summary>Pass the turn (search only). Must not be called while in check.</summary>
    public void MakeNullMove()
    {
        if (_stateCount == _states.Length) Array.Resize(ref _states, _states.Length * 2);
        ref StateInfo st = ref _states[_stateCount++];
        st.Move = Move.None;
        st.Captured = Piece.None;
        st.Castling = Castling;
        st.EnPassant = EnPassantSquare;
        st.HalfmoveClock = HalfmoveClock;
        st.Key = Key;
        st.Checkers = Checkers;

        ulong key = Key;
        if (EnPassantSquare != Square.None)
        {
            key ^= Zobrist.EnPassant(EnPassantSquare);
            EnPassantSquare = Square.None;
        }
        HalfmoveClock++;
        SideToMove = SideToMove.Opposite();
        Key = key ^ Zobrist.SideToMove;
        Checkers = 0;
    }

    public void UnmakeNullMove()
    {
        ref StateInfo st = ref _states[--_stateCount];
        SideToMove = SideToMove.Opposite();
        EnPassantSquare = st.EnPassant;
        HalfmoveClock = st.HalfmoveClock;
        Key = st.Key;
        Checkers = st.Checkers;
    }

    /// <summary>Forget the undo history (keeps the position). Used when a position becomes a new root.</summary>
    public void ClearHistory() => _stateCount = 0;

    // ------------------------------------------------------------------ setup (used by FEN parser / board editor)

    internal void Clear()
    {
        _byType = default;
        _byColor = default;
        _board = default;
        _stateCount = 0;
        SideToMove = Color.White;
        Castling = CastlingRights.None;
        EnPassantSquare = Square.None;
        HalfmoveClock = 0;
        FullmoveNumber = 1;
        Key = 0;
        Checkers = 0;
    }

    internal void PutPiece(int sq, Piece p)
    {
        if (_board[sq] != Piece.None) RemovePieceRaw(sq, _board[sq]);
        if (p != Piece.None) AddPieceRaw(sq, p);
    }

    internal void SetState(Color side, CastlingRights castling, int epSquare, int halfmove, int fullmove)
    {
        SideToMove = side;
        Castling = castling;
        EnPassantSquare = epSquare;
        HalfmoveClock = Math.Max(0, halfmove);
        FullmoveNumber = Math.Max(1, fullmove);
    }

    /// <summary>Sanitizes castling/en-passant against the actual pieces and recomputes key + checkers.</summary>
    internal void FinishSetup()
    {
        CastlingRights c = Castling;
        if (_board[Square.E1] != Piece.WhiteKing) c &= ~CastlingRights.White;
        if (_board[Square.H1] != Piece.WhiteRook) c &= ~CastlingRights.WhiteKingSide;
        if (_board[Square.A1] != Piece.WhiteRook) c &= ~CastlingRights.WhiteQueenSide;
        if (_board[Square.E8] != Piece.BlackKing) c &= ~CastlingRights.Black;
        if (_board[Square.H8] != Piece.BlackRook) c &= ~CastlingRights.BlackKingSide;
        if (_board[Square.A8] != Piece.BlackRook) c &= ~CastlingRights.BlackQueenSide;
        Castling = c;

        if (EnPassantSquare != Square.None)
        {
            Color us = SideToMove, them = us.Opposite();
            int expectedRank = us == Color.White ? 5 : 2;
            int pushedPawn = us == Color.White ? EnPassantSquare - 8 : EnPassantSquare + 8;
            bool valid = Square.Rank(EnPassantSquare) == expectedRank
                && _board[pushedPawn] == PieceType.Pawn.Of(them)
                && _board[EnPassantSquare] == Piece.None
                && (Attacks.Pawn(them, EnPassantSquare) & Pieces(us, PieceType.Pawn)) != 0;
            if (!valid) EnPassantSquare = Square.None;
        }

        ulong key = 0;
        for (int sq = 0; sq < 64; sq++)
            if (_board[sq] != Piece.None) key ^= Zobrist.Piece(_board[sq], sq);
        key ^= Zobrist.Castling(Castling);
        if (EnPassantSquare != Square.None) key ^= Zobrist.EnPassant(EnPassantSquare);
        if (SideToMove == Color.Black) key ^= Zobrist.SideToMove;
        Key = key;

        ulong kings = Pieces(SideToMove, PieceType.King);
        Checkers = kings == 0 ? 0 : AttackersTo(Bitboard.Lsb(kings), Occupied) & Pieces(SideToMove.Opposite());
        _stateCount = 0;
    }

    /// <summary>Returns null if the position is legal enough to play from, otherwise a reason.</summary>
    public string? Validate()
    {
        if (Count(Color.White, PieceType.King) != 1) return "White must have exactly one king.";
        if (Count(Color.Black, PieceType.King) != 1) return "Black must have exactly one king.";
        if ((Pieces(PieceType.Pawn) & (Bitboard.Rank1 | Bitboard.Rank8)) != 0) return "Pawns cannot stand on the first or last rank.";
        if (Bitboard.Count(Pieces(Color.White)) > 16 || Bitboard.Count(Pieces(Color.Black)) > 16) return "A side cannot have more than 16 pieces.";
        if (Count(Color.White, PieceType.Pawn) > 8 || Count(Color.Black, PieceType.Pawn) > 8) return "A side cannot have more than 8 pawns.";
        Color them = SideToMove.Opposite();
        if (IsAttacked(KingSquare(them), SideToMove)) return $"{them.Name()} is in check but it is {SideToMove.Name()}'s turn.";
        if (Bitboard.Count(Checkers) > 2) return "Too many pieces are giving check.";
        return null;
    }

    // ------------------------------------------------------------------ raw piece ops

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddPieceRaw(int sq, Piece p)
    {
        ulong bb = 1UL << sq;
        BoardAt(sq) = p;
        TypeBb((int)p) |= bb;
        ColorBb((int)p >> 3) |= bb;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemovePieceRaw(int sq, Piece p)
    {
        ulong bb = 1UL << sq;
        BoardAt(sq) = Piece.None;
        TypeBb((int)p) &= ~bb;
        ColorBb((int)p >> 3) &= ~bb;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MovePieceRaw(int from, int to, Piece p)
    {
        ulong fromTo = (1UL << from) | (1UL << to);
        BoardAt(from) = Piece.None;
        BoardAt(to) = p;
        TypeBb((int)p) ^= fromTo;
        ColorBb((int)p >> 3) ^= fromTo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ulong TypeBb(int type) => ref Unsafe.Add(ref Unsafe.As<TypeBitboards, ulong>(ref _byType), type & 7);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ulong ColorBb(int color) => ref Unsafe.Add(ref Unsafe.As<ColorBitboards, ulong>(ref _byColor), color & 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Piece BoardAt(int sq) => ref Unsafe.Add(ref Unsafe.As<Mailbox, Piece>(ref _board), sq & 63);

    [InlineArray(8)]
    private struct TypeBitboards
    {
        private ulong _element;
    }

    [InlineArray(2)]
    private struct ColorBitboards
    {
        private ulong _element;
    }

    [InlineArray(64)]
    private struct Mailbox
    {
        private Piece _element;
    }

    private static CastlingRights[] BuildCastleMask()
    {
        var mask = new CastlingRights[64];
        Array.Fill(mask, CastlingRights.All);
        mask[Square.A1] = CastlingRights.All & ~CastlingRights.WhiteQueenSide;
        mask[Square.H1] = CastlingRights.All & ~CastlingRights.WhiteKingSide;
        mask[Square.E1] = CastlingRights.All & ~CastlingRights.White;
        mask[Square.A8] = CastlingRights.All & ~CastlingRights.BlackQueenSide;
        mask[Square.H8] = CastlingRights.All & ~CastlingRights.BlackKingSide;
        mask[Square.E8] = CastlingRights.All & ~CastlingRights.Black;
        return mask;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        for (int r = 7; r >= 0; r--)
        {
            sb.Append(r + 1).Append(' ');
            for (int f = 0; f < 8; f++)
            {
                Piece p = _board[Square.Make(f, r)];
                sb.Append(p == Piece.None ? '.' : p.ToFenChar()).Append(' ');
            }
            sb.Append('\n');
        }
        sb.Append("  a b c d e f g h\n").Append(ToFen());
        return sb.ToString();
    }
}
