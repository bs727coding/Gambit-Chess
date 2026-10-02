// Gambit puzzle generator: mines tactics from bot self-play.
//   dotnet run -c Release --project tools/Gambit.PuzzleGen -- <minutes> <output.csv> [minBotRating] [maxBotRating]
// Existing puzzles in the output file are kept (runs accumulate); duplicates are skipped.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Puzzles;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;
using Gambit.Engine.Evaluation;
using Gambit.Engine.Review;
using Gambit.Engine.Search;

double minutes = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 10;
string output = args.Length > 1 ? args[1] : "puzzles.csv";
int workers = Math.Max(1, Environment.ProcessorCount - 4);
// Stay out of the way of whatever else the user is doing.
Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

var puzzles = new ConcurrentDictionary<string, Puzzle>();
if (File.Exists(output))
{
    foreach (string line in File.ReadLines(output))
        if (!line.StartsWith("id,", StringComparison.Ordinal) && Puzzle.FromCsv(line) is Puzzle p) puzzles.TryAdd(StartKey(p), p);
}
int existing = puzzles.Count;
Console.WriteLine($"Generating for {minutes} min on {workers} workers (existing: {existing})…");

int minBot = args.Length > 2 ? int.Parse(args[2]) : 600, maxBot = args.Length > 3 ? int.Parse(args[3]) : 2000;
var bots = BotRoster.Bots.Where(b => b.Rating >= minBot && b.Rating <= maxBot).ToList();
var deadline = DateTime.UtcNow.AddMinutes(minutes);
var sw = Stopwatch.StartNew();
int games = 0;

Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, w =>
{
    var rng = new Random(unchecked(w * 7919 + Environment.TickCount));
    var searcher = new Searcher(32);
    while (DateTime.UtcNow < deadline)
    {
        Game game = PlayGame(bots, rng);
        int g = Interlocked.Increment(ref games);
        foreach (Puzzle p in Mine(game, searcher, rng, deadline)) puzzles.TryAdd(StartKey(p), p);
        if (g % 20 == 0)
            Console.WriteLine($"[{sw.Elapsed:mm\\:ss}] games {g}, puzzles {puzzles.Count - existing} new / {puzzles.Count} total");
    }
});

var sorted = puzzles.Values.OrderBy(p => p.Rating).ToList();
using (var writer = new StreamWriter(output))
{
    writer.WriteLine("id,fen,moves,rating,themes");
    foreach (Puzzle p in sorted) writer.WriteLine(p.ToCsv());
}
Console.WriteLine($"Done: {games} games, {puzzles.Count - existing} new puzzles, {puzzles.Count} total → {output}");
foreach (var grp in sorted.GroupBy(p => p.Rating / 400 * 400)) Console.WriteLine($"  {grp.Key,4}–{grp.Key + 399}: {grp.Count()}");

// ---------------------------------------------------------------------------------------------

static Game PlayGame(List<BotProfile> bots, Random rng)
{
    BotProfile a = bots[rng.Next(bots.Count)], b = bots[rng.Next(bots.Count)];
    var white = new BotMoveProvider(a with { ThinkTimeMs = 40 }, rng.Next(), hashMegabytes: 4) { HumanLikeDelay = false };
    var black = new BotMoveProvider(b with { ThinkTimeMs = 40 }, rng.Next(), hashMegabytes: 4) { HumanLikeDelay = false };
    var game = new Game();
    while (!game.IsOver && game.Moves.Count < 200)
    {
        BotMoveProvider side = game.SideToMove == Color.White ? white : black;
        Move m = side.ChooseMove(new GameSnapshot(game.Position.Clone(), null, null, TimeSpan.Zero, game.Moves.Count));
        if (m.IsNone) break;
        game.Play(m);
    }
    return game;
}

static IEnumerable<Puzzle> Mine(Game game, Searcher s, Random rng, DateTime deadline)
{
    var found = new List<Puzzle>();
    for (int i = 10; i < game.Moves.Count && DateTime.UtcNow < deadline; i++)
    {
        Position before = game.PositionAt(i - 1);
        Position start = game.PositionAt(i);
        if (start.IsInsufficientMaterial() || MoveGenerator.LegalMoves(start).Count < 2) continue;

        SearchResult quick = Search(s, start, 25, 7, 1);
        if (quick.Score < 250) continue;

        // The setup move must be the mistake that created the chance.
        SearchResult pre = Search(s, before, 60, 10, 1);
        if (-pre.Score >= 120) continue;

        int stableDepth = 1;
        Move lastBest = Move.None;
        SearchResult deep = Search(s, start, 450, 22, 2, info =>
        {
            if (info.Best?.Move is Move best && best != lastBest)
            {
                lastBest = best;
                stableDepth = info.Depth;
            }
        });
        if (!IsUniqueWin(deep, firstMove: true)) continue;

        Puzzle? puzzle = BuildPuzzle(game, i, start, deep, s, stableDepth, rng);
        // Easy one-move "free piece" puzzles are plentiful: keep only a third of them.
        if (puzzle != null && puzzle.SolutionLength == 1 && puzzle.HasTheme("hangingPiece") && rng.NextDouble() > 0.35) puzzle = null;
        if (puzzle != null && puzzle.Validate() == null)
        {
            found.Add(puzzle);
            i += 4; // avoid overlapping puzzles from the same sequence
        }
    }
    return found;
}

