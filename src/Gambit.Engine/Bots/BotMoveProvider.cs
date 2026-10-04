using System.Diagnostics;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Openings;
using Gambit.Core.Sessions;
using Gambit.Engine.Evaluation;
using Gambit.Engine.Search;

namespace Gambit.Engine.Bots;

/// <summary>
/// Plays moves for a <see cref="BotProfile"/>. The engine scores the top N candidate moves; weaker
/// bots then add noise and sample with a softmax (so they prefer good moves but sometimes miss
/// things), and now and then overlook the opponent's reply altogether. Strong bots just play the
/// best line. The knobs are fitted to real players' mistakes (tools/Gambit.BotCalibration).
/// </summary>
public sealed class BotMoveProvider : IMoveProvider
{
    /// <summary>
    /// Bump when the move-choice logic changes: calibration results (tools/Gambit.BotArena) are
    /// fingerprinted with it, so games from older logic stop counting.
    /// </summary>
    public const int Revision = 4;

    /// <summary>Advantage (cp, bot's view) from which a bot plays its best move in an endgame.</summary>
    private const int ConvertMargin = 500;

    private readonly Searcher _searcher;
    private readonly Random _rng;
    private readonly object _searchLock = new();

    public BotMoveProvider(BotProfile profile, int? seed = null, int hashMegabytes = 32)
    {
        Profile = profile;
        _rng = seed is int s ? new Random(s) : new Random();
        _searcher = new Searcher(hashMegabytes);
    }

    public BotProfile Profile { get; }

    /// <summary>When false, moves are returned as soon as they are found (tests, analysis).</summary>
    public bool HumanLikeDelay { get; init; } = true;

