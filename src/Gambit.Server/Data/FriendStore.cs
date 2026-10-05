using Microsoft.Data.Sqlite;

namespace Gambit.Server.Data;

/// <summary>
/// Friends, in the friends table: a row (A, B, pending) is A's request to B; friends have a row each
/// way, both accepted. Requests and answers go by username; ids are what's stored.
/// </summary>
public sealed class FriendStore(ServerDatabase db, AccountStore accounts)
{
    public const int MaxFriends = 200, MaxPendingRequests = 20;

    /// <summary>Asks <paramref name="username"/> to be friends, or accepts their request. Null on success, otherwise why not.</summary>
    public string? Request(string myId, string username)
    {
        if (accounts.Find(username) is not Account other || other.IsBanned) return "There's no player with that name.";
        if (other.Id == myId) return "That's you.";
        using SqliteConnection c = db.Open();
        using SqliteTransaction tx = c.BeginTransaction();
        string? mine = Status(c, tx, myId, other.Id), theirs = Status(c, tx, other.Id, myId);
        if (mine == "accepted") return $"You and {other.Username} are already friends.";
        if (theirs == "pending")
        {
            // They asked first: this is a yes.
            if (Count(c, tx, "SELECT COUNT(*) FROM friends WHERE user_id = $id AND status = 'accepted'", myId) >= MaxFriends)
                return $"You have {MaxFriends} friends already.";
            AccountStore.Execute(c, tx, "UPDATE friends SET status = 'accepted' WHERE user_id = $a AND friend_id = $b", ("$a", other.Id), ("$b", myId));
            AccountStore.Execute(c, tx, "INSERT OR REPLACE INTO friends (user_id, friend_id, status, created_at) VALUES ($a, $b, 'accepted', $now)",
                ("$a", myId), ("$b", other.Id), ("$now", AccountStore.Stamp(DateTimeOffset.UtcNow)));
        }
        else
        {
            if (mine == "pending") return $"You already asked {other.Username}.";
            if (Count(c, tx, "SELECT COUNT(*) FROM friends WHERE user_id = $id AND status = 'pending'", myId) >= MaxPendingRequests)
                return "You have many requests waiting for an answer. Try again when some are answered.";
            AccountStore.Execute(c, tx, "INSERT INTO friends (user_id, friend_id, status, created_at) VALUES ($a, $b, 'pending', $now)",
                ("$a", myId), ("$b", other.Id), ("$now", AccountStore.Stamp(DateTimeOffset.UtcNow)));
        }
        tx.Commit();
        return null;
    }

    /// <summary>Ends a friendship, declines their request or withdraws mine. False if there was nothing to remove.</summary>
    public bool Remove(string myId, string username)
    {
        if (accounts.Find(username) is not Account other) return false;
        using SqliteConnection c = db.Open();
        return AccountStore.Execute(c, null,
            "DELETE FROM friends WHERE (user_id = $a AND friend_id = $b) OR (user_id = $b AND friend_id = $a)",
            ("$a", myId), ("$b", other.Id)) > 0;
    }

    public bool AreFriends(string a, string b)
    {
        using SqliteConnection c = db.Open();
        return Status(c, null, a, b) == "accepted";
    }

    /// <summary>My friends (id, name), the requests waiting for me and the ones I sent (names), alphabetical.</summary>
    public (IReadOnlyList<(string Id, string Name)> Friends, IReadOnlyList<string> Incoming, IReadOnlyList<string> Outgoing) List(string myId)
    {
        using SqliteConnection c = db.Open();
        var friends = new List<(string, string)>();
        var incoming = new List<string>();
        var outgoing = new List<string>();
        using (SqliteCommand cmd = AccountStore.Command(c, null,
            "SELECT u.id, u.username, f.status, 'out' FROM friends f JOIN users u ON u.id = f.friend_id WHERE f.user_id = $id " +
            "UNION ALL SELECT u.id, u.username, f.status, 'in' FROM friends f JOIN users u ON u.id = f.user_id " +
            "WHERE f.friend_id = $id AND f.status = 'pending'", ("$id", myId)))
        using (SqliteDataReader r = cmd.ExecuteReader())
            while (r.Read())
            {
                (string id, string name, string status, string way) = (r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3));
                if (status == "accepted") friends.Add((id, name));
                else if (way == "in") incoming.Add(name);
                else outgoing.Add(name);
            }
        return ([.. friends.OrderBy(f => f.Item2, StringComparer.OrdinalIgnoreCase)],
            [.. incoming.Order(StringComparer.OrdinalIgnoreCase)], [.. outgoing.Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>Everyone who should hear about a change to this player: friends and both sides of pending requests.</summary>
    public IReadOnlyList<string> Related(string myId)
    {
        using SqliteConnection c = db.Open();
        using SqliteCommand cmd = AccountStore.Command(c, null,
            "SELECT friend_id FROM friends WHERE user_id = $id UNION SELECT user_id FROM friends WHERE friend_id = $id", ("$id", myId));
        using SqliteDataReader r = cmd.ExecuteReader();
        var ids = new List<string>();
        while (r.Read()) ids.Add(r.GetString(0));
        return ids;
    }

    private static string? Status(SqliteConnection c, SqliteTransaction? tx, string from, string to)
    {
        using SqliteCommand cmd = AccountStore.Command(c, tx, "SELECT status FROM friends WHERE user_id = $a AND friend_id = $b", ("$a", from), ("$b", to));
        return cmd.ExecuteScalar() as string;
    }

    private static long Count(SqliteConnection c, SqliteTransaction tx, string sql, string id)
    {
        using SqliteCommand cmd = AccountStore.Command(c, tx, sql, ("$id", id));
        return (long)cmd.ExecuteScalar()!;
    }
}
