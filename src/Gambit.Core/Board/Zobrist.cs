namespace Gambit.Core.Board;

/// <summary>Deterministic Zobrist keys (fixed seed) so hashes are stable across runs and machines.</summary>
public static class Zobrist
{
    private static readonly ulong[] PieceSquare = new ulong[16 * 64];
    private static readonly ulong[] CastlingKeys = new ulong[16];
    private static readonly ulong[] EnPassantFile = new ulong[8];

    public static readonly ulong SideToMove;

    static Zobrist()
    {
        var rng = new Attacks.XorShift64(0x1F83D9ABFB41BD6BUL);
        for (int i = 0; i < PieceSquare.Length; i++) PieceSquare[i] = rng.Next();
        for (int i = 0; i < CastlingKeys.Length; i++) CastlingKeys[i] = rng.Next();
        for (int i = 0; i < EnPassantFile.Length; i++) EnPassantFile[i] = rng.Next();
        SideToMove = rng.Next();
    }

    public static ulong Piece(Piece p, int sq) => PieceSquare[((int)p << 6) | sq];
    public static ulong Castling(CastlingRights rights) => CastlingKeys[(int)rights];
    public static ulong EnPassant(int sq) => EnPassantFile[sq & 7];
}
