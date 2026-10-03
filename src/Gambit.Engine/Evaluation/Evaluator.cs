using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gambit.Core.Board;

namespace Gambit.Engine.Evaluation;

/// <summary>
/// Hand-crafted tapered evaluation: PeSTO piece-square tables (Ronald Friederich) plus mobility,
/// pawn structure, passed pawns, rook files, king shelter/attack, bishop pair, endgame scaling and
/// a mop-up term so the engine converts won endings (KQ/KR vs K) cleanly.
/// Scores are centipawns from the side to move's point of view.
/// </summary>
public static class Evaluator
{
    // Material (PeSTO), indexed by PieceType (0 = none ... 6 = king).
    public static readonly int[] MgValue = [0, 82, 337, 365, 477, 1025, 0];
    public static readonly int[] EgValue = [0, 94, 281, 297, 512, 936, 0];

    /// <summary>Simple values for SEE / move ordering / delta pruning.</summary>
    public static readonly int[] SeeValue = [0, 100, 320, 330, 500, 900, 20000];

    private static readonly int[] PhaseInc = [0, 0, 1, 1, 2, 4, 0];

    // [piece (16)][square] -> material + PST from White's point of view (black pieces negative).
    private static readonly int[] MgTable = new int[16 * 64];
    private static readonly int[] EgTable = new int[16 * 64];

    // Tables are written rank 8 first (as on a diagram), from White's point of view.
    private static readonly int[] MgPawn =
    [
          0,   0,   0,   0,   0,   0,  0,   0,
         98, 134,  61,  95,  68, 126, 34, -11,
         -6,   7,  26,  31,  65,  56, 25, -20,
        -14,  13,   6,  21,  23,  12, 17, -23,
        -27,  -2,  -5,  12,  17,   6, 10, -25,
        -26,  -4,  -4, -10,   3,   3, 33, -12,
        -35,  -1, -20, -23, -15,  24, 38, -22,
          0,   0,   0,   0,   0,   0,  0,   0,
    ];

    private static readonly int[] EgPawn =
    [
          0,   0,   0,   0,   0,   0,   0,   0,
        178, 173, 158, 134, 147, 132, 165, 187,
         94, 100,  85,  67,  56,  53,  82,  84,
         32,  24,  13,   5,  -2,   4,  17,  17,
         13,   9,  -3,  -7,  -7,  -8,   3,  -1,
          4,   7,  -6,   1,   0,  -5,  -1,  -8,
         13,   8,   8,  10,  13,   0,   2,  -7,
          0,   0,   0,   0,   0,   0,   0,   0,
    ];

    private static readonly int[] MgKnight =
    [
        -167, -89, -34, -49,  61, -97, -15, -107,
         -73, -41,  72,  36,  23,  62,   7,  -17,
         -47,  60,  37,  65,  84, 129,  73,   44,
          -9,  17,  19,  53,  37,  69,  18,   22,
         -13,   4,  16,  13,  28,  19,  21,   -8,
         -23,  -9,  12,  10,  19,  17,  25,  -16,
         -29, -53, -12,  -3,  -1,  18, -14,  -19,
        -105, -21, -58, -33, -17, -28, -19,  -23,
    ];

    private static readonly int[] EgKnight =
    [
        -58, -38, -13, -28, -31, -27, -63, -99,
        -25,  -8, -25,  -2,  -9, -25, -24, -52,
        -24, -20,  10,   9,  -1,  -9, -19, -41,
        -17,   3,  22,  22,  22,  11,   8, -18,
        -18,  -6,  16,  25,  16,  17,   4, -18,
        -23,  -3,  -1,  15,  10,  -3, -20, -22,
        -42, -20, -10,  -5,  -2, -20, -23, -44,
        -29, -51, -23, -15, -22, -18, -50, -64,
    ];

    private static readonly int[] MgBishop =
    [
        -29,   4, -82, -37, -25, -42,   7,  -8,
        -26,  16, -18, -13,  30,  59,  18, -47,
        -16,  37,  43,  40,  35,  50,  37,  -2,
         -4,   5,  19,  50,  37,  37,   7,  -2,
         -6,  13,  13,  26,  34,  12,  10,   4,
          0,  15,  15,  15,  14,  27,  18,  10,
          4,  15,  16,   0,   7,  21,  33,   1,
        -33,  -3, -14, -21, -13, -12, -39, -21,
    ];

