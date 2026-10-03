using Gambit.Core.Board;

namespace Gambit.Engine.Search;

/// <summary>
/// Multi-threaded search ("Lazy SMP"): several searchers share one transposition table and search
/// the same position. The main searcher runs on the calling thread and reports lines; the helpers
/// fill the table with results it can reuse, so it reaches each depth sooner. Results vary a little
/// from run to run, so this is for analysis only: bots, hints and the benchmark search single-threaded.
/// </summary>
public sealed class ParallelSearcher
{
    private const int HelperStackBytes = 4 << 20;

    private readonly TranspositionTable _table;
    private readonly Searcher _main;
    private readonly Searcher[] _helpers;

    public ParallelSearcher(int threads, int hashMegabytes = 64)
    {
        _table = new TranspositionTable(hashMegabytes);
        _main = new Searcher(_table);
        _helpers = [.. Enumerable.Range(1, Math.Max(1, threads) - 1).Select(i => new Searcher(_table) { HelperIndex = i })];
    }

    /// <summary>Half the processor cores, at most eight: plenty for analysis without hogging the machine.</summary>
    public static int DefaultThreads => Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    public int Threads => _helpers.Length + 1;

    /// <summary>
    /// Searches like <see cref="Searcher.Search"/>; node counts (in progress reports and the result)
    /// include the helpers. Not re-entrant: one search at a time.
    /// </summary>
    public SearchResult Search(Position position, SearchLimits limits, CancellationToken cancellationToken = default,
        Action<SearchInfo>? onInfo = null)
    {
        if (_helpers.Length == 0) return _main.Search(position, limits, cancellationToken, onInfo);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Helpers run until the main search is done; they never report.
        SearchLimits helperLimits = limits with { SoftTime = null, HardTime = null, MaxNodes = long.MaxValue };
        var threads = new Thread[_helpers.Length];
        for (int i = 0; i < _helpers.Length; i++)
        {
            Searcher helper = _helpers[i];
            threads[i] = new Thread(() =>
            {
                try
                {
                    helper.Search(position, helperLimits, stop.Token);
                }
                catch (Exception)
                {
                    // A helper only feeds the shared table; the main search carries on without it.
                }
            }, HelperStackBytes)
            {
                IsBackground = true,
                Name = $"Gambit search helper {i + 1}",
            };
            threads[i].Start();
        }

        try
        {
            Action<SearchInfo>? report = onInfo == null ? null : info => onInfo(info with { Nodes = info.Nodes + HelperNodes() });
            SearchResult result = _main.Search(position, limits, cancellationToken, report);
            return result with { Nodes = result.Nodes + HelperNodes() };
        }
        finally
        {
            stop.Cancel();
            foreach (Thread t in threads) t.Join();
        }
    }

    /// <summary>Forget everything learned from previous searches (new game).</summary>
    public void Reset()
    {
        _main.Reset(); // clears the shared table too
        foreach (Searcher helper in _helpers) helper.Reset();
    }

    private long HelperNodes()
    {
        long nodes = 0;
        foreach (Searcher helper in _helpers) nodes += helper.NodesSearched;
        return nodes;
    }
}
