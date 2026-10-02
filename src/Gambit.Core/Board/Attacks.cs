using System.Runtime.CompilerServices;

namespace Gambit.Core.Board;

/// <summary>
/// Precomputed attack tables. Sliding pieces use "fancy" magic bitboards whose magic numbers are
/// found at startup with a fixed-seed PRNG (a few milliseconds, fully deterministic).
/// </summary>
public static class Attacks
{
    private static readonly ulong[] KnightTable = new ulong[64];
    private static readonly ulong[] KingTable = new ulong[64];
    private static readonly ulong[][] PawnTable = [new ulong[64], new ulong[64]];

    private static readonly ulong[] RookMasks = new ulong[64];
    private static readonly ulong[] BishopMasks = new ulong[64];
    private static readonly ulong[] RookMagics = new ulong[64];
    private static readonly ulong[] BishopMagics = new ulong[64];
    private static readonly int[] RookShifts = new int[64];
    private static readonly int[] BishopShifts = new int[64];
    private static readonly int[] RookOffsets = new int[64];
    private static readonly int[] BishopOffsets = new int[64];
    private static readonly ulong[] RookTable;
    private static readonly ulong[] BishopTable;

    /// <summary>Squares strictly between two aligned squares (empty if not aligned).</summary>
    private static readonly ulong[] BetweenTable = new ulong[64 * 64];

    /// <summary>The full line through two aligned squares, edge to edge (empty if not aligned).</summary>
    private static readonly ulong[] LineTable = new ulong[64 * 64];

