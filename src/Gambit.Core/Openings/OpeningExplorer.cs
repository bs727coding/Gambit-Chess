using System.Globalization;
using System.Reflection;
using Gambit.Core.Board;
using Gambit.Core.Notation;

namespace Gambit.Core.Openings;

/// <summary>A move people played in a position, and how their games ended.</summary>
public sealed record ExplorerMove(Move Move, string San, int WhiteWins, int Draws, int BlackWins)
{
    public int Games => WhiteWins + Draws + BlackWins;

    /// <summary>White's score in these games, from 0 to 1 (a draw counts half).</summary>
    public double WhiteScore => Games == 0 ? 0.5 : (WhiteWins + 0.5 * Draws) / Games;
}

/// <summary>
/// The opening explorer: the moves real players chose in a position, and how those games ended
/// (embedded explorer.tsv, built from Lichess games by tools/build_explorer.py). Positions are
/// matched by Zobrist key, so move orders that transpose share their games.
/// </summary>
public static class OpeningExplorer
{
    private static readonly Lazy<Data> Loaded = new(Load);

    private sealed class Data
    {
        public string Source = "";
        public int Games;
        public readonly Dictionary<ulong, Dictionary<Move, ExplorerMove>> ByKey = [];
        public readonly List<string> Errors = [];
    }

    /// <summary>Where the games come from (the data file's first comment).</summary>
    public static string Source => Loaded.Value.Source;

    /// <summary>How many games the explorer holds (those starting from the standard position).</summary>
    public static int TotalGames => Loaded.Value.Games;

    /// <summary>Lines that failed to parse (should always be empty; checked by tests).</summary>
    public static IReadOnlyList<string> LoadErrors => Loaded.Value.Errors;

    /// <summary>The moves played in <paramref name="pos"/>, most played first; empty when no games reached it.</summary>
    public static IReadOnlyList<ExplorerMove> MovesFor(Position pos) =>
        Loaded.Value.ByKey.TryGetValue(pos.Key, out var moves)
            ? moves.Values.OrderByDescending(m => m.Games).ThenBy(m => m.San, StringComparer.Ordinal).ToList()
            : [];

    private static Data Load()
    {
        var data = new Data();
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Gambit.Core.Openings.explorer.tsv");
        if (stream == null)
        {
            data.Errors.Add("explorer.tsv resource missing");
            return data;
        }

        using var reader = new StreamReader(stream);
        // path[d] is the position after the line's first d moves; a line at depth d continues path[d - 1].
        var path = new List<Position> { Position.Start() };
        int lineNo = 0;
        while (reader.ReadLine() is string line)
        {
            lineNo++;
            if (line.Length == 0) continue;
            if (line[0] == '#')
            {
                if (data.Source.Length == 0) data.Source = line.TrimStart('#', ' ');
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 5
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int depth)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int white)
                || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int draws)
                || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int black))
            {
                data.Errors.Add($"line {lineNo}: expected depth, move and three counts");
                continue;
            }
            if (depth < 1 || depth > path.Count)
            {
                data.Errors.Add($"line {lineNo}: depth {depth} doesn't follow the line above");
                continue;
            }

            Position before = path[depth - 1];
            if (!San.TryParse(before, parts[1], out Move move))
            {
                data.Errors.Add($"line {lineNo}: illegal move '{parts[1]}'");
                continue;
            }

            if (!data.ByKey.TryGetValue(before.Key, out var moves)) data.ByKey[before.Key] = moves = [];
            ExplorerMove? seen = moves.GetValueOrDefault(move);
            moves[move] = seen == null
                ? new ExplorerMove(move, San.Format(before, move), white, draws, black)
                : seen with { WhiteWins = seen.WhiteWins + white, Draws = seen.Draws + draws, BlackWins = seen.BlackWins + black };
            if (depth == 1) data.Games += white + draws + black;

            Position after = before.Clone();
            after.MakeMove(move);
            path.RemoveRange(depth, path.Count - depth);
            path.Add(after);
        }
        return data;
    }
}
