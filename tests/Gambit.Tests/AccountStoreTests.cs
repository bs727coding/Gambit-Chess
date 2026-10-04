using Gambit.Server;
using Gambit.Server.Data;
using Microsoft.Data.Sqlite;

namespace Gambit.Tests;

/// <summary>The account store on its own (with a controllable clock), name rules and the admin commands.</summary>
public sealed class AccountStoreTests : IDisposable
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("gambit-store-").FullName;
    private readonly TestClock _clock = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ServerOptions Options(SignUpMode mode = SignUpMode.Invite) => new() { DataDirectory = _dir, SignUps = mode };

    private AccountStore Store(SignUpMode mode = SignUpMode.Invite) => new(new ServerDatabase(Options(mode)), Options(mode), _clock);

    [Theory]
    [InlineData("abc", true)]
    [InlineData("Magnus_C-99", true)]
    [InlineData("ab", false)]
    [InlineData("a23456789012345678901", false)]
    [InlineData("-dash", false)]
    [InlineData("has space", false)]
    [InlineData("émile", false)]
    [InlineData("Admin", false)]
    public void Usernames(string name, bool ok) => Assert.Equal(ok, NameRules.CheckUsername(name) == null);

    [Theory]
    [InlineData("knight-on-f5", true)]
    [InlineData("short", false)]
    [InlineData("Password123", false)]
    [InlineData("aaaabbbb", false)]
    [InlineData("Sam_the_man", false)] // same as the username
    public void Passwords(string password, bool ok) => Assert.Equal(ok, NameRules.CheckPassword(password, "sam_THE_man") == null);

    [Fact]
    public void Five_wrong_passwords_pause_sign_in_for_that_name()
    {
        AccountStore store = Store();
        Assert.NotNull(store.Register("Tom", "correct-horse", store.CreateInvite(null).Code).Account);
        for (int i = 0; i < 5; i++) Assert.Equal("Wrong username or password.", store.SignIn("Tom", "wrong-guess").Error);
        Assert.StartsWith("Too many wrong passwords", store.SignIn("tom", "correct-horse").Error);

        _clock.Now += TimeSpan.FromMinutes(11);
        Assert.NotNull(store.SignIn("Tom", "correct-horse").Account);
    }

    [Fact]
    public void Sessions_last_until_left_unused_for_months()
    {
        AccountStore store = Store();
        Account tom = store.Register("Tom", "correct-horse", store.CreateInvite(null).Code).Account!;
        string token = store.CreateSession(tom, "pc");
        _clock.Now += TimeSpan.FromDays(170);
        Assert.Equal(tom.Id, store.FindSession(token)?.Id); // use renews it
        _clock.Now += TimeSpan.FromDays(170);
        Assert.NotNull(store.FindSession(token));
        _clock.Now += TimeSpan.FromDays(181);
        Assert.Null(store.FindSession(token));
        Assert.Null(store.FindSession("made-up"));
    }

    [Fact]
    public void Invites_expire_and_open_servers_need_none()
    {
        AccountStore store = Store();
        Invite week = store.CreateInvite(null, uses: 2, validFor: TimeSpan.FromDays(7));
        Assert.NotNull(store.Register("Ann", "correct-horse", week.Code).Account);
        _clock.Now += TimeSpan.FromDays(8);
        Assert.Contains("isn't valid", store.Register("Bea", "correct-horse", week.Code).Error);
        Assert.Empty(store.OpenInvites());

        AccountStore open = Store(SignUpMode.Open);
        Assert.NotNull(open.Register("Cal", "correct-horse", null).Account);
    }

    [Fact]
    public void Admin_commands_manage_invites_and_accounts()
    {
        ServerOptions options = Options();
        string Run(params string[] args)
        {
            var output = new StringWriter();
            AdminCommands.Run(args, options, output);
            return output.ToString();
        }

        string invite = Run("invite", "--uses", "3", "--note", "chess club");
        string code = invite.Split(' ', '\n', '\r')[2];
        Assert.Contains("3 uses", invite);
        Assert.Contains(code, Run("invites"));

        var store = new AccountStore(new ServerDatabase(options), options);
        Assert.NotNull(store.Register("Dee", "correct-horse", code).Account);
        Assert.Contains("Dee", Run("users"));

        string reset = Run("reset-password", "dee");
        string password = reset.Split(": ")[1].Split('\n', '\r')[0].Trim();
        Assert.NotNull(store.SignIn("Dee", password).Account);
        Assert.Null(store.SignIn("Dee", "correct-horse").Account);

        Assert.Contains("suspended", Run("ban", "Dee", "cheating"));
        Assert.True(store.Find("Dee")!.IsBanned);
        Assert.Contains("can play again", Run("unban", "Dee"));
        Assert.Contains("is now Dex", Run("rename", "Dee", "Dex"));
        Assert.Contains("is an admin", Run("make-admin", "Dex"));
        Assert.True(store.Find("dex")!.IsAdmin);

        string copy = Path.Combine(_dir, "copy.db");
        Assert.Contains("copied", Run("backup", copy));
        var restored = new AccountStore(new ServerDatabase(copy), options);
        Assert.Equal("Dex", Assert.Single(restored.All()).Username);

        Assert.Contains("--yes", Run("delete-user", "Dex"));
        Assert.Contains("deleted", Run("delete-user", "Dex", "--yes"));
        Assert.Empty(store.All());
    }
}
