using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;

namespace Gambit.Tests;

public sealed class MoveTreeTests
{
    private static MoveNode Play(MoveTree tree, MoveNode from, params string[] sans)
    {
        MoveNode n = from;
        foreach (string san in sans)
        {
            Assert.True(San.TryParse(n.Position, san, out Move m), san);
            n = tree.Play(n, m);
        }
        return n;
    }

    [Fact]
    public void Variations_branch_without_losing_the_main_line()
    {
        var tree = new MoveTree();
        MoveNode nf3 = Play(tree, tree.Root, "e4", "e5", "Nf3", "Nc6");
        MoveNode e4 = tree.Root.Children[0];
        MoveNode c5 = Play(tree, e4, "c5", "Nf3");

        Assert.Equal(["e4", "e5", "Nf3", "Nc6"], tree.MainLine().Skip(1).Select(n => n.San));
        Assert.Equal(2, e4.Children.Count);
        Assert.True(nf3.IsMainLine);
        Assert.False(c5.IsMainLine);
        Assert.Equal("c5", c5.BranchPoint?.San);
        Assert.Null(nf3.BranchPoint);
        Assert.Same(e4.Children[0], Play(tree, e4, "e5")); // replaying an existing move reuses it

        Game line = tree.ToGame(MoveTree.LineThrough(c5));
        Assert.Equal(["e4", "c5", "Nf3"], line.Moves.Select(m => m.San));
        Assert.Equal(3, c5.Ply);
        Assert.Equal(2, c5.MoveNumber);
        Assert.Equal(Color.White, c5.Side);
    }

    [Fact]
    public void Promote_and_remove_reshape_the_tree()
    {
        var tree = new MoveTree();
        Play(tree, tree.Root, "d4", "d5");
        MoveNode e4 = Play(tree, tree.Root, "e4");
        MoveNode e5 = Play(tree, e4, "e5");

        MoveTree.Promote(e5);
        Assert.Equal(["e4", "e5"], tree.MainLine().Skip(1).Select(n => n.San));
        Assert.True(e5.IsMainLine);

        MoveTree.Remove(e4);
        Assert.Equal(["d4", "d5"], tree.MainLine().Skip(1).Select(n => n.San));
        Assert.Single(tree.Root.Children);
    }

    [Fact]
    public void Pgn_export_nests_variations()
    {
        var tree = new MoveTree();
        MoveNode e4 = Play(tree, tree.Root, "e4");
        Play(tree, e4, "e5", "Nf3", "Nc6");
        MoveNode c5 = Play(tree, e4, "c5");
        Play(tree, c5, "Nf3", "d6");
        Play(tree, c5, "c3");
        Play(tree, tree.Root, "d4");

        Assert.Equal("1. e4 ( 1. d4 ) 1... e5 ( 1... c5 2. Nf3 ( 2. c3 ) 2... d6 ) 2. Nf3 Nc6", string.Join(' ', tree.MovetextTokens()));

        Game main = tree.ToGame(tree.MainLine());
        string pgn = Pgn.Write(main, tree);
        Assert.Contains("1. e4 (1. d4) 1... e5 (1... c5 2. Nf3 (2. c3) 2... d6) 2. Nf3 Nc6 *", pgn);
        // Readers that skip variations still get the main line back.
        Assert.Equal(["e4", "e5", "Nf3", "Nc6"], Pgn.ReadOne(pgn).ToGame(out string? error).Moves.Select(m => m.San));
        Assert.Null(error);
    }

    [Fact]
    public void Pgn_variations_are_read_back_into_the_tree()
    {
        const string text = "[Event \"Study\"]\n\n1. e4 {best by test} e5 (1... c5!? 2. Nf3 (2. c3) 2... d6 $1) 2. Nf3 Nc6\n(2... d6 3. d4) 3. Bb5 *";
        PgnGame game = Pgn.ReadOne(text);
        Assert.Equal(["e4", "e5", "Nf3", "Nc6", "Bb5"], game.Moves); // the main-line reader is unchanged

        MoveTree tree = game.ToTree(out string? error);
        Assert.Null(error);
        const string expected = "1. e4 e5 ( 1... c5 2. Nf3 ( 2. c3 ) 2... d6 ) 2. Nf3 Nc6 ( 2... d6 3. d4 ) 3. Bb5";
        Assert.Equal(expected, string.Join(' ', tree.MovetextTokens()));

        // Write → read → same tree.
        string written = Pgn.Write(tree.ToGame(tree.MainLine()), tree);
        Assert.Equal(expected, string.Join(' ', Pgn.ReadOne(written).ToTree(out _).MovetextTokens()));
    }

    [Fact]
    public void A_broken_variation_is_dropped_but_the_game_loads()
    {
        MoveTree tree = Pgn.ReadOne("1. e4 e5 (1... Qxh7 2. d4 (2. c4)) 2. Nf3 *").ToTree(out string? error);
        Assert.NotNull(error);
        Assert.Equal("1. e4 e5 2. Nf3", string.Join(' ', tree.MovetextTokens()));
    }

    [Fact]
    public void Trees_can_start_from_any_position()
    {
        var tree = new MoveTree("4k3/8/8/8/8/8/4P3/4K3 b - - 0 40");
        MoveNode n = Play(tree, tree.Root, "Kd7", "e4");
        Assert.Equal("40... Kd7 41. e4", string.Join(' ', tree.MovetextTokens()));
        Assert.Equal(41, n.MoveNumber);
        Assert.Throws<ArgumentException>(() => tree.Play(tree.Root, Uci.Parse(Position.Start(), "e2e4")));
    }
}
