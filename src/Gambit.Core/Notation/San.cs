using System.Text;
using Gambit.Core.Board;

namespace Gambit.Core.Notation;

/// <summary>Standard Algebraic Notation (e.g. <c>Nbd7</c>, <c>exd5</c>, <c>e8=Q+</c>, <c>O-O-O#</c>).</summary>
public static class San
{
    /// <summary>Formats a legal move in the position <em>before</em> it is played, including +/# suffix.</summary>
    public static string Format(Position pos, Move move)
    {
        Span<Move> legal = stackalloc Move[MoveGenerator.MaxMoves];
        int n = MoveGenerator.Generate(pos, legal);
        string s = FormatCore(pos, move, legal[..n]);

        pos.MakeMove(move);
        if (pos.InCheck) s += MoveGenerator.HasLegalMove(pos) ? "+" : "#";
        pos.UnmakeMove();
        return s;
    }

    /// <summary>Formats a whole line of moves starting from <paramref name="start"/> (which is not modified).</summary>
    public static List<string> FormatLine(Position start, IEnumerable<Move> moves)
    {
        var pos = start.Clone();
        var list = new List<string>();
        foreach (Move m in moves)
        {
            if (!MoveGenerator.IsLegal(pos, m)) break;
            list.Add(Format(pos, m));
            pos.MakeMove(m);
        }
        return list;
    }

    private static string FormatCore(Position pos, Move move, ReadOnlySpan<Move> legal)
    {
        if (move.Flag == MoveFlag.KingCastle) return "O-O";
        if (move.Flag == MoveFlag.QueenCastle) return "O-O-O";

        int from = move.From, to = move.To;
        PieceType type = pos.PieceAt(from).Type();
        var sb = new StringBuilder(8);

        if (type == PieceType.Pawn)
        {
            if (move.IsCapture) sb.Append(Square.FileChar(from)).Append('x');
            sb.Append(Square.Name(to));
            if (move.IsPromotion) sb.Append('=').Append(move.PromotionType.SanLetter());
            return sb.ToString();
        }

        sb.Append(type.SanLetter());

        bool ambiguous = false, sameFile = false, sameRank = false;
        foreach (Move other in legal)
        {
            if (other.To != to || other.From == from || pos.PieceAt(other.From).Type() != type) continue;
            ambiguous = true;
            if (Square.File(other.From) == Square.File(from)) sameFile = true;
            if (Square.Rank(other.From) == Square.Rank(from)) sameRank = true;
        }

        if (ambiguous)
        {
            if (!sameFile) sb.Append(Square.FileChar(from));
            else if (!sameRank) sb.Append(Square.RankChar(from));
            else sb.Append(Square.Name(from));
        }

        if (move.IsCapture) sb.Append('x');
        sb.Append(Square.Name(to));
        return sb.ToString();
    }

    /// <summary>Parses SAN (lenient: accepts 0-0, missing '=', annotations, check marks, UCI fallback).</summary>
    public static bool TryParse(Position pos, string text, out Move move)
    {
        move = Move.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string san = Normalize(text);
        if (san.Length == 0) return false;

        Span<Move> legal = stackalloc Move[MoveGenerator.MaxMoves];
        int n = MoveGenerator.Generate(pos, legal);
        ReadOnlySpan<Move> moves = legal[..n];

        foreach (Move m in moves)
        {
            string candidate = FormatCore(pos, m, moves);
            if (candidate == san || candidate.Replace("=", "") == san.Replace("=", ""))
            {
                move = m;
                return true;
            }
        }

        // Over-disambiguated / long algebraic input such as "Ng1f3", "Ng1-f3" or "e2-e4".
        string compact = san.Replace("-", "").Replace("x", "").Replace("=", "");
        foreach (Move m in moves)
        {
            string longForm = pos.PieceAt(m.From).Type().SanLetter() + Square.Name(m.From) + Square.Name(m.To)
                + (m.IsPromotion ? m.PromotionType.SanLetter() : "");
            if (compact == longForm)
            {
                move = m;
                return true;
            }
        }

        move = Uci.Parse(pos, text.Trim());
        return !move.IsNone;
    }

    public static Move Parse(Position pos, string text) =>
        TryParse(pos, text, out Move m) ? m : throw new FormatException($"Illegal or unknown move '{text}' in {pos.ToFen()}");

    private static string Normalize(string text)
    {
        string s = text.Trim();
        s = s.Replace("e.p.", "").Trim();
        while (s.Length > 0 && s[^1] is '+' or '#' or '!' or '?') s = s[..^1];
        s = s.Replace("0-0-0", "O-O-O").Replace("0-0", "O-O");
        return s;
    }
}

/// <summary>UCI long algebraic moves (e2e4, e7e8q).</summary>
public static class Uci
{
    /// <summary>Finds the legal move matching a UCI string, or <see cref="Move.None"/>.</summary>
    public static Move Parse(Position pos, string uci)
    {
        if (string.IsNullOrEmpty(uci) || uci.Length < 4) return Move.None;
        int from = Square.Parse(uci.AsSpan(0, 2)), to = Square.Parse(uci.AsSpan(2, 2));
        if (from == Square.None || to == Square.None) return Move.None;
        PieceType promo = uci.Length >= 5 ? PieceExtensions.FromFenChar(char.ToLowerInvariant(uci[4])).Type() : PieceType.None;

        Span<Move> legal = stackalloc Move[MoveGenerator.MaxMoves];
        int n = MoveGenerator.Generate(pos, legal);
        for (int i = 0; i < n; i++)
        {
            Move m = legal[i];
            if (m.From == from && m.To == to && m.PromotionType == promo) return m;
        }
        return Move.None;
    }

    public static string Format(Move move) => move.ToUci();
}
