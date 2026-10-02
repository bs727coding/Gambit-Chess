using System.Text;
using Gambit.Core.Board;

namespace Gambit.Core.Notation;

public sealed class FenException(string message) : FormatException(message);

/// <summary>Forsyth–Edwards Notation.</summary>
public static class Fen
{
    public static Position Parse(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) throw new FenException("FEN is empty.");
        string[] parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new FenException("FEN needs at least a board and a side to move.");

        var pos = new Position();
        pos.Clear();

        string[] ranks = parts[0].Split('/');
        if (ranks.Length != 8) throw new FenException("FEN board must have 8 ranks.");
        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i, file = 0;
            foreach (char ch in ranks[i])
            {
                if (char.IsDigit(ch))
                {
                    file += ch - '0';
                }
                else
                {
                    Piece p = PieceExtensions.FromFenChar(ch);
                    if (p == Piece.None) throw new FenException($"Invalid piece '{ch}' in FEN.");
                    if (file > 7) throw new FenException($"Rank {rank + 1} has too many squares.");
                    pos.PutPiece(Square.Make(file, rank), p);
                    file++;
                }
            }
            if (file != 8) throw new FenException($"Rank {rank + 1} must describe exactly 8 squares.");
        }

        Color side = parts[1] switch
        {
            "w" or "W" => Color.White,
            "b" or "B" => Color.Black,
            _ => throw new FenException("Side to move must be 'w' or 'b'."),
        };

        CastlingRights castling = CastlingRights.None;
        if (parts.Length > 2 && parts[2] != "-")
        {
            foreach (char ch in parts[2])
            {
                castling |= ch switch
                {
                    'K' => CastlingRights.WhiteKingSide,
                    'Q' => CastlingRights.WhiteQueenSide,
                    'k' => CastlingRights.BlackKingSide,
                    'q' => CastlingRights.BlackQueenSide,
                    _ => throw new FenException($"Invalid castling flag '{ch}'."),
                };
            }
        }

        int ep = Square.None;
        if (parts.Length > 3 && parts[3] != "-")
        {
            ep = Square.Parse(parts[3]);
            if (ep == Square.None) throw new FenException("Invalid en-passant square.");
        }

        int halfmove = parts.Length > 4 && int.TryParse(parts[4], out int h) ? h : 0;
        int fullmove = parts.Length > 5 && int.TryParse(parts[5], out int f) ? f : 1;

        pos.SetState(side, castling, ep, halfmove, fullmove);
        pos.FinishSetup();
        return pos;
    }

    public static bool TryParse(string fen, out Position? position, out string? error)
    {
        try
        {
            position = Parse(fen);
            error = position.Validate();
            if (error != null) position = null;
            return position != null;
        }
        catch (FenException ex)
        {
            position = null;
            error = ex.Message;
            return false;
        }
    }

    public static string Format(Position pos)
    {
        var sb = new StringBuilder(90);
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                Piece p = pos.PieceAt(Square.Make(file, rank));
                if (p == Piece.None)
                {
                    empty++;
                    continue;
                }
                if (empty > 0) sb.Append(empty);
                empty = 0;
                sb.Append(p.ToFenChar());
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }

        sb.Append(pos.SideToMove == Color.White ? " w " : " b ");
        if (pos.Castling == CastlingRights.None)
        {
            sb.Append('-');
        }
        else
        {
            if (pos.Castling.HasFlag(CastlingRights.WhiteKingSide)) sb.Append('K');
            if (pos.Castling.HasFlag(CastlingRights.WhiteQueenSide)) sb.Append('Q');
            if (pos.Castling.HasFlag(CastlingRights.BlackKingSide)) sb.Append('k');
            if (pos.Castling.HasFlag(CastlingRights.BlackQueenSide)) sb.Append('q');
        }
        sb.Append(' ').Append(Square.Name(pos.EnPassantSquare));
        sb.Append(' ').Append(pos.HalfmoveClock).Append(' ').Append(pos.FullmoveNumber);
        return sb.ToString();
    }

    /// <summary>Board + side + castling + en passant only (ignores move counters) — for comparing positions.</summary>
    public static string PositionKey(Position pos)
    {
        string full = Format(pos);
        int cut = full.LastIndexOf(' ');
        cut = full.LastIndexOf(' ', cut - 1);
        return full[..cut];
    }
}
