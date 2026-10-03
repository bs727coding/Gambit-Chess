using Microsoft.UI.Dispatching;

namespace Gambit.App.Helpers;

/// <summary>
/// One-shot delays on the UI thread. Each timer stays referenced until it fires: a
/// <see cref="DispatcherQueueTimer"/> that nothing references can be garbage-collected before its
/// Tick, which silently drops the callback (a toast that never hides, a puzzle reply that never comes).
/// </summary>
public static class Delay
{
    private static readonly HashSet<DispatcherQueueTimer> Pending = [];

    /// <summary>Runs <paramref name="action"/> on <paramref name="queue"/>'s thread after <paramref name="delay"/>.</summary>
    public static void Run(DispatcherQueue queue, TimeSpan delay, Action action)
    {
        DispatcherQueueTimer timer = queue.CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            Pending.Remove(t);
            action();
        };
        Pending.Add(timer);
        timer.Start();
    }
}
