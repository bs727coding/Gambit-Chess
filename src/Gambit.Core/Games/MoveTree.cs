using Gambit.Core.Board;
using Gambit.Core.Notation;

namespace Gambit.Core.Games;

/// <summary>One position in a <see cref="MoveTree"/>: the move that reached it and the moves that follow.</summary>
public sealed class MoveNode
{
    internal readonly List<MoveNode> ChildList = [];

    internal MoveNode(MoveNode? parent, Move move, string san, Position position)
    {
        Parent = parent;
        Move = move;
        San = san;
        Position = position;
        if (parent != null)
        {
            Ply = parent.Ply + 1;
            Side = parent.Position.SideToMove;
            MoveNumber = parent.Position.FullmoveNumber;
        }
    }

    public MoveNode? Parent { get; }

    /// <summary>The move that reached this node (<see cref="Move.None"/> for the root).</summary>
    public Move Move { get; }

    public string San { get; }

    /// <summary>The position after <see cref="Move"/>.</summary>
    public Position Position { get; }

    /// <summary>The first child is the main continuation; the rest are variations.</summary>
    public IReadOnlyList<MoveNode> Children => ChildList;

    public int Ply { get; }

    /// <summary>The side that played <see cref="Move"/>.</summary>
    public Color Side { get; }

    public int MoveNumber { get; }

    public bool IsRoot => Parent == null;

    /// <summary>True when every step from the root to here is a main continuation.</summary>
    public bool IsMainLine => Parent == null || ReferenceEquals(Parent.ChildList[0], this) && Parent.IsMainLine;

    /// <summary>The node where this line leaves the main line (the first non-main step), or null on the main line.</summary>
    public MoveNode? BranchPoint
    {
        get
        {
            MoveNode? branch = null;
            for (MoveNode n = this; n.Parent != null; n = n.Parent)
                if (!ReferenceEquals(n.Parent.ChildList[0], n)) branch = n;
            return branch;
        }
    }
}

/// <summary>
/// Moves with side lines, for analysis. Every node is the position after one move; a node's first
/// child is its main continuation and later children are variations. The UI shows one line at a time
/// (<see cref="LineThrough"/>) as an ordinary <see cref="Game"/>.
/// </summary>
public sealed class MoveTree
{
    public MoveTree(string? startFen = null)
    {
        StartFen = startFen ?? Position.StartFen;
        Root = new MoveNode(null, Move.None, "", Position.FromFen(StartFen));
    }

    public string StartFen { get; }

    public MoveNode Root { get; }

    /// <summary>The child of <paramref name="parent"/> reached by <paramref name="move"/>; a new move is added as the last variation.</summary>
    public MoveNode Play(MoveNode parent, Move move)
    {
        foreach (MoveNode child in parent.ChildList)
            if (child.Move == move) return child;
        if (!MoveGenerator.LegalMoves(parent.Position).Contains(move)) throw new ArgumentException($"Illegal move {move.ToUci()}.", nameof(move));
        string san = San.Format(parent.Position, move);
        Position after = parent.Position.Clone();
        after.MakeMove(move);
        var node = new MoveNode(parent, move, san, after);
        parent.ChildList.Add(node);
        return node;
    }

    /// <summary>The root, the path to <paramref name="node"/>, then main continuations to the end of the line.</summary>
    public static List<MoveNode> LineThrough(MoveNode node)
    {
        var line = new List<MoveNode>();
        for (MoveNode? n = node; n != null; n = n.Parent) line.Add(n);
        line.Reverse();
        for (MoveNode n = node; n.ChildList.Count > 0;)
        {
            n = n.ChildList[0];
            line.Add(n);
        }
        return line;
    }

    public List<MoveNode> MainLine() => LineThrough(Root);

    /// <summary>A line (as returned by <see cref="LineThrough"/>) replayed as a <see cref="Game"/>.</summary>
    public Game ToGame(IReadOnlyList<MoveNode> line)
    {
        var game = new Game(StartFen) { AutoDrawRules = false };
        foreach (MoveNode n in line)
            if (!n.IsRoot && !game.IsOver) game.Play(n.Move);
        return game;
    }

    /// <summary>Makes the line through <paramref name="node"/> the main line.</summary>
    public static void Promote(MoveNode node)
    {
        for (MoveNode n = node; n.Parent != null; n = n.Parent)
        {
            List<MoveNode> siblings = n.Parent.ChildList;
            int i = siblings.IndexOf(n);
            if (i <= 0) continue;
            siblings.RemoveAt(i);
            siblings.Insert(0, n);
        }
    }

    /// <summary>Deletes <paramref name="node"/> and every move after it.</summary>
    public static void Remove(MoveNode node) => node.Parent?.ChildList.Remove(node);

    public static MoveTree FromGame(Game game)
    {
        var tree = new MoveTree(game.StartFen);
        MoveNode n = tree.Root;
        foreach (GameMove m in game.Moves) n = tree.Play(n, m.Move);
        return tree;
    }

    /// <summary>PGN movetext tokens: the main line with variations in parentheses (no result token).</summary>
    public List<string> MovetextTokens()
    {
        var tokens = new List<string>();
        EmitLine(Root, numberFirst: true, tokens);
        return tokens;
    }

    private static void EmitLine(MoveNode from, bool numberFirst, List<string> tokens)
    {
        bool needNumber = numberFirst;
        for (MoveNode node = from; node.ChildList.Count > 0;)
        {
            MoveNode main = node.ChildList[0];
            EmitMove(main, needNumber, tokens);
            needNumber = false;
            foreach (MoveNode alternative in node.ChildList.Skip(1))
            {
                tokens.Add("(");
                EmitMove(alternative, number: true, tokens);
                EmitLine(alternative, numberFirst: false, tokens);
                tokens.Add(")");
                needNumber = true; // the main line resumes after a variation
            }
            node = main;
        }
    }

    private static void EmitMove(MoveNode node, bool number, List<string> tokens)
    {
        if (node.Side == Color.White) tokens.Add($"{node.MoveNumber}.");
        else if (number) tokens.Add($"{node.MoveNumber}...");
        tokens.Add(node.San);
    }
}
