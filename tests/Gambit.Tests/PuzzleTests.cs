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
    public void Every_practice_theme_has_a_name_and_plenty_of_puzzles()
    {
        foreach ((string title, string[] themes) in PuzzleThemes.PracticeGroups)
        {
            foreach (string theme in themes)
            {
                Assert.True(PuzzleThemes.Names.ContainsKey(theme), $"{title}: no display name for {theme}");
                Assert.True(PuzzleCatalog.CountWithTheme(theme) >= 100, $"{title}: only {PuzzleCatalog.CountWithTheme(theme)} puzzles for {theme}");
            }
        }
    }

    [Fact]
    public void Ratings_cover_beginners_to_masters()
    {
        IReadOnlyList<Puzzle> all = PuzzleCatalog.All;
        for (int band = 400; band < 3000; band += 100)
        {
            int from = band;
            Assert.True(all.Count(p => p.Rating >= from && p.Rating < from + 100) >= 100, $"fewer than 100 puzzles rated {from}-{from + 99}");
        }
    }

    [Fact]
    public void Csv_round_trip()
    {
        var p = new Puzzle("abc1234", Position.StartFen, ["e2e4", "e7e5"], 1234, ["opening", "short"], "Kings_Pawn_Game");
        Puzzle? back = Puzzle.FromCsv(p.ToCsv());
        Assert.NotNull(back);
        Assert.Equal(p.Moves, back!.Moves);
        Assert.Equal(p.Themes, back.Themes);
        Assert.Equal(1234, back.Rating);
        Assert.Equal("Kings_Pawn_Game", back.Opening);
        Assert.Equal("King's Pawn Game", back.OpeningName);

        Puzzle? none = Puzzle.FromCsv((p with { Opening = null }).ToCsv());
        Assert.Null(none!.Opening);
        Assert.Null(none.OpeningName);
        Assert.Null(Puzzle.FromCsv("abc1234,8/8/8/8/8/8/8/8 w - - 0 1,e2e4 e7e5,1234,short")!.Opening); // older five-column lines
    }

    [Theory]
    [InlineData("Queens_Gambit_Declined", "Queen's Gambit Declined")]
    [InlineData("Caro-Kann_Defense", "Caro-Kann Defense")]
    [InlineData("Kings_Gambit_Accepted", "King's Gambit Accepted")]
    [InlineData("Grunfeld_Defense", "Grünfeld Defense")]
    [InlineData("Russian_Game", "Petrov's Defense")]
    [InlineData("Some_Unknown_Opening", "Some Unknown Opening")]
    public void Opening_tags_read_as_book_names(string tag, string name) => Assert.Equal(name, PuzzleOpenings.Name(tag));

    [Fact]
    public void Common_openings_have_plenty_of_puzzles()
    {
        IReadOnlyList<(string Name, int Count)> openings = PuzzleCatalog.Openings(minimum: 50);
        Assert.True(openings.Count >= 20, $"only {openings.Count} openings with 50+ puzzles");
        Assert.Contains(openings, o => o.Name == "Sicilian Defense");
        Assert.DoesNotContain(openings, o => o.Name.Contains('_'));
        Assert.Equal(openings.OrderByDescending(o => o.Count).Select(o => o.Name), openings.Select(o => o.Name));
    }
}
