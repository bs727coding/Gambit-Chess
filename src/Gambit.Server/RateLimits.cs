using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace Gambit.Server;

/// <summary>
/// Per-connection token bucket for hub calls, so one misbehaving client gets errors instead of
/// consuming the server. Normal play needs a call or two per move, far below the limit.
/// </summary>
public sealed class CallRateLimitFilter(ServerOptions options) : IHubFilter
{
    private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _buckets = new();

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        TokenBucketRateLimiter bucket = _buckets.GetOrAdd(context.Context.ConnectionId, _ => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.CallBurst,
            TokensPerPeriod = options.CallsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
        using RateLimitLease lease = bucket.AttemptAcquire();
        if (!lease.IsAcquired) throw new HubException("Too many requests - slow down.");
        return await next(context);
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        if (_buckets.TryRemove(context.Context.ConnectionId, out TokenBucketRateLimiter? bucket)) bucket.Dispose();
        await next(context, exception);
    }
}
