using Gambit.Core.Lessons;
using Gambit.ViewModels;

namespace Gambit.Tests;

public class OpeningReviewTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    [Fact]
    public void Every_opening_drill_is_a_line_to_review()
    {
        IReadOnlyList<ReviewLine> lines = OpeningReview.Lines;
        Assert.True(lines.Count >= 14, $"only {lines.Count} lines");
        Assert.Equal(lines.Count, lines.Select(l => l.Id).Distinct().Count());
        Assert.All(lines, l =>
        {
            Assert.StartsWith("openings/", l.Lesson.Key);
            Assert.Equal(StepKind.Moves, l.Step.Kind);
            Assert.True(l.Step.Moves.Count >= 3, l.Id);
        });
        Assert.Contains(lines, l => l.Id == "openings/italian#2");
        Assert.DoesNotContain(lines, l => l.Lesson.Id == "principles"); // a single move (1.e4) isn't worth reviewing
    }

    [Fact]
    public void Finished_lessons_join_the_schedule_the_next_day()
    {
        var states = new Dictionary<string, ReviewState>();
        DateOnly? FinishedItalian(string key) => key == "openings/italian" ? Monday : null;

        Assert.Empty(OpeningReview.Schedule(states, _ => null));
        Assert.Empty(OpeningReview.Due(states, FinishedItalian, Monday));

        IReadOnlyList<ReviewLine> tomorrow = OpeningReview.Due(states, FinishedItalian, Monday.AddDays(1));
        Assert.Equal(new[] { "openings/italian#2", "openings/italian#3" }, tomorrow.Select(l => l.Id));
    }

    [Fact]
    public void Clean_reviews_space_out_and_a_slip_starts_over()
    {
        ReviewState? state = null;
        DateOnly day = Monday;
        var gaps = new List<int>();
        for (int i = 0; i < 9; i++)
        {
            state = OpeningReview.Record(state, clean: true, day);
            gaps.Add(state.Due.DayNumber - day.DayNumber);
            day = state.Due;
        }
        Assert.Equal(new[] { 3, 7, 14, 30, 60, 120, 120, 120, 120 }, gaps); // the lesson itself was box 1 (one day)

        state = OpeningReview.Record(state, clean: false, day);
        Assert.Equal(1, state.Box);
        Assert.Equal(day.AddDays(1), state.Due);
        Assert.Equal(10, state.Reviews);
        Assert.Equal(1, state.Lapses);
    }

    [Fact]
    public void Reviewed_lines_follow_their_own_dates_longest_waiting_first()
    {
        var states = new Dictionary<string, ReviewState>
        {
            ["openings/italian#2"] = new() { Box = 2, Due = Monday.AddDays(3) },
            ["openings/italian#3"] = new() { Box = 1, Due = Monday.AddDays(-2) },
        };
        DateOnly? Finished(string key) => key is "openings/italian" or "openings/french" ? Monday.AddDays(-1) : null;

        Assert.Equal(new[] { "openings/italian#3", "openings/french#2" }, OpeningReview.Due(states, Finished, Monday).Select(l => l.Id));
        Assert.Equal(Monday.AddDays(3), OpeningReview.Schedule(states, Finished).Last().Due);
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(-3, "today")]
    [InlineData(1, "tomorrow")]
    [InlineData(4, "in 4 days")]
    public void When_reads_naturally(int days, string text) => Assert.Equal(text, OpeningReview.When(Monday.AddDays(days), Monday));
}
