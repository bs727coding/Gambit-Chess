// Gambit bot arena: plays neighbouring bots on the ladder against each other to check that every
// step is a real, even jump in strength.
//   dotnet run -c Release --project tools/Gambit.BotArena -- <minutes> [results.csv] [bot ids, comma-separated]
//   dotnet run -c Release --project tools/Gambit.BotArena -- 0 artifacts/arena.csv        (summary only)
// Games are untimed (bots use their ThinkTimeMs, as in untimed games in the app). Every finished game
// is appended to the CSV, so runs accumulate and an interrupted run loses nothing.
using System.Diagnostics;
using System.Globalization;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;

double minutes = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 10;
string output = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine("artifacts", "arena.csv"));
List<BotProfile> bots = args.Length > 2
    ? args[2].Split(',', StringSplitOptions.TrimEntries).Select(BotRoster.Get).ToList()
    : BotRoster.Bots.ToList();
int workers = Math.Max(1, Environment.ProcessorCount - 2);
// Stay out of the way of whatever else the user is doing.
Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

var pairs = Enumerable.Range(0, bots.Count - 1).Select(i => (Low: bots[i], High: bots[i + 1])).ToList();
var rows = new List<Row>();
if (File.Exists(output))
    foreach (string line in File.ReadLines(output))
        if (Row.Parse(line) is Row r) rows.Add(r);
var gate = new object();
int[] started = pairs.Select(p => rows.Count(r => r.Low == p.Low.Id && r.High == p.High.Id)).ToArray();

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var writer = new StreamWriter(output, append: true);
var deadline = DateTime.UtcNow.AddMinutes(minutes);
var sw = Stopwatch.StartNew();
int played = 0;
if (minutes > 0) Console.WriteLine($"Arena: {pairs.Count} pairings on {workers} workers for {minutes} min (existing games: {rows.Count})...");

Parallel.For(0, minutes > 0 ? workers : 0, new ParallelOptions { MaxDegreeOfParallelism = workers }, w =>
{
    var rng = new Random(unchecked(w * 7919 + Environment.TickCount));
    while (DateTime.UtcNow < deadline)
    {
        int pi, nth;
        lock (gate)
        {
            // Spread games evenly over the pairings (slow, strong pairings simply take longer).
            pi = Enumerable.Range(0, pairs.Count).MinBy(i => started[i]);
            nth = started[pi]++;
        }
        Row row = Play(pairs[pi].Low, pairs[pi].High, highIsWhite: nth % 2 == 0, rng);
        lock (gate)
        {
            rows.Add(row);
            writer.WriteLine(row.ToCsv());
            writer.Flush();
            if (++played % 25 == 0) Console.WriteLine($"[{sw.Elapsed:hh\\:mm\\:ss}] {played} games");
        }
    }
});

Summarize(rows, pairs);

static Row Play(BotProfile low, BotProfile high, bool highIsWhite, Random rng)
{
    BotProfile w = highIsWhite ? high : low, b = highIsWhite ? low : high;
    var white = new BotMoveProvider(w, rng.Next(), hashMegabytes: 16) { HumanLikeDelay = false };
    var black = new BotMoveProvider(b, rng.Next(), hashMegabytes: 16) { HumanLikeDelay = false };
    var game = new Game { AutoDrawRules = true };
    while (!game.IsOver && game.Moves.Count < 400)
    {
        BotMoveProvider side = game.SideToMove == Color.White ? white : black;
        Move m = side.ChooseMove(new GameSnapshot(game.Position.Clone(), null, null, TimeSpan.Zero, game.Moves.Count));
        if (m.IsNone) break;
        game.Play(m);
    }
    double whiteScore = game.Result switch { GameResult.WhiteWins => 1, GameResult.BlackWins => 0, _ => 0.5 };
    return new Row(low.Id, high.Id, highIsWhite, highIsWhite ? whiteScore : 1 - whiteScore, game.Moves.Count);
}

// Elo gap from a score: D = 400*log10(s / (1 - s)); the 95% margin uses the binomial standard error.
static void Summarize(List<Row> rows, List<(BotProfile Low, BotProfile High)> pairs)
{
    Console.WriteLine();
    Console.WriteLine("| Step | Games | Stronger bot scores | Measured gap (+/-95%) | Labelled gap | Avg plies |");
    Console.WriteLine("|---|---|---|---|---|---|");
    var gaps = new List<double?>();
    foreach (var (low, high) in pairs)
    {
        var g = rows.Where(r => r.Low == low.Id && r.High == high.Id).ToList();
        if (g.Count == 0)
        {
            gaps.Add(null);
            Console.WriteLine($"| {low.Name} -> {high.Name} | 0 | - | - | {high.Rating - low.Rating} | - |");
            continue;
        }
        int n = g.Count;
        double s = g.Average(r => r.HighScore);
        double sc = Math.Clamp(s, 0.5 / n, 1 - 0.5 / n);
        double gap = 400 * Math.Log10(sc / (1 - sc));
        double margin = 1.96 * Math.Sqrt(sc * (1 - sc) / n) * 400 / (Math.Log(10) * sc * (1 - sc));
        gaps.Add(gap);
        int wins = g.Count(r => r.HighScore == 1), draws = g.Count(r => r.HighScore == 0.5), losses = n - wins - draws;
        Console.WriteLine($"| {low.Name} -> {high.Name} | {n} | {s:P0} (+{wins} ={draws} -{losses}) | {gap:+0;-0} +/- {margin:0} | {high.Rating - low.Rating} | {g.Average(r => r.Plies):0} |");
    }

    // Chain the measured gaps into a ladder anchored at the middle bot's label.
    var ladder = new List<BotProfile> { pairs[0].Low };
    ladder.AddRange(pairs.Select(p => p.High));
    int anchor = ladder.Count / 2;
    var measured = new double?[ladder.Count];
    measured[anchor] = ladder[anchor].Rating;
    for (int i = anchor + 1; i < ladder.Count; i++) measured[i] = measured[i - 1] + gaps[i - 1];
    for (int i = anchor - 1; i >= 0; i--) measured[i] = measured[i + 1] - gaps[i];
    Console.WriteLine();
    Console.WriteLine($"Ladder anchored at {ladder[anchor].Name} = {ladder[anchor].Rating}:");
    for (int i = 0; i < ladder.Count; i++)
        Console.WriteLine($"  {ladder[i].Name,-9} labelled {ladder[i].RatingText,5}   measured {(measured[i] is double m ? m.ToString("0") : "?"),5}");
}

sealed record Row(string Low, string High, bool HighIsWhite, double HighScore, int Plies)
{
    public string ToCsv() => string.Join(',', Low, High, HighIsWhite ? "1" : "0", HighScore.ToString(CultureInfo.InvariantCulture), Plies);

    public static Row? Parse(string line)
    {
        string[] p = line.Split(',');
        if (p.Length != 5 || !double.TryParse(p[3], CultureInfo.InvariantCulture, out double score) || !int.TryParse(p[4], out int plies)) return null;
        return new Row(p[0], p[1], p[2] == "1", score, plies);
    }
}
