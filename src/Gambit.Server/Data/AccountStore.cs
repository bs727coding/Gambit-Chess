using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace Gambit.Server.Data;

/// <summary>A registered player.</summary>
public sealed record Account(string Id, string Username, bool IsAdmin, DateTimeOffset CreatedAt, DateTimeOffset? BannedAt, string? BanReason)
{
    public bool IsBanned => BannedAt != null;
}

/// <summary>A code that lets someone create an account. <see cref="Code"/> is shown as "ABCD-EFGH".</summary>
public sealed record Invite(string Code, string? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, int UsesLeft, bool MakesAdmin, string? Note);

/// <summary>An account, or a message for the player explaining what went wrong.</summary>
public readonly record struct AccountResult(Account? Account, string? Error)
{
    public static AccountResult Fail(string error) => new(null, error);
}

/// <summary>
/// Accounts, sign-in sessions and invites. Passwords are kept as salted PBKDF2 hashes (ASP.NET Core's
/// password hasher) and session keys only as SHA-256 hashes, so a copy of the database reveals
/// neither. Thread-safe: every call uses its own pooled connection.
/// </summary>
public sealed class AccountStore(ServerDatabase db, ServerOptions options, TimeProvider? clock = null)
{
    private const string InviteAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O or 1/I
    private const int MaxFailures = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LastUsedGranularity = TimeSpan.FromHours(1);
    private static readonly PasswordHasher<Account> Hasher = new();
    private static readonly Account Nobody = new("", "", false, DateTimeOffset.MinValue, null, null);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset First)> _failures = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset Now => _clock.GetUtcNow();

    // ------------------------------------------------------------------ registration & sign-in

    /// <summary>Creates an account. On invite-only servers <paramref name="inviteCode"/> must be a valid, unused code.</summary>
    public AccountResult Register(string? username, string? password, string? inviteCode)
    {
        username = (username ?? "").Trim();
        password ??= "";
        if (NameRules.CheckUsername(username) is string badName) return AccountResult.Fail(badName);
        if (NameRules.CheckPassword(password, username) is string badPassword) return AccountResult.Fail(badPassword);

        using SqliteConnection c = db.Open();
        using SqliteTransaction tx = c.BeginTransaction();
        Invite? invite = null;
        if (options.SignUps == SignUpMode.Invite || !string.IsNullOrWhiteSpace(inviteCode))
        {
            invite = ReadInvites(c, tx, "code = $code", ("$code", NormalizeCode(inviteCode))).FirstOrDefault();
            if (invite == null || !IsOpen(invite))
                return AccountResult.Fail(options.SignUps == SignUpMode.Invite && string.IsNullOrWhiteSpace(inviteCode)
                    ? "This server is invite-only: enter the invite code you were given."
                    : "That invite code isn't valid (or was already used). Ask for a new one.");
        }
        if (Scalar(c, tx, "SELECT COUNT(*) FROM users WHERE username = $name", ("$name", username)) > 0)
            return AccountResult.Fail("That username is taken.");

        var account = new Account(NewId(), username, invite?.MakesAdmin == true, Now, null, null);
        Execute(c, tx, "INSERT INTO users (id, username, password_hash, is_admin, created_at, invited_by) VALUES ($id, $name, $hash, $admin, $at, $by)",
            ("$id", account.Id), ("$name", username), ("$hash", Hasher.HashPassword(account, password)), ("$admin", account.IsAdmin ? 1 : 0),
            ("$at", Stamp(account.CreatedAt)), ("$by", invite?.CreatedBy ?? (invite != null ? "admin" : null)));
        if (invite != null) Execute(c, tx, "UPDATE invites SET uses_left = uses_left - 1 WHERE code = $code", ("$code", NormalizeCode(invite.Code)));
        tx.Commit();
        return new AccountResult(account, null);
    }

    /// <summary>Checks a username and password. Repeated failures for one name pause sign-in for it for a while.</summary>
    public AccountResult SignIn(string? username, string? password)
    {
        username = (username ?? "").Trim();
        password ??= "";
        if (_failures.TryGetValue(username, out var f) && f.Count >= MaxFailures && Now - f.First < FailureWindow)
            return AccountResult.Fail("Too many wrong passwords. Wait a few minutes and try again.");

        (Account? account, string? hash) = FindWithHash(username);
        if (account == null || hash == null)
        {
            Hasher.HashPassword(Nobody, password); // the same work either way: timing doesn't reveal which names exist
            NoteFailure(username);
            return AccountResult.Fail("Wrong username or password.");
        }
        PasswordVerificationResult check = Hasher.VerifyHashedPassword(account, hash, password);
        if (check == PasswordVerificationResult.Failed)
        {
            NoteFailure(username);
            return AccountResult.Fail("Wrong username or password.");
        }
        _failures.TryRemove(username, out _);
        if (account.IsBanned) return AccountResult.Fail(BannedMessage(account));
        if (check == PasswordVerificationResult.SuccessRehashNeeded) SetPasswordHash(account, password);
        return new AccountResult(account, null);
    }

    /// <summary>Changes a password after checking the current one; signs out every other device.</summary>
    public string? ChangePassword(Account account, string? current, string? replacement, string keepSessionToken)
    {
        (_, string? hash) = FindWithHash(account.Username);
        if (hash == null || Hasher.VerifyHashedPassword(account, hash, current ?? "") == PasswordVerificationResult.Failed)
            return "Your current password isn't right.";
        if (NameRules.CheckPassword(replacement, account.Username) is string bad) return bad;
        SetPasswordHash(account, replacement!);
        using SqliteConnection c = db.Open();
        Execute(c, null, "DELETE FROM sessions WHERE user_id = $id AND token_hash <> $keep", ("$id", account.Id), ("$keep", HashToken(keepSessionToken)));
        return null;
    }

    // ------------------------------------------------------------------ sessions

    /// <summary>Starts a session and returns its secret key for the app; only a hash of it is stored.</summary>
    public string CreateSession(Account account, string? device)
    {
        string token = Base64Url(RandomNumberGenerator.GetBytes(32));
        using SqliteConnection c = db.Open();
        Execute(c, null, "INSERT INTO sessions (token_hash, user_id, created_at, last_used_at, device) VALUES ($hash, $user, $at, $at, $device)",
            ("$hash", HashToken(token)), ("$user", account.Id), ("$at", Stamp(Now)), ("$device", Truncate(device, 60)));
        return token;
    }

    /// <summary>The account signed in with this session key, or null (unknown, expired or banned).</summary>
    public Account? FindSession(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
        string hash = HashToken(token);
        using SqliteConnection c = db.Open();
        using SqliteCommand cmd = Command(c, null,
            "SELECT u.id, u.username, u.is_admin, u.created_at, u.banned_at, u.ban_reason, s.last_used_at FROM sessions s JOIN users u ON u.id = s.user_id WHERE s.token_hash = $hash",
            ("$hash", hash));
        using SqliteDataReader r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        Account account = ReadAccount(r);
        DateTimeOffset lastUsed = Parse(r.GetString(6));
        r.Close();

        if (Now - lastUsed > TimeSpan.FromDays(options.SessionDays))
        {
            Execute(c, null, "DELETE FROM sessions WHERE token_hash = $hash", ("$hash", hash));
            return null;
        }
        if (account.IsBanned) return null;
        if (Now - lastUsed > LastUsedGranularity)
            Execute(c, null, "UPDATE sessions SET last_used_at = $at WHERE token_hash = $hash", ("$at", Stamp(Now)), ("$hash", hash));
        return account;
    }

    public void EndSession(string? token)
    {
        if (string.IsNullOrEmpty(token)) return;
        using SqliteConnection c = db.Open();
        Execute(c, null, "DELETE FROM sessions WHERE token_hash = $hash", ("$hash", HashToken(token)));
    }

    // ------------------------------------------------------------------ invites

    /// <summary>Creates an invite code (<paramref name="validFor"/> null = never expires).</summary>
    public Invite CreateInvite(string? createdBy, int uses = 1, TimeSpan? validFor = null, bool makesAdmin = false, string? note = null)
    {
        var invite = new Invite(FormatCode(NewCode()), createdBy, Now, validFor is TimeSpan v ? Now + v : null, Math.Max(1, uses), makesAdmin, Truncate(note, 80));
        using SqliteConnection c = db.Open();
        Execute(c, null, "INSERT INTO invites (code, created_by, created_at, expires_at, uses_left, makes_admin, note) VALUES ($code, $by, $at, $exp, $uses, $admin, $note)",
            ("$code", NormalizeCode(invite.Code)), ("$by", createdBy), ("$at", Stamp(invite.CreatedAt)),
            ("$exp", invite.ExpiresAt is DateTimeOffset e ? Stamp(e) : null), ("$uses", invite.UsesLeft), ("$admin", makesAdmin ? 1 : 0), ("$note", invite.Note));
        return invite;
    }

    /// <summary>An invite a member makes for a friend (one use, limited number open at once), or why not.</summary>
    public (Invite? Invite, string? Error) CreateMemberInvite(Account member)
    {
        if (!member.IsAdmin && !options.MembersCanInvite) return (null, "Only the server's admins can invite people.");
        int open = OpenInvites(member.Id).Count;
        if (!member.IsAdmin && open >= options.InvitesPerMember)
            return (null, $"You already have {open} unused invites. Share those first; each works once.");
        return (CreateInvite(member.Id, 1, TimeSpan.FromDays(options.InviteDays)), null);
    }

    /// <summary>Invites that still work, newest first (all of them, or one member's).</summary>
    public IReadOnlyList<Invite> OpenInvites(string? createdBy = null)
    {
        using SqliteConnection c = db.Open();
        var invites = createdBy == null ? ReadInvites(c, null, "1 = 1") : ReadInvites(c, null, "created_by = $by", ("$by", createdBy));
        return [.. invites.Where(IsOpen).OrderByDescending(i => i.CreatedAt)];
    }

    public bool DeleteInvite(string code)
    {
        using SqliteConnection c = db.Open();
        return Execute(c, null, "DELETE FROM invites WHERE code = $code", ("$code", NormalizeCode(code))) > 0;
    }

    /// <summary>
    /// While nobody has an account: the code that creates the first one (an admin), reusing an unused
    /// one. Null once any account exists.
    /// </summary>
    public Invite? OwnerInvite()
    {
        if (All().Count > 0) return null;
        return OpenInvites().FirstOrDefault(i => i.MakesAdmin) ?? CreateInvite(null, 1, null, makesAdmin: true, note: "first account (admin)");
    }

    // ------------------------------------------------------------------ administration

    public Account? Find(string? username) => FindWithHash((username ?? "").Trim()).Account;

    public Account? FindById(string id)
    {
        using SqliteConnection c = db.Open();
        using SqliteCommand cmd = Command(c, null, $"SELECT {AccountColumns} FROM users WHERE id = $id", ("$id", id));
        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? ReadAccount(r) : null;
    }

    public IReadOnlyList<Account> All()
    {
        using SqliteConnection c = db.Open();
        using SqliteCommand cmd = Command(c, null, $"SELECT {AccountColumns} FROM users ORDER BY created_at");
        using SqliteDataReader r = cmd.ExecuteReader();
        var list = new List<Account>();
        while (r.Read()) list.Add(ReadAccount(r));
        return list;
    }

    /// <summary>Gives the account a new random password (returned) and signs it out everywhere; null if no such account.</summary>
    public string? ResetPassword(string username)
    {
        if (Find(username) is not Account account) return null;
        string password = string.Concat(Enumerable.Range(0, 12).Select(_ => InviteAlphabet[RandomNumberGenerator.GetInt32(InviteAlphabet.Length)])).ToLowerInvariant();
        SetPasswordHash(account, password);
        EndAllSessions(account.Id);
        return password;
    }

    /// <summary>Bans (with a reason) or unbans (reason null); a ban signs the account out everywhere.</summary>
    public bool SetBanned(string username, string? reason)
    {
        if (Find(username) is not Account account) return false;
        using SqliteConnection c = db.Open();
        Execute(c, null, "UPDATE users SET banned_at = $at, ban_reason = $reason WHERE id = $id",
            ("$at", reason == null ? null : Stamp(Now)), ("$reason", reason), ("$id", account.Id));
        if (reason != null) EndAllSessions(account.Id);
        return true;
    }

    /// <summary>Renames an account (same rules as signing up); returns why not, or null.</summary>
    public string? Rename(string username, string newName)
    {
        if (Find(username) is not Account account) return "No such account.";
        newName = (newName ?? "").Trim();
        if (NameRules.CheckUsername(newName) is string bad) return bad;
        if (!string.Equals(newName, account.Username, StringComparison.OrdinalIgnoreCase) && Find(newName) != null) return "That username is taken.";
        using SqliteConnection c = db.Open();
        Execute(c, null, "UPDATE users SET username = $name WHERE id = $id", ("$name", newName), ("$id", account.Id));
        return null;
    }

    public bool SetAdmin(string username, bool admin)
    {
        if (Find(username) is not Account account) return false;
        using SqliteConnection c = db.Open();
        Execute(c, null, "UPDATE users SET is_admin = $admin WHERE id = $id", ("$admin", admin ? 1 : 0), ("$id", account.Id));
        return true;
    }

    /// <summary>Deletes an account with its sessions and ratings. Finished games keep only its id.</summary>
    public bool Delete(string username)
    {
        if (Find(username) is not Account account) return false;
        using SqliteConnection c = db.Open();
        Execute(c, null, "DELETE FROM users WHERE id = $id", ("$id", account.Id));
        return true;
    }

    public static string BannedMessage(Account account) =>
        string.IsNullOrWhiteSpace(account.BanReason) ? "This account is suspended." : $"This account is suspended: {account.BanReason}";

    // ------------------------------------------------------------------ helpers

    private const string AccountColumns = "id, username, is_admin, created_at, banned_at, ban_reason";

    private (Account? Account, string? Hash) FindWithHash(string username)
    {
        using SqliteConnection c = db.Open();
        using SqliteCommand cmd = Command(c, null, $"SELECT {AccountColumns}, password_hash FROM users WHERE username = $name", ("$name", username));
        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? (ReadAccount(r), r.GetString(6)) : (null, null);
    }

    private void SetPasswordHash(Account account, string password)
    {
        using SqliteConnection c = db.Open();
        Execute(c, null, "UPDATE users SET password_hash = $hash WHERE id = $id", ("$hash", Hasher.HashPassword(account, password)), ("$id", account.Id));
    }

    private void EndAllSessions(string userId)
    {
        using SqliteConnection c = db.Open();
        Execute(c, null, "DELETE FROM sessions WHERE user_id = $id", ("$id", userId));
    }

    private void NoteFailure(string username) =>
        _failures.AddOrUpdate(username, _ => (1, Now), (_, f) => Now - f.First > FailureWindow ? (1, Now) : (f.Count + 1, f.First));

    private bool IsOpen(Invite invite) => invite.UsesLeft > 0 && (invite.ExpiresAt is not DateTimeOffset e || e > Now);

    private static Account ReadAccount(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetInt64(2) != 0, Parse(r.GetString(3)),
        r.IsDBNull(4) ? null : Parse(r.GetString(4)), r.IsDBNull(5) ? null : r.GetString(5));

    private static List<Invite> ReadInvites(SqliteConnection c, SqliteTransaction? tx, string where, params (string Name, object? Value)[] args)
    {
        using SqliteCommand cmd = Command(c, tx, $"SELECT code, created_by, created_at, expires_at, uses_left, makes_admin, note FROM invites WHERE {where}", args);
        using SqliteDataReader r = cmd.ExecuteReader();
        var list = new List<Invite>();
        while (r.Read())
        {
            list.Add(new Invite(FormatCode(r.GetString(0)), r.IsDBNull(1) ? null : r.GetString(1), Parse(r.GetString(2)),
                r.IsDBNull(3) ? null : Parse(r.GetString(3)), (int)r.GetInt64(4), r.GetInt64(5) != 0, r.IsDBNull(6) ? null : r.GetString(6)));
        }
        return list;
    }

    internal static SqliteCommand Command(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        SqliteCommand cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach ((string name, object? value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    internal static int Execute(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using SqliteCommand cmd = Command(c, tx, sql, args);
        return cmd.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using SqliteCommand cmd = Command(c, tx, sql, args);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>UTC, second precision, sortable as text.</summary>
    internal static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    internal static DateTimeOffset Parse(string s) =>
        DateTimeOffset.ParseExact(s, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    private static string NewCode() => string.Concat(Enumerable.Range(0, 8).Select(_ => InviteAlphabet[RandomNumberGenerator.GetInt32(InviteAlphabet.Length)]));

    /// <summary>"abcd-efgh " → "ABCDEFGH".</summary>
    public static string NormalizeCode(string? code) => string.Concat((code ?? "").Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static string FormatCode(string code)
    {
        string c = NormalizeCode(code);
        return c.Length == 8 ? $"{c[..4]}-{c[4..]}" : c;
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Truncate(string? s, int max) => s == null ? null : s.Length <= max ? s : s[..max];
}
