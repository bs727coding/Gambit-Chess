using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;
using Gambit.ViewModels;

namespace Gambit.Tests;

/// <summary>The game page's logic (GameViewModel) against real local sessions with an instant scripted bot.</summary>
public class GameViewModelTests
{
    private sealed class FakeHost : IGameHost
    {
        public string AppName => "Gambit";
        public string PlayerName => "Tester";
        public bool PremovesEnabled { get; set; } = true;
        public List<SavedGame> Saved { get; } = [];
        public int Clears { get; private set; }
        public List<FinishedGame> Recorded { get; } = [];
        public List<GameCue> Cues { get; } = [];

        public void SaveUnfinished(SavedGame game) => Saved.Add(game);
        public void ClearUnfinished() => Clears++;
        public void RecordFinished(FinishedGame game) => Recorded.Add(game);

        public (int Wins, int Losses, int Draws) RecordAgainst(string botId) => (
            Recorded.Count(r => r.Game.Winner is Color w && w == r.PlayerColor),
            Recorded.Count(r => r.Game.Winner is Color w && w != r.PlayerColor),
            Recorded.Count(r => r.Game.Winner == null));

        public void PlayMoveSound(GameMove move) { }
        public void Play(GameCue cue) => Cues.Add(cue);
    }

    /// <summary>Replies instantly with the next scripted move (UCI), then the first legal move.</summary>
    private sealed class ScriptedBot(params string[] replies) : IMoveProvider
    {
        private int _next;

        public Task<Move> ChooseMoveAsync(GameSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.FromResult(_next < replies.Length ? Uci.Parse(snapshot.Position, replies[_next++]) : MoveGenerator.LegalMoves(snapshot.Position)[0]);
    }

    /// <summary>Someone else's game, moved along by the test (as the server would for a spectator).</summary>
    private sealed class WatchedSession : IGameSession
    {
        public Game Game { get; } = new();
        public PlayerInfo White { get; } = new("Alice", PlayerKind.Remote, 1500);
        public PlayerInfo Black { get; } = new("Bob", PlayerKind.Remote, 1600);
        public ChessClock? Clock => null;
        public bool IsOpponentThinking => false;
        public bool CanTakeback => false;
        public bool CanOfferDraw => false;
        public bool IsLocalSide(Color side) => false;

        public event EventHandler<MovePlayedEventArgs>? MovePlayed;
        public event EventHandler<GameEndedEventArgs>? GameEnded;
        public event EventHandler? StateReset { add { } remove { } }
        public event EventHandler? ThinkingChanged { add { } remove { } }
        public event EventHandler<ChatEventArgs>? ChatReceived { add { } remove { } }
        public event EventHandler<bool>? DrawOfferAnswered { add { } remove { } }
        public event EventHandler? DrawOfferReceived { add { } remove { } }

        public void Start() { }
        public bool TrySubmitMove(Move move) => false;
        public void Resign() { }
        public void OfferDraw() { }
        public bool Takeback() => false;
        public void RespondToDraw(bool accept) { }
        public void Dispose() { }

        public void Play(string uci) => MovePlayed?.Invoke(this, new MovePlayedEventArgs(Game.Play(Uci.Parse(Game.Position, uci)), false));

        public void Finish(Color winner)
        {
            Game.Resign(winner.Opposite());
            GameEnded?.Invoke(this, new GameEndedEventArgs(Game.Result, Game.Termination, Game.ResultDescription));
        }
    }

    private static (GameViewModel Vm, FakeHost Host) NewBotGame(params string[] botReplies)
    {
        SynchronizationContext.SetSynchronizationContext(null); // session events run inline
        var host = new FakeHost();
        var vm = new GameViewModel(host, _ => new ScriptedBot(botReplies));
        vm.StartLocal(new GameSetup(BotRoster.Get("acorn"), Color.White, TimeControl.Unlimited, AllowTakebacks: true));
        return (vm, host);
    }

    private static void Play(GameViewModel vm, string uci) => Assert.True(vm.SubmitMove(Uci.Parse(vm.Session!.Game.Position, uci)), $"{uci} was refused");

