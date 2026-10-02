using System.Runtime.CompilerServices;

namespace Gambit.Core.Board;

public enum Color : byte
{
    White = 0,
    Black = 1,
}

public enum PieceType : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6,
}

/// <summary>A colored piece. Low 3 bits = <see cref="PieceType"/>, bit 3 = <see cref="Color"/>.</summary>
public enum Piece : byte
{
    None = 0,
    WhitePawn = 1, WhiteKnight = 2, WhiteBishop = 3, WhiteRook = 4, WhiteQueen = 5, WhiteKing = 6,
    BlackPawn = 9, BlackKnight = 10, BlackBishop = 11, BlackRook = 12, BlackQueen = 13, BlackKing = 14,
}

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    White = WhiteKingSide | WhiteQueenSide,
    Black = BlackKingSide | BlackQueenSide,
    All = White | Black,
}

public static class ColorExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Opposite(this Color c) => (Color)((int)c ^ 1);

    /// <summary>+1 for White, -1 for Black.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Sign(this Color c) => c == Color.White ? 1 : -1;

    public static string Name(this Color c) => c == Color.White ? "White" : "Black";
}

public static class PieceExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PieceType Type(this Piece p) => (PieceType)((int)p & 7);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Color(this Piece p) => (Color)((int)p >> 3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Piece Of(this PieceType t, Color c) => (Piece)(((int)c << 3) | (int)t);

    public static char ToFenChar(this Piece p)
    {
        char c = p.Type() switch
        {
            PieceType.Pawn => 'p',
            PieceType.Knight => 'n',
            PieceType.Bishop => 'b',
            PieceType.Rook => 'r',
            PieceType.Queen => 'q',
            PieceType.King => 'k',
            _ => '.',
        };
        return p.Color() == Board.Color.White ? char.ToUpperInvariant(c) : c;
    }

    public static Piece FromFenChar(char c)
    {
        var color = char.IsUpper(c) ? Board.Color.White : Board.Color.Black;
        PieceType t = char.ToLowerInvariant(c) switch
        {
            'p' => PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => PieceType.None,
        };
        return t == PieceType.None ? Piece.None : t.Of(color);
    }

    /// <summary>Upper-case SAN letter (empty for pawns).</summary>
    public static string SanLetter(this PieceType t) => t switch
    {
        PieceType.Knight => "N",
        PieceType.Bishop => "B",
        PieceType.Rook => "R",
        PieceType.Queen => "Q",
        PieceType.King => "K",
        _ => "",
    };

    /// <summary>Conventional material value in centipawns (for UI material counts, not the engine).</summary>
    public static int NominalValue(this PieceType t) => t switch
    {
        PieceType.Pawn => 1,
        PieceType.Knight => 3,
        PieceType.Bishop => 3,
        PieceType.Rook => 5,
        PieceType.Queen => 9,
        _ => 0,
    };
}

/// <summary>Squares are ints 0..63 with a1 = 0, b1 = 1, ..., h8 = 63.</summary>
public static class Square
{
    public const int None = -1;

    public const int A1 = 0, B1 = 1, C1 = 2, D1 = 3, E1 = 4, F1 = 5, G1 = 6, H1 = 7;
    public const int A2 = 8, B2 = 9, C2 = 10, D2 = 11, E2 = 12, F2 = 13, G2 = 14, H2 = 15;
    public const int A3 = 16, B3 = 17, C3 = 18, D3 = 19, E3 = 20, F3 = 21, G3 = 22, H3 = 23;
    public const int A4 = 24, B4 = 25, C4 = 26, D4 = 27, E4 = 28, F4 = 29, G4 = 30, H4 = 31;
    public const int A5 = 32, B5 = 33, C5 = 34, D5 = 35, E5 = 36, F5 = 37, G5 = 38, H5 = 39;
    public const int A6 = 40, B6 = 41, C6 = 42, D6 = 43, E6 = 44, F6 = 45, G6 = 46, H6 = 47;
    public const int A7 = 48, B7 = 49, C7 = 50, D7 = 51, E7 = 52, F7 = 53, G7 = 54, H7 = 55;
    public const int A8 = 56, B8 = 57, C8 = 58, D8 = 59, E8 = 60, F8 = 61, G8 = 62, H8 = 63;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int File(int sq) => sq & 7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Rank(int sq) => sq >> 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Make(int file, int rank) => (rank << 3) | file;

    /// <summary>Rank from the given side's point of view (0 = its back rank).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RelativeRank(int sq, Color c) => c == Color.White ? sq >> 3 : 7 - (sq >> 3);

    /// <summary>Mirror vertically (a1 ↔ a8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Flip(int sq) => sq ^ 56;

    public static bool IsValid(int sq) => sq is >= 0 and < 64;

    public static bool IsLight(int sq) => ((File(sq) + Rank(sq)) & 1) == 1;

    public static int Distance(int a, int b) =>
        Math.Max(Math.Abs(File(a) - File(b)), Math.Abs(Rank(a) - Rank(b)));

    public static string Name(int sq) =>
        sq is < 0 or > 63 ? "-" : string.Create(2, sq, static (span, s) =>
        {
            span[0] = (char)('a' + (s & 7));
            span[1] = (char)('1' + (s >> 3));
        });

    public static int Parse(ReadOnlySpan<char> s)
    {
        if (s.Length < 2) return None;
        int f = s[0] - 'a', r = s[1] - '1';
        return f is >= 0 and < 8 && r is >= 0 and < 8 ? Make(f, r) : None;
    }

    public static char FileChar(int sq) => (char)('a' + File(sq));
    public static char RankChar(int sq) => (char)('1' + Rank(sq));
}
