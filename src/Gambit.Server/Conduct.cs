using System.Collections.Concurrent;

namespace Gambit.Server;

/// <summary>
/// Remembers who walks out of games: not making the first move, resigning before the game gets going,
/// or disconnecting for good. A player with <see cref="ServerOptions.WalkoutsBeforePause"/> walkouts
/// within <see cref="ServerOptions.WalkoutWindow"/> sits out quick pairing (strangers) for
/// <see cref="ServerOptions.PairingPause"/> after the last one; games with friends stay open.
/// In memory: a server restart forgives everyone.
/// </summary>
public sealed class Conduct(ServerOptions options, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _walkouts = new();

    public void RecordWalkout(string playerId)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        List<DateTimeOffset> list = _walkouts.GetOrAdd(playerId, _ => []);
        lock (list)
        {
            list.RemoveAll(t => now - t > options.WalkoutWindow);
            list.Add(now);
        }
    }

    /// <summary>How much longer the player sits out quick pairing, or null.</summary>
    public TimeSpan? PairingPause(string playerId)
    {
        if (!_walkouts.TryGetValue(playerId, out List<DateTimeOffset>? list)) return null;
        DateTimeOffset now = _clock.GetUtcNow();
        lock (list)
        {
            list.RemoveAll(t => now - t > options.WalkoutWindow);
            if (list.Count < options.WalkoutsBeforePause) return null;
            TimeSpan left = list[^1] + options.PairingPause - now;
            return left > TimeSpan.Zero ? left : null;
        }
    }
}
