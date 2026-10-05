using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Openings;

namespace Gambit.Tests;

public class OpeningExplorerTests
{
    private static Position After(string moves)
    {
        var game = new Game();
        foreach (string san in moves.Split(' ', StringSplitOptions.RemoveEmptyEntries)) Assert.NotNull(game.TryPlay(san));
        return game.Position;
    }

    [Fact]
    public void Data_loads_without_errors()
    {
        Assert.Empty(OpeningExplorer.LoadErrors);
        Assert.True(OpeningExplorer.TotalGames > 10_000, $"only {OpeningExplorer.TotalGames} games");
        Assert.Contains("Lichess", OpeningExplorer.Source);
    }

    [Fact]
    public void Start_position_lists_the_main_first_moves_most_played_first()
    {
        IReadOnlyList<ExplorerMove> moves = OpeningExplorer.MovesFor(Position.Start());
        Assert.Equal("e4", moves[0].San);
        Assert.Contains(moves.Take(3), m => m.San == "d4");
        Assert.True(moves.Zip(moves.Skip(1)).All(p => p.First.Games >= p.Second.Games));
        Assert.Equal(OpeningExplorer.TotalGames, moves.Sum(m => m.Games));
        Assert.All(moves, m => Assert.InRange(m.WhiteScore, 0, 1));
    }

    [Fact]
    public void Moves_are_legal_in_their_position()
    {
        Position pos = After("e4 e5 Nf3 Nc6");
        IReadOnlyList<ExplorerMove> moves = OpeningExplorer.MovesFor(pos);
        Assert.Contains(moves, m => m.San == "Bc4");
        Assert.Contains(moves, m => m.San == "Bb5");
        var legal = MoveGenerator.LegalMoves(pos).ToHashSet();
        Assert.All(moves, m => Assert.Contains(m.Move, legal));
    }

    [Fact]
    public void Transpositions_share_their_games()
    {
        IReadOnlyList<ExplorerMove> a = OpeningExplorer.MovesFor(After("d4 Nf6 c4 e6"));
        IReadOnlyList<ExplorerMove> b = OpeningExplorer.MovesFor(After("c4 e6 d4 Nf6"));
        Assert.NotEmpty(a);
        Assert.Equal(a.Select(m => (m.San, m.Games)), b.Select(m => (m.San, m.Games)));
    }

    [Fact]
    public void Unknown_positions_have_no_moves()
    {
        Assert.Empty(OpeningExplorer.MovesFor(Position.FromFen("8/8/4k3/8/8/4K3/4P3/8 w - - 0 1")));
    }
}
