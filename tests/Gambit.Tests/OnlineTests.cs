using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Sessions;
using Gambit.Online;
using Gambit.Online.Client;
using Gambit.Server;
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

    private async Task<OnlineClient> Connect(string name)
    {
        var client = new OnlineClient();
        _clients.Add(client);
        await client.ConnectAsync(_url, $"token-{name}-{Guid.NewGuid():N}", name);
        return client;
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
        OnlineClient alice = await Connect("Ada"), bob = await Connect("Bo");
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
        Assert.Equal("Bo", second.White.Name);
        Assert.Equal(first.TimeControl, second.TimeControl);

        // The finished game can't be rematched twice.
        Task<GameStartDto> extra = Next<GameStartDto>(h => alice.GameStarted += h);
        await bob.OfferRematchAsync(first.GameId);
        await alice.OfferRematchAsync(first.GameId);
        await Assert.ThrowsAsync<TimeoutException>(() => extra.WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Remote_sessions_negotiate_a_rematch()
    {
        OnlineClient alice = await Connect("Cy"), bob = await Connect("Di");
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
