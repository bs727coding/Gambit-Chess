using Gambit.Core.Games;
using Gambit.Core.Rating;
using Microsoft.Data.Sqlite;

namespace Gambit.Server.Data;

/// <summary>Glicko-2 ratings per account and time category (bullet, blitz, rapid, classical), in the database.</summary>
public sealed class RatingStore(ServerDatabase db)
{
    private readonly object _gate = new(); // one read-modify-write at a time

    public Glicko2Rating Get(string playerId, TimeCategory category)
    {
        using SqliteConnection c = db.Open();
        return Read(c, null, playerId, category);
    }

    /// <summary>Rates a finished game; returns (white change, black change).</summary>
    public (int White, int Black) Rate(string whiteId, string blackId, TimeCategory category, double whiteScore)
    {
        lock (_gate)
        {
            using SqliteConnection c = db.Open();
            using SqliteTransaction tx = c.BeginTransaction();
            Glicko2Rating w = Read(c, tx, whiteId, category), b = Read(c, tx, blackId, category);
            Glicko2Rating w2 = Glicko2.Update(w, b, whiteScore, minDeviation: 45);
            Glicko2Rating b2 = Glicko2.Update(b, w, 1 - whiteScore, minDeviation: 45);
            Write(c, tx, whiteId, category, w2);
            Write(c, tx, blackId, category, b2);
            tx.Commit();
            return (w2.Rounded - w.Rounded, b2.Rounded - b.Rounded);
        }
    }

    private static Glicko2Rating Read(SqliteConnection c, SqliteTransaction? tx, string playerId, TimeCategory category)
    {
        using SqliteCommand cmd = AccountStore.Command(c, tx, "SELECT rating, deviation, volatility FROM ratings WHERE user_id = $id AND category = $cat",
            ("$id", playerId), ("$cat", category.ToString()));
        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? new Glicko2Rating(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2)) : Glicko2Rating.Default;
    }

    private static void Write(SqliteConnection c, SqliteTransaction tx, string playerId, TimeCategory category, Glicko2Rating r) =>
        AccountStore.Execute(c, tx,
            "INSERT INTO ratings (user_id, category, rating, deviation, volatility) VALUES ($id, $cat, $r, $d, $v) " +
            "ON CONFLICT (user_id, category) DO UPDATE SET rating = $r, deviation = $d, volatility = $v",
            ("$id", playerId), ("$cat", category.ToString()), ("$r", r.Rating), ("$d", r.Deviation), ("$v", r.Volatility));
}

/// <summary>Finished games (with their PGN) in the database.</summary>
public sealed class GameArchive(ServerDatabase db)
{
    public void Save(string id, string whiteId, string blackId, string timeControl, string result, string termination,
        bool rated, int? whiteChange, int? blackChange, DateTimeOffset startedAt, DateTimeOffset endedAt, string pgn)
    {
        using SqliteConnection c = db.Open();
        AccountStore.Execute(c, null,
            "INSERT OR REPLACE INTO games (id, white_id, black_id, time_control, result, termination, rated, white_change, black_change, started_at, ended_at, pgn) " +
            "VALUES ($id, $w, $b, $tc, $result, $term, $rated, $wc, $bc, $start, $end, $pgn)",
            ("$id", id), ("$w", whiteId), ("$b", blackId), ("$tc", timeControl), ("$result", result), ("$term", termination),
            ("$rated", rated ? 1 : 0), ("$wc", whiteChange), ("$bc", blackChange),
            ("$start", AccountStore.Stamp(startedAt)), ("$end", AccountStore.Stamp(endedAt)), ("$pgn", pgn));
    }

    public int Count()
    {
        using SqliteConnection c = db.Open();
        using SqliteCommand cmd = AccountStore.Command(c, null, "SELECT COUNT(*) FROM games");
        return (int)(long)cmd.ExecuteScalar()!;
    }

    /// <summary>A player's most recent games: (id, PGN), newest first.</summary>
    public IReadOnlyList<(string Id, string Pgn)> Recent(string playerId, int count)
    {
        using SqliteConnection c = db.Open();
        using SqliteCommand cmd = AccountStore.Command(c, null,
            "SELECT id, pgn FROM games WHERE white_id = $id OR black_id = $id ORDER BY ended_at DESC LIMIT $n", ("$id", playerId), ("$n", count));
        using SqliteDataReader r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }
}
