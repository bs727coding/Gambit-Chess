using System.Reflection;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;

namespace Gambit.Core.Openings;

/// <summary>A named opening line (ECO code + name + SAN moves from the initial position).</summary>
public sealed record Opening(string Eco, string Name, IReadOnlyList<string> Moves)
{
    public string Family => Name.Split(':')[0];
    public override string ToString() => $"{Eco} {Name}";
}

/// <summary>
/// Curated opening lines (embedded openings.tsv). Positions are matched by Zobrist key, so
/// transpositions are recognised. Also serves as the bots' opening book: every move that continues
/// a known line is a book move, weighted by how many lines use it.
/// </summary>
public static class OpeningBook
{
    private static readonly Lazy<Data> Loaded = new(Load);

    private sealed class Data
    {
        public readonly List<Opening> Openings = [];
        public readonly Dictionary<ulong, Opening> ByKey = [];
        public readonly Dictionary<ulong, Dictionary<Move, int>> BookMoves = [];
        public readonly List<string> Errors = [];
    }

    public static IReadOnlyList<Opening> All => Loaded.Value.Openings;

    /// <summary>Lines that failed to parse (should always be empty; checked by tests).</summary>
    public static IReadOnlyList<string> LoadErrors => Loaded.Value.Errors;

    /// <summary>The opening whose final position equals this one (null if not a named position).</summary>
    public static Opening? ForPosition(Position pos) => Loaded.Value.ByKey.GetValueOrDefault(pos.Key);

    /// <summary>The deepest named opening reached in a game (standard start position only).</summary>
    public static Opening? Identify(Game game, int maxPly = 40)
    {
        if (!game.StartsFromStandardPosition) return null;
        Opening? found = ForPosition(Position.Start());
        var pos = Position.Start();
        for (int i = 0; i < Math.Min(game.Moves.Count, maxPly); i++)
        {
            pos.MakeMove(game.Moves[i].Move);
            if (ForPosition(pos) is Opening o) found = o;
        }
        return found;
    }

    /// <summary>The opening reached after <paramref name="ply"/> half-moves of a game.</summary>
    public static Opening? IdentifyAt(Game game, int ply)
    {
        if (!game.StartsFromStandardPosition) return null;
        Opening? found = null;
        var pos = Position.Start();
        for (int i = 0; i < Math.Min(ply, Math.Min(game.Moves.Count, 40)); i++)
        {
            pos.MakeMove(game.Moves[i].Move);
            if (ForPosition(pos) is Opening o) found = o;
        }
        return found;
    }

    /// <summary>Book moves in this position with popularity weights (empty when out of book).</summary>
    public static IReadOnlyList<(Move Move, int Weight)> MovesFor(Position pos) =>
        Loaded.Value.BookMoves.TryGetValue(pos.Key, out var moves)
            ? moves.Select(kv => (kv.Key, kv.Value)).ToList()
            : [];

    public static bool IsBookPosition(Position pos) => Loaded.Value.BookMoves.ContainsKey(pos.Key) || Loaded.Value.ByKey.ContainsKey(pos.Key);

    private static Data Load()
    {
        var data = new Data();
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Gambit.Core.Openings.openings.tsv");
        if (stream == null)
        {
            data.Errors.Add("openings.tsv resource missing");
            return data;
        }

        using var reader = new StreamReader(stream);
        int lineNo = 0;
        while (reader.ReadLine() is string line)
        {
            lineNo++;
            if (line.Length == 0 || line[0] == '#') continue;
            string[] parts = line.Split('\t');
            if (parts.Length < 3)
            {
                data.Errors.Add($"line {lineNo}: expected 3 tab-separated fields");
                continue;
            }

            string[] sans = parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var opening = new Opening(parts[0].Trim(), parts[1].Trim(), sans);
            var pos = Position.Start();
            bool ok = true;
            foreach (string san in sans)
            {
                if (!San.TryParse(pos, san, out Move m))
                {
                    data.Errors.Add($"line {lineNo} ({opening.Name}): illegal move '{san}'");
                    ok = false;
                    break;
                }
                if (!data.BookMoves.TryGetValue(pos.Key, out var moves)) data.BookMoves[pos.Key] = moves = [];
                moves[m] = moves.GetValueOrDefault(m) + 1;
                pos.MakeMove(m);
            }
            if (!ok) continue;

            data.Openings.Add(opening);
            // Prefer the more specific (longer) line if two lines reach the same position.
            if (!data.ByKey.TryGetValue(pos.Key, out Opening? existing) || existing.Moves.Count < sans.Length)
                data.ByKey[pos.Key] = opening;
        }
        return data;
    }
}