static Puzzle? BuildPuzzle(Game game, int i, Position start, SearchResult deep, Searcher s, int stableDepth, Random rng)
{
    Color solver = start.SideToMove;
    var line = new List<Move> { game.Moves[i - 1].Move, deep.BestMove };
    var pos = start.Clone();
    pos.MakeMove(deep.BestMove);
    int startMaterial = Material(start, solver);
    bool mate = false, promotion = deep.BestMove.IsPromotion;

    for (int k = 0; k < 6; k++)
    {
        if (!MoveGenerator.HasLegalMove(pos))
        {
            mate = pos.InCheck;
            break;
        }
        SearchResult reply = Search(s, pos, 250, 20, 1);
        if (reply.BestMove.IsNone) break;
        int gained = Material(pos, solver) - startMaterial;
        if (!Searcher.IsMateScore(reply.Score) && gained >= 2 && !reply.BestMove.IsCapture) break;

        pos.MakeMove(reply.BestMove);
        line.Add(reply.BestMove);
        SearchResult cont = Search(s, pos, 400, 22, 2);
        bool collects = !cont.BestMove.IsNone && cont.BestMove.IsCapture && Searcher.See(pos, cont.BestMove) >= 200;
        if (cont.BestMove.IsNone || !(IsUniqueWin(cont, firstMove: false) || (collects && cont.Score >= 180)))
        {
            pos.UnmakeMove();
            line.RemoveAt(line.Count - 1);
            break;
        }
        pos.MakeMove(cont.BestMove);
        line.Add(cont.BestMove);
        promotion |= cont.BestMove.IsPromotion;
    }

    if (line.Count % 2 != 0) return null;
    int finalGain = Material(pos, solver) - startMaterial;
    if (!MoveGenerator.HasLegalMove(pos)) mate = pos.InCheck;
    if (!mate && finalGain < 2 && !promotion) return null;

    int solverMoves = line.Count / 2;
    Move first = line[1];
    var afterFirst = start.Clone();
    afterFirst.MakeMove(first);
    bool quiet = !first.IsCapture && !first.IsPromotion && !afterFirst.InCheck;

    var themes = new List<string>();
    var sacrifice = false;
    var fork = false;
    var discovered = false;
    var p = start.Clone();
    for (int k = 1; k < line.Count; k++)
    {
        Move m = line[k];
        bool solverMove = k % 2 == 1;
        if (solverMove)
        {
            if (Searcher.See(p, m) <= -200) sacrifice = true;
            if (CreatesDiscoveredAttack(p, m)) discovered = true;
        }
        p.MakeMove(m);
        if (solverMove && IsFork(p, m.To)) fork = true;
    }

    if (mate)
    {
        themes.Add("mate");
        themes.Add(solverMoves >= 4 ? "mateIn4" : $"mateIn{solverMoves}");
        int k = pos.KingSquare(pos.SideToMove);
        ulong checkers = pos.Checkers;
        int checker = Bitboard.Lsb(checkers);
        PieceType checkerType = pos.PieceAt(checker).Type();
        if (Square.RelativeRank(k, pos.SideToMove) == 0 && checkerType is PieceType.Rook or PieceType.Queen
            && Square.Rank(checker) == Square.Rank(k)) themes.Add("backRankMate");
        if (checkerType == PieceType.Knight && (Attacks.King(k) & ~pos.Pieces(pos.SideToMove)) == 0) themes.Add("smotheredMate");
    }
    if (fork) themes.Add("fork");
    if (discovered) themes.Add("discoveredAttack");
    if (CreatesPin(start, first)) themes.Add("pin");
    if (first.IsCapture && !first.IsEnPassant && start.PieceAt(first.To).Type() is var victim && victim.NominalValue() >= 3
        && Searcher.See(start, first) >= Evaluator.SeeValue[(int)victim] - 20) themes.Add("hangingPiece");
    if (sacrifice) themes.Add("sacrifice");
    if (promotion) themes.Add("promotion");
    themes.Add(Evaluator.Phase(start) <= 8 ? "endgame" : i < 20 ? "opening" : "middlegame");
    themes.Add(solverMoves == 1 ? "oneMove" : solverMoves == 2 ? "short" : "long");
    themes.Add(mate || finalGain >= 5 ? "crushing" : "advantage");

    int rating = 650 + 230 * (solverMoves - 1);
    if (mate) rating -= solverMoves == 1 ? 150 : 40;
    if (quiet) rating += 220;
    if (sacrifice) rating += 200;
    if (discovered) rating += 60;
    rating += 32 * Math.Min(stableDepth, 18);
    rating += rng.Next(-60, 61);
    rating = Math.Clamp(rating, 400, 2700);

    Position setupPos = game.PositionAt(i - 1);
    string fen = setupPos.ToFen();
    return new Puzzle(MakeId(fen), fen, line.Select(m => m.ToUci()).ToList(), rating, themes);
}

