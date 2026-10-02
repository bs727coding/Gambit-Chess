using Gambit.Core.Board;
using Gambit.Engine.Search;
using Microsoft.UI.Dispatching;

namespace Gambit.App.Services;

/// <summary>
/// Background engine for the analysis board: searches the given position (MultiPV) and streams
/// results back to the UI thread after every completed depth. Starting a new analysis cancels the old one.
/// </summary>
public sealed class AnalysisEngine : IDisposable
{
    private readonly Searcher _searcher = new(64);
    private readonly object _gate = new();
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();
    private CancellationTokenSource? _cts;

    public int MultiPv { get; set; } = 3;
    public int MaxDepth { get; set; } = 32;
    public TimeSpan MaxTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Raised on the UI thread with the position that was analysed and the latest info.</summary>
    public event Action<Position, SearchInfo>? InfoUpdated;

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public void Analyze(Position position)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        Position root = position.Clone();
        var limits = new SearchLimits
        {
            MaxDepth = MaxDepth,
            MultiPv = MultiPv,
            SoftTime = MaxTime,
            HardTime = MaxTime,
        };

        Task.Run(() =>
        {
            lock (_gate)
            {
                if (cts.IsCancellationRequested) return;
                try
                {
                    _searcher.Search(root, limits, cts.Token, info =>
                    {
                        if (cts.IsCancellationRequested) return;
                        _queue.TryEnqueue(() =>
                        {
                            if (!cts.IsCancellationRequested) InfoUpdated?.Invoke(root, info);
                        });
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn($"Analysis failed: {ex.Message}");
                }
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();
}
