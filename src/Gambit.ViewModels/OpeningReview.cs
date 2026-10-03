using Gambit.Core.Lessons;

namespace Gambit.ViewModels;

/// <summary>One opening line to review: a "play the line" step of an opening lesson.</summary>
public sealed record ReviewLine(string Id, Lesson Lesson, LessonStep Step);

/// <summary>Where a line stands in the review schedule (Leitner boxes).</summary>
public sealed class ReviewState
{
    /// <summary>1 = just learned or just missed … <see cref="OpeningReview.Intervals"/>.Length = well known.</summary>
    public int Box { get; set; }

    public DateOnly Due { get; set; }
    public int Reviews { get; set; }
    public int Lapses { get; set; }
}

/// <summary>
/// Spaced repetition for the opening drills. Finishing an opening lesson puts its lines in box 1
/// (due the next day); each clean review moves a line up a box and further out (3, 7, 14, 30, 60,
/// 120 days), and any slip sends it back to box 1 (tomorrow).
/// </summary>
public static class OpeningReview
{
    /// <summary>Days until the next review, by box.</summary>
    public static IReadOnlyList<int> Intervals { get; } = [1, 3, 7, 14, 30, 60, 120];

    /// <summary>Every reviewable line: the multi-move drills of the openings course, in course order.</summary>
    public static IReadOnlyList<ReviewLine> Lines { get; } =
    [
        .. LessonCatalog.Courses.Where(c => c.Id == "openings").SelectMany(c => c.Lessons)
            .SelectMany(lesson => lesson.Steps.Select((step, i) => (lesson, step, i)))
            .Where(x => x.step.Kind == StepKind.Moves && x.step.Goal == null && x.step.Moves.Count >= 3)
            .Select(x => new ReviewLine($"{x.lesson.Key}#{x.i + 1}", x.lesson, x.step)),
    ];

    /// <summary>
    /// The lines in rotation with their due dates: every line of a finished lesson (first due the
    /// day after the lesson), soonest first.
    /// </summary>
    public static IReadOnlyList<(ReviewLine Line, DateOnly Due)> Schedule(
        IReadOnlyDictionary<string, ReviewState> states, Func<string, DateOnly?> lessonFinishedOn)
    {
        var schedule = new List<(ReviewLine, DateOnly)>();
        foreach (ReviewLine line in Lines)
        {
            if (states.TryGetValue(line.Id, out ReviewState? state)) schedule.Add((line, state.Due));
            else if (lessonFinishedOn(line.Lesson.Key) is DateOnly finished) schedule.Add((line, finished.AddDays(1)));
        }
        return [.. schedule.OrderBy(x => x.Item2)]; // stable: course order within a day
    }

    /// <summary>The lines due on <paramref name="today"/> (or overdue), longest waiting first.</summary>
    public static IReadOnlyList<ReviewLine> Due(
        IReadOnlyDictionary<string, ReviewState> states, Func<string, DateOnly?> lessonFinishedOn, DateOnly today) =>
        [.. Schedule(states, lessonFinishedOn).Where(x => x.Due <= today).Select(x => x.Line)];

    /// <summary>
    /// A review's outcome: a clean run moves the line up a box, anything else back to box 1. A line
    /// never reviewed is in box 1 (the lesson taught it).
    /// </summary>
    public static ReviewState Record(ReviewState? state, bool clean, DateOnly today)
    {
        int box = clean ? Math.Min((state?.Box ?? 1) + 1, Intervals.Count) : 1;
        return new ReviewState
        {
            Box = box,
            Due = today.AddDays(Intervals[box - 1]),
            Reviews = (state?.Reviews ?? 0) + 1,
            Lapses = (state?.Lapses ?? 0) + (clean ? 0 : 1),
        };
    }

    /// <summary>"today", "tomorrow", "in 3 days", "on Oct 20".</summary>
    public static string When(DateOnly due, DateOnly today) => (due.DayNumber - today.DayNumber) switch
    {
        <= 0 => "today",
        1 => "tomorrow",
        int days and < 7 => $"in {days} days",
        _ => $"on {due:MMM d}",
    };
}
