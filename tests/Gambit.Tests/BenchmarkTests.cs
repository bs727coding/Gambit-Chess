using System.Diagnostics;
using Gambit.Core.Board;
using Gambit.Engine.Search;
using Xunit.Abstractions;

namespace Gambit.Tests;

/// <summary>Speed reports (not pass/fail gates). Run with: ./build.ps1 perft  — or dotnet test -c Release --filter Benchmark.</summary>
public class BenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public void Benchmark_perft_and_search_speed()
    {
        var pos = Position.FromFen(PerftTests.Kiwipete);
        var sw = Stopwatch.StartNew();
        long nodes = MoveGenerator.Perft(pos, 4);
        sw.Stop();
        output.WriteLine($"perft(4) kiwipete: {nodes:N0} nodes in {sw.ElapsedMilliseconds} ms = {nodes / sw.Elapsed.TotalSeconds / 1e6:0.0} Mnps");

        var searcher = new Searcher(64);
        SearchResult r = searcher.Search(Position.FromFen("r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4"),
            new SearchLimits { SoftTime = TimeSpan.FromSeconds(2), HardTime = TimeSpan.FromSeconds(3) });
        output.WriteLine($"search: depth {r.Depth}, {r.Nodes:N0} nodes in {r.Elapsed.TotalMilliseconds:0} ms = {r.NodesPerSecond / 1000:N0} knps, best {r.BestMove} {Searcher.FormatScore(r.Score)}");
        Assert.True(r.Depth >= 6);
    }

    /// <summary>
    /// The search signature: total nodes of the fixed benchmark (tools/bench.cs). Speed-ups must
    /// leave it alone; it changes only when the search or evaluation plays differently. If that is
    /// intended, update the number here, and bump BotMoveProvider.Revision when bot moves change.
    /// </summary>
    [Fact]
    public void Search_signature_is_unchanged()
    {
        foreach (string fen in Bench.Positions) Assert.Null(Position.FromFen(fen).Validate());
        (long nodes, TimeSpan elapsed) = Bench.Run(Bench.Positions, depth: 8);
        output.WriteLine($"bench depth 8: {nodes:N0} nodes in {elapsed.TotalMilliseconds:0} ms");
        Assert.Equal(478_571, nodes);
    }
}
