// Gambit bot calibration: measures how real players at each rating play, and how each bot plays the
// very same positions, with one engine yardstick. A bot labelled 1000 should make mistakes as often,
// and as big, as people rated 1000.
//
//   python tools/extract_lichess_games.py <Lichess .pgn.zst (slice)> artifacts/rapid-games.tsv
//   dotnet run -c Release --project tools/Gambit.BotCalibration -- humans artifacts/rapid-games.tsv artifacts/calibration [positions per band] [bands]
//   dotnet run -c Release --project tools/Gambit.BotCalibration -- bots artifacts/calibration [bot ids|all] [id.Property=value ...]
//   dotnet run -c Release --project tools/Gambit.BotCalibration -- grid artifacts/calibration ember OversightChance=0.1,0.2 Temperature=40,80
//   dotnet run -c Release --project tools/Gambit.BotCalibration -- fit artifacts/calibration acorn,bramble
//
// "humans" samples positions from rated rapid games of players at each bot's rating, and scores
// the move each player chose. "bots" lets each bot choose a move in its band's positions and scores
// that. Moves are scored like Game Review (drop in win chances: inaccuracy 5, mistake 10, blunder 20
// points). Overrides try other settings without editing the roster: ember.Temperature=40; "grid"
// tries every combination of settings for one bot and ranks them by how close they come to the people;
// "fit" works out each bot's temperature and share of oversights from its band's players.
// Bots are labelled on the Chess.com rapid scale; Lichess ratings are converted with ChessGoals'
// survey of players on both sites. docs/BOT-CALIBRATION.md has the method and the results.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Gambit.Core.Board;
using Gambit.Core.Notation;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;
using Gambit.Engine.Review;
using Gambit.Engine.Search;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
// Stay out of the way of whatever else the user is doing.
Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

string command = args.Length > 0 ? args[0] : "";
switch (command)
{
    case "humans" when args.Length >= 3:
        Humans.Run(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 1500,
            args.Length > 4 ? args[4].Split(',').Select(int.Parse).ToList() : Bands.All);
        break;
    case "bots" when args.Length >= 2:
        Bots.Run(args[1], args.Length > 2 ? args[2] : "all", args.Skip(3).ToList());
        break;
    case "grid" when args.Length >= 3:
        Bots.Grid(args[1], args[2], args.Skip(3).ToList());
        break;
    case "fit" when args.Length >= 3:
        Bots.Fit(args[1], args[2], args.Skip(3).ToList());
        break;
    default:
        Console.WriteLine("usage: humans <games.tsv> <dir> [positions per band] [bands, e.g. 800,1000]");
        Console.WriteLine("       bots <dir> [ids|all] [id.Property=value ...]");
        Console.WriteLine("       grid <dir> <id> Property=v1,v2,... [Property=...] [band=1000] [limit=800]");
        Console.WriteLine("       fit <dir> <ids> [temps=0,10,20,35,55,80,120] [limit=1500]");
        break;
}

/// <summary>Rating bands: one per bot label, on the Chess.com rapid scale.</summary>
static class Bands
{
    /// <summary>
    /// (Chess.com rapid, Lichess rapid) for the same players: ChessGoals' rating comparison survey
    /// (chessgoals.com/rating-comparison, updated July 2026), read off its rows by Chess.com blitz cohort.
    /// </summary>
    private static readonly (double Com, double Li)[] Cohorts =
    [
        (815, 1290), (905, 1360), (1085, 1490), (1255, 1615), (1420, 1740), (1580, 1850), (1655, 1905),
        (1800, 2015), (1870, 2065), (1995, 2165), (2110, 2260), (2215, 2355), (2260, 2400),
    ];

    /// <summary>Players within this many points of a band's Lichess rating count for it.</summary>
    public const int Width = 60;

    public static IReadOnlyList<int> All { get; } = BotRoster.Bots.Where(b => !b.IsMaxStrength).Select(b => b.Rating).ToList();

    /// <summary>Lichess rapid rating for a Chess.com rapid rating (piecewise linear, extrapolated at the ends).</summary>
    public static double ToLichess(double chessCom)
    {
        int i = 0;
        while (i < Cohorts.Length - 2 && chessCom > Cohorts[i + 1].Com) i++;
        var (x0, y0) = Cohorts[i];
        var (x1, y1) = Cohorts[i + 1];
        return y0 + (chessCom - x0) * (y1 - y0) / (x1 - x0);
    }

