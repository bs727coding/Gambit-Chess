using Gambit.Core.Board;
using Gambit.Core.Lessons;
using Gambit.Core.Notation;
using Gambit.Engine.Search;

namespace Gambit.Tests;

public class LessonTests
{
    /// <summary>
    /// A tactic or mate lesson must teach the best move: the engine (fixed depth, so deterministic)
    /// has to choose the scripted solution. Steps with a goal ("mate"/"check") accept any such move.
    /// </summary>
    [Fact]
    public void Tactic_and_mate_solutions_are_the_engines_choice()
    {
        var problems = new List<string>();
        foreach (Lesson lesson in LessonCatalog.Courses.Where(c => c.Id is "tactics" or "mates").SelectMany(c => c.Lessons))
            foreach (LessonStep step in lesson.Steps.Where(s => s.Kind == StepKind.Moves && s.Goal == null))
            {
                Position pos = Position.FromFen(step.Fen);
                Assert.True(San.TryParse(pos, step.Moves[0], out Move solution), $"{lesson.Key}: {step.Moves[0]}");
                SearchResult result = new Searcher(16).Search(pos, new SearchLimits { MaxDepth = 8 });
                if (result.BestMove != solution)
                    problems.Add($"{lesson.Key}: lesson plays {step.Moves[0]}, engine prefers {San.Format(pos, result.BestMove)} ({Searcher.FormatScore(result.Score)})");
            }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Courses_load_in_order()
    {
        var ids = LessonCatalog.Courses.Select(c => c.Id).ToList();
        Assert.True(ids.Count >= 3, $"only {ids.Count} courses loaded");
        Assert.Equal("basics", ids[0]);
    }

    [Fact]
    public void Every_lesson_step_is_playable()
    {
        List<string> problems = LessonCatalog.Validate();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Lesson_keys_are_unique_and_lessons_have_steps()
    {
        var lessons = LessonCatalog.AllLessons.ToList();
        Assert.Equal(lessons.Count, lessons.Select(l => l.Key).Distinct().Count());
        Assert.All(lessons, l => Assert.NotEmpty(l.Steps));
    }

    [Fact]
    public void Next_lesson_crosses_course_boundaries()
    {
        var first = LessonCatalog.Courses[0];
        var last = first.Lessons[^1];
        var next = LessonCatalog.Next(last.Key);
        Assert.NotNull(next);
        Assert.NotEqual(first.Id, next!.Value.Course.Id);
    }
}
