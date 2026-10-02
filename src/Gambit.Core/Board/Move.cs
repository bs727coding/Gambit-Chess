using System.Runtime.CompilerServices;

namespace Gambit.Core.Board;

/// <summary>
/// Move kind stored in the top 4 bits of a <see cref="Move"/>.
/// Bit 2 = capture, bit 3 = promotion, low 2 bits of a promotion = piece (N, B, R, Q).
/// </summary>
public enum MoveFlag : byte
{
    Quiet = 0,
    DoublePawnPush = 1,
    KingCastle = 2,
    QueenCastle = 3,
    Capture = 4,
    EnPassant = 5,
    KnightPromotion = 8,
    BishopPromotion = 9,
    RookPromotion = 10,
    QueenPromotion = 11,
    KnightPromotionCapture = 12,
    BishopPromotionCapture = 13,
    RookPromotionCapture = 14,
    QueenPromotionCapture = 15,
}

/// <summary>A move packed into 16 bits: from (6) | to (6) | flag (4). <see cref="None"/> is all zeroes.</summary>
public readonly struct Move : IEquatable<Move>
{
    private readonly ushort _value;

    public static readonly Move None = default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Move(int from, int to, MoveFlag flag = MoveFlag.Quiet) =>
        _value = (ushort)(from | (to << 6) | ((int)flag << 12));

    private Move(ushort raw) => _value = raw;

    public static Move FromRaw(ushort raw) => new(raw);

    public ushort Raw => _value;

    public int From
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value & 63;
    }

    public int To
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_value >> 6) & 63;
    }

    public MoveFlag Flag
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (MoveFlag)(_value >> 12);
    }

    public bool IsNone => _value == 0;
    public bool IsCapture => (_value & (4 << 12)) != 0;
    public bool IsPromotion => (_value & (8 << 12)) != 0;
    public bool IsCastle => Flag is MoveFlag.KingCastle or MoveFlag.QueenCastle;
    public bool IsEnPassant => Flag == MoveFlag.EnPassant;
    public bool IsDoublePawnPush => Flag == MoveFlag.DoublePawnPush;

    /// <summary>Captures and promotions — the moves quiescence search looks at.</summary>
    public bool IsNoisy => (_value & (12 << 12)) != 0;

    public PieceType PromotionType => IsPromotion ? (PieceType)(((_value >> 12) & 3) + 2) : PieceType.None;

    public static MoveFlag PromotionFlag(PieceType type, bool capture) =>
        (MoveFlag)(8 + ((int)type - 2) + (capture ? 4 : 0));

    /// <summary>Long algebraic / UCI form, e.g. <c>e2e4</c>, <c>e7e8q</c>.</summary>
    public string ToUci()
    {
        if (IsNone) return "0000";
        string s = Square.Name(From) + Square.Name(To);
        return IsPromotion ? s + char.ToLowerInvariant(PromotionType.SanLetter()[0]) : s;
    }

    public override string ToString() => ToUci();

    public bool Equals(Move other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Move m && Equals(m);
    public override int GetHashCode() => _value;
    public static bool operator ==(Move a, Move b) => a._value == b._value;
    public static bool operator !=(Move a, Move b) => a._value != b._value;
}