    /// <summary>The analysis budget must stay well above what the band's bot searches.</summary>
    public static long AnalyzerNodes(int band) => band switch
    {
        <= 1400 => 250_000,
        <= 1800 => 800_000,
        <= 2000 => 2_000_000,
        _ => 5_000_000,
    };
}

/// <summary>A position from a real game and the move the player chose, scored by the engine.</summary>
sealed record Sample(int Band, int Ply, int Clock, int Rating, string Fen, string HumanUci, string BestUci, int Before, int AfterHuman)
{
    public string ToTsv() => string.Join('\t', Band, Ply, Clock, Rating, Fen, HumanUci, BestUci, Before, AfterHuman);

    public static Sample Parse(string line)
    {
        string[] f = line.Split('\t');
        return new Sample(int.Parse(f[0]), int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[3]), f[4], f[5], f[6], int.Parse(f[7]), int.Parse(f[8]));
    }
}

/// <summary>
/// Engine scores of positions (side to move's view, mates as ±2000), at a fixed node budget so they
/// don't depend on the machine. Results are cached in analysis-*.tsv files in the data folder: each
/// run reads them all and appends to its own, so several runs can work side by side.
/// </summary>
sealed class Analyzer : IDisposable
{
    private readonly ConcurrentDictionary<string, (int Score, string Best)> _cache = new();
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    public Analyzer(string dir)
    {
        foreach (string file in Directory.GetFiles(dir, "analysis*.tsv"))
            using (var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                while (reader.ReadLine() is string line)
                {
                    string[] f = line.Split('\t');
                    if (f.Length == 4 && int.TryParse(f[2], out int score)) _cache[f[0] + "|" + f[1]] = (score, f[3]);
                }
        _writer = new StreamWriter(Path.Combine(dir, $"analysis-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.tsv"));
    }

    public int Count => _cache.Count;

    public (int Score, string Best) Analyze(Searcher searcher, Position pos, long nodes)
    {
        string fen = pos.ToFen();
        string key = nodes + "|" + fen;
        if (_cache.TryGetValue(key, out var hit)) return hit;

        (int Score, string Best) result;
        List<Move> legal = MoveGenerator.LegalMoves(pos);
        if (legal.Count == 0) result = (pos.InCheck ? -2000 : 0, "");
        else if (pos.IsInsufficientMaterial()) result = (0, legal[0].ToUci());
        else
        {
            searcher.Reset();
            SearchResult r = searcher.Search(pos, new SearchLimits { MaxDepth = 24, MaxNodes = nodes });
            int score = Searcher.IsMateScore(r.Score) ? Math.Sign(r.Score) * 2000 : Math.Clamp(r.Score, -2000, 2000);
            result = (score, r.BestMove.ToUci());
        }
        _cache[key] = result;
        lock (_gate) _writer.WriteLine($"{nodes}\t{fen}\t{result.Score}\t{result.Best}");
        return result;
    }

    public void Flush()
    {
        lock (_gate) _writer.Flush();
    }

    public void Dispose()
    {
        Flush();
        _writer.Dispose();
    }
}

/// <summary>Move-quality tallies, scored like Game Review.</summary>
sealed class Stats
{
    public double N, Blunders, Mistakes, Inaccuracies, Best, Matches;
    public double WinLoss, Accuracy, CpLoss;

    public void Add(int before, int after, bool isBest, bool matchesHuman)
    {
        double loss = Math.Max(0, GameReviewer.WinPercent(before) - GameReviewer.WinPercent(after));
        N++;
        if (loss >= 20) Blunders++;
        else if (loss >= 10) Mistakes++;
        else if (loss >= 5) Inaccuracies++;
        if (isBest || loss < 0.5) Best++;
        if (matchesHuman) Matches++;
        WinLoss += loss;
        Accuracy += Math.Clamp(103.1668 * Math.Exp(-0.04354 * loss) - 3.1669, 0, 100);
        CpLoss += Math.Clamp(before - after, 0, 1000);
    }

