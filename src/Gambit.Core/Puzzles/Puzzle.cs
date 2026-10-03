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

/// <summary>Human-friendly names for puzzle themes (the Lichess theme keys) and the practice groups.</summary>
public static class PuzzleThemes
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mate"] = "Checkmate",
        ["mateIn1"] = "Mate in 1",
        ["mateIn2"] = "Mate in 2",
        ["mateIn3"] = "Mate in 3",
        ["mateIn4"] = "Mate in 4",
        ["mateIn5"] = "Mate in 5 or more",
        ["anastasiaMate"] = "Anastasia's mate",
        ["arabianMate"] = "Arabian mate",
        ["backRankMate"] = "Back-rank mate",
        ["balestraMate"] = "Balestra mate",
        ["blindSwineMate"] = "Blind swine mate",
        ["bodenMate"] = "Boden's mate",
        ["cornerMate"] = "Corner mate",
        ["doubleBishopMate"] = "Double bishop mate",
        ["dovetailMate"] = "Dovetail mate",
        ["epauletteMate"] = "Epaulette mate",
        ["hookMate"] = "Hook mate",
        ["killBoxMate"] = "Kill box mate",
        ["morphysMate"] = "Morphy's mate",
        ["operaMate"] = "Opera mate",
        ["pillsburysMate"] = "Pillsbury's mate",
        ["smotheredMate"] = "Smothered mate",
        ["swallowstailMate"] = "Swallow's tail mate",
        ["triangleMate"] = "Triangle mate",
        ["vukovicMate"] = "Vuković mate",
        ["advancedPawn"] = "Advanced pawn",
        ["attackingF2F7"] = "Attacking f2 or f7",
        ["attraction"] = "Attraction",
        ["capturingDefender"] = "Capture the defender",
        ["castling"] = "Castling",
        ["clearance"] = "Clearance",
        ["collinearMove"] = "Collinear move",
        ["defensiveMove"] = "Defensive move",
        ["deflection"] = "Deflection",
        ["discoveredAttack"] = "Discovered attack",
        ["discoveredCheck"] = "Discovered check",
        ["doubleCheck"] = "Double check",
        ["enPassant"] = "En passant",
        ["exposedKing"] = "Exposed king",
        ["fork"] = "Fork",
        ["hangingPiece"] = "Hanging piece",
        ["interference"] = "Interference",
        ["intermezzo"] = "Intermezzo",
        ["kingsideAttack"] = "Kingside attack",
        ["pin"] = "Pin",
        ["promotion"] = "Promotion",
        ["queensideAttack"] = "Queenside attack",
        ["quietMove"] = "Quiet move",
        ["sacrifice"] = "Sacrifice",
        ["skewer"] = "Skewer",
        ["trappedPiece"] = "Trapped piece",
        ["underPromotion"] = "Underpromotion",
        ["xRayAttack"] = "X-ray attack",
        ["zugzwang"] = "Zugzwang",
        ["opening"] = "Opening",
        ["middlegame"] = "Middlegame",
        ["endgame"] = "Endgame",
        ["bishopEndgame"] = "Bishop endgame",
        ["knightEndgame"] = "Knight endgame",
        ["pawnEndgame"] = "Pawn endgame",
        ["queenEndgame"] = "Queen endgame",
        ["queenRookEndgame"] = "Queen and rook endgame",
        ["rookEndgame"] = "Rook endgame",
        ["advantage"] = "Advantage",
        ["crushing"] = "Crushing",
        ["equality"] = "Equality",
        ["oneMove"] = "One-move puzzle",
        ["short"] = "Short puzzle",
        ["long"] = "Long puzzle",
        ["veryLong"] = "Very long puzzle",
        ["master"] = "Master game",
        ["masterVsMaster"] = "Master vs master game",
        ["superGM"] = "Super GM game",
    };

    /// <summary>
    /// The themes offered as practice on the Puzzles page, in groups. The collection is built so
    /// each has plenty of puzzles (tools/import_lichess_puzzles.py: keep its list in sync).
    /// </summary>
    public static readonly IReadOnlyList<(string Title, string[] Themes)> PracticeGroups =
    [
        ("Checkmate patterns", ["mateIn1", "mateIn2", "mateIn3", "mateIn4", "mateIn5", "backRankMate", "smotheredMate",
            "anastasiaMate", "arabianMate", "hookMate", "bodenMate", "doubleBishopMate", "dovetailMate"]),
        ("Tactics", ["fork", "pin", "skewer", "discoveredAttack", "doubleCheck", "hangingPiece", "trappedPiece",
            "sacrifice", "deflection", "attraction", "clearance", "interference", "intermezzo", "xRayAttack",
            "capturingDefender", "quietMove", "defensiveMove", "zugzwang", "exposedKing", "kingsideAttack",
            "queensideAttack", "attackingF2F7", "advancedPawn", "promotion", "underPromotion", "enPassant", "castling"]),
        ("Game phases", ["opening", "middlegame", "endgame", "rookEndgame", "pawnEndgame", "bishopEndgame",
            "knightEndgame", "queenEndgame", "queenRookEndgame"]),
    ];

    public static string Name(string theme) => Names.TryGetValue(theme, out string? n) ? n : theme;
}

/// <summary>The built-in puzzle collection (embedded puzzles.csv).</summary>
public static class PuzzleCatalog
{
    private static readonly Lazy<List<Puzzle>> Loaded = new(Load);
    private static readonly Lazy<Dictionary<string, int>> Counts = new(() =>
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Puzzle p in Loaded.Value)
            foreach (string theme in p.Themes) counts[theme] = counts.GetValueOrDefault(theme) + 1;
        return counts;
    });

    public static IReadOnlyList<Puzzle> All => Loaded.Value;

    /// <summary>How many puzzles carry each theme.</summary>
    public static int CountWithTheme(string theme) => Counts.Value.GetValueOrDefault(theme);

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
