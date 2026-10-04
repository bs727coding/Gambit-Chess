using Gambit.Core.Games;
using Gambit.Online;
using Gambit.Online.Client;
using Gambit.Server;
using Gambit.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Gambit.Tests;

/// <summary>Accounts end to end: a real server on a random localhost port, the HTTP account API and the hub.</summary>
public sealed class AccountTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gambit-accounts-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private string _url = "";

    public Task InitializeAsync() => StartServerAsync();

    public async Task DisposeAsync()
    {
        await StopServerAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }
    }

    private async Task StartServerAsync()
    {
        _app = ServerHost.CreateApp([], new ServerOptions { DataDirectory = _dir });
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();
        _url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    private async Task StopServerAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private AccountStore Accounts => _app.Services.GetRequiredService<AccountStore>();

    private Task<SessionDto> Register(string name) =>
        AccountClient.RegisterAsync(_url, name, $"pw-{name}-long-enough", Accounts.CreateInvite(null).Code, "tests");

    [Fact]
    public async Task The_first_account_uses_the_owner_invite_and_becomes_an_admin()
    {
        Invite owner = Assert.IsType<Invite>(Accounts.OwnerInvite());
        Assert.Equal(owner.Code, Accounts.OwnerInvite()!.Code); // reused until someone signs up
        Assert.True((await AccountClient.GetInfoAsync(_url)).InviteOnly);

        SessionDto session = await AccountClient.RegisterAsync(_url, "Blake", "a-good-password", owner.Code.ToLowerInvariant(), "tests");
        Assert.True(session.Account.IsAdmin);
        Assert.Null(Accounts.OwnerInvite());
    }

    [Fact]
    public async Task Sign_up_needs_a_valid_unused_invite_and_a_free_name()
    {
        var noCode = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.RegisterAsync(_url, "Sam", "a-good-password", null, null));
        Assert.Contains("invite-only", noCode.Message);
        await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.RegisterAsync(_url, "Sam", "a-good-password", "ABCD-EFGH", null));

        Invite invite = Accounts.CreateInvite(null);
        SessionDto sam = await AccountClient.RegisterAsync(_url, "Sam", "a-good-password", invite.Code, null);
        Assert.False(sam.Account.IsAdmin);

        var reused = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.RegisterAsync(_url, "Max", "a-good-password", invite.Code, null));
        Assert.Contains("isn't valid", reused.Message);
        var taken = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.RegisterAsync(_url, "SAM", "a-good-password", Accounts.CreateInvite(null).Code, null));
        Assert.Equal("That username is taken.", taken.Message);
        var weak = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.RegisterAsync(_url, "Max", "password1", Accounts.CreateInvite(null).Code, null));
        Assert.Equal("That password is too easy to guess.", weak.Message);
    }

    [Fact]
    public async Task Sign_in_checks_the_password_and_the_hub_needs_a_session()
    {
        await Register("Kim");
        var wrong = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.SignInAsync(_url, "Kim", "not-the-password", null));
        Assert.Equal("Wrong username or password.", wrong.Message);
        Assert.False(wrong.SignInRequired);

        SessionDto session = await AccountClient.SignInAsync(_url, "kim", "pw-Kim-long-enough", "laptop"); // names ignore case
        Assert.Equal("Kim", session.Account.Username);
        await using var client = new OnlineClient();
        await client.ConnectAsync(_url, session.Token);
        Assert.Equal("Kim", client.Me!.Name);

        await using var stranger = new OnlineClient();
        var rejected = await Assert.ThrowsAsync<OnlineAccountException>(() => stranger.ConnectAsync(_url, "not-a-real-session-key"));
        Assert.True(rejected.SignInRequired);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        SessionDto session = await Register("Lou");
        Assert.Equal("Lou", (await AccountClient.GetAccountAsync(_url, session.Token)).Username);
        await AccountClient.SignOutAsync(_url, session.Token);

        await using var client = new OnlineClient();
        Assert.True((await Assert.ThrowsAsync<OnlineAccountException>(() => client.ConnectAsync(_url, session.Token))).SignInRequired);
        Assert.True((await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.GetAccountAsync(_url, session.Token))).SignInRequired);
    }

    [Fact]
    public async Task Members_invite_friends_up_to_a_limit()
    {
        SessionDto member = await Register("Mia");
        InviteDto invite = await AccountClient.CreateInviteAsync(_url, member.Token);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", invite.Code);
        Assert.Equal(1, invite.UsesLeft);
        Assert.NotNull(invite.ExpiresAt);

        SessionDto friend = await AccountClient.RegisterAsync(_url, "Ned", "another-password", invite.Code, null);
        Assert.Equal("Ned", friend.Account.Username);

        for (int i = 0; i < 5; i++) await AccountClient.CreateInviteAsync(_url, member.Token);
        var limit = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.CreateInviteAsync(_url, member.Token));
        Assert.Contains("unused invites", limit.Message);
        Assert.Equal(5, (await AccountClient.GetInvitesAsync(_url, member.Token)).Count);
    }

    [Fact]
    public async Task A_ban_signs_the_player_out_and_keeps_them_out()
    {
        SessionDto session = await Register("Oz1");
        Assert.True(Accounts.SetBanned("Oz1", "abusive chat"));

        await using var client = new OnlineClient();
        Assert.True((await Assert.ThrowsAsync<OnlineAccountException>(() => client.ConnectAsync(_url, session.Token))).SignInRequired);
        var signIn = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.SignInAsync(_url, "Oz1", "pw-Oz1-long-enough", null));
        Assert.Equal("This account is suspended: abusive chat", signIn.Message);

        Assert.True(Accounts.SetBanned("Oz1", null));
        await AccountClient.SignInAsync(_url, "Oz1", "pw-Oz1-long-enough", null);
    }

    [Fact]
    public async Task Bans_and_renames_reach_players_who_are_connected()
    {
        SessionDto ann = await Register("Ann"), bob = await Register("Bob");
        await using var annClient = new OnlineClient();
        await using var bobClient = new OnlineClient();
        await annClient.ConnectAsync(_url, ann.Token);
        await bobClient.ConnectAsync(_url, bob.Token);
        AccountWatch watch = _app.Services.GetRequiredService<AccountWatch>();

        Assert.Null(Accounts.Rename("Ann", "Annie"));
        await watch.CheckAsync();
        Assert.Equal("Annie", (await annClient.CreateChallengeAsync(new TimeControlDto(300, 0), "white")).Creator.Name);

        var notice = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropped = new TaskCompletionSource<OnlineState>(TaskCreationOptions.RunContinuationsAsynchronously);
        bobClient.Notice += m => notice.TrySetResult(m);
        bobClient.StateChanged += s =>
        {
            if (s != OnlineState.Connected) dropped.TrySetResult(s);
        };
        Assert.True(Accounts.SetBanned("Bob", "cheating"));
        await watch.CheckAsync();
        Assert.Equal("This account is suspended: cheating", await notice.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await dropped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(OnlineState.Connected, annClient.State); // nobody else is affected
    }

    [Fact]
    public async Task Admins_download_the_database_and_members_cannot()
    {
        SessionDto owner = await AccountClient.RegisterAsync(_url, "Boss", "an-owner-password", Accounts.OwnerInvite()!.Code, "tests");
        SessionDto member = await Register("Mel");

        string file = Path.Combine(_dir, "download.db");
        await using (FileStream out1 = File.Create(file)) await AccountClient.DownloadBackupAsync(_url, owner.Token, out1);
        Assert.Equal("SQLite format 3 ", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(file), 0, 16));
        var copy = new AccountStore(new ServerDatabase(file), new ServerOptions { DataDirectory = _dir });
        Assert.Equal(new[] { "Boss", "Mel" }, copy.All().Select(a => a.Username).Order());

        var refused = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.DownloadBackupAsync(_url, member.Token, Stream.Null));
        Assert.Equal("Only the server's admins can download its database.", refused.Message);
    }

    [Fact]
    public async Task Changing_the_password_signs_out_other_devices()
    {
        SessionDto laptop = await Register("Pat");
        SessionDto phone = await AccountClient.SignInAsync(_url, "Pat", "pw-Pat-long-enough", "phone");
        var wrongCurrent = await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.ChangePasswordAsync(_url, laptop.Token, "nope-nope-nope", "a-brand-new-password"));
        Assert.Equal("Your current password isn't right.", wrongCurrent.Message);

        await AccountClient.ChangePasswordAsync(_url, laptop.Token, "pw-Pat-long-enough", "a-brand-new-password");
        await AccountClient.GetAccountAsync(_url, laptop.Token); // this device stays signed in
        await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.GetAccountAsync(_url, phone.Token));
        await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.SignInAsync(_url, "Pat", "pw-Pat-long-enough", null));
        await AccountClient.SignInAsync(_url, "Pat", "a-brand-new-password", null);
    }

    [Fact]
    public async Task Ratings_and_games_survive_a_server_restart()
    {
        SessionDto ray = await Register("Ray"), sue = await Register("Sue");
        await using (var white = new OnlineClient())
        await using (var black = new OnlineClient())
        {
            await white.ConnectAsync(_url, ray.Token);
            await black.ConnectAsync(_url, sue.Token);
            var started = new TaskCompletionSource<GameStartDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            var over = new TaskCompletionSource<GameOverDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            white.GameStarted += g => started.TrySetResult(g);
            white.GameOver += r => over.TrySetResult(r);
            ChallengeDto challenge = await white.CreateChallengeAsync(new TimeControlDto(180, 2), "white");
            Assert.True(await black.AcceptChallengeAsync(challenge.Code));
            string id = (await started.Task.WaitAsync(TimeSpan.FromSeconds(10))).GameId;
            Assert.True(await white.MakeMoveAsync(id, 1, "f2f3"));
            Assert.True(await black.MakeMoveAsync(id, 2, "e7e5"));
            Assert.True(await white.MakeMoveAsync(id, 3, "g2g4"));
            Assert.True(await black.MakeMoveAsync(id, 4, "d8h4"));
            Assert.Equal("0-1", (await over.Task.WaitAsync(TimeSpan.FromSeconds(10))).Result);
        }

        await StopServerAsync();
        await StartServerAsync();

        RatingStore ratings = _app.Services.GetRequiredService<RatingStore>();
        Assert.True(ratings.Get(sue.Account.Id, TimeCategory.Blitz).Rating > 1500);
        Assert.True(ratings.Get(ray.Account.Id, TimeCategory.Blitz).Rating < 1500);
        GameArchive archive = _app.Services.GetRequiredService<GameArchive>();
        Assert.Equal(1, archive.Count());
        Assert.Contains("Qh4#", Assert.Single(archive.Recent(sue.Account.Id, 5)).Pgn);
        await AccountClient.SignInAsync(_url, "Ray", "pw-Ray-long-enough", null); // accounts too, of course
    }
}
