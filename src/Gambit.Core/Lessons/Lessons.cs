using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gambit.Core.Board;
using Gambit.Core.Notation;

namespace Gambit.Core.Lessons;

public enum StepKind
{
    /// <summary>Read the explanation (with arrows/highlights), then continue.</summary>
    Info,

    /// <summary>Move a piece to collect every target square ("stars"); the other side never moves.</summary>
    Stars,

    /// <summary>Play a line of moves (SAN; the opponent's replies are scripted), or reach a goal.</summary>
    Moves,

    /// <summary>Answer a multiple-choice question about the position.</summary>
    Quiz,
}

public sealed record LessonStep
{
    public StepKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string Fen { get; set; } = Position.StartFen;
    public IReadOnlyList<string> Arrows { get; set; } = [];
    public IReadOnlyList<string> Highlights { get; set; } = [];

    /// <summary>Moves steps: solver move, opponent reply, solver move, ... in SAN.</summary>
    public IReadOnlyList<string> Moves { get; set; } = [];

    /// <summary>Moves steps: "mate" or "check" accepts any move achieving it (single-move steps).</summary>
    public string? Goal { get; set; }

    /// <summary>Stars steps: squares to visit ("e4").</summary>
    public IReadOnlyList<string> Targets { get; set; } = [];

    public IReadOnlyList<string> Choices { get; set; } = [];
    public int Answer { get; set; }

    public string? Hint { get; set; }
    public string? Success { get; set; }
}

public sealed record Lesson(string Id, string Title, string Summary, IReadOnlyList<LessonStep> Steps)
{
    /// <summary>Set by the catalog: "course/lesson".</summary>
    public string Key { get; set; } = Id;
}

public sealed record Course(string Id, string Title, string Description, string Glyph, string Color, IReadOnlyList<Lesson> Lessons);

/// <summary>Source-generated JSON reader for the lesson files (no reflection, so trimming-safe).</summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = new[] { typeof(StepKindConverter) })]
[JsonSerializable(typeof(Course))]
internal sealed partial class LessonJson : JsonSerializerContext;

internal sealed class StepKindConverter() : JsonStringEnumConverter<StepKind>(JsonNamingPolicy.CamelCase);

/// <summary>Built-in courses (embedded Lessons/*.json), in display order.</summary>
public static class LessonCatalog
{
    private static readonly string[] Order = ["basics", "mates", "tactics", "openings", "endgames"];
    private static readonly Lazy<List<Course>> Loaded = new(Load);

    public static IReadOnlyList<Course> Courses => Loaded.Value;

    public static IEnumerable<Lesson> AllLessons => Loaded.Value.SelectMany(c => c.Lessons);

    public static (Course Course, Lesson Lesson)? Find(string key)
    {
        foreach (Course c in Loaded.Value)
            foreach (Lesson l in c.Lessons)
                if (l.Key == key) return (c, l);
        return null;
    }

    /// <summary>The lesson after this one (crossing into the next course), or null at the end.</summary>
    public static (Course Course, Lesson Lesson)? Next(string key)
    {
        var flat = Loaded.Value.SelectMany(c => c.Lessons.Select(l => (c, l))).ToList();
        int i = flat.FindIndex(x => x.l.Key == key);
        return i >= 0 && i + 1 < flat.Count ? flat[i + 1] : null;
    }

    private static List<Course> Load()
    {
        var courses = new List<Course>();
        Assembly asm = Assembly.GetExecutingAssembly();
        foreach (string name in asm.GetManifestResourceNames().Where(n => n.StartsWith("Gambit.Core.Lessons.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using Stream s = asm.GetManifestResourceStream(name)!;
            Course? c = JsonSerializer.Deserialize(s, LessonJson.Default.Course);
            if (c == null) continue;
            courses.Add(c with { Lessons = c.Lessons.Select(l => l with { Key = $"{c.Id}/{l.Id}" }).ToList() });
        }
        return courses.OrderBy(c => Array.IndexOf(Order, c.Id) is int i && i >= 0 ? i : 99).ToList();
    }

    /// <summary>Checks every step can be played. Returns human-readable problems (empty = valid).</summary>
    public static List<string> Validate()
    {
        var problems = new List<string>();
        foreach (Course c in Courses)
        {
            foreach (Lesson l in c.Lessons)
            {
                for (int i = 0; i < l.Steps.Count; i++)
                {
                    LessonStep step = l.Steps[i];
                    string where = $"{l.Key} step {i + 1}";
                    Position pos;
                    try
                    {
                        pos = Position.FromFen(step.Fen);
                    }
                    catch (FenException ex)
                    {
                        problems.Add($"{where}: bad FEN ({ex.Message})");
                        continue;
                    }
                    // Info diagrams and star games may omit kings; playable steps need legal positions.
                    if (step.Kind is StepKind.Moves or StepKind.Quiz && pos.Validate() is string bad) problems.Add($"{where}: {bad}");
                    foreach (string sq in step.Highlights.Concat(step.Targets))
                        if (Square.Parse(sq) == Square.None) problems.Add($"{where}: bad square '{sq}'");
                    foreach (string a in step.Arrows)
                        if (a.Length != 4 || Square.Parse(a.AsSpan(0, 2)) == Square.None || Square.Parse(a.AsSpan(2, 2)) == Square.None)
                            problems.Add($"{where}: bad arrow '{a}'");

                    switch (step.Kind)
                    {
                        case StepKind.Moves:
                            if (step.Moves.Count == 0) problems.Add($"{where}: no moves");
                            foreach (string san in step.Moves)
                            {
                                if (!San.TryParse(pos, san, out Move m))
                                {
                                    problems.Add($"{where}: illegal move '{san}' in {pos.ToFen()}");
                                    break;
                                }
                                pos.MakeMove(m);
                            }
                            if (step.Goal == "mate" && !(pos.InCheck && !MoveGenerator.HasLegalMove(pos)))
                                problems.Add($"{where}: goal is mate but the line does not end in mate");
                            if (step.Moves.Count % 2 == 0) problems.Add($"{where}: a line must end with the student's move");
                            break;
                        case StepKind.Stars:
                            if (step.Targets.Count == 0) problems.Add($"{where}: no targets");
                            if (Bitboard.Count(pos.Pieces(pos.SideToMove)) == 0) problems.Add($"{where}: the side to move has no pieces");
                            break;
                        case StepKind.Quiz:
                            if (step.Choices.Count < 2 || step.Answer < 0 || step.Answer >= step.Choices.Count) problems.Add($"{where}: bad quiz");
                            break;
                    }
                }
            }
        }
        return problems;
    }
}
