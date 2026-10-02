using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Openings;
using Gambit.Engine.Search;

namespace Gambit.Engine.Review;

/// <summary>How good a move was, chess.com style.</summary>
public enum MoveClass
{
    Brilliant,
    Great,
    Best,
    Excellent,
    Good,
    Book,
    Forced,
    Inaccuracy,
    Mistake,
    Miss,
    Blunder,
}

/// <summary>The verdict on one move. Evaluations are centipawns from White's point of view.</summary>
public sealed record MoveReview(
    int Ply,
    GameMove Move,
    MoveClass Class,
    int EvalBefore,
    int EvalAfter,
    Move BestMove,
    string BestSan,
    int BestEval,
    double WinBefore,
    double WinAfter,
    double Accuracy)
{
    public Color Side => Move.Side;

    /// <summary>Drop in the mover's winning chances (percentage points).</summary>
    public double WinLoss => Math.Max(0, WinBefore - WinAfter);

    public bool IsKeyMoment => Class is MoveClass.Brilliant or MoveClass.Great or MoveClass.Mistake or MoveClass.Miss or MoveClass.Blunder;
}

public sealed record GameReview(
    IReadOnlyList<MoveReview> Moves,
    IReadOnlyList<int> EvalCurve,
    double WhiteAccuracy,
    double BlackAccuracy)
{
    public int Count(Color side, MoveClass cls) => Moves.Count(m => m.Side == side && m.Class == cls);
}

/// <summary>
/// Reviews a finished game: evaluates every position with the engine (in parallel, one searcher per
/// worker) and classifies each move by how much it changed the mover's winning chances. Win-chance
/// and accuracy formulas follow Lichess's published ones.
/// </summary>
public sealed class GameReviewer
{
    /// <summary>Per-position think time; total review time ≈ plies × this ÷ worker count.</summary>
    public TimeSpan TimePerPosition { get; init; } = TimeSpan.FromMilliseconds(350);
    public int MaxDepth { get; init; } = 18;
    public int Workers { get; init; } = Math.Max(1, Environment.ProcessorCount - 2);

    private readonly record struct Analysis(int Score, Move Best, int SecondScore, bool HasSecond, int LegalCount);

    public Task<GameReview> ReviewAsync(Game game, IProgress<double>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Review(game, progress, ct), ct);

    public GameReview Review(Game game, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        int n = game.Moves.Count;
        var positions = new Position[n + 1];
        for (int i = 0; i <= n; i++) positions[i] = game.PositionAt(i);

        var results = new Analysis[n + 1];
        int done = 0;
        using var searchers = new ThreadLocal<Searcher>(() => new Searcher(16), trackAllValues: false);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Workers, CancellationToken = ct };
        Parallel.For(0, n + 1, options, i =>
        {
            results[i] = Analyze(searchers.Value!, positions[i], ct);
            progress?.Report((double)Interlocked.Increment(ref done) / (n + 1));
        });
        ct.ThrowIfCancellationRequested();

        // Evaluations from White's point of view.
        var curve = new int[n + 1];
        for (int i = 0; i <= n; i++)
        {
            Position p = positions[i];
            int score = results[i].LegalCount == 0 ? TerminalScore(p) : results[i].Score;
            curve[i] = p.SideToMove == Color.White ? score : -score;
        }

        var reviews = new List<MoveReview>(n);
        for (int i = 0; i < n; i++)
        {
            GameMove gm = game.Moves[i];
            Position before = positions[i];
            Color mover = before.SideToMove;
            int sign = mover == Color.White ? 1 : -1;
            Analysis a = results[i];

            double winBefore = WinPercent(sign * curve[i]);
            double winAfter = WinPercent(sign * curve[i + 1]);
            double loss = Math.Max(0, winBefore - winAfter);
            double accuracy = Math.Clamp(103.1668 * Math.Exp(-0.04354 * loss) - 3.1669, 0, 100);

            MoveClass cls = Classify(game, i, before, gm, a, winBefore, winAfter, positions[i + 1]);
            if (cls is MoveClass.Best or MoveClass.Great or MoveClass.Brilliant or MoveClass.Book or MoveClass.Forced) accuracy = Math.Max(accuracy, 99);

            string bestSan = a.Best.IsNone ? "" : San.Format(before, a.Best);
            reviews.Add(new MoveReview(i + 1, gm, cls, curve[i], curve[i + 1], a.Best, bestSan, sign * a.Score, winBefore, winAfter, accuracy));
        }

