using System.Collections.Concurrent;
using Gambit.Core.Games;
using Gambit.Core.Rating;
using Gambit.Online;
using Gambit.Server.Data;

namespace Gambit.Server;

/// <summary>A connected (or recently connected) player: an account, by its public id.</summary>
public sealed class Player(string publicId, string name)
{
    public string PublicId { get; } = publicId;
    public string Name { get; set; } = name;
    public string? ConnectionId { get; set; }
    public bool Connected => ConnectionId != null;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Tracks players and their hub connections (one live connection per account).</summary>
public sealed class PlayerRegistry(RatingStore ratings)
{
    private readonly ConcurrentDictionary<string, Player> _byId = new();
    private readonly ConcurrentDictionary<string, Player> _byConnection = new();

    public int OnlineCount => _byConnection.Count;

    /// <summary>Registers a connection for an account; returns the connection it replaced, if any.</summary>
    public Player Connect(string accountId, string username, string connectionId, out string? replaced)
    {
        Player p = _byId.GetOrAdd(accountId, id => new Player(id, username));
        replaced = p.ConnectionId;
        if (replaced != null) _byConnection.TryRemove(replaced, out _);
        p.ConnectionId = connectionId;
        p.Name = username; // renames take effect on the next connection
        p.LastSeen = DateTimeOffset.UtcNow;
        _byConnection[connectionId] = p;
        return p;
    }

    public Player? ByConnection(string connectionId) => _byConnection.GetValueOrDefault(connectionId);

    public Player? ById(string accountId) => _byId.GetValueOrDefault(accountId);

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
}

/// <summary>Who may create an account.</summary>
public enum SignUpMode
{
    /// <summary>New accounts need an invite code (the default).</summary>
    Invite,

    /// <summary>Anyone who can reach the server may sign up.</summary>
    Open,
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

    /// <summary>Sign-in and sign-up requests per client IP per minute.</summary>
    public int AccountRequestsPerMinute { get; set; } = 20;

    /// <summary>Open challenge codes per player; creating more drops the oldest.</summary>
    public int MaxOpenChallenges { get; set; } = 5;

    public SignUpMode SignUps { get; set; } = SignUpMode.Invite;

    /// <summary>Members (not only admins) may create invite codes for friends.</summary>
    public bool MembersCanInvite { get; set; } = true;

    /// <summary>Unused invites a member may have at once (admins: no limit).</summary>
    public int InvitesPerMember { get; set; } = 5;

    /// <summary>How long a member's invite stays valid.</summary>
    public int InviteDays { get; set; } = 14;

    /// <summary>A sign-in lasts until it goes unused this long.</summary>
    public int SessionDays { get; set; } = 180;

    /// <summary>Games walked out of (not moving, resigning at once, leaving) within <see cref="WalkoutWindow"/> before quick pairing pauses.</summary>
    public int WalkoutsBeforePause { get; set; } = 3;

    public TimeSpan WalkoutWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long quick pairing stays paused after the last walkout.</summary>
    public TimeSpan PairingPause { get; set; } = TimeSpan.FromMinutes(10);
}
