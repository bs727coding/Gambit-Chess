using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Rating;

namespace Gambit.Tests;

public class GameRulesTests
{
    private static Game Play(string? fen, params string[] moves)
    {
        var g = new Game(fen);
        foreach (string m in moves) Assert.NotNull(g.TryPlay(m));
        return g;
    }

    [Fact]
    public void Fools_mate_is_checkmate()
    {
        var g = Play(null, "f3", "e5", "g4", "Qh4#");
        Assert.Equal(GameResult.BlackWins, g.Result);
        Assert.Equal(Termination.Checkmate, g.Termination);
        Assert.Equal("Black wins by checkmate", g.ResultDescription);
        Assert.Empty(g.LegalMoves);
    }

    [Fact]
    public void Stalemate_is_a_draw()
    {
        var g = Play("7k/8/6Q1/8/8/8/8/K7 w - - 0 1", "Qf7");
        Assert.Equal(GameResult.Draw, g.Result);
        Assert.Equal(Termination.Stalemate, g.Termination);
    }

    [Fact]
    public void Insufficient_material_ends_the_game()
    {
        var g = Play("4k3/8/8/8/8/8/3q4/4KN2 w - - 0 1", "Nxd2");
        Assert.Equal(Termination.InsufficientMaterial, g.Termination);
    }

    [Fact]
    public void King_and_bishop_vs_king_is_dead()
    {
        var g = Play("4k3/8/8/8/8/8/3r4/3BK3 w - - 0 1", "Kxd2");
        Assert.Equal(GameResult.Draw, g.Result);
        Assert.Equal(Termination.InsufficientMaterial, g.Termination);
    }

    [Fact]
    public void Threefold_repetition_draws_automatically()
    {
        var g = Play(null, "Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1");
        Assert.False(g.IsOver);
        g.TryPlay("Ng8");
        Assert.Equal(Termination.ThreefoldRepetition, g.Termination);
    }

    [Fact]
    public void Fifty_move_rule_draws()
    {
        var g = Play("4k3/8/8/8/8/8/8/R3K3 w - - 99 80", "Ra2");
        Assert.Equal(Termination.FiftyMoveRule, g.Termination);
    }

    [Fact]
    public void Timeout_against_lone_king_is_a_draw()
    {
        var g = new Game("4k3/8/8/8/8/8/8/R3K3 b - - 0 1");
        g.Timeout(Color.White); // Black has only a king: cannot win on time
        Assert.Equal(Termination.TimeoutVsInsufficientMaterial, g.Termination);

        var g2 = new Game("4k3/8/8/8/8/8/8/R3K3 b - - 0 1");
        g2.Timeout(Color.Black);
        Assert.Equal(GameResult.WhiteWins, g2.Result);
    }

    [Fact]
    public void Undo_restores_previous_state_and_clears_result()
    {
        var g = Play(null, "f3", "e5", "g4", "Qh4#");
        Assert.True(g.Undo());
        Assert.False(g.IsOver);
        Assert.Equal(3, g.Moves.Count);
        Assert.Equal("rnbqkbnr/pppp1ppp/8/4p3/6P1/5P2/PPPPP2P/RNBQKBNR b KQkq - 0 2", g.Position.ToFen());
    }

    [Fact]
    public void Resignation_and_draw_agreement()
    {
        var g = Play(null, "e4");
        g.Resign(Color.Black);
        Assert.Equal(GameResult.WhiteWins, g.Result);
        Assert.Equal("White wins by resignation", g.ResultDescription);

        var d = Play(null, "d4", "d5");
        d.AgreeDraw();
        Assert.Equal(GameResult.Draw, d.Result);
    }

    [Fact]
    public void Clock_switch_applies_increment()
    {
        var time = new FakeTime();
        var clock = new ChessClock(TimeControl.Minutes(1, 2), time);
        clock.Start(Color.White);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(50), clock.Remaining(Color.White));
        TimeSpan spent = clock.Switch();
        Assert.Equal(TimeSpan.FromSeconds(10), spent);
        Assert.Equal(TimeSpan.FromSeconds(52), clock.Remaining(Color.White));
        Assert.Equal(Color.Black, clock.Running);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(clock.IsFlagged(Color.Black));
    }

    [Fact]
    public void Glicko2_matches_glickman_example()
    {
        var player = new Glicko2Rating(1500, 200, 0.06);
        var results = new List<(Glicko2Rating, double)>
        {
            (new Glicko2Rating(1400, 30, 0.06), 1),
            (new Glicko2Rating(1550, 100, 0.06), 0),
            (new Glicko2Rating(1700, 300, 0.06), 0),
        };
        var updated = Glicko2.Update(player, results);
        Assert.Equal(1464.06, updated.Rating, 1);
        Assert.Equal(151.52, updated.Deviation, 1);
        Assert.Equal(0.05999, updated.Volatility, 4);
    }

    internal sealed class FakeTime : TimeProvider
    {
        private long _ticks;
        public void Advance(TimeSpan by) => _ticks += (long)(by.TotalSeconds * TimestampFrequency);
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => 1_000_000;
    }
}
