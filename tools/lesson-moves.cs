#:project ../src/Gambit.Core/Gambit.Core.csproj
#:property PublishAot=false
// Prints every lesson's steps as JSON with the moves in UCI (and quiz answers, star targets), so
// tools/play-lesson.ps1 can drive lessons in a test-profile window:
//   dotnet run tools/lesson-moves.cs > artifacts/lesson-moves.json
using System.Text.Json;
using Gambit.Core.Board;
using Gambit.Core.Lessons;
using Gambit.Core.Notation;

var result = new Dictionary<string, Dictionary<string, object>>();
foreach (Lesson lesson in LessonCatalog.AllLessons)
{
    var steps = new List<Dictionary<string, object>>();
    foreach (LessonStep step in lesson.Steps)
    {
        var pos = Position.FromFen(step.Fen);
        var uci = new List<string>();
        foreach (string san in step.Moves)
        {
            Move m = San.Parse(pos, san);
            uci.Add(m.ToUci());
            pos.MakeMove(m);
        }
        steps.Add(new()
        {
            ["kind"] = step.Kind.ToString().ToLowerInvariant(),
            ["uci"] = uci,
            ["answer"] = step.Answer,
            ["choices"] = step.Choices,
            ["targets"] = step.Targets,
        });
    }
    result[lesson.Key] = new() { ["title"] = lesson.Title, ["steps"] = steps };
}
Console.WriteLine(JsonSerializer.Serialize(result));
