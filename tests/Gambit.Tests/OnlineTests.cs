using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Sessions;
using Gambit.Online;
using Gambit.Online.Client;
using Gambit.Server;
using Gambit.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Gambit.Tests;

/// <summary>End-to-end: a real server on a random localhost port and real SignalR clients.</summary>
public sealed class OnlineTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private WebApplication _app = null!;
    private string _url = "";
    private readonly List<OnlineClient> _clients = [];

    public async Task InitializeAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gambit-test-" + Guid.NewGuid().ToString("N"));
        _app = ServerHost.CreateApp([], new ServerOptions { DataDirectory = dir, ReconnectGrace = TimeSpan.FromSeconds(5) });
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();
        _url = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async Task DisposeAsync()
    {
        foreach (OnlineClient c in _clients) await c.DisposeAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>Signs a new player up (with an invite, as on a real server) and connects them.</summary>
    private async Task<OnlineClient> Connect(string name) => (await Join(name)).Client;

    /// <summary>Like <see cref="Connect"/>, also returning the session key for the HTTP endpoints.</summary>
    private async Task<(OnlineClient Client, string Token)> Join(string name)
    {
        Invite invite = _app.Services.GetRequiredService<AccountStore>().CreateInvite(null);
        SessionDto session = await AccountClient.RegisterAsync(_url, name, $"pw-{name}-long-enough", invite.Code, "tests");
        var client = new OnlineClient();
        _clients.Add(client);
        await client.ConnectAsync(_url, session.Token);
        return (client, session.Token);
    }

    private static Task Signal(Action<Action> subscribe)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        subscribe(() => tcs.TrySetResult());
        return tcs.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task Friends_ask_accept_see_each_other_and_remove()
    {
        var (alice, aliceKey) = await Join("Alicia");
        var (bob, bobKey) = await Join("Bobby");

        Task bobHears = Signal(h => bob.FriendsChanged += h);
        FriendsDto asked = await AccountClient.AddFriendAsync(_url, aliceKey, "bobby"); // names are case-insensitive
        Assert.Equal(["Bobby"], asked.Outgoing);
        await bobHears;
        Assert.Equal(["Alicia"], (await AccountClient.GetFriendsAsync(_url, bobKey)).Incoming);

        Task aliceHears = Signal(h => alice.FriendsChanged += h);
        FriendsDto bobs = await AccountClient.AddFriendAsync(_url, bobKey, "Alicia"); // yes
        await aliceHears;
        FriendDto friend = Assert.Single(bobs.Friends);
        Assert.Equal(("Alicia", "online"), (friend.Username, friend.Status));
        Assert.Empty(bobs.Incoming);
        Assert.Equal("Bobby", Assert.Single((await AccountClient.GetFriendsAsync(_url, aliceKey)).Friends).Username);

        async Task<string> Refused(string name) =>
            (await Assert.ThrowsAsync<OnlineAccountException>(() => AccountClient.AddFriendAsync(_url, aliceKey, name))).Message;
        Assert.Equal("There's no player with that name.", await Refused("Nobody"));
        Assert.Equal("That's you.", await Refused("Alicia"));
        Assert.Contains("already friends", await Refused("Bobby"));

        // Bob goes offline: Alice hears, and sees it.
        aliceHears = Signal(h => alice.FriendsChanged += h);
        await bob.DisconnectAsync();
        await aliceHears;
        Assert.Equal("offline", Assert.Single((await AccountClient.GetFriendsAsync(_url, aliceKey)).Friends).Status);

        FriendsDto after = await AccountClient.RemoveFriendAsync(_url, aliceKey, "Bobby");
        Assert.Empty(after.Friends);
        Assert.Empty((await AccountClient.GetFriendsAsync(_url, bobKey)).Friends);
    }

    [Fact]
    public async Task A_friend_challenge_goes_straight_to_the_friend()
    {
        var (alice, aliceKey) = await Join("Ava");
        var (bob, bobKey) = await Join("Boris");
        OnlineClient stranger = await Connect("Stranger");
        await AccountClient.AddFriendAsync(_url, aliceKey, "Boris");
        await AccountClient.AddFriendAsync(_url, bobKey, "Ava");
        var tc = new TimeControlDto(300, 3);

        Exception notFriends = await Assert.ThrowsAnyAsync<Exception>(() => alice.ChallengeFriendAsync("Stranger", tc, "white"));
        Assert.Contains("Add them as a friend", OnlineClient.ServerMessage(notFriends));

        // Declined: Ava is told, and the code is gone.
        Task<ChallengeDto> received = Next<ChallengeDto>(h => bob.ChallengeReceived += h);
        Task<string> declined = Next<string>(h => alice.ChallengeDeclined += (_, by) => h(by));
        ChallengeDto first = await alice.ChallengeFriendAsync("Boris", tc, "white");
        Assert.Equal(first.Code, (await received).Code);
        await bob.DeclineChallengeAsync(first.Code);
        Assert.Equal("Boris", await declined);
        Assert.False(await bob.AcceptChallengeAsync(first.Code));

        // Accepted: only the friend may take it.
        received = Next<ChallengeDto>(h => bob.ChallengeReceived += h);
        Task<GameStartDto> started = Next<GameStartDto>(h => alice.GameStarted += h);
        ChallengeDto second = await alice.ChallengeFriendAsync("Boris", tc, "white");
        await received;
        Assert.False(await stranger.AcceptChallengeAsync(second.Code));
        Assert.True(await bob.AcceptChallengeAsync(second.Code));
        GameStartDto game = await started;
        Assert.Equal(("white", "Ava", "Boris"), (game.YourColor, game.White.Name, game.Black.Name));
        Assert.Equal("playing", Assert.Single((await AccountClient.GetFriendsAsync(_url, aliceKey)).Friends).Status);

        Exception busy = await Assert.ThrowsAnyAsync<Exception>(() => bob.ChallengeFriendAsync("Ava", tc, "white"));
        Assert.Contains("playing a game", OnlineClient.ServerMessage(busy));
    }

    [Fact]
    public async Task Players_chat_masked_limited_and_kept_from_spectators_and_games_are_kept()
    {
        var (alice, aliceKey) = await Join("Ada");
        OnlineClient bob = await Connect("Bram"), watcher = await Connect("Watcher");
        Task<GameStartDto> aStart = Next<GameStartDto>(h => alice.GameStarted += h);
        ChallengeDto ch = await alice.CreateChallengeAsync(new TimeControlDto(180, 0), "white");
        Assert.True(await bob.AcceptChallengeAsync(ch.Code));
        string id = (await aStart).GameId;
        Assert.NotNull(await watcher.WatchAsync(id));
        var watcherHeard = new List<ChatDto>();
        watcher.ChatMessage += watcherHeard.Add;

        Task<ChatDto> bobHears = Next<ChatDto>(h => bob.ChatMessage += h);
        await alice.SendChatAsync(id, "  good luck, sh1thead!  ");
        ChatDto line = await bobHears;
        Assert.Equal(("Ada", "good luck, ********!"), (line.From, line.Text));

        Exception notMine = await Assert.ThrowsAnyAsync<Exception>(() => watcher.SendChatAsync(id, "hi"));
        Assert.Contains("your own games", OnlineClient.ServerMessage(notMine));
        Exception tooFast = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            for (int i = 0; i < 6; i++) await alice.SendChatAsync(id, $"spam {i}");
        });
        Assert.Contains("too fast", OnlineClient.ServerMessage(tooFast));

        // The game ends (Ada resigns) and is kept, with its moves, for both players.
        Task<GameOverDto> over = Next<GameOverDto>(h => alice.GameOver += h);
        Assert.True(await alice.MakeMoveAsync(id, 1, "e2e4"));
        Assert.True(await bob.MakeMoveAsync(id, 2, "e7e5"));
        await alice.ResignAsync(id);
        await over;
        GameRecordDto kept = Assert.Single(await AccountClient.GetGamesAsync(_url, aliceKey));
        Assert.Equal((id, "Ada", "Bram", "0-1"), (kept.Id, kept.White, kept.Black, kept.Result));
        Assert.Contains("1. e4", kept.Pgn); // clock comments follow each move
        Assert.Contains(" e5", kept.Pgn);
        Assert.Empty(await AccountClient.GetGamesAsync(_url, aliceKey, before: kept.EndedAt));
        Assert.Empty(watcherHeard);
    }

    private static Task<T> Next<T>(Action<Action<T>> subscribe)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscribe(v => tcs.TrySetResult(v));
        return tcs.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task Matchmaking_pairs_two_players_and_plays_to_checkmate()
    {
        OnlineClient alice = await Connect("Alice"), bob = await Connect("Bob");
        Task<GameStartDto> aStart = Next<GameStartDto>(h => alice.GameStarted += h);
        Task<GameStartDto> bStart = Next<GameStartDto>(h => bob.GameStarted += h);
        var tc = new TimeControlDto(180, 2);
        await alice.SeekAsync(tc);
        await bob.SeekAsync(tc);

        GameStartDto ga = await aStart, gb = await bStart;
        Assert.Equal(ga.GameId, gb.GameId);
        Assert.NotEqual(ga.YourColor, gb.YourColor);
        OnlineClient white = ga.YourColor == "white" ? alice : bob, black = ga.YourColor == "white" ? bob : alice;
        string id = ga.GameId;

        Task<GameOverDto> over = Next<GameOverDto>(h => alice.GameOver += h);
        Assert.True(await white.MakeMoveAsync(id, 1, "f2f3"));
        Assert.False(await white.MakeMoveAsync(id, 2, "e2e4"));   // not White's turn
        Assert.True(await black.MakeMoveAsync(id, 2, "e7e5"));
        Assert.False(await white.MakeMoveAsync(id, 3, "e1e3"));   // illegal
        Assert.True(await white.MakeMoveAsync(id, 3, "g2g4"));
        Assert.True(await black.MakeMoveAsync(id, 4, "d8h4"));    // Qh4#

        GameOverDto result = await over;
        Assert.Equal("0-1", result.Result);
        Assert.Equal(nameof(Termination.Checkmate), result.Termination);
        Assert.True(result.BlackRatingChange > 0);
        Assert.True(result.WhiteRatingChange < 0);
    }

    [Fact]
    public async Task Challenge_codes_pair_friends()
    {
        OnlineClient host = await Connect("Host"), friend = await Connect("Friend");
        Task<GameStartDto> hostStart = Next<GameStartDto>(h => host.GameStarted += h);
        ChallengeDto challenge = await host.CreateChallengeAsync(new TimeControlDto(300, 0), "white");
        Assert.Equal(6, challenge.Code.Length);
        Assert.False(await friend.AcceptChallengeAsync("NOPE00"));
        Assert.True(await friend.AcceptChallengeAsync(challenge.Code.ToLowerInvariant()));
        GameStartDto g = await hostStart;
        Assert.Equal("white", g.YourColor);
        Assert.Equal("Host", g.White.Name);
        Assert.Equal("Friend", g.Black.Name);
    }

    [Fact]
    public async Task Flooding_calls_is_rate_limited_and_challenge_codes_are_capped()
    {
        OnlineClient spammer = await Connect("Spam");
        var codes = new List<string>();
        for (int i = 0; i < 7; i++) codes.Add((await spammer.CreateChallengeAsync(new TimeControlDto(180, 0), "random")).Code);

        OnlineClient friend = await Connect("Pal");
        Assert.False(await friend.AcceptChallengeAsync(codes[0])); // only the newest 5 stay open
        Assert.False(await friend.AcceptChallengeAsync(codes[1]));
        Assert.True(await friend.AcceptChallengeAsync(codes[6]));

        Exception ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            for (int i = 0; i < 300; i++) await spammer.CancelSeekAsync();
        });
        Assert.Contains("Too many requests", ex.Message);
    }

    [Fact]
    public async Task Remote_sessions_mirror_each_other()
    {
        OnlineClient alice = await Connect("Ann"), bob = await Connect("Ben");
        Task<GameStartDto> aStart = Next<GameStartDto>(h => alice.GameStarted += h);
        Task<GameStartDto> bStart = Next<GameStartDto>(h => bob.GameStarted += h);
        ChallengeDto ch = await alice.CreateChallengeAsync(new TimeControlDto(600, 5), "white");
        await bob.AcceptChallengeAsync(ch.Code);

        using var whiteSession = new RemoteGameSession(alice, await aStart);
        using var blackSession = new RemoteGameSession(bob, await bStart);
        whiteSession.Start();
        blackSession.Start();
        Assert.True(whiteSession.IsLocalSide(Gambit.Core.Board.Color.White));
        Assert.False(blackSession.IsLocalSide(Gambit.Core.Board.Color.White));

        Task<MovePlayedEventArgs> blackSees = Next<MovePlayedEventArgs>(h => blackSession.MovePlayed += (_, e) => h(e));
        Assert.True(whiteSession.TrySubmitMove(Uci.Parse(whiteSession.Game.Position, "e2e4")));
        MovePlayedEventArgs seen = await blackSees;
        Assert.Equal("e4", seen.Move.San);
        Assert.False(seen.ByLocalPlayer);
        Assert.False(blackSession.TrySubmitMove(Uci.Parse(blackSession.Game.Position, "e2e4"))); // not legal for Black

        Task<GameEndedEventArgs> ended = Next<GameEndedEventArgs>(h => whiteSession.GameEnded += (_, e) => h(e));
        blackSession.Resign();
        GameEndedEventArgs end = await ended;
        Assert.Equal(GameResult.Draw, end.Result); // resigning before both sides moved aborts the game
        Assert.Equal(Termination.Aborted, end.Termination);
    }

    [Fact]
    public async Task Rematch_starts_when_both_agree_with_colors_swapped()
    {
        OnlineClient alice = await Connect("Ada"), bob = await Connect("Boe");
        Task<GameStartDto> aStart = Next<GameStartDto>(h => alice.GameStarted += h);
        ChallengeDto ch = await alice.CreateChallengeAsync(new TimeControlDto(300, 0), "white");
        await bob.AcceptChallengeAsync(ch.Code);
        GameStartDto first = await aStart;

        await alice.OfferRematchAsync(first.GameId); // ignored: the game is still running
        Task<GameOverDto> over = Next<GameOverDto>(h => alice.GameOver += h);
        Assert.True(await alice.MakeMoveAsync(first.GameId, 1, "e2e4"));
        Assert.True(await bob.MakeMoveAsync(first.GameId, 2, "e7e5"));
        await bob.ResignAsync(first.GameId);
        await over;

        // Offer → decline.
        Task<string> offered = Next<string>(h => bob.RematchOffered += h);
        await alice.OfferRematchAsync(first.GameId);
        Assert.Equal(first.GameId, await offered);
        Task<(string Id, bool Unavailable)> declined = Next<(string, bool)>(h => alice.RematchDeclined += (id, u) => h((id, u)));
        await bob.DeclineRematchAsync(first.GameId);
        Assert.Equal((first.GameId, false), await declined);

        // Offer → offer back = accepted.
        Task<GameStartDto> aNext = Next<GameStartDto>(h => alice.GameStarted += h);
        Task<GameStartDto> bNext = Next<GameStartDto>(h => bob.GameStarted += h);
        await bob.OfferRematchAsync(first.GameId);
        await alice.OfferRematchAsync(first.GameId);
        GameStartDto second = await aNext;
        Assert.Equal(second.GameId, (await bNext).GameId);
        Assert.NotEqual(first.GameId, second.GameId);
        Assert.Equal("black", second.YourColor);
        Assert.Equal("Boe", second.White.Name);
        Assert.Equal(first.TimeControl, second.TimeControl);

        // The finished game can't be rematched twice.
        Task<GameStartDto> extra = Next<GameStartDto>(h => alice.GameStarted += h);
        await bob.OfferRematchAsync(first.GameId);
        await alice.OfferRematchAsync(first.GameId);
        await Assert.ThrowsAsync<TimeoutException>(() => extra.WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Spectators_list_watch_and_follow_games()
    {
        OnlineClient alice = await Connect("Eve"), bob = await Connect("Fay"), viewer = await Connect("Gus");
        Task<GameStartDto> aStart = Next<GameStartDto>(h => alice.GameStarted += h);
        ChallengeDto ch = await alice.CreateChallengeAsync(new TimeControlDto(300, 0), "white");
        await bob.AcceptChallengeAsync(ch.Code);
        GameStartDto game = await aStart;
        Assert.True(await alice.MakeMoveAsync(game.GameId, 1, "e2e4"));

        LiveGameDto listed = Assert.Single(await viewer.ListGamesAsync(), g => g.GameId == game.GameId);
        Assert.Equal("Eve", listed.White.Name);
        Assert.Equal(1, listed.Plies);

        GameStartDto? watched = await viewer.WatchAsync(game.GameId);
        Assert.NotNull(watched);
        Assert.Equal("spectator", watched.YourColor);
        Assert.Equal(["e2e4"], watched.Moves);
        Assert.Equal(1, (await viewer.ListGamesAsync()).Single(g => g.GameId == game.GameId).Spectators);

        using var session = new RemoteGameSession(viewer, watched);
        Assert.True(session.IsSpectator);
        Assert.False(session.IsLocalSide(Gambit.Core.Board.Color.White));
        Assert.False(session.IsLocalSide(Gambit.Core.Board.Color.Black));
        Assert.False(session.TrySubmitMove(Uci.Parse(session.Game.Position, "e7e5")));

        Task<MovePlayedEventArgs> seen = Next<MovePlayedEventArgs>(h => session.MovePlayed += (_, e) => h(e));
        Assert.True(await bob.MakeMoveAsync(game.GameId, 2, "e7e5"));
        Assert.Equal("e5", (await seen).Move.San);

        await viewer.UnwatchAsync(game.GameId);
        Assert.Equal(0, (await viewer.ListGamesAsync()).Single(g => g.GameId == game.GameId).Spectators);
        Assert.Null(await viewer.WatchAsync("no-such-game"));
    }

    [Fact]
    public async Task Walking_out_of_games_pauses_quick_pairing_only()
    {
        OnlineClient quitter = await Connect("Quin"), patient = await Connect("Pam");
        for (int i = 0; i < 3; i++)
        {
            Task<GameStartDto> started = Next<GameStartDto>(h => quitter.GameStarted += h);
            Task<GameOverDto> over = Next<GameOverDto>(h => quitter.GameOver += h);
            ChallengeDto ch = await patient.CreateChallengeAsync(new TimeControlDto(300, 0), "black");
            Assert.True(await quitter.AcceptChallengeAsync(ch.Code));
            GameStartDto game = await started;
            await quitter.ResignAsync(game.GameId); // before moving: aborted, and a walkout
            Assert.Equal(nameof(Termination.Aborted), (await over).Termination);
        }

        Exception paused = await Assert.ThrowsAnyAsync<Exception>(() => quitter.SeekAsync(new TimeControlDto(180, 0)));
        Assert.Equal("You left several games early, so quick pairing is paused for 10 more minutes. Games with friends still work.",
            OnlineClient.ServerMessage(paused));
        await patient.SeekAsync(new TimeControlDto(180, 0)); // the other player isn't affected
        Assert.Equal(6, (await quitter.CreateChallengeAsync(new TimeControlDto(300, 0), "random")).Code.Length); // friends still work
    }

    [Fact]
    public async Task Remote_sessions_negotiate_a_rematch()
    {
        OnlineClient alice = await Connect("Cyd"), bob = await Connect("Dia");
        Task<GameStartDto> aStart = Next<GameStartDto>(h => alice.GameStarted += h);
        Task<GameStartDto> bStart = Next<GameStartDto>(h => bob.GameStarted += h);
        ChallengeDto ch = await alice.CreateChallengeAsync(new TimeControlDto(300, 0), "black");
        await bob.AcceptChallengeAsync(ch.Code);
        using var a = new RemoteGameSession(alice, await aStart);
        using var b = new RemoteGameSession(bob, await bStart);

        a.OfferRematch(); // not before the game is over
        Assert.Equal(RematchStatus.None, a.Rematch);
        Task<GameEndedEventArgs> aEnded = Next<GameEndedEventArgs>(h => a.GameEnded += (_, e) => h(e));
        Task<GameEndedEventArgs> bEnded = Next<GameEndedEventArgs>(h => b.GameEnded += (_, e) => h(e));
        a.Resign();
        await aEnded;
        await bEnded;

        Task<EventArgs> bAsked = Next<EventArgs>(h => b.RematchChanged += (_, e) => h(e));
        a.OfferRematch();
        Assert.Equal(RematchStatus.Offered, a.Rematch);
        await bAsked;
        Assert.Equal(RematchStatus.Received, b.Rematch);

        Task<GameStartDto> aNext = Next<GameStartDto>(h => alice.GameStarted += h);
        b.OfferRematch(); // accepts
        GameStartDto next = await aNext;
        Assert.Equal("white", next.YourColor); // Alice had Black
    }
}
