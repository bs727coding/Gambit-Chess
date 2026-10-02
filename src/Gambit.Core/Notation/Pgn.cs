using System.Globalization;
using System.Text;
using Gambit.Core.Games;

namespace Gambit.Core.Notation;

/// <summary>A game as read from PGN text, before its moves are validated.</summary>
public sealed class PgnGame
{
    public List<KeyValuePair<string, string>> Tags { get; } = [];
    public List<string> Moves { get; } = [];

    /// <summary>Comment following each move (same index as <see cref="Moves"/>), or null.</summary>
    public List<string?> Comments { get; } = [];

    public string? InitialComment { get; set; }
    public string Result { get; set; } = "*";

    /// <summary>The raw movetext (moves, comments and variations) as it appeared in the file.</summary>
    public string Movetext { get; set; } = "";

    public string? Tag(string name) => Tags.FirstOrDefault(t => t.Key == name).Value;

    /// <summary>
    /// Replays the movetext into a <see cref="MoveTree"/>, keeping variations. Illegal moves end their
    /// line (a broken variation is dropped from that point; the rest of the game still loads).
    /// </summary>
    public MoveTree ToTree(out string? error)
    {
        error = null;
        string? fen = Tag("FEN");
        var tree = new MoveTree(Tag("SetUp") == "1" || fen != null ? fen : null);
        var resume = new Stack<MoveNode?>(); // where each open variation's enclosing line continues
        MoveNode? last = tree.Root;           // null while skipping the rest of a broken line
        int skipDepth = 0;                    // nested variations inside a broken line

        foreach (string token in MovetextTokens(Movetext))
        {
            if (token == "(")
            {
                if (last == null)
                {
                    skipDepth++;
                    continue;
                }
                resume.Push(last);
                last = last.Parent ?? last; // a variation replaces the move just played
                continue;
            }
            if (token == ")")
            {
                if (skipDepth > 0) skipDepth--;
                else if (resume.Count > 0) last = resume.Pop();
                continue;
            }
            if (token is "1-0" or "0-1" or "1/2-1/2" or "*") break;
            if (last == null) continue;
            if (San.TryParse(last.Position, token, out Board.Move m))
            {
                last = tree.Play(last, m);
            }
            else
            {
                error ??= $"Illegal move '{token}' after {(last.IsRoot ? "the start" : last.San)}.";
                last = null;
            }
        }
        return tree;
    }

    /// <summary>Moves, "(" and ")" from movetext; comments, NAGs and move numbers are dropped.</summary>
    private static IEnumerable<string> MovetextTokens(string text)
    {
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '{')
            {
                int end = text.IndexOf('}', i);
                i = end < 0 ? n : end + 1;
            }
            else if (c == ';')
            {
                int end = text.IndexOf('\n', i);
                i = end < 0 ? n : end + 1;
            }
            else if (c is '(' or ')')
            {
                i++;
                yield return c.ToString();
            }
            else if (c == '$')
            {
                i++;
                while (i < n && char.IsDigit(text[i])) i++;
            }
            else
            {
                int start = i;
                while (i < n && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '(' or ')' or ';')) i++;
                string token = text[start..i];
                if (token is "1-0" or "0-1" or "1/2-1/2" or "*")
                {
                    yield return token;
                    continue;
                }
                int k = 0;
                while (k < token.Length && char.IsDigit(token[k])) k++;
                if (k == token.Length) continue; // bare number
                if (k > 0 && token[k] == '.')
                {
                    while (k < token.Length && token[k] == '.') k++;
                    token = token[k..];
                }
                token = token.TrimEnd('!', '?');
                if (token.Length > 0) yield return token;
            }
        }
    }

    /// <summary>Replays the moves into a <see cref="Game"/>. Stops (without throwing) at the first illegal move.</summary>
    public Game ToGame(out string? error)
    {
        error = null;
        string? fen = Tag("FEN");
        var game = new Game(Tag("SetUp") == "1" || fen != null ? fen : null) { AutoDrawRules = false };
        foreach (var (k, v) in Tags) game.Tags[k] = v;

        for (int i = 0; i < Moves.Count; i++)
        {
            if (game.IsOver)
            {
                error = $"Moves continue after the game ended (move {i + 1}).";
                break;
            }
            if (!San.TryParse(game.Position, Moves[i], out var m))
            {
                error = $"Illegal move '{Moves[i]}' at ply {i + 1}.";
                break;
            }
            game.Play(m);
        }
        return game;
    }
}

/// <summary>Portable Game Notation reader and writer.</summary>
public static class Pgn
{
    private static readonly string[] SevenTagRoster = ["Event", "Site", "Date", "Round", "White", "Black", "Result"];