    private static readonly int[] EgBishop =
    [
        -14, -21, -11,  -8,  -7,  -9, -17, -24,
         -8,  -4,   7, -12,  -3, -13,  -4, -14,
          2,  -8,   0,  -1,  -2,   6,   0,   4,
         -3,   9,  12,   9,  14,  10,   3,   2,
         -6,   3,  13,  19,   7,  10,  -3,  -9,
        -12,  -3,   8,  10,  13,   3,  -7, -15,
        -14, -18,  -7,  -1,   4,  -9, -15, -27,
        -23,  -9, -23,  -5,  -9, -16,  -5, -17,
    ];

    private static readonly int[] MgRook =
    [
         32,  42,  32,  51,  63,   9,  31,  43,
         27,  32,  58,  62,  80,  67,  26,  44,
         -5,  19,  26,  36,  17,  45,  61,  16,
        -24, -11,   7,  26,  24,  35,  -8, -20,
        -36, -26, -12,  -1,   9,  -7,   6, -23,
        -45, -25, -16, -17,   3,   0,  -5, -33,
        -44, -16, -20,  -9,  -1,  11,  -6, -71,
        -19, -13,   1,  17,  16,   7, -37, -26,
    ];

    private static readonly int[] EgRook =
    [
        13, 10, 18, 15, 12,  12,   8,   5,
        11, 13, 13, 11, -3,   3,   8,   3,
         7,  7,  7,  5,  4,  -3,  -5,  -3,
         4,  3, 13,  1,  2,   1,  -1,   2,
         3,  5,  8,  4, -5,  -6,  -8, -11,
        -4,  0, -5, -1, -7, -12,  -8, -16,
        -6, -6,  0,  2, -9,  -9, -11,  -3,
        -9,  2,  3, -1, -5, -13,   4, -20,
    ];

    private static readonly int[] MgQueen =
    [
        -28,   0,  29,  12,  59,  44,  43,  45,
        -24, -39,  -5,   1, -16,  57,  28,  54,
        -13, -17,   7,   8,  29,  56,  47,  57,
        -27, -27, -16, -16,  -1,  17,  -2,   1,
         -9, -26,  -9, -10,  -2,  -4,   3,  -3,
        -14,   2, -11,  -2,  -5,   2,  14,   5,
        -35,  -8,  11,   2,   8,  15,  -3,   1,
         -1, -18,  -9,  10, -15, -25, -31, -50,
    ];

    private static readonly int[] EgQueen =
    [
         -9,  22,  22,  27,  27,  19,  10,  20,
        -17,  20,  32,  41,  58,  25,  30,   0,
        -20,   6,   9,  49,  47,  35,  19,   9,
          3,  22,  24,  45,  57,  40,  57,  36,
        -18,  28,  19,  47,  31,  34,  39,  23,
        -16, -27,  15,   6,   9,  17,  10,   5,
        -22, -23, -30, -16, -16, -23, -36, -32,
        -33, -28, -22, -43,  -5, -32, -20, -41,
    ];

    private static readonly int[] MgKing =
    [
        -65,  23,  16, -15, -56, -34,   2,  13,
         29,  -1, -20,  -7,  -8,  -4, -38, -29,
         -9,  24,   2, -16, -20,   6,  22, -22,
        -17, -20, -12, -27, -30, -25, -14, -36,
        -49,  -1, -27, -39, -46, -44, -33, -51,
        -14, -14, -22, -46, -44, -30, -15, -27,
          1,   7,  -8, -64, -43, -16,   9,   8,
        -15,  36,  12, -54,   8, -28,  24,  14,
    ];

    private static readonly int[] EgKing =
    [
        -74, -35, -18, -18, -11,  15,   4, -17,
        -12,  17,  14,  17,  17,  38,  23,  11,
         10,  17,  23,  15,  20,  45,  44,  13,
         -8,  22,  24,  27,  26,  33,  26,   3,
        -18,  -4,  21,  24,  27,  23,   9, -11,
        -19,  -3,  11,  21,  23,  16,   7,  -9,
        -27, -11,   4,  13,  14,   4,  -5, -17,
        -53, -34, -21, -11, -28, -14, -24, -43,
    ];

