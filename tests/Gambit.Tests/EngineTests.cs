using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;
using Gambit.Engine.Evaluation;
using Gambit.Engine.Search;

namespace Gambit.Tests;

public class EngineTests
{
    private static SearchResult Search(string fen, int depth, int multiPv = 1) =>
        new Searcher(8).Search(Position.FromFen(fen), new SearchLimits { MaxDepth = depth, MultiPv = multiPv });

    [Theory]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1", "Ra8#")]                       // back-rank mate
    [InlineData("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4", "Qxf7#")] // scholar's mate
    [InlineData("7k/8/6K1/8/8/8/8/1Q6 w - - 0 1", "Qb8#")]
    public void Finds_mate_in_one(string fen, string expectedSan)
    {
        SearchResult r = Search(fen, 3);
        var pos = Position.FromFen(fen);
        Assert.Equal(expectedSan, San.Format(pos, r.BestMove));
        Assert.Equal(1, Searcher.MateIn(r.Score));
    }

    [Fact]
    public void Finds_mate_in_two()
    {
        // 1. Kg6! (only waiting move) Kg8 2. Ra8# — no mate in one exists.
        SearchResult r = Search("7k/8/5K2/8/8/8/8/R7 w - - 0 1", 6);
        Assert.True(r.IsMate);
        Assert.Equal(2, Searcher.MateIn(r.Score));
    }

    [Fact]
    public void Wins_a_hanging_queen()
    {
        SearchResult r = Search("rnb1kbnr/pppp1ppp/8/4p1q1/4P3/3P4/PPP2PPP/RNBQKBNR w KQkq - 1 3", 4);
        Assert.Equal("c1g5", r.BestMove.ToUci());
        Assert.True(r.Score > 500);
    }

    [Fact]
    public void Evaluation_is_symmetric()
    {
        foreach (string fen in new[] { Position.StartFen, PerftTests.Kiwipete, "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4" })
        {
            var pos = Position.FromFen(fen);
            var mirrored = Position.FromFen(Mirror(fen));
            Assert.Equal(Evaluator.Evaluate(pos), Evaluator.Evaluate(mirrored));
        }
    }

    [Fact]
    public void See_detects_losing_and_winning_captures()
    {
        // Pawn-defended knight: QxN loses the queen for a knight.
        var pos = Position.FromFen("4k3/8/2p5/3n4/8/8/3Q4/4K3 w - - 0 1");
        Assert.True(Searcher.See(pos, Uci.Parse(pos, "d2d5")) < 0);
        // Undefended knight: free piece.
        var pos2 = Position.FromFen("4k3/8/8/3n4/8/8/3Q4/4K3 w - - 0 1");
        Assert.Equal(320, Searcher.See(pos2, Uci.Parse(pos2, "d2d5")));
    }

    [Fact]
    public void MultiPv_returns_distinct_sorted_lines()
    {
        SearchResult r = Search(Position.StartFen, 4, multiPv: 4);
        Assert.Equal(4, r.Lines.Count);
        Assert.Equal(4, r.Lines.Select(l => l.Move).Distinct().Count());
        for (int i = 1; i < r.Lines.Count; i++) Assert.True(r.Lines[i - 1].Score >= r.Lines[i].Score);
    }

    [Fact]
    public void Every_bot_returns_a_legal_move_quickly()
    {
        var pos = Position.FromFen(PerftTests.Kiwipete);
        foreach (BotProfile profile in BotRoster.Bots)
        {
            var bot = new BotMoveProvider(profile with { ThinkTimeMs = Math.Min(profile.ThinkTimeMs, 300) }, seed: 7) { HumanLikeDelay = false };
            var snapshot = new GameSnapshot(pos.Clone(), null, null, TimeSpan.Zero, 0);
            Move m = bot.ChooseMove(snapshot);
            Assert.True(MoveGenerator.IsLegal(pos, m), $"{profile.Name} played illegal {m}");
        }
    }

    [Fact]
    public void An_oversight_misses_the_recapture()
    {
        // Qxd5 is the only capture, and the pawn is defended (exd5 wins the queen). An oversight judges
        // the board right after the move, so the pawn looks free; with no oversights the bot sees exd5.
        var pos = Position.FromFen("r1b2rk1/pp3ppp/2n1p3/3p4/8/5N2/PP3PPP/R1BQ1RK1 w - - 0 12");
        var careless = BotRoster.Get("ember") with { OversightChance = 1, Temperature = 0, EvalNoise = 0, BookDepth = 0 };
        var careful = careless with { OversightChance = 0 };
        Move Choose(BotProfile profile) =>
            new BotMoveProvider(profile, seed: 1) { HumanLikeDelay = false }.ChooseMove(new GameSnapshot(pos.Clone(), null, null, TimeSpan.Zero, 22));

        Assert.Equal("d1d5", Choose(careless).ToUci());
        Assert.NotEqual("d1d5", Choose(careful).ToUci());
    }

    [Fact]
    public void Strong_bot_beats_weakest_bot()
    {
        var weak = new BotMoveProvider(BotRoster.Get("acorn"), seed: 1) { HumanLikeDelay = false };
        var strong = new BotMoveProvider(BotRoster.Get("harbor") with { ThinkTimeMs = 150 }, seed: 1) { HumanLikeDelay = false };
        var game = new Game();
        while (!game.IsOver && game.Moves.Count < 300)
        {
            IMoveProvider side = game.SideToMove == Color.White ? strong : weak;
            var snapshot = new GameSnapshot(game.Position.Clone(), null, null, TimeSpan.Zero, game.Moves.Count);
            Move m = ((BotMoveProvider)side).ChooseMove(snapshot);
            game.Play(m);
        }
        Assert.Equal(GameResult.WhiteWins, game.Result);
    }

    [Fact]
    public async Task Local_session_runs_a_bot_game_to_completion()
    {
        var white = new BotMoveProvider(BotRoster.Get("ember") with { ThinkTimeMs = 50 }, seed: 3) { HumanLikeDelay = false };
        var black = new BotMoveProvider(BotRoster.Get("bramble"), seed: 4) { HumanLikeDelay = false };
        using var session = new LocalGameSession(new Game(), new PlayerInfo("Ember", PlayerKind.Bot), new PlayerInfo("Bramble", PlayerKind.Bot), white, black);

        var done = new TaskCompletionSource<GameEndedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        int moves = 0;
        session.MovePlayed += (_, _) =>
        {
            if (++moves >= 400) session.Resign();
        };
        session.GameEnded += (_, e) => done.TrySetResult(e);
        session.Start();

        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(60)));
        Assert.Same(done.Task, finished);
        Assert.True(session.Game.IsOver);
    }

    private static string Mirror(string fen)
    {
        string[] parts = fen.Split(' ');
        string board = string.Join('/', parts[0].Split('/').Reverse().Select(SwapCase));
        string side = parts[1] == "w" ? "b" : "w";
        string castling = parts[2] == "-" ? "-" : new string(SwapCase(parts[2]).OrderBy(c => "KQkq".IndexOf(c)).ToArray());
        string ep = parts[3] == "-" ? "-" : $"{parts[3][0]}{(char)('1' + '8' - parts[3][1])}";
        return $"{board} {side} {castling} {ep} {parts[4]} {parts[5]}";

        static string SwapCase(string s) => new(s.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
    }
}
