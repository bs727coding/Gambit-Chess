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
}
