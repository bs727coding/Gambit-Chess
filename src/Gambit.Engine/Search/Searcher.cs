using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gambit.Core.Board;
using Gambit.Engine.Evaluation;

namespace Gambit.Engine.Search;

/// <summary>
/// Alpha-beta searcher: iterative deepening with aspiration windows, principal variation search,
/// transposition table, quiescence search with SEE pruning, null-move pruning, reverse futility
/// pruning, late-move reductions/pruning, killer + history move ordering, check extension and MultiPV.
/// One instance is single-threaded; create one per concurrent search.
/// </summary>
public sealed class Searcher
{
    public const int MaxPly = 128;
    public const int Infinity = 32001;
    public const int Mate = 32000;
    public const int MateBound = Mate - 2 * MaxPly;

    private const int TtMoveScore = 4_000_000;
    private const int GoodCaptureScore = 2_000_000;
    private const int PromotionScore = 1_900_000;
    private const int Killer1Score = 1_000_000;
    private const int Killer2Score = 999_000;
    private const int BadCaptureScore = -2_000_000;
    private const int HistoryMax = 16384;

    private static readonly int[] LmrTable = BuildLmrTable();

    private readonly TranspositionTable _tt;
    private readonly int[] _history = new int[2 * 64 * 64];
    private readonly Move[] _killers = new Move[MaxPly * 2];
    private readonly Move[] _pvTable = new Move[MaxPly * MaxPly];
    private readonly int[] _pvLength = new int[MaxPly + 1];

    private Position _pos = new();
    private SearchLimits _limits = new();
    private CancellationToken _ct;
    private readonly Stopwatch _clock = new();
    private long _nodes;
    private int _selDepth;
    private bool _stop;
    private RootMove[] _rootMoves = [];

    private sealed class RootMove(Move move)
    {
        public Move Move { get; } = move;
        public int Score = -Infinity;
        public int PrevScore = -Infinity;
        public Move[] Pv = [move];
    }

    public Searcher(int hashMegabytes = 16) => _tt = new TranspositionTable(hashMegabytes);

    public TranspositionTable Table => _tt;

    /// <summary>Forget everything learned from previous searches (new game).</summary>
    public void Reset()
    {
        _tt.Clear();
        Array.Clear(_history);
        Array.Clear(_killers);
    }

    public SearchResult Search(Position position, SearchLimits limits, CancellationToken cancellationToken = default,
        Action<SearchInfo>? onInfo = null)
    {
        _pos = position.Clone();
        _limits = limits;
        _ct = cancellationToken;
        _nodes = 0;
        _stop = false;
        _clock.Restart();
        _tt.NewSearch();
        Array.Clear(_killers);
        for (int i = 0; i < _history.Length; i++) _history[i] /= 2;

        var legal = MoveGenerator.LegalMoves(_pos);
        if (limits.SearchMoves is { Count: > 0 } only) legal = legal.Where(only.Contains).ToList();
        if (legal.Count == 0)
        {
            int terminal = _pos.InCheck ? -Mate : 0;
            return new SearchResult(Move.None, terminal, 0, 0, TimeSpan.Zero, []);
        }

        _rootMoves = OrderRootMoves(legal);
        int multiPv = Math.Clamp(limits.MultiPv, 1, _rootMoves.Length);
        int maxDepth = Math.Clamp(limits.MaxDepth, 1, MaxPly - 1);

        List<PvLine>? completed = null;
        int completedDepth = 0;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            foreach (RootMove rm in _rootMoves) rm.PrevScore = rm.Score;

            for (int pvIdx = 0; pvIdx < multiPv && !_stop; pvIdx++)
            {
                _selDepth = 0;
                int prev = _rootMoves[pvIdx].PrevScore;
                int delta = 20;
                int alpha = -Infinity, beta = Infinity;
                if (depth >= 4 && prev > -Infinity && Math.Abs(prev) < MateBound)
                {
                    alpha = Math.Max(prev - delta, -Infinity);
                    beta = Math.Min(prev + delta, Infinity);
                }

                while (true)
                {
                    int score = SearchRoot(depth, alpha, beta, pvIdx);
                    SortRootMoves(pvIdx);
                    if (_stop) break;

                    if (score <= alpha)
                    {
                        beta = (alpha + beta) / 2;
                        alpha = Math.Max(score - delta, -Infinity);
                    }
                    else if (score >= beta)
                    {
                        beta = Math.Min(score + delta, Infinity);
                    }
                    else
                    {
                        break;
                    }

                    delta += delta / 2 + 10;
                    if (delta > 1500)
                    {
                        alpha = -Infinity;
                        beta = Infinity;
                    }
                }
            }

            if (_stop && completed != null) break;

            completedDepth = depth;
            completed = new List<PvLine>(multiPv);
            for (int i = 0; i < multiPv; i++)
            {
                RootMove rm = _rootMoves[i];
                completed.Add(new PvLine(rm.Move, rm.Score, depth, rm.Pv));
            }

            onInfo?.Invoke(new SearchInfo(depth, _selDepth, _nodes, _clock.Elapsed, completed));

            if (_stop) break;
            if (limits.SoftTime is TimeSpan soft && _clock.Elapsed >= soft) break;

            // A forced mate that has been fully resolved will not change with more depth.
            int best = completed[0].Score;
            if (IsMateScore(best) && depth >= 2 * Math.Abs(MateIn(best)) + 2 && multiPv == 1) break;
            if (_rootMoves.Length == 1 && depth >= 4 && limits.SoftTime != null) break;
        }