    /// <summary>The expected tallies when a share <paramref name="p"/> of moves come from <paramref name="b"/>.</summary>
    public static Stats Mix(Stats a, Stats b, double p) => new()
    {
        N = a.N,
        Blunders = (1 - p) * a.Blunders + p * b.Blunders,
        Mistakes = (1 - p) * a.Mistakes + p * b.Mistakes,
        Inaccuracies = (1 - p) * a.Inaccuracies + p * b.Inaccuracies,
        Best = (1 - p) * a.Best + p * b.Best,
        Matches = (1 - p) * a.Matches + p * b.Matches,
        WinLoss = (1 - p) * a.WinLoss + p * b.WinLoss,
        Accuracy = (1 - p) * a.Accuracy + p * b.Accuracy,
        CpLoss = (1 - p) * a.CpLoss + p * b.CpLoss,
    };

    public static string Header => $"{"moves",6} {"blunder",8} {"mistake",8} {"inacc.",7} {"best",6} {"acc.",6} {"cp loss",7}";

    public override string ToString() => N == 0 ? "(none)" :
        $"{N,6:F0} {Pct(Blunders),8} {Pct(Mistakes),8} {Pct(Inaccuracies),7} {Pct(Best),6} {Accuracy / N,6:F1} {CpLoss / N,7:F0}";

    private string Pct(double count) => $"{100.0 * count / N:F1}%";
}

static class Humans
{
    /// <summary>Opening moves and scrambles say little about a player's level.</summary>
    private const int MinPly = 8, MinClockSeconds = 30, PerGame = 8, OpponentWindow = 150;

    public static void Run(string gamesFile, string dir, int perBand, IReadOnlyList<int> bands)
    {
        Directory.CreateDirectory(dir);
        var centers = bands.ToDictionary(b => b, b => Bands.ToLichess(b));
        var samples = bands.ToDictionary(b => b, _ => new List<Sample>());
        int games = 0, skipped = 0;

        foreach (string line in File.ReadLines(gamesFile))
        {
            if (samples.Values.All(s => s.Count >= perBand)) break;
            string[] f = line.Split('\t');
            int white = int.Parse(f[1]), black = int.Parse(f[2]);
            // Which side (if any) is a player at a band that still needs positions.
            var wanted = new List<(Color Side, int Band, int Rating)>();
            foreach (var (side, me, opp) in new[] { (Color.White, white, black), (Color.Black, black, white) })
                foreach (int band in bands)
                    if (Math.Abs(me - centers[band]) <= Bands.Width && Math.Abs(opp - me) <= OpponentWindow && samples[band].Count < perBand)
                        wanted.Add((side, band, me));
            if (wanted.Count == 0) continue;

            string[] sans = f[6].Split(' ');
            int[] clocks = f.Length > 7 && f[7].Length > 0 ? f[7].Split(' ').Select(int.Parse).ToArray() : [];
            var plies = new List<(int Ply, string Fen, string Uci)>();
            try
            {
                Position pos = Position.Start();
                for (int ply = 0; ply < sans.Length; ply++)
                {
                    Move m = San.Parse(pos, sans[ply]);
                    bool timeOk = clocks.Length == 0 || clocks[ply] >= MinClockSeconds;
                    if (ply >= MinPly && timeOk) plies.Add((ply, pos.ToFen(), m.ToUci()));
                    pos.MakeMove(m);
                }
            }
            catch (Exception)
            {
                skipped++;
                continue;
            }
            games++;

            foreach (var (side, band, rating) in wanted)
            {
                var mine = plies.Where(p => (p.Ply % 2 == 0) == (side == Color.White)).ToList();
                int take = Math.Min(PerGame, mine.Count);
                for (int k = 0; k < take && samples[band].Count < perBand; k++)
                {
                    var p = mine[k * mine.Count / take];
                    int clock = clocks.Length > 0 ? clocks[p.Ply] : -1;
                    samples[band].Add(new Sample(band, p.Ply, clock, rating, p.Fen, p.Uci, "", 0, 0));
                }
            }
        }
        Console.WriteLine($"Read {games} games ({skipped} unreadable).");
        foreach (int band in bands)
            Console.WriteLine($"  {band,5} (Lichess {centers[band]:F0} ±{Bands.Width}): {samples[band].Count} positions");

        // Score every position and the player's move.
        var all = samples.Values.SelectMany(s => s).ToList();
        var scored = new Sample[all.Count];
        using var analyzer = new Analyzer(dir);
        var sw = Stopwatch.StartNew();
        int done = 0;
        int workers = Math.Max(1, Environment.ProcessorCount - 2);
        Parallel.For(0, all.Count, new ParallelOptions { MaxDegreeOfParallelism = workers }, () => new Searcher(16), (i, _, searcher) =>
        {
            Sample s = all[i];
            long nodes = Bands.AnalyzerNodes(s.Band);
            Position pos = Position.FromFen(s.Fen);
            var before = analyzer.Analyze(searcher, pos, nodes);
            pos.MakeMove(Uci.Parse(pos, s.HumanUci));
            int after = -analyzer.Analyze(searcher, pos, nodes).Score;
            scored[i] = s with { BestUci = before.Best, Before = before.Score, AfterHuman = after };
            int n = Interlocked.Increment(ref done);
            if (n % 500 == 0)
            {
                analyzer.Flush();
                Console.WriteLine($"  scored {n}/{all.Count} ({sw.Elapsed:mm\\:ss})");
            }
            return searcher;
        }, _ => { });

        foreach (var g in scored.GroupBy(s => s.Band))
            File.WriteAllLines(Path.Combine(dir, $"samples-{g.Key}.tsv"), g.Select(s => s.ToTsv()));
        Console.WriteLine($"Scored {all.Count} positions in {sw.Elapsed:hh\\:mm\\:ss}.");
        Report.Humans(scored);
    }
}

static class Bots
{
    private static readonly int Workers = Math.Max(1, Environment.ProcessorCount - 2);

