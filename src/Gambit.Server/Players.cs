using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gambit.Core.Games;
using Gambit.Core.Rating;
using Gambit.Online;

namespace Gambit.Server;

/// <summary>A connected (or recently connected) player. Identity = secret client token (guest account).</summary>
public sealed class Player(string secret, string publicId)
{
    public string Secret { get; } = secret;
    public string PublicId { get; } = publicId;
    public string Name { get; set; } = "Guest";
    public string? ConnectionId { get; set; }
    public bool Connected => ConnectionId != null;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Tracks players and their connections. The client's secret token never leaves the server.</summary>
public sealed class PlayerRegistry(RatingStore ratings)
{
    private readonly ConcurrentDictionary<string, Player> _bySecret = new();
    private readonly ConcurrentDictionary<string, Player> _byConnection = new();

    public int OnlineCount => _byConnection.Count;

    public Player Connect(string secret, string connectionId, string name)
    {
        Player p = _bySecret.GetOrAdd(secret, s => new Player(s, PublicIdFor(s)));
        if (p.ConnectionId != null) _byConnection.TryRemove(p.ConnectionId, out _);
        p.ConnectionId = connectionId;
        p.Name = Sanitize(name);
        p.LastSeen = DateTimeOffset.UtcNow;
        _byConnection[connectionId] = p;
        return p;
    }

    public Player? ByConnection(string connectionId) => _byConnection.GetValueOrDefault(connectionId);

    public Player? Disconnect(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out Player? p)) return null;
        if (p.ConnectionId == connectionId) p.ConnectionId = null;
        p.LastSeen = DateTimeOffset.UtcNow;
        return p;
    }

    public PlayerDto ToDto(Player p, TimeCategory category)
    {
        Glicko2Rating r = ratings.Get(p.PublicId, category);
        return new PlayerDto(p.PublicId, p.Name, r.Rounded, r.IsProvisional);
    }

    private static string PublicIdFor(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))[..12].ToLowerInvariant();

    private static string Sanitize(string name)
    {
        string clean = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '.').ToArray()).Trim();
        return clean.Length == 0 ? "Guest" : clean.Length > 24 ? clean[..24] : clean;
    }
}

/// <summary>Glicko-2 ratings per player and time category, persisted to ratings.json.</summary>
public sealed class RatingStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, Dictionary<TimeCategory, Glicko2Rating>> _ratings;

    public RatingStore(ServerOptions options)
    {
        _path = Path.Combine(options.DataDirectory, "ratings.json");
        _ratings = Load(_path);
    }

    public Glicko2Rating Get(string playerId, TimeCategory category)
    {
        lock (_gate)
        {
            return _ratings.TryGetValue(playerId, out var map) && map.TryGetValue(category, out var r) ? r : Glicko2Rating.Default;
        }
    }

    /// <summary>Rates a finished game; returns (white change, black change).</summary>
    public (int White, int Black) Rate(string whiteId, string blackId, TimeCategory category, double whiteScore)
    {
        lock (_gate)
        {
            Glicko2Rating w = Get(whiteId, category), b = Get(blackId, category);
            Glicko2Rating w2 = Glicko2.Update(w, b, whiteScore, minDeviation: 45);
            Glicko2Rating b2 = Glicko2.Update(b, w, 1 - whiteScore, minDeviation: 45);
            Set(whiteId, category, w2);
            Set(blackId, category, b2);
            Save();
            return (w2.Rounded - w.Rounded, b2.Rounded - b.Rounded);
        }
    }

    private void Set(string id, TimeCategory category, Glicko2Rating r)
    {
        if (!_ratings.TryGetValue(id, out var map)) _ratings[id] = map = [];
        map[category] = r;
    }

    private void Save()
    {
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_ratings));
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Ratings are best-effort persistence; the in-memory state stays authoritative.
        }
    }

    private static Dictionary<string, Dictionary<TimeCategory, Glicko2Rating>> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<TimeCategory, Glicko2Rating>>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            // Start fresh if the file is unreadable.
        }
        return [];
    }
}

public sealed class ServerOptions
{
    public string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>How long a disconnected player has to come back before losing by abandonment.</summary>
    public TimeSpan ReconnectGrace { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Games are aborted if White hasn't moved within this time.</summary>
    public TimeSpan FirstMoveTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Hub calls per connection per second (sustained); short bursts up to <see cref="CallBurst"/>.</summary>
    public int CallsPerSecond { get; set; } = 10;

    public int CallBurst { get; set; } = 30;

    /// <summary>Hub HTTP requests (negotiate + connect) per client IP per minute.</summary>
    public int ConnectionsPerMinute { get; set; } = 60;

    /// <summary>Open challenge codes per player; creating more drops the oldest.</summary>
    public int MaxOpenChallenges { get; set; } = 5;
}
