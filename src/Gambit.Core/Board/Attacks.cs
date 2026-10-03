using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gambit.Core.Board;

/// <summary>
/// Precomputed attack tables. Sliding pieces use "fancy" magic bitboards; the magic factors below
/// were found once with a fixed-seed search (searching at startup took about a second).
/// </summary>
public static class Attacks
{
    /// <summary>Everything a magic lookup needs for one square, kept together in memory.</summary>
    private struct Magic
    {
        public ulong Mask;
        public ulong Factor;
        public int Shift;
        public int Offset;
    }

    private static readonly ulong[] KnightTable = new ulong[64];
    private static readonly ulong[] KingTable = new ulong[64];
    private static readonly ulong[] PawnTable = new ulong[2 * 64]; // [color * 64 + square]

    private static readonly Magic[] RookMagics = new Magic[64];
    private static readonly Magic[] BishopMagics = new Magic[64];
    private static readonly ulong[] RookTable;
    private static readonly ulong[] BishopTable;

    /// <summary>Squares strictly between two aligned squares (empty if not aligned).</summary>
    private static readonly ulong[] BetweenTable = new ulong[64 * 64];

    /// <summary>The full line through two aligned squares, edge to edge (empty if not aligned).</summary>
    private static readonly ulong[] LineTable = new ulong[64 * 64];