    private static readonly (int df, int dr)[] RookDirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int df, int dr)[] BishopDirs = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    static Attacks()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            KnightTable[sq] = Steps(sq, [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)]);
            KingTable[sq] = Steps(sq, [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)]);
            PawnTable[(int)Color.White][sq] = Steps(sq, [(-1, 1), (1, 1)]);
            PawnTable[(int)Color.Black][sq] = Steps(sq, [(-1, -1), (1, -1)]);
        }

        RookTable = InitMagics(RookDirs, RookMasks, RookMagics, RookShifts, RookOffsets, seed: 0x9E3779B97F4A7C15UL);
        BishopTable = InitMagics(BishopDirs, BishopMasks, BishopMagics, BishopShifts, BishopOffsets, seed: 0xD1B54A32D192ED03UL);

        for (int a = 0; a < 64; a++)
        {
            for (int b = 0; b < 64; b++)
            {
                if (a == b) continue;
                ulong bbA = 1UL << a, bbB = 1UL << b;
                if ((SlidingAttack(RookDirs, a, 0) & bbB) != 0)
                {
                    LineTable[a * 64 + b] = (SlidingAttack(RookDirs, a, 0) & SlidingAttack(RookDirs, b, 0)) | bbA | bbB;
                    BetweenTable[a * 64 + b] = SlidingAttack(RookDirs, a, bbB) & SlidingAttack(RookDirs, b, bbA);
                }
                else if ((SlidingAttack(BishopDirs, a, 0) & bbB) != 0)
                {
                    LineTable[a * 64 + b] = (SlidingAttack(BishopDirs, a, 0) & SlidingAttack(BishopDirs, b, 0)) | bbA | bbB;
                    BetweenTable[a * 64 + b] = SlidingAttack(BishopDirs, a, bbB) & SlidingAttack(BishopDirs, b, bbA);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Knight(int sq) => KnightTable[sq];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong King(int sq) => KingTable[sq];

    /// <summary>Squares attacked by a pawn of color <paramref name="c"/> standing on <paramref name="sq"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Pawn(Color c, int sq) => PawnTable[(int)c][sq];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Rook(int sq, ulong occupied) =>
        RookTable[RookOffsets[sq] + (int)(((occupied & RookMasks[sq]) * RookMagics[sq]) >> RookShifts[sq])];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Bishop(int sq, ulong occupied) =>
        BishopTable[BishopOffsets[sq] + (int)(((occupied & BishopMasks[sq]) * BishopMagics[sq]) >> BishopShifts[sq])];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Queen(int sq, ulong occupied) => Rook(sq, occupied) | Bishop(sq, occupied);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Between(int a, int b) => BetweenTable[a * 64 + b];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Line(int a, int b) => LineTable[a * 64 + b];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Aligned(int a, int b, int c) => (LineTable[a * 64 + b] & (1UL << c)) != 0;

    /// <summary>Attacks of a non-pawn piece type from a square given an occupancy.</summary>
    public static ulong Of(PieceType type, int sq, ulong occupied) => type switch
    {
        PieceType.Knight => KnightTable[sq],
        PieceType.Bishop => Bishop(sq, occupied),
        PieceType.Rook => Rook(sq, occupied),
        PieceType.Queen => Queen(sq, occupied),
        PieceType.King => KingTable[sq],
        _ => 0,
    };

    private static ulong Steps(int sq, (int df, int dr)[] deltas)
    {
        ulong bb = 0;
        int f = Square.File(sq), r = Square.Rank(sq);
        foreach (var (df, dr) in deltas)
        {
            int nf = f + df, nr = r + dr;
            if (nf is >= 0 and < 8 && nr is >= 0 and < 8) bb |= 1UL << Square.Make(nf, nr);
        }
        return bb;
    }

    private static ulong SlidingAttack((int df, int dr)[] dirs, int sq, ulong occupied)
    {
        ulong bb = 0;
        foreach (var (df, dr) in dirs)
        {
            int f = Square.File(sq) + df, r = Square.Rank(sq) + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                int s = Square.Make(f, r);
                bb |= 1UL << s;
                if ((occupied & (1UL << s)) != 0) break;
                f += df;
                r += dr;
            }
        }
        return bb;
    }

    private static ulong[] InitMagics((int df, int dr)[] dirs, ulong[] masks, ulong[] magics, int[] shifts, int[] offsets, ulong seed)
    {
        // Relevant-occupancy masks exclude board edges that cannot block anything further.
        int total = 0;
        for (int sq = 0; sq < 64; sq++)
        {
            ulong edges = ((Bitboard.Rank1 | Bitboard.Rank8) & ~Bitboard.RankMask(Square.Rank(sq)))
                        | ((Bitboard.FileA | Bitboard.FileH) & ~Bitboard.FileMask(Square.File(sq)));
            masks[sq] = SlidingAttack(dirs, sq, 0) & ~edges;
            int bits = Bitboard.Count(masks[sq]);
            shifts[sq] = 64 - bits;
            offsets[sq] = total;
            total += 1 << bits;
        }

        var table = new ulong[total];
        var occupancy = new ulong[4096];
        var reference = new ulong[4096];
        var epoch = new int[4096];
        var rng = new XorShift64(seed);
        int attempt = 0;

        for (int sq = 0; sq < 64; sq++)
        {
            // Enumerate every subset of the mask (Carry-Rippler trick).
            int size = 0;
            ulong b = 0;
            do
            {
                occupancy[size] = b;
                reference[size] = SlidingAttack(dirs, sq, b);
                size++;
                b = (b - masks[sq]) & masks[sq];
            } while (b != 0);

            int bits = 64 - shifts[sq];
            while (true)
            {
                ulong magic;
                do magic = rng.Sparse();
                while (Bitboard.Count((masks[sq] * magic) >> 56) < 6);

                attempt++;
                bool ok = true;
                for (int i = 0; i < size; i++)
                {
                    int idx = (int)((occupancy[i] * magic) >> shifts[sq]);
                    if (epoch[idx] < attempt)
                    {
                        epoch[idx] = attempt;
                        table[offsets[sq] + idx] = reference[i];
                    }
                    else if (table[offsets[sq] + idx] != reference[i])
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    magics[sq] = magic;
                    break;
                }
            }
            _ = bits;
        }

        return table;
    }

    /// <summary>Small deterministic PRNG used for magic search and Zobrist keys.</summary>
    internal struct XorShift64(ulong seed)
    {
        private ulong _s = seed == 0 ? 0x2545F4914F6CDD1DUL : seed;

        public ulong Next()
        {
            _s ^= _s >> 12;
            _s ^= _s << 25;
            _s ^= _s >> 27;
            return _s * 2685821657736338717UL;
        }

        public ulong Sparse() => Next() & Next() & Next();
    }
}