static bool IsUniqueWin(SearchResult r, bool firstMove)
{
    if (r.Lines.Count == 0) return false;
    int best = r.Lines[0].Score;
    bool bestMate = Searcher.IsMateScore(best) && best > 0;
    if (bestMate && Searcher.MateIn(best) == 1) return true; // any mating move is accepted
    if (!bestMate && best < (firstMove ? 250 : 180)) return false;
    if (r.Lines.Count < 2) return true;
    int second = r.Lines[1].Score;
    if (bestMate) return !(Searcher.IsMateScore(second) && second > 0);
    return GameReviewer.WinPercent(best) - GameReviewer.WinPercent(second) >= (firstMove ? 20 : 10);
}

static SearchResult Search(Searcher s, Position pos, int ms, int depth, int multiPv, Action<SearchInfo>? onInfo = null) =>
    s.Search(pos, new SearchLimits
    {
        MaxDepth = depth,
        MultiPv = Math.Min(multiPv, Math.Max(1, MoveGenerator.LegalMoves(pos).Count)),
        SoftTime = TimeSpan.FromMilliseconds(ms * 0.7),
        HardTime = TimeSpan.FromMilliseconds(ms),
    }, default, onInfo);

static int Material(Position pos, Color side)
{
    int total = 0;
    for (int t = 1; t <= 5; t++)
    {
        var type = (PieceType)t;
        total += type.NominalValue() * (pos.Count(side, type) - pos.Count(side.Opposite(), type));
    }
    return total;
}

static bool IsFork(Position after, int sq)
{
    Piece piece = after.PieceAt(sq);
    if (piece == Piece.None) return false;
    Color us = piece.Color(), them = us.Opposite();
    ulong attacks = piece.Type() == PieceType.Pawn ? Attacks.Pawn(us, sq) : Attacks.Of(piece.Type(), sq, after.Occupied);
    ulong targets = attacks & after.Pieces(them);
    int count = 0;
    while (targets != 0)
    {
        int t = Bitboard.PopLsb(ref targets);
        PieceType victim = after.PieceAt(t).Type();
        if (victim == PieceType.King || victim.NominalValue() > piece.Type().NominalValue() || victim.NominalValue() >= 3) count++;
    }
    return count >= 2;
}

static bool CreatesPin(Position before, Move m)
{
    Color them = before.SideToMove.Opposite();
    ulong pinnedBefore = before.PinnedPieces(them);
    var after = before.Clone();
    after.MakeMove(m);
    ulong pinnedAfter = after.PinnedPieces(them);
    // A new pin on a piece worth at least a knight.
    ulong fresh = pinnedAfter & ~pinnedBefore;
    while (fresh != 0)
    {
        int sq = Bitboard.PopLsb(ref fresh);
        if (after.PieceAt(sq).Type().NominalValue() >= 3) return true;
    }
    return false;
}

static bool CreatesDiscoveredAttack(Position before, Move m)
{
    Color us = before.SideToMove, them = us.Opposite();
    var after = before.Clone();
    after.MakeMove(m);
    ulong valuable = after.Pieces(them, PieceType.Queen) | after.Pieces(them, PieceType.Rook) | after.Pieces(them, PieceType.King);
    ulong sliders = after.Pieces(us, PieceType.Bishop) | after.Pieces(us, PieceType.Rook) | after.Pieces(us, PieceType.Queen);
    sliders &= ~Bitboard.Of(m.To);
    while (sliders != 0)
    {
        int sq = Bitboard.PopLsb(ref sliders);
        PieceType t = after.PieceAt(sq).Type();
        ulong now = Attacks.Of(t, sq, after.Occupied) & valuable;
        ulong was = Attacks.Of(t, sq, before.Occupied) & valuable;
        if ((now & ~was) != 0) return true;
    }
    return false;
}

static string StartKey(Puzzle p) => p.StartPosition().Key.ToString("x16");

static string MakeId(string fen)
{
    ulong h = 14695981039346656037UL;
    foreach (char c in fen)
    {
        h ^= c;
        h *= 1099511628211UL;
    }
    const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    var chars = new char[7];
    for (int k = 0; k < chars.Length; k++)
    {
        chars[k] = alphabet[(int)(h % 36)];
        h /= 36;
    }
    return new string(chars);
}