        completed ??= [new PvLine(_rootMoves[0].Move, _rootMoves[0].Score, 0, _rootMoves[0].Pv)];
        return new SearchResult(completed[0].Move, completed[0].Score, completedDepth, _nodes, _clock.Elapsed, completed);
    }

    // ------------------------------------------------------------------ root

    private int SearchRoot(int depth, int alpha, int beta, int pvIdx)
    {
        int bestScore = -Infinity;
        for (int i = pvIdx; i < _rootMoves.Length; i++)
        {
            RootMove rm = _rootMoves[i];
            Move m = rm.Move;
            _pos.MakeMove(m);
            _nodes++;

            int score;
            if (i == pvIdx)
            {
                score = -Negamax(depth - 1, 1, -beta, -alpha, true, true);
            }
            else
            {
                score = -Negamax(depth - 1, 1, -alpha - 1, -alpha, false, true);
                if (score > alpha && score < beta && !_stop)
                    score = -Negamax(depth - 1, 1, -beta, -alpha, true, true);
            }
            _pos.UnmakeMove();

            if (_stop) return bestScore;

            if (i == pvIdx || score > alpha)
            {
                rm.Score = score;
                var pv = new Move[1 + Math.Max(0, _pvLength[1] - 1)];
                pv[0] = m;
                for (int j = 1; j < _pvLength[1]; j++) pv[j] = _pvTable[MaxPly + j];
                rm.Pv = pv;
            }
            else
            {
                rm.Score = -Infinity; // only an upper bound; sorts behind the real lines
            }

            if (score > bestScore) bestScore = score;
            if (score > alpha) alpha = score;
            if (alpha >= beta) break;
        }
        return bestScore;
    }

    private void SortRootMoves(int from)
    {
        // Stable insertion sort (few elements; keeps previous order among equal scores).
        for (int i = from + 1; i < _rootMoves.Length; i++)
        {
            RootMove key = _rootMoves[i];
            int j = i - 1;
            while (j >= from && _rootMoves[j].Score < key.Score)
            {
                _rootMoves[j + 1] = _rootMoves[j];
                j--;
            }
            _rootMoves[j + 1] = key;
        }
    }

    private RootMove[] OrderRootMoves(List<Move> legal)
    {
        var scored = new List<(Move m, int s)>(legal.Count);
        foreach (Move m in legal)
        {
            int s = 0;
            if (m.IsCapture) s = 10_000 + MvvLva(m);
            if (m.IsPromotion) s += 9_000 + Evaluator.SeeValue[(int)m.PromotionType];
            scored.Add((m, s));
        }
        return scored.OrderByDescending(x => x.s).Select(x => new RootMove(x.m)).ToArray();
    }

    // ------------------------------------------------------------------ main search

    private int Negamax(int depth, int ply, int alpha, int beta, bool pvNode, bool allowNull)
    {
        _pvLength[ply] = ply;
        if (_stop) return 0;

        bool inCheck = _pos.InCheck;
        if (inCheck) depth++;
        if (depth <= 0) return Quiesce(ply, alpha, beta);

        if ((++_nodes & 1023) == 0) CheckLimits();
        if (_stop) return 0;
        if (ply > _selDepth) _selDepth = ply;
        if (ply >= MaxPly - 1) return inCheck ? 0 : Evaluator.Evaluate(_pos);

        // Draws and mate-distance pruning.
        if (_pos.HalfmoveClock >= 100 || _pos.IsRepetition() || _pos.IsInsufficientMaterial())
            return 0;
        alpha = Math.Max(alpha, -Mate + ply);
        beta = Math.Min(beta, Mate - ply - 1);
        if (alpha >= beta) return alpha;

        ulong key = _pos.Key;
        Move ttMove = Move.None;
        int ttEval = 0;
        bool ttHit = _tt.Probe(key, ply, out ttMove, out int ttScore, out ttEval, out int ttDepth, out Bound ttBound);
        if (ttHit && !pvNode && ttDepth >= depth)
        {
            if (ttBound == Bound.Exact
                || (ttBound == Bound.Lower && ttScore >= beta)
                || (ttBound == Bound.Upper && ttScore <= alpha))
                return ttScore;
        }

        int staticEval = inCheck ? -Infinity : (ttHit ? ttEval : Evaluator.Evaluate(_pos));
        Color us = _pos.SideToMove;

        if (!pvNode && !inCheck)
        {
            // Reverse futility pruning: far above beta at low depth -> assume a cutoff.
            if (depth <= 6 && staticEval - 85 * depth >= beta && Math.Abs(beta) < MateBound)
                return staticEval;

            // Null-move pruning.
            if (allowNull && depth >= 3 && staticEval >= beta && _pos.HasNonPawnMaterial(us))
            {
                int r = 3 + depth / 4 + Math.Min(3, (staticEval - beta) / 200);
                _pos.MakeNullMove();
                int nullScore = -Negamax(depth - 1 - r, ply + 1, -beta, -beta + 1, false, false);
                _pos.UnmakeNullMove();
                if (_stop) return 0;
                if (nullScore >= beta) return nullScore >= MateBound ? beta : nullScore;
            }
        }

        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        Span<int> scores = stackalloc int[MoveGenerator.MaxMoves];
        int n = MoveGenerator.Generate(_pos, moves);
        if (n == 0) return inCheck ? -Mate + ply : 0;
        ScoreMoves(moves, scores, n, ttMove, ply);

        int bestScore = -Infinity;
        Move bestMove = Move.None;
        int originalAlpha = alpha;
        int movesSearched = 0, quietsSearched = 0;
        Span<Move> quietsTried = stackalloc Move[64];
        int quietsTriedCount = 0;

        for (int i = 0; i < n; i++)
        {
            PickNext(moves, scores, i, n);
            Move m = moves[i];
            bool quiet = !m.IsNoisy;

            if (!pvNode && !inCheck && quiet && bestScore > -MateBound)
            {
                // Late move pruning and futility pruning of quiet moves near the leaves.
                if (depth <= 4 && quietsSearched >= 3 + depth * depth) continue;
                if (depth <= 3 && staticEval + 110 + 130 * depth <= alpha) continue;
            }

            _pos.MakeMove(m);
            bool givesCheck = _pos.InCheck;
            int newDepth = depth - 1;
            int score;

            if (movesSearched == 0)
            {
                score = -Negamax(newDepth, ply + 1, -beta, -alpha, pvNode, true);
            }
            else
            {
                int reduction = 0;
                if (depth >= 3 && movesSearched >= (pvNode ? 3 : 2) && quiet && !inCheck && !givesCheck)
                {
                    reduction = LmrTable[Math.Min(depth, 63) * 64 + Math.Min(movesSearched, 63)];
                    if (pvNode) reduction--;
                    if (m == _killers[ply * 2] || m == _killers[ply * 2 + 1]) reduction--;
                    reduction -= Math.Clamp(_history[HistoryIndex(us, m)] / 6000, -2, 2);
                    reduction = Math.Clamp(reduction, 0, newDepth - 1);
                }

                score = -Negamax(newDepth - reduction, ply + 1, -alpha - 1, -alpha, false, true);
                if (score > alpha && reduction > 0)
                    score = -Negamax(newDepth, ply + 1, -alpha - 1, -alpha, false, true);
                if (score > alpha && score < beta && pvNode)
                    score = -Negamax(newDepth, ply + 1, -beta, -alpha, true, true);
            }

            _pos.UnmakeMove();
            if (_stop) return 0;

            movesSearched++;
            if (quiet)
            {
                quietsSearched++;
                if (quietsTriedCount < quietsTried.Length) quietsTried[quietsTriedCount++] = m;
            }

            if (score > bestScore)
            {
                bestScore = score;
                if (score > alpha)
                {
                    alpha = score;
                    bestMove = m;
                    UpdatePv(ply, m);

                    if (alpha >= beta)
                    {
                        if (quiet) UpdateQuietStats(us, m, ply, depth, quietsTried[..quietsTriedCount]);
                        break;
                    }
                }
            }
        }

        if (movesSearched == 0) return staticEval; // everything was pruned (cannot happen in check)

        Bound bound = bestScore >= beta ? Bound.Lower : bestScore > originalAlpha ? Bound.Exact : Bound.Upper;
        _tt.Store(key, bestMove, bestScore, staticEval == -Infinity ? 0 : staticEval, depth, bound, ply);
        return bestScore;
    }

    private int Quiesce(int ply, int alpha, int beta)
    {
        _pvLength[ply] = ply;
        if ((++_nodes & 1023) == 0) CheckLimits();
        if (_stop) return 0;
        if (ply > _selDepth) _selDepth = ply;

        bool inCheck = _pos.InCheck;
        if (ply >= MaxPly - 1) return inCheck ? 0 : Evaluator.Evaluate(_pos);

        int best, standPat = 0;
        if (inCheck)
        {
            best = -Infinity;
        }
        else
        {
            standPat = Evaluator.Evaluate(_pos);
            if (standPat >= beta) return standPat;
            if (standPat > alpha) alpha = standPat;
            best = standPat;
        }

        Span<Move> moves = stackalloc Move[MoveGenerator.MaxMoves];
        Span<int> scores = stackalloc int[MoveGenerator.MaxMoves];
        int n = MoveGenerator.Generate(_pos, moves, inCheck ? GenType.All : GenType.Noisy);
        if (n == 0) return inCheck ? -Mate + ply : best;
        ScoreMoves(moves, scores, n, Move.None, ply);

        for (int i = 0; i < n; i++)
        {
            PickNext(moves, scores, i, n);
            Move m = moves[i];

            if (!inCheck)
            {
                if (m.IsPromotion && m.PromotionType != PieceType.Queen) continue;
                if (m.IsCapture && !m.IsPromotion)
                {
                    int victim = m.IsEnPassant ? 100 : Evaluator.SeeValue[(int)_pos.PieceAt(m.To).Type()];
                    if (standPat + victim + 200 < alpha) continue; // delta pruning
                    if (scores[i] < GoodCaptureScore && See(_pos, m) < 0) continue;
                }
            }

            _pos.MakeMove(m);
            int score = -Quiesce(ply + 1, -beta, -alpha);
            _pos.UnmakeMove();
            if (_stop) return 0;

            if (score > best)
            {
                best = score;
                if (score > alpha)
                {
                    alpha = score;
                    UpdatePv(ply, m);
                    if (alpha >= beta) break;
                }
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ helpers

    private void CheckLimits()
    {
        if (_ct.IsCancellationRequested) _stop = true;
        else if (_nodes >= _limits.MaxNodes) _stop = true;
        else if (_limits.HardTime is TimeSpan hard && _clock.Elapsed >= hard) _stop = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdatePv(int ply, Move m)
    {
        int row = ply * MaxPly;
        _pvTable[row + ply] = m;
        int childLen = _pvLength[ply + 1];
        for (int j = ply + 1; j < childLen; j++) _pvTable[row + j] = _pvTable[row + MaxPly + j];
        _pvLength[ply] = Math.Max(childLen, ply + 1);
    }

    private void UpdateQuietStats(Color us, Move m, int ply, int depth, ReadOnlySpan<Move> quietsTried)
    {
        if (_killers[ply * 2] != m)
        {
            _killers[ply * 2 + 1] = _killers[ply * 2];
            _killers[ply * 2] = m;
        }

        int bonus = Math.Min(depth * depth * 16, 1600);
        foreach (Move q in quietsTried)
        {
            int idx = HistoryIndex(us, q);
            int delta = q == m ? bonus : -bonus;
            _history[idx] += delta - _history[idx] * Math.Abs(delta) / HistoryMax;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HistoryIndex(Color c, Move m) => ((int)c << 12) | (m.From << 6) | m.To;

    private void ScoreMoves(Span<Move> moves, Span<int> scores, int n, Move ttMove, int ply)
    {
        Color us = _pos.SideToMove;
        Move k1 = _killers[ply * 2], k2 = _killers[ply * 2 + 1];
        for (int i = 0; i < n; i++)
        {
            Move m = moves[i];
            if (m == ttMove)
            {
                scores[i] = TtMoveScore;
            }
            else if (m.IsCapture)
            {
                int mvvLva = MvvLva(m);
                int attacker = Evaluator.SeeValue[(int)_pos.PieceAt(m.From).Type()];
                int victim = m.IsEnPassant ? 100 : Evaluator.SeeValue[(int)_pos.PieceAt(m.To).Type()];
                bool good = victim >= attacker || See(_pos, m) >= 0;
                scores[i] = (good ? GoodCaptureScore : BadCaptureScore) + mvvLva;
                if (m.IsPromotion) scores[i] += Evaluator.SeeValue[(int)m.PromotionType];
            }
            else if (m.IsPromotion)
            {
                scores[i] = m.PromotionType == PieceType.Queen ? PromotionScore : BadCaptureScore - 1000;
            }
            else if (m == k1)
            {
                scores[i] = Killer1Score;
            }
            else if (m == k2)
            {
                scores[i] = Killer2Score;
            }
            else
            {
                scores[i] = _history[HistoryIndex(us, m)];
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int MvvLva(Move m)
    {
        int victim = m.IsEnPassant ? 1 : (int)_pos.PieceAt(m.To).Type();
        int attacker = (int)_pos.PieceAt(m.From).Type();
        return victim * 100 - attacker;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PickNext(Span<Move> moves, Span<int> scores, int start, int n)
    {
        int best = start;
        for (int j = start + 1; j < n; j++)
            if (scores[j] > scores[best]) best = j;
        if (best != start)
        {
            (moves[start], moves[best]) = (moves[best], moves[start]);
            (scores[start], scores[best]) = (scores[best], scores[start]);
        }
    }

    /// <summary>Static exchange evaluation: material outcome of the capture sequence on the target square.</summary>
    public static int See(Position pos, Move m)
    {
        if (m.IsCastle) return 0;
        int from = m.From, to = m.To;
        Span<int> gain = stackalloc int[34];
        int d = 0;
        ulong occ = pos.Occupied;
        Piece mover = pos.PieceAt(from);

        gain[0] = m.IsEnPassant ? Evaluator.SeeValue[1] : Evaluator.SeeValue[(int)pos.PieceAt(to).Type()];
        int attackerValue = Evaluator.SeeValue[(int)mover.Type()];
        if (m.IsPromotion)
        {
            gain[0] += Evaluator.SeeValue[(int)m.PromotionType] - Evaluator.SeeValue[1];
            attackerValue = Evaluator.SeeValue[(int)m.PromotionType];
        }

        occ ^= 1UL << from;
        if (m.IsEnPassant) occ ^= 1UL << (mover.Color() == Color.White ? to - 8 : to + 8);

        ulong diagonal = pos.Pieces(PieceType.Bishop) | pos.Pieces(PieceType.Queen);
        ulong straight = pos.Pieces(PieceType.Rook) | pos.Pieces(PieceType.Queen);
        ulong attackers = pos.AttackersTo(to, occ) & occ;
        Color side = mover.Color().Opposite();

        while (d < 32)
        {
            ulong mine = attackers & pos.Pieces(side);
            if (mine == 0) break;

            int sq = -1;
            int pt;
            for (pt = 1; pt <= 6; pt++)
            {
                ulong bb = mine & pos.Pieces((PieceType)pt);
                if (bb != 0)
                {
                    sq = Bitboard.Lsb(bb);
                    break;
                }
            }

            d++;
            gain[d] = attackerValue - gain[d - 1];
            if (Math.Max(-gain[d - 1], gain[d]) < 0) break;

            attackerValue = Evaluator.SeeValue[pt];
            occ ^= 1UL << sq;
            attackers |= (Attacks.Bishop(to, occ) & diagonal) | (Attacks.Rook(to, occ) & straight);
            attackers &= occ;
            side = side.Opposite();
        }

        while (d > 0)
        {
            gain[d - 1] = -Math.Max(-gain[d - 1], gain[d]);
            d--;
        }
        return gain[0];
    }

    private static int[] BuildLmrTable()
    {
        var t = new int[64 * 64];
        for (int d = 1; d < 64; d++)
            for (int m = 1; m < 64; m++)
                t[d * 64 + m] = (int)(0.8 + Math.Log(d) * Math.Log(m) / 2.3);
        return t;
    }

    // ------------------------------------------------------------------ score utilities

    public static bool IsMateScore(int score) => Math.Abs(score) >= MateBound && Math.Abs(score) <= Mate;

    /// <summary>Full moves to mate: positive if the side to move mates, negative if it gets mated.</summary>
    public static int MateIn(int score)
    {
        if (!IsMateScore(score)) return 0;
        return score > 0 ? (Mate - score + 1) / 2 : -(Mate + score + 1) / 2;
    }

    public static string FormatScore(int score)
    {
        if (IsMateScore(score))
        {
            int mi = MateIn(score);
            return mi > 0 ? $"M{mi}" : $"-M{-mi}";
        }
        double pawns = score / 100.0;
        return pawns >= 0 ? $"+{pawns:0.00}" : $"{pawns:0.00}";
    }
}
