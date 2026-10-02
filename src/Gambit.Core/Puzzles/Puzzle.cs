using System.Reflection;
using Gambit.Core.Board;
using Gambit.Core.Notation;

namespace Gambit.Core.Puzzles;

/// <summary>
/// A tactics puzzle in the Lichess format: <see cref="Fen"/> is the position <em>before</em> the
/// opponent's setup move; <see cref="Moves"/> = [setup, solution1, reply1, solution2, ...] in UCI.
/// The solver plays the side to move after the setup move.
/// </summary>
public sealed record Puzzle(string Id, string Fen, IReadOnlyList<string> Moves, int Rating, IReadOnlyList<string> Themes)
{
    /// <summary>Number of moves the solver has to find.</summary>
    public int SolutionLength => Moves.Count / 2;

    /// <summary>The color the solver plays.</summary>
    public Color SolverColor => Position.FromFen(Fen).SideToMove.Opposite();

    /// <summary>The position the solver faces (after the setup move).</summary>
    public Position StartPosition()
    {
        var pos = Position.FromFen(Fen);
        pos.MakeMove(Uci.Parse(pos, Moves[0]));
        return pos;
    }

    public bool HasTheme(string theme) => Themes.Contains(theme, StringComparer.OrdinalIgnoreCase);

    public string ToCsv() => $"{Id},{Fen},{string.Join(' ', Moves)},{Rating},{string.Join(' ', Themes)}";

    public static Puzzle? FromCsv(string line)
    {
        string[] p = line.Split(',');
        if (p.Length < 5 || !int.TryParse(p[3], out int rating)) return null;
        return new Puzzle(p[0], p[1], p[2].Split(' ', StringSplitOptions.RemoveEmptyEntries), rating,
            p[4].Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Checks every move is legal and the line has a setup move plus at least one solution move.</summary>
    public string? Validate()
    {
        if (Moves.Count < 2 || Moves.Count % 2 != 0) return "a puzzle needs a setup move and must end on a solver move";
        Position pos;
        try
        {
            pos = Position.FromFen(Fen);
        }
        catch (FenException ex)
        {
            return ex.Message;
        }
        foreach (string uci in Moves)
        {
            Move m = Uci.Parse(pos, uci);
            if (m.IsNone) return $"illegal move {uci}";
            pos.MakeMove(m);
        }
        return null;
    }
}

/// <summary>Human-friendly names for puzzle themes.</summary>
public static class PuzzleThemes
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mate"] = "Checkmate",
        ["mateIn1"] = "Mate in 1",
        ["mateIn2"] = "Mate in 2",
        ["mateIn3"] = "Mate in 3",
        ["mateIn4"] = "Mate in 4+",
        ["backRankMate"] = "Back-rank mate",
        ["smotheredMate"] = "Smothered mate",
        ["fork"] = "Fork",
        ["pin"] = "Pin",
        ["discoveredAttack"] = "Discovered attack",
        ["hangingPiece"] = "Hanging piece",
        ["sacrifice"] = "Sacrifice",
        ["promotion"] = "Promotion",
        ["crushing"] = "Crushing",
        ["advantage"] = "Advantage",
        ["opening"] = "Opening",
        ["middlegame"] = "Middlegame",
        ["endgame"] = "Endgame",
        ["oneMove"] = "One-move",
        ["short"] = "Short",
        ["long"] = "Long",
    };

    public static string Name(string theme) => Names.TryGetValue(theme, out string? n) ? n : theme;
}

/// <summary>The built-in puzzle collection (embedded puzzles.csv).</summary>
public static class PuzzleCatalog
{
    private static readonly Lazy<List<Puzzle>> Loaded = new(Load);

    public static IReadOnlyList<Puzzle> All => Loaded.Value;

    public static Puzzle? Get(string id) => Loaded.Value.FirstOrDefault(p => p.Id == id);

    private static List<Puzzle> Load()
    {
        var list = new List<Puzzle>();
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Gambit.Core.Puzzles.puzzles.csv");
        if (s == null) return list;
        using var reader = new StreamReader(s);
        while (reader.ReadLine() is string line)
        {
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("id,", StringComparison.Ordinal)) continue;
            if (Puzzle.FromCsv(line) is Puzzle p) list.Add(p);
        }
        return list;
    }
}