    // Passed pawn bonus by relative rank.
    private static readonly int[] PassedMg = [0, 5, 10, 15, 25, 45, 70, 0];
    private static readonly int[] PassedEg = [0, 10, 20, 35, 60, 100, 160, 0];

    // Mobility weights and king-zone attack weights are per piece type, in EvaluateSide.
    private static readonly int[] KingDanger = BuildKingDanger();

    private static readonly ulong[] PassedMask = new ulong[2 * 64];
    private static readonly ulong[] AdjacentFiles = new ulong[8];
    private static readonly ulong[] KingZone = new ulong[64];

    static Evaluator()
    {
        int[][] mg = [[], MgPawn, MgKnight, MgBishop, MgRook, MgQueen, MgKing];
        int[][] eg = [[], EgPawn, EgKnight, EgBishop, EgRook, EgQueen, EgKing];
        for (int t = 1; t <= 6; t++)
        {
            for (int sq = 0; sq < 64; sq++)
            {
                int white = (int)((PieceType)t).Of(Color.White), black = (int)((PieceType)t).Of(Color.Black);
                // Our squares have a1 = 0; the tables have a8 = 0. White uses the flipped index, Black the raw one.
                MgTable[white * 64 + sq] = MgValue[t] + mg[t][sq ^ 56];
                EgTable[white * 64 + sq] = EgValue[t] + eg[t][sq ^ 56];
                MgTable[black * 64 + sq] = -(MgValue[t] + mg[t][sq]);
                EgTable[black * 64 + sq] = -(EgValue[t] + eg[t][sq]);
            }
        }

        for (int f = 0; f < 8; f++)
        {
            ulong adj = 0;
            if (f > 0) adj |= Bitboard.FileMask(f - 1);
            if (f < 7) adj |= Bitboard.FileMask(f + 1);
            AdjacentFiles[f] = adj;
        }

        for (int sq = 0; sq < 64; sq++)
        {
            int f = Square.File(sq), r = Square.Rank(sq);
            ulong files = Bitboard.FileMask(f) | AdjacentFiles[f];
            ulong whiteAhead = 0, blackAhead = 0;
            for (int rr = r + 1; rr < 8; rr++) whiteAhead |= Bitboard.RankMask(rr);
            for (int rr = r - 1; rr >= 0; rr--) blackAhead |= Bitboard.RankMask(rr);
            PassedMask[0 * 64 + sq] = files & whiteAhead;
            PassedMask[1 * 64 + sq] = files & blackAhead;
            KingZone[sq] = Attacks.King(sq) | Bitboard.Of(sq);
        }
    }

    private static int[] BuildKingDanger()
    {
        var t = new int[64];
        for (int i = 0; i < 64; i++) t[i] = Math.Min(400, i * i / 2);
        return t;
    }

    /// <summary>Game phase 0 (pawn ending) .. 24 (all pieces on).</summary>
    public static int Phase(Position pos)
    {
        int phase = 0;
        for (int t = 2; t <= 5; t++) phase += PhaseInc[t] * Bitboard.Count(pos.Pieces((PieceType)t));
        return Math.Min(phase, 24);
    }

    /// <summary>Static evaluation from the side to move's point of view.</summary>
    public static int Evaluate(Position pos)
    {
        int mg = 0, eg = 0;
        ulong occ = pos.Occupied;

        // Material + piece-square tables (iterate occupied squares only; the index is always in range).
        ref int mgTable = ref MemoryMarshal.GetArrayDataReference(MgTable);
        ref int egTable = ref MemoryMarshal.GetArrayDataReference(EgTable);
        ulong occupiedSquares = occ;
        while (occupiedSquares != 0)
        {
            int sq = Bitboard.PopLsb(ref occupiedSquares);
            int idx = (((int)pos.PieceAt(sq) & 15) << 6) | sq;
            mg += Unsafe.Add(ref mgTable, idx);
            eg += Unsafe.Add(ref egTable, idx);
        }

        int phase = Phase(pos);
        EvaluateSide(pos, Color.White, occ, ref mg, ref eg, sign: 1);
        EvaluateSide(pos, Color.Black, occ, ref mg, ref eg, sign: -1);

        int score = (mg * phase + eg * (24 - phase)) / 24;
        score = ScaleEndgame(pos, score);

        int fromStm = pos.SideToMove == Color.White ? score : -score;
        return fromStm + 10; // tempo
    }

