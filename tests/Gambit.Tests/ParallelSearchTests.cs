using System.Diagnostics;
using Gambit.Core.Board;
using Gambit.Core.Notation;
using Gambit.Engine.Search;

namespace Gambit.Tests;

public class ParallelSearchTests
{
    [Theory]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1", "Ra8#", 1)]
    [InlineData("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4", "Qxf7#", 1)]
    [InlineData("7k/8/5K2/8/8/8/8/R7 w - - 0 1", null, 2)] // 1. Kg6 (or another waiting move) and 2. Ra8#
    public void Finds_the_same_mates_as_one_thread(string fen, string? san, int mateIn)
    {
        var position = Position.FromFen(fen);
        SearchResult r = new ParallelSearcher(4, 8).Search(position, SearchLimits.Depth(8));
        Assert.True(r.IsMate);
        Assert.Equal(mateIn, Searcher.MateIn(r.Score));
        if (san != null) Assert.Equal(san, San.Format(position, r.BestMove));
    }

    [Fact]
    public void Stops_on_time_with_a_legal_move()
    {
        var position = Position.FromFen(PerftTests.Kiwipete);
        var parallel = new ParallelSearcher(4, 8);
        var sw = Stopwatch.StartNew();
        SearchResult r = parallel.Search(position, new SearchLimits { SoftTime = TimeSpan.FromMilliseconds(250), HardTime = TimeSpan.FromMilliseconds(400) });
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
        Assert.True(MoveGenerator.IsLegal(position, r.BestMove));
        Assert.Equal(4, parallel.Threads);
        Assert.True(r.Depth >= 4);
    }

    [Fact]
    public void Cancelling_stops_every_thread()
    {
        var parallel = new ParallelSearcher(4, 8);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var sw = Stopwatch.StartNew();
        SearchResult r = parallel.Search(Position.Start(), SearchLimits.Infinite, cts.Token);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
        Assert.False(r.BestMove.IsNone);
    }

    [Fact]
    public void Several_lines_come_from_the_main_thread_and_progress_counts_all_nodes()
    {
        var parallel = new ParallelSearcher(3, 8);
        var reports = new List<SearchInfo>();
        SearchResult r = parallel.Search(Position.Start(), SearchLimits.Depth(7) with { MultiPv = 3 }, onInfo: reports.Add);
        Assert.Equal(3, r.Lines.Count);
        Assert.Equal(3, r.Lines.Select(l => l.Move).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 7), reports.Select(i => i.Depth));
        Assert.True(r.Nodes >= reports[^1].Nodes);
    }

    [Fact]
    public void Table_entries_round_trip()
    {
        var table = new TranspositionTable(1);
        const ulong key = 0x1234_5678_9ABC_DEF0UL;
        var move = new Move(Square.E2, Square.E4, MoveFlag.DoublePawnPush);
        table.Store(key, move, -Searcher.Mate + 10, -1234, 17, Bound.Lower, ply: 3);

        Assert.True(table.Probe(key, 3, out Move m, out int score, out int eval, out int depth, out Bound bound));
        Assert.Equal(move, m);
        Assert.Equal(-Searcher.Mate + 10, score);
        Assert.Equal(-1234, eval);
        Assert.Equal(17, depth);
        Assert.Equal(Bound.Lower, bound);
        Assert.False(table.Probe(key ^ (1UL << 40), 3, out _, out _, out _, out _, out _)); // same slot, other position
    }
}