    private static void WaitForMoves(GameViewModel vm, int count) =>
        Assert.True(SpinWait.SpinUntil(() => vm.MoveCount == count && !vm.Session!.IsOpponentThinking, TimeSpan.FromSeconds(5)), "the bot didn't reply");

    [Fact]
    public void A_new_bot_game_waits_for_the_human()
    {
        var (vm, host) = NewBotGame();
        Assert.Equal(MoveInput.White, vm.Input);
        Assert.True(vm.AllowPremoves);
        Assert.Equal("Your move", vm.Status);
        Assert.False(vm.CanResign); // nothing to resign before the first move
        Assert.False(vm.CanTakeback);
        Assert.True(vm.ShowGameActions);
        Assert.False(vm.ShowPostGame);
        Assert.True(vm.HasUnfinishedGame);
        Assert.Contains(GameCue.Start, host.Cues);
        Assert.Equal(1, host.Clears); // a new game replaces any saved one
        Assert.Equal("Tester", vm.Badge(Color.White).Name);
        Assert.Equal("Acorn", vm.Badge(Color.Black).Name);
        Assert.Equal("Acorn (250)", vm.Opponent.Name);
        Assert.False(vm.StartFlipped);
    }

    [Fact]
    public void The_bot_replies_and_the_game_is_saved_for_resuming()
    {
        var (vm, host) = NewBotGame("e7e5");
        int opponentMoves = 0;
        vm.OpponentMoved += (_, _) => opponentMoves++;
        Play(vm, "e2e4");
        WaitForMoves(vm, 2);
        Assert.Equal(1, opponentMoves);
        Assert.Equal("Your move", vm.Status);
        Assert.True(vm.CanResign);
        Assert.True(vm.CanTakeback);
        Assert.Equal(["e2e4", "e7e5"], host.Saved[^1].Moves);
        Assert.NotEmpty(vm.OpeningName);
    }

    [Fact]
    public void Browsing_history_shows_earlier_positions_and_locks_the_board()
    {
        var (vm, _) = NewBotGame("e7e5");
        Play(vm, "e2e4");
        WaitForMoves(vm, 2);
        var updates = new List<BoardUpdate>();
        vm.BoardChanged += (_, update) => updates.Add(update);

        vm.StepPly(-1);
        Assert.Equal(1, vm.ViewPly);
        Assert.Equal(vm.Session!.Game.PositionAt(1).Key, vm.Position!.Key);
        Assert.Equal("e2e4", vm.LastMove.ToUci());
        Assert.Equal(MoveInput.None, vm.Input);
        Assert.False(vm.AllowPremoves);
        Assert.Equal("Viewing an earlier position", vm.Status);
        Assert.False(vm.SubmitMove(Uci.Parse(vm.Session.Game.Position, "g1f3"))); // no moves from the past
        Assert.True(vm.CanGoForward);
        Assert.True(updates[^1].ClearPremove);

        vm.ShowPly(int.MaxValue);
        Assert.Equal(-1, vm.ViewPly);
        Assert.Equal(MoveInput.White, vm.Input);
        Assert.True(updates[^1].Animate); // stepping forward one ply animates it
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public void Takeback_undoes_the_last_move_pair_and_says_so()
    {
        var (vm, _) = NewBotGame("e7e5");
        Play(vm, "e2e4");
        WaitForMoves(vm, 2);
        GameNotice? notice = null;
        vm.Notice += (_, n) => notice = n;
        vm.Takeback();
        Assert.Equal(0, vm.MoveCount);
        Assert.Equal("Move taken back", notice?.Title);
    }

    [Fact]
    public void Resigning_ends_the_game_records_it_once_and_offers_post_game_actions()
    {
        var (vm, host) = NewBotGame("e7e5");
        Play(vm, "e2e4");
        WaitForMoves(vm, 2);
        GameOverSummary? summary = null;
        vm.GameOver += (_, s) => summary = s;

        vm.Resign();
        Assert.True(vm.IsGameOver);
        Assert.True(vm.ShowPostGame);
        Assert.False(vm.ShowGameActions);
        Assert.False(vm.CanResign);
        Assert.False(vm.HasUnfinishedGame);
        Assert.Equal(MoveInput.None, vm.Input);
        FinishedGame record = Assert.Single(host.Recorded);
        Assert.Equal("Acorn", record.Opponent);
        Assert.Equal(Color.White, record.PlayerColor);
        Assert.Equal(250, record.OpponentRating);
        Assert.Equal(2, host.Clears); // the saved game is gone once the game ends
        Assert.Contains(GameCue.End, host.Cues);
        Assert.Equal("You lost", summary?.Title);
        Assert.Contains(summary!.Details, d => d.StartsWith("Your record vs Acorn: 0 W · 1 L", StringComparison.Ordinal));
        Assert.Equal("Rematch", summary.RematchLabel);

        vm.Resign(); // nothing happens once it's over
        Assert.Single(host.Recorded);
    }

    [Fact]
    public void Pass_and_play_lets_both_sides_move_and_reports_results_neutrally()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var host = new FakeHost();
        var vm = new GameViewModel(host);
        vm.StartLocal(new GameSetup(null, Color.White, TimeControl.Unlimited, AllowTakebacks: true));
        Assert.Equal(MoveInput.Both, vm.Input);
        Assert.False(vm.AllowPremoves);
        Assert.Equal("White to move", vm.Status);
        Assert.Equal("Pass and play", vm.Opponent.Name);

        Play(vm, "e2e4");
        Assert.Equal("Black to move", vm.Status);
        GameOverSummary? summary = null;
        vm.GameOver += (_, s) => summary = s;
        vm.Resign(); // the side to move resigns
        Assert.Equal("White wins", summary?.Title);
        Assert.Null(Assert.Single(host.Recorded).PlayerColor);
    }