    /// <summary>
    /// Writes <paramref name="game"/> as PGN. With a <paramref name="tree"/>, the movetext is the tree's
    /// main line with its variations (tags and result still come from the game).
    /// </summary>
    public static string Write(Game game, MoveTree? tree = null)
    {
        var sb = new StringBuilder();
        var tags = new Dictionary<string, string>(game.Tags);
        tags["Result"] = game.ResultString;
        if (!game.StartsFromStandardPosition)
        {
            tags["SetUp"] = "1";
            tags["FEN"] = game.StartFen;
        }
        if (game.IsOver && !tags.ContainsKey("Termination")) tags["Termination"] = TerminationTag(game.Termination);

        foreach (string name in SevenTagRoster)
        {
            string value = tags.TryGetValue(name, out var v) ? v : name switch
            {
                "Date" => DateTime.Now.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
                "Round" => "-",
                _ => "?",
            };
            sb.Append('[').Append(name).Append(" \"").Append(Escape(value)).Append("\"]\n");
        }
        foreach (var (name, value) in tags.Where(t => !SevenTagRoster.Contains(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal))
            sb.Append('[').Append(name).Append(" \"").Append(Escape(value)).Append("\"]\n");
        sb.Append('\n');

        var line = new StringBuilder();
        string previous = "";
        void Emit(string token)
        {
            if (line.Length > 0 && line.Length + 1 + token.Length > 80)
            {
                sb.Append(line).Append('\n');
                line.Clear();
            }
            // Variations print as "(1... c5 2. Nf3)": no space inside the parentheses.
            if (line.Length > 0 && previous != "(" && token != ")") line.Append(' ');
            line.Append(token);
            previous = token;
        }

        if (tree != null)
        {
            foreach (string token in tree.MovetextTokens()) Emit(token);
        }
        else
        {
            foreach (GameMove m in game.Moves)
            {
                if (m.Side == Board.Color.White) Emit($"{m.MoveNumber}.");
                else if (m.Ply == 1) Emit($"{m.MoveNumber}...");
                Emit(m.San);
                if (m.ClockAfter is TimeSpan clk) Emit($"{{[%clk {(int)clk.TotalHours}:{clk.Minutes:00}:{clk.Seconds:00}]}}");
            }
        }
        Emit(game.ResultString);
        sb.Append(line).Append('\n');
        return sb.ToString();
    }

    public static string TerminationTag(Termination t) => t switch
    {
        Termination.Timeout or Termination.TimeoutVsInsufficientMaterial => "time forfeit",
        Termination.Abandoned => "abandoned",
        Termination.Aborted => "aborted",
        Termination.None => "unterminated",
        _ => "normal",
    };

    public static PgnGame ReadOne(string text) =>
        ReadAll(text).FirstOrDefault() ?? throw new FormatException("No game found in PGN.");

    /// <summary>
    /// Reads every game in a PGN database. <see cref="PgnGame.Moves"/> is the main line (variations
    /// skipped, comments kept); <see cref="PgnGame.ToTree"/> also reads the variations.
    /// </summary>
    public static List<PgnGame> ReadAll(string text)
    {
        var games = new List<PgnGame>();
        PgnGame? current = null;
        bool inMoves = false;
        int i = 0, n = text.Length, movetextStart = 0;

        while (i < n)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '[' && !inMoves)
            {
                int end = text.IndexOf(']', i);
                if (end < 0) break;
                current ??= new PgnGame();
                ParseTag(text.AsSpan(i + 1, end - i - 1), current);
                i = end + 1;
                continue;
            }

            if (c == '[' && inMoves)
            {
                // A new game starts without a result token in between.
                if (current != null)
                {
                    current.Movetext = text[movetextStart..i];
                    games.Add(current);
                }
                current = null;
                inMoves = false;
                continue;
            }

            current ??= new PgnGame();
            if (!inMoves) movetextStart = i;
            inMoves = true;

            switch (c)
            {
                case '{':
                {
                    int end = text.IndexOf('}', i);
                    if (end < 0) end = n;
                    string comment = text[(i + 1)..end].Trim();
                    if (current.Moves.Count == 0) current.InitialComment = comment;
                    else current.Comments[^1] = current.Comments[^1] is { } prev ? prev + " " + comment : comment;
                    i = end + 1;
                    continue;
                }
                case ';':
                {
                    int end = text.IndexOf('\n', i);
                    i = end < 0 ? n : end + 1;
                    continue;
                }
                case '(':
                {
                    int depth = 0;
                    for (; i < n; i++)
                    {
                        if (text[i] == '{')
                        {
                            int end = text.IndexOf('}', i);
                            i = end < 0 ? n - 1 : end;
                            continue;
                        }
                        if (text[i] == '(') depth++;
                        else if (text[i] == ')' && --depth == 0)
                        {
                            i++;
                            break;
                        }
                    }
                    continue;
                }
                case '$':
                {
                    i++;
                    while (i < n && char.IsDigit(text[i])) i++;
                    continue;
                }
            }

            int start = i;
            while (i < n && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '(' or ')' or ';' or '[')) i++;
            string token = text[start..i];
            if (token.Length == 0)
            {
                i++;
                continue;
            }

            if (token is "1-0" or "0-1" or "1/2-1/2" or "*")
            {
                current.Result = token;
                current.Movetext = text[movetextStart..i];
                games.Add(current);
                current = null;
                inMoves = false;
                continue;
            }

            // Strip move numbers like "12." or "12..." (possibly glued to the move: "12.e4").
            int k = 0;
            while (k < token.Length && char.IsDigit(token[k])) k++;
            if (k > 0 && k < token.Length && token[k] == '.')
            {
                while (k < token.Length && token[k] == '.') k++;
                token = token[k..];
            }
            else if (k == token.Length)
            {
                continue; // bare number
            }
            if (token.Length == 0) continue;

            current.Moves.Add(token);
            current.Comments.Add(null);
        }

        if (current != null && (current.Moves.Count > 0 || current.Tags.Count > 0))
        {
            if (inMoves) current.Movetext = text[movetextStart..n];
            games.Add(current);
        }
        return games;
    }

    private static void ParseTag(ReadOnlySpan<char> body, PgnGame game)
    {
        body = body.Trim();
        int space = body.IndexOf(' ');
        if (space <= 0) return;
        string name = body[..space].ToString();
        string value = body[(space + 1)..].Trim().ToString();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        game.Tags.Add(new(name, value.Replace("\\\"", "\"").Replace("\\\\", "\\")));
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
