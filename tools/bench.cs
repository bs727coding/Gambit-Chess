// Engine benchmark: searches fixed positions to a fixed depth on one thread and prints the total
// node count (the search "signature") and speed. A pure speed-up must leave the signature
// unchanged; if it changes, the engine plays differently (see BotMoveProvider.Revision).
//
//   dotnet run -c Release tools/bench.cs [depth=11] [rounds=5]
//
// Also times perft (move generation + make/unmake) and the static evaluation on their own.
#:project ../src/Gambit.Engine/Gambit.Engine.csproj
#:property PublishAot=false
#:property TieredPGO=true

using System.Diagnostics;
using Gambit.Core.Board;
using Gambit.Engine.Evaluation;
using Gambit.Engine.Search;

int depth = args.Length > 0 ? int.Parse(args[0]) : 11;
int rounds = args.Length > 1 ? int.Parse(args[1]) : 5;

string[] fens = Gambit.Engine.Search.Bench.Positions;

// Warm up the JIT so tiered compilation settles before timing.
for (int i = 0; i < 3; i++) Gambit.Engine.Search.Bench.Run(fens[..4], 7);

var perftPos = Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
var sw = Stopwatch.StartNew();
long perft = MoveGenerator.Perft(perftPos, 5);
sw.Stop();
Console.WriteLine($"perft(5) kiwipete: {perft:N0} leaves in {sw.ElapsedMilliseconds} ms = {perft / sw.Elapsed.TotalSeconds / 1e6:0.0} M/s");

var evalPositions = fens.Select(Position.FromFen).ToArray();
const int evalRepeats = 200_000;
long checksum = 0;
sw.Restart();
for (int r = 0; r < evalRepeats; r++)
    foreach (Position p in evalPositions) checksum += Evaluator.Evaluate(p);
sw.Stop();
double evalNs = sw.Elapsed.TotalMilliseconds * 1e6 / (evalRepeats * (double)evalPositions.Length);
Console.WriteLine($"evaluate: {evalNs:0} ns per call (checksum {checksum})");

// Timings on a busy machine are noisy: compare the best of several rounds.
TimeSpan best = TimeSpan.MaxValue;
long signature = 0;
for (int r = 0; r < rounds; r++)
{
    var result = Gambit.Engine.Search.Bench.Run(fens, depth, r == 0 ? line => Console.WriteLine("  " + line) : null);
    Console.WriteLine($"round {r + 1}: {result.Elapsed.TotalMilliseconds:0} ms");
    signature = result.Nodes;
    if (result.Elapsed < best) best = result.Elapsed;
}
Console.WriteLine($"bench depth {depth}: {signature:N0} nodes, best {best.TotalMilliseconds:0} ms = {signature / best.TotalSeconds / 1000:N0} knps");