    [Fact]
    public void Pass_and_play_uses_the_players_names()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var host = new FakeHost();
        var vm = new GameViewModel(host);
        vm.StartLocal(new GameSetup(null, Color.White, TimeControl.Unlimited, AllowTakebacks: true) { WhiteName = "Alice", BlackName = "Bob" });
        Assert.Equal("Alice to move", vm.Status);
        Assert.Equal("Bob", vm.Badge(Color.Black).Name);
        Assert.Equal("Alice", vm.Session!.Game.Tags["White"]);

        GameOverSummary? summary = null;
        vm.GameOver += (_, s) => summary = s;
        foreach (string move in new[] { "f2f3", "e7e5", "g2g4", "d8h4" }) Play(vm, move); // fool's mate
        Assert.Equal("Bob wins", summary?.Title);
        Assert.False(Assert.Single(host.Recorded).Practice);
    }

    [Fact]
    public void A_game_from_a_set_up_position_is_practice()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var host = new FakeHost();
        using var vm = new GameViewModel(host, _ => new ScriptedBot());
        vm.StartLocal(new GameSetup(BotRoster.Get("ember"), Color.White, TimeControl.Unlimited, AllowTakebacks: true, "6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1"));
        Assert.True(vm.Setup!.IsPractice);
        Assert.Equal("Practice from a position", vm.Session!.Game.Tags["Event"]);

