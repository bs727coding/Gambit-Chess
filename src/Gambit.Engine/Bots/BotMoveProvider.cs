using System.Diagnostics;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Sessions;
using Gambit.Engine.Search;

namespace Gambit.Engine.Bots;

/// <summary>
/// Plays moves for a <see cref="BotProfile"/>. The engine scores the top N candidate moves; weaker
/// bots then add noise and sample with a softmax (so they prefer good moves but sometimes miss
/// things), and occasionally play a random move. Strong bots just play the best line.
/// </summary>
public sealed class BotMoveProvider : IMoveProvider
{
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

        if (Profile.RandomMoveChance > 0 && _rng.NextDouble() < Profile.RandomMoveChance)
            return legal[_rng.Next(legal.Count)];

        SearchLimits limits = BuildLimits(snapshot, legal.Count);
        SearchResult result;
        lock (_searchLock) result = _searcher.Search(pos, limits, cancellationToken);
        if (result.Lines.Count <= 1 || Profile.Temperature <= 0) return result.BestMove;

        // Weakened choice: noisy scores + softmax sampling over the candidate lines.
        var lines = result.Lines;
        Span<double> noisy = stackalloc double[lines.Count];
        double best = double.MinValue;
        for (int i = 0; i < lines.Count; i++)
        {
            double score = Math.Clamp(lines[i].Score, -1500, 1500);
            if (Profile.EvalNoise > 0) score += Gaussian() * Profile.EvalNoise;
            noisy[i] = score;
            best = Math.Max(best, score);
        }

        double total = 0;
        Span<double> weights = stackalloc double[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            weights[i] = Math.Exp((noisy[i] - best) / Profile.Temperature);
            total += weights[i];
        }

        double pick = _rng.NextDouble() * total;
        for (int i = 0; i < lines.Count; i++)
        {
            pick -= weights[i];
            if (pick <= 0) return lines[i].Move;
        }
        return lines[0].Move;
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

    private double Gaussian()
    {
        double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