    public static Dictionary<int, List<Sample>> LoadSamples(string dir) =>
        Directory.GetFiles(dir, "samples-*.tsv").SelectMany(File.ReadLines).Select(Sample.Parse)
            .GroupBy(s => s.Band).ToDictionary(g => g.Key, g => g.ToList());

    public static Stats People(List<Sample> band)
    {
        var human = new Stats();
        foreach (Sample s in band) human.Add(s.Before, s.AfterHuman, s.HumanUci == s.BestUci, true);
        return human;
    }

    public static void Run(string dir, string ids, List<string> overrides)
    {
        var byBand = LoadSamples(dir);
        var bots = (ids == "all" ? BotRoster.Bots.Where(b => !b.IsMaxStrength) : ids.Split(',').Select(BotRoster.Get))
            .Select(b => Override(b, overrides)).ToList();

        using var analyzer = new Analyzer(dir);
        Console.WriteLine($"{"bot",-9} {"band",5}   {"who",-6} {Stats.Header} {"same as human",13}");
        foreach (BotProfile bot in bots)
        {
            if (!byBand.TryGetValue(bot.Rating, out var band)) continue;
            var sw = Stopwatch.StartNew();
            Stats stats = Evaluate(bot, band, bot.Rating, analyzer);
            Console.WriteLine($"{bot.Name,-9} {bot.Rating,5}   {"people",-6} {People(band)}");
            Console.WriteLine($"{"",-9} {"",5}   {"bot",-6} {stats} {100.0 * stats.Matches / stats.N,12:F1}%   ({sw.Elapsed.TotalSeconds:F0} s, distance {Distance(stats, People(band)):F2})");
        }
    }

    /// <summary>
    /// Tries every combination of the given settings for one bot against a band's players:
    /// grid dir ember OversightChance=0.1,0.2 Temperature=40,80 [band=1000] [limit=800]
    /// </summary>
    public static void Grid(string dir, string id, List<string> specs)
    {
        var byBand = LoadSamples(dir);
        BotProfile baseBot = BotRoster.Get(id);
        int bandRating = baseBot.Rating, limit = int.MaxValue;
        var axes = new List<(string Prop, string[] Values)>();
        foreach (string spec in specs)
        {
            string[] kv = spec.Split('=', 2);
            if (kv[0] == "band") bandRating = int.Parse(kv[1]);
            else if (kv[0] == "limit") limit = int.Parse(kv[1]);
            else axes.Add((kv[0], kv[1].Split(',')));
        }
        var band = byBand[bandRating].Take(limit).ToList();
        Stats human = People(band);
        using var analyzer = new Analyzer(dir);

        string names = string.Join(" ", axes.Select(a => $"{Short(a.Prop),8}"));
        Console.WriteLine($"{baseBot.Name} on the {bandRating} band ({band.Count} positions)");
        Console.WriteLine($"{names} {Stats.Header}  distance");
        Console.WriteLine($"{string.Join(" ", axes.Select(_ => $"{"people",8}"))} {human}");
        var results = new List<(string Line, double Distance)>();
        foreach (string[] combo in Combinations(axes.Select(a => a.Values).ToList()))
        {
            BotProfile bot = Override(baseBot, axes.Select((a, i) => $"{id}.{a.Prop}={combo[i]}").ToList());
            Stats stats = Evaluate(bot, band, bandRating, analyzer);
            double d = Distance(stats, human);
            string line = $"{string.Join(" ", combo.Select(v => $"{v,8}"))} {stats}  {d,8:F2}";
            Console.WriteLine(line);
            results.Add((line, d));
        }
        Console.WriteLine("Closest:");
        foreach (var r in results.OrderBy(r => r.Distance).Take(5)) Console.WriteLine(r.Line);
    }

