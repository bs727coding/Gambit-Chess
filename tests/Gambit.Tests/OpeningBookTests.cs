using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Openings;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;

namespace Gambit.Tests;

public class OpeningBookTests
{
    [Fact]
    public void All_lines_are_legal()
    {
        Assert.Empty(OpeningBook.LoadErrors);
        Assert.True(OpeningBook.All.Count >= 100, $"only {OpeningBook.All.Count} openings loaded");
    }

    [Fact]
    public void Names_are_unique()
    {
        var duplicates = OpeningBook.All.GroupBy(o => o.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("e4 e5 Nf3 Nc6 Bb5 a6", "Ruy Lopez: Morphy Defense")]
    [InlineData("e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6 h3", "Sicilian Defense: Najdorf Variation")]
    [InlineData("d4 Nf6 c4 e6 Nc3 Bb4", "Nimzo-Indian Defense")]
    [InlineData("e4 e6 d4 d5 e5 c5", "French Defense: Advance Variation")]
    public void Identifies_openings(string moves, string expected)
    {
        var game = new Game();
        foreach (string san in moves.Split(' ')) Assert.NotNull(game.TryPlay(san));
        Assert.Equal(expected, OpeningBook.Identify(game)?.Name);
    }

    [Fact]
    public void Recognises_transpositions()
    {
        // Queen's Gambit Declined reached via 1.c4 e6 2.d4 d5 (an English move order).
        var game = new Game();
        foreach (string san in new[] { "c4", "e6", "d4", "d5" }) Assert.NotNull(game.TryPlay(san));
        Assert.Equal("Queen's Gambit Declined", OpeningBook.Identify(game)?.Name);
    }

    [Fact]
    public void Bots_play_book_moves_in_the_opening()
    {
        var bot = new BotMoveProvider(BotRoster.Get("jade"), seed: 5) { HumanLikeDelay = false };
        var start = Position.Start();
        var bookMoves = OpeningBook.MovesFor(start).Select(m => m.Move).ToHashSet();
        for (int i = 0; i < 10; i++)
        {
            Move m = bot.ChooseMove(new GameSnapshot(start.Clone(), null, null, TimeSpan.Zero, 0));
            Assert.Contains(m, bookMoves);
        }
    }
}
