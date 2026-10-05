using Microsoft.Data.Sqlite;

namespace Gambit.Server.Data;

/// <summary>
/// The server's SQLite database, gambit.db in the data folder: accounts, sign-in sessions, invites,
/// ratings, finished games and friends. A single file, so a copy of it is a complete backup. WAL mode lets the
/// admin commands (a second process) read and write while the server runs.
/// </summary>
public sealed class ServerDatabase
{
    /// <summary>Schema version this server writes (PRAGMA user_version). Add a migration step when raising it.</summary>
    public const int SchemaVersion = 2;

    public const string FileName = "gambit.db";

    private readonly string _connectionString;

    public ServerDatabase(ServerOptions options) : this(System.IO.Path.Combine(options.DataDirectory, FileName))
    {
    }

    public ServerDatabase(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
        }.ToString();
        Migrate();
    }

    public string Path { get; }

    /// <summary>An open connection (pooled; dispose it when done). Foreign keys are enforced.</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return connection;
    }

    /// <summary>The file that, when present at startup, replaces the database (how a backup is put back).</summary>
    public const string RestoreFileName = "restore.db";

    /// <summary>
    /// If the data folder holds <see cref="RestoreFileName"/>, makes it the database: the current one (with
    /// its -wal/-shm files) is kept as gambit-replaced-TIME.db. Call before the database is opened.
    /// Returns a description of what happened, or null.
    /// </summary>
    public static string? ApplyPendingRestore(string dataDirectory)
    {
        string restore = System.IO.Path.Combine(dataDirectory, RestoreFileName);
        if (!File.Exists(restore)) return null;
        string current = System.IO.Path.Combine(dataDirectory, FileName);
        string kept = System.IO.Path.Combine(dataDirectory, $"gambit-replaced-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
        foreach (string suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(current + suffix)) File.Move(current + suffix, kept + suffix);
        File.Move(restore, current);
        return $"Restored the database from {RestoreFileName}; the previous one is kept as {System.IO.Path.GetFileName(kept)}.";
    }

    /// <summary>Writes a consistent copy of the database to <paramref name="file"/> (safe while the server runs).</summary>
    public void BackupTo(string file)
    {
        string full = System.IO.Path.GetFullPath(file);
        if (File.Exists(full)) File.Delete(full);
        using SqliteConnection c = Open();
        using SqliteCommand cmd = c.CreateCommand();
        cmd.CommandText = "VACUUM INTO $file";
        cmd.Parameters.AddWithValue("$file", full);
        cmd.ExecuteNonQuery();
    }

    public static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Migrate()
    {
        using SqliteConnection c = Open();
        Execute(c, "PRAGMA journal_mode = WAL;");
        long version;
        using (SqliteCommand cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            version = (long)cmd.ExecuteScalar()!;
        }
        if (version > SchemaVersion)
            throw new InvalidOperationException($"{Path} was written by a newer server (schema {version}); update the server.");

        if (version < 1)
        {
            using SqliteTransaction tx = c.BeginTransaction();
            Execute(c, SchemaV1, tx);
            Execute(c, "PRAGMA user_version = 1;", tx);
            tx.Commit();
        }
        if (version < 2)
        {
            using SqliteTransaction tx = c.BeginTransaction();
            Execute(c, SchemaV2, tx);
            Execute(c, "PRAGMA user_version = 2;", tx);
            tx.Commit();
        }
    }

    /// <summary>2: friends (a pending row is a request; friends have an accepted row each way).</summary>
    private const string SchemaV2 = """
        CREATE TABLE friends (
            user_id    TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            friend_id  TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            status     TEXT NOT NULL,
            created_at TEXT NOT NULL,
            PRIMARY KEY (user_id, friend_id)
        );
        CREATE INDEX friends_by_friend ON friends(friend_id);
        """;

    private const string SchemaV1 = """
        CREATE TABLE users (
            id            TEXT PRIMARY KEY,
            username      TEXT NOT NULL COLLATE NOCASE UNIQUE,
            password_hash TEXT NOT NULL,
            is_admin      INTEGER NOT NULL DEFAULT 0,
            created_at    TEXT NOT NULL,
            invited_by    TEXT,
            banned_at     TEXT,
            ban_reason    TEXT
        );
        CREATE TABLE sessions (
            token_hash   TEXT PRIMARY KEY,
            user_id      TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            created_at   TEXT NOT NULL,
            last_used_at TEXT NOT NULL,
            device       TEXT
        );
        CREATE INDEX sessions_by_user ON sessions(user_id);
        CREATE TABLE invites (
            code        TEXT PRIMARY KEY,
            created_by  TEXT REFERENCES users(id) ON DELETE SET NULL,
            created_at  TEXT NOT NULL,
            expires_at  TEXT,
            uses_left   INTEGER NOT NULL,
            makes_admin INTEGER NOT NULL DEFAULT 0,
            note        TEXT
        );
        CREATE TABLE ratings (
            user_id    TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            category   TEXT NOT NULL,
            rating     REAL NOT NULL,
            deviation  REAL NOT NULL,
            volatility REAL NOT NULL,
            PRIMARY KEY (user_id, category)
        );
        CREATE TABLE games (
            id           TEXT PRIMARY KEY,
            white_id     TEXT NOT NULL,
            black_id     TEXT NOT NULL,
            time_control TEXT NOT NULL,
            result       TEXT NOT NULL,
            termination  TEXT NOT NULL,
            rated        INTEGER NOT NULL,
            white_change INTEGER,
            black_change INTEGER,
            started_at   TEXT NOT NULL,
            ended_at     TEXT NOT NULL,
            pgn          TEXT NOT NULL
        );
        CREATE INDEX games_by_white ON games(white_id, ended_at);
        CREATE INDEX games_by_black ON games(black_id, ended_at);
        """;
}