    private static readonly (int df, int dr)[] RookDirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int df, int dr)[] BishopDirs = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    private static readonly ulong[] RookFactors =
    [
        0x1080004008801020UL, 0x0840092002C03000UL, 0x1900200010400900UL, 0x0880100008000480UL,
        0x4200100420080200UL, 0x8100020100080400UL, 0x0200040110886200UL, 0x0200008040220411UL,
        0x0404800084400220UL, 0x0000401000402000UL, 0x0086001081220440UL, 0x0408800800100280UL,
        0x000A001201040820UL, 0x8848800200840080UL, 0x4001000100040200UL, 0x0442000102105084UL,
        0x9080010020804100UL, 0x0040404000201009UL, 0x0000808010002009UL, 0x2200090021D00100UL,
        0x0008008008040080UL, 0x0004004002010040UL, 0x0011040008015042UL, 0x00000A0001768104UL,
        0x0000800080204009UL, 0x2010004140002001UL, 0x9800200280100080UL, 0x1000100080080080UL,
        0x0442000A00049020UL, 0x2100040080020080UL, 0x0800120400900148UL, 0x0010040A00128541UL,
        0x2800804000800030UL, 0x1010002000400041UL, 0x4000200011004100UL, 0x0610008410800800UL,
        0x0400802402800800UL, 0xC100020080800400UL, 0x0002000802000401UL, 0x0182085882000401UL,
        0x0220204000808000UL, 0x2860100040024022UL, 0x0001002004110040UL, 0x99101042000A0020UL,
        0x0004080004008080UL, 0x0010040002008080UL, 0x2012004881020004UL, 0x8300842444820011UL,
        0x0088403882010200UL, 0x0820400080210100UL, 0x0110910040A00300UL, 0x0801100280080480UL,
        0x0242009008200600UL, 0x1002000489500200UL, 0x0040800200010080UL, 0x0091800041000080UL,
        0x0000209300488001UL, 0x04C1002414824001UL, 0x020020000B001041UL, 0x7000100004200901UL,
        0x8002002004100802UL, 0x30010002084C0007UL, 0x0888221800813004UL, 0x4000002840840112UL,
    ];
    private static readonly ulong[] BishopFactors =
    [
        0x2048017020910100UL, 0x0044410424008008UL, 0x040828A400900000UL, 0x8002209200022000UL,
        0x0002021000540002UL, 0x0021018840000000UL, 0x00009E8420204002UL, 0x00A0920110084480UL,
        0x4003062018010110UL, 0x0221046812004E09UL, 0x01E11002958912A0UL, 0x0000044410804000UL,
        0x0000821210000080UL, 0x080201102210A800UL, 0x0080040411045004UL, 0x00704A1842021000UL,
        0x1005061070322800UL, 0x0018001010410444UL, 0x0010000800401420UL, 0x2204002844000800UL,
        0x2052020412022280UL, 0x000A020101008208UL, 0x0040400201042000UL, 0x03E1082040480410UL,
        0x1004200004208414UL, 0x08700400984808C8UL, 0x0088080004004410UL, 0x008C0240140100A2UL,
        0x0008840001822000UL, 0x0050088001080100UL, 0x98140840040A2200UL, 0x3002020900210110UL,
        0x1004040640206000UL, 0x1090909000840400UL, 0x9002444810100020UL, 0x4000020080080080UL,
        0x0028020400011010UL, 0x0290808300020100UL, 0x8010020882004410UL, 0x0604010040082C20UL,
        0x20040104C0801008UL, 0x6004208424001050UL, 0x1002840041000800UL, 0x0200042018000102UL,
        0xA8002000A0821C00UL, 0x0040080802201910UL, 0x0222620444000100UL, 0x0002080041020088UL,
        0x1500820110401050UL, 0x0000492090100080UL, 0x0900410041100000UL, 0x0302000420880000UL,
        0x0010501202020020UL, 0x0008200490049040UL, 0x0462080214A40120UL, 0x2421310102008100UL,
        0x2400420080884060UL, 0x0800804406184208UL, 0x0B0080124A084400UL, 0x082E082300840412UL,
        0x6051049040082200UL, 0xC610211002102101UL, 0x0000048808010433UL, 0x0010200804405440UL,
    ];

    static Attacks()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            KnightTable[sq] = Steps(sq, [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)]);
            KingTable[sq] = Steps(sq, [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)]);
            PawnTable[(int)Color.White * 64 + sq] = Steps(sq, [(-1, 1), (1, 1)]);
            PawnTable[(int)Color.Black * 64 + sq] = Steps(sq, [(-1, -1), (1, -1)]);
        }

        RookTable = InitMagics(RookDirs, RookFactors, RookMagics);
        BishopTable = InitMagics(BishopDirs, BishopFactors, BishopMagics);

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
    public static ulong Pawn(Color c, int sq) => PawnTable[((int)c << 6) | sq];

    // Slider lookups run millions of times a second: no bounds checks (the index is in range for
    // any square 0..63 by construction of the tables).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Rook(int sq, ulong occupied)
    {
        ref Magic m = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RookMagics), sq & 63);
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(RookTable), m.Offset + (int)(((occupied & m.Mask) * m.Factor) >> m.Shift));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Bishop(int sq, ulong occupied)
    {
        ref Magic m = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(BishopMagics), sq & 63);
        return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(BishopTable), m.Offset + (int)(((occupied & m.Mask) * m.Factor) >> m.Shift));
    }

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

    /// <summary>Builds the attack table for every square from its magic factor (and checks the factor works).</summary>
    private static ulong[] InitMagics((int df, int dr)[] dirs, ulong[] factors, Magic[] magics)
    {
        // Relevant-occupancy masks exclude board edges that cannot block anything further.
        int total = 0;
        for (int sq = 0; sq < 64; sq++)
        {
            ulong edges = ((Bitboard.Rank1 | Bitboard.Rank8) & ~Bitboard.RankMask(Square.Rank(sq)))
                        | ((Bitboard.FileA | Bitboard.FileH) & ~Bitboard.FileMask(Square.File(sq)));
            ulong mask = SlidingAttack(dirs, sq, 0) & ~edges;
            int bits = Bitboard.Count(mask);
            magics[sq] = new Magic { Mask = mask, Factor = factors[sq], Shift = 64 - bits, Offset = total };
            total += 1 << bits;
        }

        var table = new ulong[total];
        var filled = new bool[total];
        for (int sq = 0; sq < 64; sq++)
        {
            Magic m = magics[sq];
            // Every subset of the mask (Carry-Rippler trick).
            ulong b = 0;
            do
            {
                ulong attack = SlidingAttack(dirs, sq, b);
                int idx = m.Offset + (int)((b * m.Factor) >> m.Shift);
                if (filled[idx] && table[idx] != attack)
                    throw new InvalidOperationException($"Magic factor for square {sq} collides.");
                table[idx] = attack;
                filled[idx] = true;
                b = (b - m.Mask) & m.Mask;
            } while (b != 0);
        }
        return table;
    }

    /// <summary>Small deterministic PRNG (Zobrist keys).</summary>
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
    }
}
