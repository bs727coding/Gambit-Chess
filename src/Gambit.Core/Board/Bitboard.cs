using System.Numerics;
using System.Runtime.CompilerServices;

namespace Gambit.Core.Board;

/// <summary>Helpers for 64-bit square sets (bit i = square i, a1 = bit 0).</summary>
public static class Bitboard
{
    public const ulong Empty = 0UL;
    public const ulong All = ~0UL;

    public const ulong FileA = 0x0101010101010101UL;
    public const ulong FileB = FileA << 1;
    public const ulong FileG = FileA << 6;
    public const ulong FileH = FileA << 7;
    public const ulong Rank1 = 0xFFUL;
    public const ulong Rank2 = Rank1 << 8;
    public const ulong Rank3 = Rank1 << 16;
    public const ulong Rank4 = Rank1 << 24;
    public const ulong Rank5 = Rank1 << 32;
    public const ulong Rank6 = Rank1 << 40;
    public const ulong Rank7 = Rank1 << 48;
    public const ulong Rank8 = Rank1 << 56;
    public const ulong LightSquares = 0x55AA55AA55AA55AAUL;
    public const ulong DarkSquares = ~LightSquares;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Of(int sq) => 1UL << sq;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains(ulong bb, int sq) => (bb & (1UL << sq)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count(ulong bb) => BitOperations.PopCount(bb);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Lsb(ulong bb) => BitOperations.TrailingZeroCount(bb);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Msb(ulong bb) => 63 - BitOperations.LeadingZeroCount(bb);

    /// <summary>Returns the lowest set square and clears it from <paramref name="bb"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopLsb(ref ulong bb)
    {
        int sq = BitOperations.TrailingZeroCount(bb);
        bb &= bb - 1;
        return sq;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MoreThanOne(ulong bb) => (bb & (bb - 1)) != 0;

    public static ulong FileMask(int file) => FileA << file;
    public static ulong RankMask(int rank) => Rank1 << (rank * 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong North(ulong bb) => bb << 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong South(ulong bb) => bb >> 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong East(ulong bb) => (bb << 1) & ~FileA;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong West(ulong bb) => (bb >> 1) & ~FileH;

    /// <summary>Shift one rank "forward" for the given color.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Forward(ulong bb, Color c) => c == Color.White ? bb << 8 : bb >> 8;

    public static IEnumerable<int> Squares(ulong bb)
    {
        while (bb != 0) yield return PopLsb(ref bb);
    }

    /// <summary>Debug view, rank 8 at the top.</summary>
    public static string ToDiagram(ulong bb)
    {
        var sb = new System.Text.StringBuilder();
        for (int r = 7; r >= 0; r--)
        {
            for (int f = 0; f < 8; f++) sb.Append(Contains(bb, Square.Make(f, r)) ? 'X' : '.');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
