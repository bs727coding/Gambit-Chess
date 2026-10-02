using Gambit.Core.Lessons;

namespace Gambit.Tests;

public class LessonTests
{
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