    private static void EvaluateSide(Position pos, Color us, ulong occ, ref int mg, ref int eg, int sign)
    {
        Color them = us.Opposite();
        ulong ourPawns = pos.Pieces(us, PieceType.Pawn);
        ulong theirPawns = pos.Pieces(them, PieceType.Pawn);
        ulong ours = pos.Pieces(us);

        // Squares attacked by enemy pawns are excluded from mobility.
        ulong enemyPawnAttacks = them == Color.White
            ? Bitboard.East(Bitboard.North(theirPawns)) | Bitboard.West(Bitboard.North(theirPawns))
            : Bitboard.East(Bitboard.South(theirPawns)) | Bitboard.West(Bitboard.South(theirPawns));
        ulong mobilityArea = ~(ours | enemyPawnAttacks);

        int ourKing = pos.KingSquare(us);
        int theirKing = pos.KingSquare(them);
        ulong theirKingZone = KingZone[theirKing];
        int kingAttackers = 0, kingAttackWeight = 0;

        int mgS = 0, egS = 0;

        // Pieces: mobility (per reachable square, centred on a typical count) + attacks on the king zone.
        //                                                                 centre  mg  eg  king zone
        for (ulong bb = pos.Pieces(us, PieceType.Knight); bb != 0;)
        {
            int sq = Bitboard.PopLsb(ref bb);
            Activity(Attacks.Knight(sq), mobilityArea, theirKingZone,      4,   4,  4,  2, ref mgS, ref egS, ref kingAttackers, ref kingAttackWeight);
        }
        for (ulong bb = pos.Pieces(us, PieceType.Bishop); bb != 0;)
        {
            int sq = Bitboard.PopLsb(ref bb);
            Activity(Attacks.Bishop(sq, occ), mobilityArea, theirKingZone, 6,   5,  5,  2, ref mgS, ref egS, ref kingAttackers, ref kingAttackWeight);
        }
        for (ulong bb = pos.Pieces(us, PieceType.Rook); bb != 0;)
        {
            int sq = Bitboard.PopLsb(ref bb);
            Activity(Attacks.Rook(sq, occ), mobilityArea, theirKingZone,   6,   3,  4,  3, ref mgS, ref egS, ref kingAttackers, ref kingAttackWeight);

            ulong file = Bitboard.FileMask(Square.File(sq));
            if ((file & ourPawns) == 0)
            {
                if ((file & theirPawns) == 0)
                {
                    mgS += 25;
                    egS += 10;
                }
                else
                {
                    mgS += 12;
                    egS += 5;
                }
            }
            if (Square.RelativeRank(sq, us) == 6 && Square.RelativeRank(theirKing, us) == 7)
            {
                mgS += 15;
                egS += 25;
            }
        }
        for (ulong bb = pos.Pieces(us, PieceType.Queen); bb != 0;)
        {
            int sq = Bitboard.PopLsb(ref bb);
            Activity(Attacks.Queen(sq, occ), mobilityArea, theirKingZone,  12,  1,  2,  5, ref mgS, ref egS, ref kingAttackers, ref kingAttackWeight);
        }

        if (kingAttackers >= 2 && pos.Pieces(us, PieceType.Queen) != 0)
            mgS += KingDanger[Math.Min(kingAttackWeight, 63)];

        // Bishop pair.
        if (Bitboard.MoreThanOne(pos.Pieces(us, PieceType.Bishop)))
        {
            mgS += 30;
            egS += 50;
        }

        // Pawn structure.
        int forward = us == Color.White ? 8 : -8;
        ulong pawns = ourPawns;
        while (pawns != 0)
        {
            int sq = Bitboard.PopLsb(ref pawns);
            int f = Square.File(sq);
            ulong front = PassedMask[(int)us * 64 + sq];
            ulong ownAhead = front & Bitboard.FileMask(f) & ourPawns;

            if ((AdjacentFiles[f] & ourPawns) == 0) // isolated
            {
                mgS -= 10;
                egS -= 15;
            }

            if (ownAhead != 0) // doubled (the rear pawn pays)
            {
                mgS -= 10;
                egS -= 20;
            }
            else if ((front & theirPawns) == 0) // passed
            {
                int rr = Square.RelativeRank(sq, us);
                mgS += PassedMg[rr];
                egS += PassedEg[rr];

                // King proximity to the passer matters in the endgame.
                int stop = sq + forward;
                if (stop is >= 0 and < 64)
                {
                    egS += 5 * Square.Distance(stop, theirKing) - 2 * Square.Distance(stop, ourKing);
                    if (pos.PieceAt(stop) != Piece.None) egS -= PassedEg[rr] / 4;
                }
            }
        }

        // King shelter (middlegame only): own pawns directly in front of the king.
        int kf = Square.File(ourKing);
        ulong shelterFiles = Bitboard.FileMask(kf) | AdjacentFiles[kf];
        ulong shelterZone = PassedMask[(int)us * 64 + ourKing] & shelterFiles;
        int shelter = Bitboard.Count(shelterZone & ourPawns & (us == Color.White
            ? Bitboard.RankMask(Math.Min(7, Square.Rank(ourKing) + 1)) | Bitboard.RankMask(Math.Min(7, Square.Rank(ourKing) + 2))
            : Bitboard.RankMask(Math.Max(0, Square.Rank(ourKing) - 1)) | Bitboard.RankMask(Math.Max(0, Square.Rank(ourKing) - 2))));
        mgS += 12 * Math.Min(shelter, 3);
        if ((Bitboard.FileMask(kf) & ourPawns) == 0) mgS -= 20; // open file in front of the king

        mg += sign * mgS;
        eg += sign * egS;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Activity(ulong attacks, ulong mobilityArea, ulong kingZone, int centre, int mgWeight, int egWeight,
        int kingWeight, ref int mgS, ref int egS, ref int kingAttackers, ref int kingAttackWeight)
    {
        int mob = Bitboard.Count(attacks & mobilityArea) - centre;
        mgS += mgWeight * mob;
        egS += egWeight * mob;

        ulong zoneHits = attacks & kingZone;
        if (zoneHits != 0)
        {
            kingAttackers++;
            kingAttackWeight += kingWeight * Bitboard.Count(zoneHits);
        }
    }

    /// <summary>Scale down drawish material and add mop-up for lone-king endings.</summary>
    private static int ScaleEndgame(Position pos, int score)
    {
        if (score == 0) return 0;
        Color strong = score > 0 ? Color.White : Color.Black;
        Color weak = strong.Opposite();

        ulong strongPawns = pos.Pieces(strong, PieceType.Pawn);
        int strongNonPawn = NonPawnMaterial(pos, strong);
        int weakNonPawn = NonPawnMaterial(pos, weak);

        if (strongPawns == 0)
        {
            // Two knights (or less than a rook up) without pawns rarely wins.
            if (strongNonPawn - weakNonPawn <= SeeValue[(int)PieceType.Bishop])
                return score / 8;
            if (pos.Pieces(strong) == (pos.Pieces(strong, PieceType.Knight) | pos.Pieces(strong, PieceType.King))
                && Bitboard.Count(pos.Pieces(strong, PieceType.Knight)) == 2 && pos.Pieces(weak) == pos.Pieces(weak, PieceType.King))
                return score / 16;
        }

        // Mop-up: winning side drives the lone king to the edge and brings its own king close.
        if (pos.Pieces(weak) == pos.Pieces(weak, PieceType.King) && strongNonPawn >= SeeValue[(int)PieceType.Rook])
        {
            int wk = pos.KingSquare(weak), sk = pos.KingSquare(strong);
            int centerDist = Math.Max(3 - Square.File(wk), Square.File(wk) - 4) + Math.Max(3 - Square.Rank(wk), Square.Rank(wk) - 4);
            int kingDist = Math.Abs(Square.File(wk) - Square.File(sk)) + Math.Abs(Square.Rank(wk) - Square.Rank(sk));
            int bonus = 300 + 20 * centerDist + 10 * (14 - kingDist);
            return score + (strong == Color.White ? bonus : -bonus);
        }

        return score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NonPawnMaterial(Position pos, Color c) =>
        Bitboard.Count(pos.Pieces(c, PieceType.Knight)) * SeeValue[2]
        + Bitboard.Count(pos.Pieces(c, PieceType.Bishop)) * SeeValue[3]
        + Bitboard.Count(pos.Pieces(c, PieceType.Rook)) * SeeValue[4]
        + Bitboard.Count(pos.Pieces(c, PieceType.Queen)) * SeeValue[5];
}
