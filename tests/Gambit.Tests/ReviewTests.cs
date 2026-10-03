using Gambit.Core.Games;
using Gambit.Engine.Review;

namespace Gambit.Tests;

public class ReviewTests
{
    private static Game Play(params string[] moves)
    {
        var g = new Game();
        foreach (string m in moves) Assert.NotNull(g.TryPlay(m));
        return g;
    }

    private static readonly GameReviewer Reviewer = new() { TimePerPosition = TimeSpan.FromMilliseconds(150), Workers = 4 };

    [Fact]
    public void Flags_a_hung_queen_as_a_blunder()
    {
        // 3...Qg5?? hangs the queen to Bxg5.
        var g = Play("e4", "e5", "d3", "Qg5", "Bxg5");
        GameReview review = Reviewer.Review(g);
        Assert.Equal(MoveClass.Blunder, review.Moves[3].Class);
        Assert.True(review.Moves[4].Class is MoveClass.Best or MoveClass.Great or MoveClass.Brilliant);
        Assert.True(review.WhiteAccuracy > review.BlackAccuracy);
    }

    [Fact]
    public void Book_moves_are_recognised_and_curve_has_one_point_per_position()
    {
        var g = Play("e4", "e5", "Nf3", "Nc6", "Bb5");
        GameReview review = Reviewer.Review(g);
        Assert.All(review.Moves, m => Assert.Equal(MoveClass.Book, m.Class));
        Assert.Equal(g.Moves.Count + 1, review.EvalCurve.Count);
    }

    [Fact]
    public void Checkmate_is_scored_as_won()
    {
        var g = Play("f3", "e5", "g4", "Qh4#");
        GameReview review = Reviewer.Review(g);
        Assert.True(review.EvalCurve[^1] < -1000); // Black has mated
        Assert.Equal(MoveClass.Blunder, review.Moves[2].Class); // 2.g4?? allows mate in one
    }

    [Fact]
    public void Explains_hung_pieces_and_allowed_mates()
    {
        GameReview hung = Reviewer.Review(Play("e4", "e5", "d3", "Qg5", "Bxg5"));
        Assert.Equal("The queen on g5 can simply be taken: Bxg5.", hung.Moves[3].Explanation);
        Assert.Equal("c1g5", hung.Moves[3].Refutation.ToUci());

        GameReview mated = Reviewer.Review(Play("f3", "e5", "g4", "Qh4#"));
        Assert.Equal("This allows mate: Qh4#.", mated.Moves[2].Explanation);
    }

    [Fact]
    public void Explains_a_missed_mate()
    {
        GameReview review = Reviewer.Review(Play("e4", "e5", "Qh5", "Nc6", "Bc4", "Nf6", "Qe2"));
        Assert.Equal("This allows mate: Qxf7#.", review.Moves[5].Explanation); // 3...Nf6??
        Assert.True(review.Moves[6].Class is MoveClass.Miss or MoveClass.Blunder); // 4.Qe2
        Assert.Equal("Qxf7# was mate in one.", review.Moves[6].Explanation);
    }

    [Fact]
    public void Recognises_forks()
    {
        Gambit.Core.Board.Position pos = Gambit.Core.Board.Position.FromFen("r3k3/8/8/1N6/8/8/8/4K3 w - - 0 1");
        Assert.True(MoveExplainer.IsFork(pos, Gambit.Core.Notation.Uci.Parse(pos, "b5c7")));  // king and rook
        Assert.False(MoveExplainer.IsFork(pos, Gambit.Core.Notation.Uci.Parse(pos, "b5d6"))); // only the king
    }

    [Theory]
    [InlineData(0, 50.0)]
    [InlineData(1000, 97.5)]
    [InlineData(-1000, 2.5)]
    public void Win_percent_model(int cp, double expected) =>
        Assert.Equal(expected, GameReviewer.WinPercent(cp), 0);
}
