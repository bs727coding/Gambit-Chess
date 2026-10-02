using Gambit.Core.Board;
using Gambit.Core.Notation;
using Gambit.Core.Puzzles;

namespace Gambit.Tests;

public class PuzzleTests
{
    [Fact]
    public void Embedded_puzzles_are_valid()
    {
        IReadOnlyList<Puzzle> all = PuzzleCatalog.All;
        Assert.NotEmpty(all);
        foreach (Puzzle p in all)
        {
            Assert.Null(p.Validate());
            Assert.InRange(p.Rating, 300, 3000);
            Assert.NotEmpty(p.Themes);
        }
    }

    [Fact]
    public void Puzzle_ids_and_start_positions_are_unique()
    {
        IReadOnlyList<Puzzle> all = PuzzleCatalog.All;
        Assert.Equal(all.Count, all.Select(p => p.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(p => p.StartPosition().Key).Distinct().Count());
    }

    [Fact]
    public void Mate_puzzles_end_in_checkmate()
    {
        foreach (Puzzle p in PuzzleCatalog.All.Where(p => p.HasTheme("mate")))
        {
            var pos = Position.FromFen(p.Fen);
            foreach (string uci in p.Moves) pos.MakeMove(Uci.Parse(pos, uci));
            Assert.True(pos.InCheck && !MoveGenerator.HasLegalMove(pos), $"puzzle {p.Id} is tagged mate but does not end in mate");
        }
    }

    [Fact]
    public void Csv_round_trip()
    {
        var p = new Puzzle("abc1234", Position.StartFen, ["e2e4", "e7e5"], 1234, ["opening", "short"]);
        Puzzle? back = Puzzle.FromCsv(p.ToCsv());
        Assert.NotNull(back);
        Assert.Equal(p.Moves, back!.Moves);
        Assert.Equal(p.Themes, back.Themes);
        Assert.Equal(1234, back.Rating);
    }
}