    /// <summary>
    /// How far a bot's error profile is from the people's: log-ratios of blunder, mistake and
    /// inaccuracy rates (blunders count double) plus the average loss.
    /// </summary>
    public static double Distance(Stats bot, Stats human)
    {
        static double Rate(double count, double n) => (count + 0.5) / (n + 1.0);
        double Log(double b, double h) => Math.Abs(Math.Log(Rate(b, bot.N) / Rate(h, human.N)));
        return 2 * Log(bot.Blunders, human.Blunders) + Log(bot.Mistakes, human.Mistakes) + Log(bot.Inaccuracies, human.Inaccuracies)
            + Math.Abs(Math.Log((bot.WinLoss / bot.N + 0.1) / (human.WinLoss / human.N + 0.1)));
    }

    /// <summary>
    /// Fits a bot's mistakes to its band: plays the band's positions with plain moves and with
    /// oversights at several temperatures (evaluation noise = temperature), works out the share of
    /// oversights that best matches the people at each temperature, then checks the best pair with a
    /// real run. fit dir ember,flint [temps=0,10,20,35,55,80,120] [limit=1500]
    /// </summary>
    public static void Fit(string dir, string ids, List<string> options)
    {
        var byBand = LoadSamples(dir);
        double[] temps = [0, 10, 20, 35, 55, 80, 120];
        int limit = int.MaxValue;
        foreach (string o in options)
        {
            string[] kv = o.Split('=', 2);
            if (kv[0] == "temps") temps = kv[1].Split(',').Select(double.Parse).ToArray();
            else if (kv[0] == "limit") limit = int.Parse(kv[1]);
        }
        using var analyzer = new Analyzer(dir);
        foreach (BotProfile roster in ids.Split(',').Select(BotRoster.Get))
        {
            if (!byBand.TryGetValue(roster.Rating, out var all)) continue;
            var band = all.Take(limit).ToList();
            Stats human = People(band);
            // Node budgets, not the clock, limit the search here: the machine is busy with many games.
            BotProfile bot = roster with { ThinkTimeMs = Math.Max(roster.ThinkTimeMs, 60_000) };
            Console.WriteLine($"{roster.Name} on the {roster.Rating} band ({band.Count} positions)");
            Console.WriteLine($"{"temp",6} {"oversight",9} {Stats.Header}  distance");
            Console.WriteLine($"{"people",6} {"",9} {human}");

            var fits = new List<(double Temp, double P, Stats Mixed, double Distance)>();
            foreach (double t in temps)
            {
                BotProfile plain = bot with { Temperature = t, EvalNoise = t, OversightChance = 0 };
                Stats a = Evaluate(plain, band, roster.Rating, analyzer);
                Stats b = Evaluate(plain with { OversightChance = 1 }, band, roster.Rating, analyzer);
                var best = Enumerable.Range(0, 241).Select(i => i * 0.0025)
                    .Select(p => (P: p, Mixed: Stats.Mix(a, b, p)))
                    .Select(x => (x.P, x.Mixed, D: Distance(x.Mixed, human)))
                    .MinBy(x => x.D);
                Console.WriteLine($"{t,6} {"none",9} {a}");
                Console.WriteLine($"{t,6} {"all",9} {b}");
                Console.WriteLine($"{t,6} {best.P,9:P1} {best.Mixed}  {best.D,8:F2}   (predicted)");
                fits.Add((t, best.P, best.Mixed, best.D));
            }

            var pick = fits.MinBy(f => f.Distance);
            BotProfile tuned = bot with { Temperature = pick.Temp, EvalNoise = pick.Temp, OversightChance = Math.Round(pick.P, 4) };
            Stats check = Evaluate(tuned, band, roster.Rating, analyzer);
            Console.WriteLine($"{pick.Temp,6} {tuned.OversightChance,9:P1} {check}  {Distance(check, human),8:F2}   (checked)");
            Console.WriteLine($"=> {roster.Id}: Temperature = {pick.Temp}, EvalNoise = {pick.Temp}, OversightChance = {tuned.OversightChance}");
            Console.WriteLine();
        }
    }