        Play(vm, "a1a8"); // mate
        FinishedGame finished = Assert.Single(host.Recorded);
        Assert.True(finished.Practice);
        Assert.Equal(GameResult.WhiteWins, finished.Game.Result);
        GameOverSummary summary = vm.Summary();
        Assert.Contains(summary.Details, d => d.Contains("isn't counted", StringComparison.Ordinal));
        Assert.Equal("Try again", summary.RematchLabel);
    }

    [Fact]
    public void A_saved_game_keeps_the_names_and_the_start_position()
    {
        var setup = new GameSetup(null, Color.White, TimeControl.Minutes(5), AllowTakebacks: true, "6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1")
        {
            WhiteName = "Alice",
            BlackName = "Bob",
        };
        GameSetup restored = SavedGame.From(setup, new Game(setup.StartFen), null).ToSetup();
        Assert.Equal(("Alice", "Bob"), (restored.WhiteName, restored.BlackName));
        Assert.True(restored.IsPractice);
        Assert.Equal(3, TimeControlChoices.IndexOf("5+0"));
        Assert.Equal(0, TimeControlChoices.IndexOf("no such key"));
    }

    [Fact]
    public void A_premove_is_played_when_legal_and_dropped_when_not()
    {
        var (vm, _) = NewBotGame("e7e5", "b8c6");
        Play(vm, "e2e4");
        WaitForMoves(vm, 2);
        Assert.False(vm.PlayPremove(Square.Parse("e1"), Square.Parse("e3"))); // not a legal move: dropped
        Assert.Equal(2, vm.MoveCount);
        Assert.True(vm.PlayPremove(Square.Parse("g1"), Square.Parse("f3")));
        WaitForMoves(vm, 4);
    }

    [Fact]
    public void A_resumed_game_restores_moves_and_clock_and_warns_once_on_low_time()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var host = new FakeHost();
        using var vm = new GameViewModel(host, _ => new ScriptedBot());
        GameNotice? notice = null;
        vm.Notice += (_, n) => notice = n;

        vm.Resume(new SavedGame { BotId = "acorn", InitialSeconds = 60, Moves = ["e2e4", "e7e5"], WhiteMs = 5_000, BlackMs = 50_000 });
        Assert.Equal(2, vm.MoveCount);
        Assert.Equal("Welcome back", notice?.Title);
        Assert.DoesNotContain(GameCue.Start, host.Cues); // no start sound when continuing a game
        Assert.Equal(0, host.Clears); // the saved game stays until the next move replaces it
        Assert.InRange(vm.Remaining(Color.White)!.Value.TotalSeconds, 4, 5);
        Assert.True(vm.IsClockRunning(Color.White));

        vm.TickClock();
        vm.TickClock();
        Assert.Equal(1, host.Cues.Count(c => c == GameCue.LowTime));
    }

    [Fact]
    public void Captures_and_material_follow_the_position()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var vm = new GameViewModel(new FakeHost());
        // White is missing a knight; Black a rook and a pawn.
        vm.StartLocal(new GameSetup(null, Color.White, TimeControl.Unlimited, true, "1nbqkbnr/1ppppppp/8/8/8/8/PPPPPPPP/R1BQKBNR w KQk - 0 1"));
        Captures white = vm.CapturesBy(Color.White), black = vm.CapturesBy(Color.Black);
        Assert.Equal(2, white.Pieces.Count);
        Assert.Equal(3, white.Advantage);
        Assert.Single(black.Pieces);
        Assert.Equal(-3, black.Advantage);
    }

    [Fact]
    public void Watching_a_game_is_read_only_and_never_recorded()
    {
        var host = new FakeHost();
        var vm = new GameViewModel(host);
        var session = new WatchedSession();
        vm.Attach(session, new GameSetup(null, Color.White, TimeControl.Minutes(3, 2), AllowTakebacks: false) { IsOnline = true, IsSpectating = true }, playStartSound: false);
        Assert.Equal(MoveInput.None, vm.Input);
        Assert.False(vm.ShowDrawAndResign);
        Assert.False(vm.ShowTakeback);
        Assert.False(vm.ShowGameActions);
        Assert.False(vm.ShowRematch);
        Assert.Null(vm.RematchLabel);
        Assert.False(vm.HasUnfinishedGame);
        Assert.Equal("Watching · Alice to move", vm.Status);
        Assert.StartsWith("Watching Alice vs Bob", vm.Opponent.Tagline, StringComparison.Ordinal);

        session.Play("e2e4");
        Assert.Equal("Watching · Bob to move", vm.Status);
        Assert.Empty(host.Saved); // online games aren't kept for resuming

        GameOverSummary? summary = null;
        vm.GameOver += (_, s) => summary = s;
        session.Finish(Color.White);
        Assert.Equal("White wins", summary?.Title);
        Assert.Null(summary!.RematchLabel);
        Assert.Empty(host.Recorded);
    }
}