    public async Task<Move> ChooseMoveAsync(GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        Move move = ChooseMove(snapshot, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (HumanLikeDelay)
        {
            int delay = _rng.Next(Profile.MinMoveDelayMs, Math.Max(Profile.MinMoveDelayMs, Profile.MaxMoveDelayMs) + 1);
            if (snapshot.OwnTime is TimeSpan own && own < TimeSpan.FromSeconds(30)) delay /= 4;
            int remaining = delay - (int)sw.ElapsedMilliseconds;
            if (remaining > 0) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
        return move;
    }

    /// <summary>Synchronous move choice (no artificial delay).</summary>
    public Move ChooseMove(GameSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        Position pos = snapshot.Position;
        List<Move> legal = MoveGenerator.LegalMoves(pos);
        if (legal.Count == 0) return Move.None;
        if (legal.Count == 1) return legal[0];

        if (snapshot.Ply < Profile.BookDepth && TryBookMove(pos) is Move book && legal.Contains(book))
            return book;

        bool oversight = Profile.OversightChance > 0 && _rng.NextDouble() < Profile.OversightChance;

        SearchLimits limits = BuildLimits(snapshot, legal.Count);
        SearchResult result;
        lock (_searchLock) result = _searcher.Search(pos, limits, cancellationToken);
        if (result.BestMove.IsNone) return legal[_rng.Next(legal.Count)];

        // A found mate, or a decisive endgame advantage, is played straight: weakened bots otherwise
        // shuffle won endgames into 50-move draws. Their mistakes stay in the opening and middlegame.
        bool converting = result.Score >= Searcher.MateBound || result.Score >= ConvertMargin && IsEndgame(pos);
        if (converting) return result.BestMove;
        if (oversight) return Overlooking(pos, legal);
        if (result.Lines.Count <= 1 || Profile.Temperature <= 0) return result.BestMove;

        // Weakened choice: noisy scores + softmax sampling over the candidate lines.
        var lines = result.Lines;
        var moves = new Move[lines.Count];
        var scores = new double[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            double score = Math.Clamp(lines[i].Score, -1500, 1500);
            if (Profile.EvalNoise > 0) score += Gaussian() * Profile.EvalNoise;
            moves[i] = lines[i].Move;
            scores[i] = score + BotStyles.Bonus(Profile.Style, pos, lines[i].Move);
        }
        return Pick(moves, scores);
    }

    /// <summary>
    /// An oversight: every move is judged by the board right after it, as if the opponent had no reply
    /// (static evaluation plus the bot's noise and style). A mate in one is still noticed.
    /// </summary>
    private Move Overlooking(Position position, List<Move> legal)
    {
        Position pos = position.Clone();
        var moves = legal.ToArray();
        var scores = new double[moves.Length];
        for (int i = 0; i < moves.Length; i++)
        {
            pos.MakeMove(moves[i]);
            double score = pos.InCheck && MoveGenerator.LegalMoves(pos).Count == 0 ? 5000 : -Evaluator.Evaluate(pos);
            pos.UnmakeMove();
            if (Profile.EvalNoise > 0) score += Gaussian() * Profile.EvalNoise;
            scores[i] = score + BotStyles.Bonus(Profile.Style, position, moves[i]);
        }
        return Pick(moves, scores);
    }

    /// <summary>Softmax sampling: better-scored moves are likelier, by how much depends on the temperature.</summary>
    private Move Pick(Move[] moves, double[] scores)
    {
        double best = scores.Max();
        if (Profile.Temperature <= 0) return moves[Array.IndexOf(scores, best)];
        double total = 0;
        var weights = new double[moves.Length];
        for (int i = 0; i < moves.Length; i++)
        {
            weights[i] = Math.Exp((scores[i] - best) / Profile.Temperature);
            total += weights[i];
        }

        double pick = _rng.NextDouble() * total;
        for (int i = 0; i < moves.Length; i++)
        {
            pick -= weights[i];
            if (pick <= 0) return moves[i];
        }
        return moves[Array.IndexOf(scores, best)];
    }

    public async ValueTask<bool> ConsiderDrawOfferAsync(GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        await Task.Yield();
        SearchResult r;
        var limits = new SearchLimits { MaxDepth = Math.Min(Profile.MaxDepth, 8), HardTime = TimeSpan.FromMilliseconds(400), SoftTime = TimeSpan.FromMilliseconds(250) };
        lock (_searchLock) r = _searcher.Search(snapshot.Position, limits, cancellationToken);

        // Accept when losing, or when dead level late in the game.
        if (r.Score <= -150) return true;
        return Math.Abs(r.Score) <= 25 && snapshot.Ply >= 60;
    }

    public string? Chat(GameEvent evt, Game game)
    {
        IReadOnlyList<string> pool = evt switch
        {
            GameEvent.GameStarted => Profile.Greetings,
            GameEvent.WonGame => Profile.WinLines,
            GameEvent.LostGame => Profile.LossLines,
            GameEvent.DrawnGame => Profile.DrawLines,
            _ => [],
        };
        return pool.Count == 0 ? null : pool[_rng.Next(pool.Count)];
    }

    /// <summary>
    /// A weighted-random opening-book move, or null when out of book. Stronger bots square the
    /// popularity weights so they stick to main lines; weaker bots sample more widely.
    /// </summary>
    private Move? TryBookMove(Position pos)
    {
        var moves = OpeningBook.MovesFor(pos);
        if (moves.Count == 0) return null;
        double exponent = Profile.Rating >= 1400 ? 2.0 : 1.0;
        double total = moves.Sum(m => Math.Pow(m.Weight, exponent));
        double pick = _rng.NextDouble() * total;
        foreach (var (move, weight) in moves)
        {
            pick -= Math.Pow(weight, exponent);
            if (pick <= 0) return move;
        }
        return moves[^1].Move;
    }

    /// <summary>Clears learned search state (call between games).</summary>
    public void NewGame()
    {
        lock (_searchLock) _searcher.Reset();
    }

    private SearchLimits BuildLimits(GameSnapshot snapshot, int legalCount)
    {
        int candidates = Math.Clamp(Profile.Candidates, 1, legalCount);
        TimeSpan think = TimeSpan.FromMilliseconds(Profile.ThinkTimeMs);
        TimeSpan soft = think * 0.6, hard = think;

        if (Profile.UsesClock && snapshot.OwnTime is TimeSpan own)
        {
            SearchLimits clock = SearchLimits.ForClock(own, snapshot.Increment);
            soft = Min(soft, clock.SoftTime!.Value);
            hard = Min(hard, clock.HardTime!.Value);
        }
        else if (snapshot.OwnTime is TimeSpan ownTime)
        {
            // Weak bots still must not flag: cap thinking to a small slice of the clock.
            hard = Min(hard, ownTime / 20);
            soft = Min(soft, hard);
        }

        return new SearchLimits
        {
            MaxDepth = Profile.MaxDepth,
            MaxNodes = Profile.MaxNodes,
            SoftTime = soft,
            HardTime = hard,
            MultiPv = candidates,
        };
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// <summary>No queens, or at most four other pieces (besides kings and pawns) left on the board.</summary>
    private static bool IsEndgame(Position pos)
    {
        ulong queens = pos.Pieces(PieceType.Queen);
        ulong minorsAndRooks = pos.Pieces(PieceType.Knight) | pos.Pieces(PieceType.Bishop) | pos.Pieces(PieceType.Rook);
        return queens == 0 || Bitboard.Count(minorsAndRooks | queens) <= 4;
    }

    private double Gaussian()
    {
        double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