    private static string Short(string prop) => prop switch
    {
        "OversightChance" => "oversight",
        "Temperature" => "temp",
        "EvalNoise" => "noise",
        "MaxDepth" => "depth",
        "MaxNodes" => "nodes",
        "Candidates" => "cands",
        _ => prop.Length > 8 ? prop[..8] : prop,
    };

    private static IEnumerable<string[]> Combinations(List<string[]> axes)
    {
        if (axes.Count == 0) { yield return []; yield break; }
        foreach (string v in axes[0])
            foreach (string[] rest in Combinations(axes.Skip(1).ToList()))
                yield return [v, .. rest];
    }

    /// <summary>The bot's move in each of the band's positions, scored like the players' moves.</summary>
    public static Stats Evaluate(BotProfile bot, List<Sample> band, int bandRating, Analyzer analyzer)
    {
        long nodes = Bands.AnalyzerNodes(bandRating);
        var results = new (int After, bool Best, bool Match)[band.Count];
        Parallel.For(0, band.Count, new ParallelOptions { MaxDegreeOfParallelism = Workers },
            () => (Bot: new BotMoveProvider(bot, hashMegabytes: 16) { HumanLikeDelay = false }, Searcher: new Searcher(16)),
            (i, _, state) =>
            {
                Sample s = band[i];
                Position pos = Position.FromFen(s.Fen);
                state.Bot.NewGame();
                Move move = state.Bot.ChooseMove(new GameSnapshot(pos.Clone(), null, null, TimeSpan.Zero, s.Ply));
                string uci = move.ToUci();
                int after;
                if (uci == s.HumanUci) after = s.AfterHuman;
                else
                {
                    pos.MakeMove(move);
                    after = -analyzer.Analyze(state.Searcher, pos, nodes).Score;
                }
                results[i] = (after, uci == s.BestUci, uci == s.HumanUci);
                return state;
            }, _ => { });
        analyzer.Flush();

        var stats = new Stats();
        for (int i = 0; i < band.Count; i++) stats.Add(band[i].Before, results[i].After, results[i].Best, results[i].Match);
        return stats;
    }

    /// <summary>Applies "id.Property=value" overrides (e.g. ember.Temperature=40) to a copy of the profile.</summary>
    private static BotProfile Override(BotProfile bot, List<string> overrides)
    {
        foreach (string o in overrides)
        {
            int dot = o.IndexOf('.'), eq = o.IndexOf('=');
            if (dot < 0 || eq < dot || o[..dot] != bot.Id) continue;
            PropertyInfo prop = typeof(BotProfile).GetProperty(o[(dot + 1)..eq])
                ?? throw new ArgumentException($"No property {o[(dot + 1)..eq]} on BotProfile");
            object value = Convert.ChangeType(o[(eq + 1)..], Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType, CultureInfo.InvariantCulture);
            bot = bot with { };
            prop.SetValue(bot, value);
        }
        return bot;
    }
}

static class Report
{
    public static void Humans(IEnumerable<Sample> samples)
    {
        Console.WriteLine($"{"band",5} {"Lichess",7}   {Stats.Header}");
        foreach (var g in samples.GroupBy(s => s.Band).OrderBy(g => g.Key))
        {
            var stats = new Stats();
            foreach (Sample s in g) stats.Add(s.Before, s.AfterHuman, s.HumanUci == s.BestUci, true);
            Console.WriteLine($"{g.Key,5} {Bands.ToLichess(g.Key),7:F0}   {stats}");
        }
    }
}