        return new GameReview(reviews, curve, SideAccuracy(reviews, Color.White), SideAccuracy(reviews, Color.Black));
    }

    private Analysis Analyze(Searcher searcher, Position pos, CancellationToken ct)
    {
        int legal = MoveGenerator.LegalMoves(pos).Count;
        if (legal == 0) return new Analysis(TerminalScore(pos), Move.None, 0, false, 0);
        if (pos.IsInsufficientMaterial()) return new Analysis(0, MoveGenerator.LegalMoves(pos)[0], 0, false, legal);

        searcher.Reset();
        var limits = new SearchLimits
        {
            MaxDepth = MaxDepth,
            MultiPv = Math.Min(2, legal),
            SoftTime = TimePerPosition * 0.7,
            HardTime = TimePerPosition,
        };
        SearchResult r = searcher.Search(pos, limits, ct);
        bool hasSecond = r.Lines.Count > 1;
        return new Analysis(Clamp(r.Score), r.BestMove, hasSecond ? Clamp(r.Lines[1].Score) : 0, hasSecond, legal);
    }

    private static MoveClass Classify(Game game, int index, Position before, GameMove gm, Analysis a,
        double winBefore, double winAfter, Position after)
    {
        double loss = Math.Max(0, winBefore - winAfter);
        if (a.LegalCount == 1) return MoveClass.Forced;
        if (index < 24 && game.StartsFromStandardPosition && OpeningBook.IsBookPosition(after) && OpeningBook.ForPosition(after) != null)
            return MoveClass.Book;

        bool isBest = gm.Move == a.Best || loss < 0.5;
        if (isBest)
        {
            // A sound sacrifice: the moved piece can be won, yet the move keeps (or wins) the game.
            PieceType moved = gm.Piece.Type();
            if (moved is not (PieceType.Pawn or PieceType.King) && Searcher.See(before, gm.Move) <= -200
                && winAfter >= 50 && winBefore < 92)
                return MoveClass.Brilliant;

            // The only good move: the second-best alternative was much worse. Obvious recaptures
            // (taking back on the square the opponent just captured on) don't count as "great".
            bool obviousRecapture = index > 0 && gm.Move.IsCapture && game.Moves[index - 1].Move.IsCapture
                && game.Moves[index - 1].Move.To == gm.Move.To;
            if (a.HasSecond && winBefore < 90 && !obviousRecapture)
            {
                double secondWin = WinPercent(a.SecondScore);
                double bestWin = WinPercent(a.Score);
                if (bestWin - secondWin >= 18 && winAfter >= 40) return MoveClass.Great;
            }
            return MoveClass.Best;
        }

        // A big chance was missed, but the position is still fine.
        double bestWinChance = WinPercent(a.Score);
        if (bestWinChance >= 70 && loss >= 12 && winAfter >= 35) return MoveClass.Miss;

        return loss switch
        {
            < 2 => MoveClass.Excellent,
            < 5 => MoveClass.Good,
            < 10 => MoveClass.Inaccuracy,
            < 20 => MoveClass.Mistake,
            _ => MoveClass.Blunder,
        };
    }

    /// <summary>Lichess win-percentage model: 0..100 for the side whose point of view the score is from.</summary>
    public static double WinPercent(int centipawns)
    {
        double cp = Math.Clamp(centipawns, -1000, 1000);
        return 50 + 50 * (2 / (1 + Math.Exp(-0.00368208 * cp)) - 1);
    }

    private static double SideAccuracy(List<MoveReview> reviews, Color side)
    {
        var acc = reviews.Where(r => r.Side == side).Select(r => r.Accuracy).ToList();
        if (acc.Count == 0) return 100;
        // Blend of arithmetic and harmonic means (harmonic punishes blunders, as Lichess does).
        double mean = acc.Average();
        double harmonic = acc.Count / acc.Sum(a => 1 / Math.Max(a, 1));
        return Math.Round((mean + harmonic) / 2, 1);
    }

    private static int TerminalScore(Position p) => p.InCheck ? -Searcher.Mate : 0;

    /// <summary>Mate scores become large finite values so win chances saturate sensibly.</summary>
    private static int Clamp(int score) => Searcher.IsMateScore(score) ? Math.Sign(score) * 2000 : score;
}
