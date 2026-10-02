using Gambit.Core.Board;
using Gambit.Core.Notation;

namespace Gambit.Core.Games;

public enum GameResult
{
    Ongoing,
    WhiteWins,
    BlackWins,
    Draw,
}

public enum Termination
{
    None,
    Checkmate,
    Stalemate,
    InsufficientMaterial,
    ThreefoldRepetition,
    FiftyMoveRule,
    Resignation,
    Timeout,
    TimeoutVsInsufficientMaterial,
    DrawAgreement,
    Abandoned,
    Aborted,
}

/// <summary>One played move with everything the UI, PGN export and game review need.</summary>
public sealed record GameMove(
    int Ply,
    Move Move,
    string San,
    Piece Piece,
    Piece Captured,
    string FenBefore,
    string FenAfter,
    bool GivesCheck)
{
    public string Uci => Move.ToUci();
    public Color Side => Piece.Color();

    /// <summary>1-based full-move number this move belongs to.</summary>
    public int MoveNumber { get; init; }

    /// <summary>Clock remaining for the mover after the move (if the game was timed).</summary>
    public TimeSpan? ClockAfter { get; init; }

    /// <summary>Time spent on this move (if known).</summary>
    public TimeSpan? ThinkTime { get; init; }
}

/// <summary>
/// A game in progress or finished: start position, moves, and the result. Rules-only — clocks and
/// players live in the session layer, so the same class backs local, bot and online games.
/// </summary>
public sealed class Game
{
    private readonly Position _position;
    private readonly List<GameMove> _moves = [];
    private List<Move>? _legalCache;

    public Game(string? startFen = null)
    {
        StartFen = string.IsNullOrWhiteSpace(startFen) ? Position.StartFen : startFen.Trim();
        _position = Position.FromFen(StartFen);
        StartFen = _position.ToFen();
        UpdateResultFromPosition();
    }

    public string StartFen { get; }

    /// <summary>The current position. Treat as read-only; use <see cref="Play"/> to change it.</summary>
    public Position Position => _position;

    public IReadOnlyList<GameMove> Moves => _moves;

    public GameResult Result { get; private set; }

    public Termination Termination { get; private set; }

    public bool IsOver => Result != GameResult.Ongoing;

    public Color SideToMove => _position.SideToMove;

    /// <summary>
    /// When true (default, like most chess sites), threefold repetition and the 50-move rule end the
    /// game automatically instead of needing a claim.
    /// </summary>
    public bool AutoDrawRules { get; set; } = true;

    /// <summary>PGN header tags (Event, Site, Date, White, Black, ...).</summary>
    public Dictionary<string, string> Tags { get; } = new(StringComparer.Ordinal);

    public Color? Winner => Result switch
    {
        GameResult.WhiteWins => Color.White,
        GameResult.BlackWins => Color.Black,
        _ => null,
    };

    public bool StartsFromStandardPosition => StartFen == Position.StartFen;

    public IReadOnlyList<Move> LegalMoves => _legalCache ??= IsOver ? [] : MoveGenerator.LegalMoves(_position);

    public GameMove? LastMove => _moves.Count > 0 ? _moves[^1] : null;

    public bool IsLegal(Move move) => !IsOver && LegalMoves.Contains(move);

    /// <summary>Plays a legal move. Throws if the move is illegal or the game is over.</summary>
    public GameMove Play(Move move, TimeSpan? clockAfter = null, TimeSpan? thinkTime = null)
    {
        if (IsOver) throw new InvalidOperationException("The game is over.");
        if (!LegalMoves.Contains(move)) throw new InvalidOperationException($"Illegal move {move} in {_position.ToFen()}");

        string fenBefore = _position.ToFen();
        string san = San.Format(_position, move);
        Piece piece = _position.PieceAt(move.From);
        int moveNumber = _position.FullmoveNumber;

        _position.MakeMove(move);
        _legalCache = null;

        var gm = new GameMove(_moves.Count + 1, move, san, piece, _position.LastCaptured, fenBefore, _position.ToFen(), _position.InCheck)
        {
            MoveNumber = moveNumber,
            ClockAfter = clockAfter,
            ThinkTime = thinkTime,
        };
        _moves.Add(gm);
        UpdateResultFromPosition();
        return gm;
    }

    /// <summary>Plays a move given in SAN or UCI. Returns null if it is not legal here.</summary>
    public GameMove? TryPlay(string sanOrUci)
    {
        if (IsOver) return null;
        Move m = Uci.Parse(_position, sanOrUci);
        if (m.IsNone && !San.TryParse(_position, sanOrUci, out m)) return null;
        return Play(m);
    }

    /// <summary>Takes back the last move (also clears a result that came from the board).</summary>
    public bool Undo()
    {
        if (_moves.Count == 0) return false;
        _position.UnmakeMove();
        _moves.RemoveAt(_moves.Count - 1);
        _legalCache = null;
        Result = GameResult.Ongoing;
        Termination = Termination.None;
        UpdateResultFromPosition();
        return true;
    }

    public void Resign(Color loser) =>
        End(loser == Color.White ? GameResult.BlackWins : GameResult.WhiteWins, Termination.Resignation);

    public void AgreeDraw() => End(GameResult.Draw, Termination.DrawAgreement);

    public void Abort() => End(GameResult.Draw, Termination.Aborted);

    public void Abandon(Color loser) =>
        End(loser == Color.White ? GameResult.BlackWins : GameResult.WhiteWins, Termination.Abandoned);

    /// <summary>A side ran out of time: they lose unless the opponent cannot possibly mate.</summary>
    public void Timeout(Color flagged)
    {
        if (!_position.HasMatingMaterial(flagged.Opposite()))
            End(GameResult.Draw, Termination.TimeoutVsInsufficientMaterial);
        else
            End(flagged == Color.White ? GameResult.BlackWins : GameResult.WhiteWins, Termination.Timeout);
    }

    /// <summary>Claim a draw by threefold repetition / 50 moves when <see cref="AutoDrawRules"/> is off.</summary>
    public bool TryClaimDraw()
    {
        if (IsOver) return false;
        if (_position.RepetitionCount() >= 2) End(GameResult.Draw, Termination.ThreefoldRepetition);
        else if (_position.HalfmoveClock >= 100) End(GameResult.Draw, Termination.FiftyMoveRule);
        return IsOver;
    }

    /// <summary>A fresh <see cref="Position"/> as it was after <paramref name="ply"/> half-moves (0 = start).</summary>
    public Position PositionAt(int ply)
    {
        if (ply <= 0) return Position.FromFen(StartFen);
        if (ply > _moves.Count) ply = _moves.Count;
        return Position.FromFen(_moves[ply - 1].FenAfter);
    }

    public string FenAt(int ply) => ply <= 0 ? StartFen : _moves[Math.Min(ply, _moves.Count) - 1].FenAfter;

    /// <summary>Result in PGN notation: 1-0, 0-1, 1/2-1/2 or *.</summary>
    public string ResultString => Result switch
    {
        GameResult.WhiteWins => "1-0",
        GameResult.BlackWins => "0-1",
        GameResult.Draw => "1/2-1/2",
        _ => "*",
    };

    /// <summary>Human-readable explanation, e.g. "White wins by checkmate".</summary>
    public string ResultDescription
    {
        get
        {
            string how = Termination switch
            {
                Termination.Checkmate => "by checkmate",
                Termination.Resignation => "by resignation",
                Termination.Timeout => "on time",
                Termination.Abandoned => "by abandonment",
                Termination.Stalemate => "by stalemate",
                Termination.InsufficientMaterial => "by insufficient material",
                Termination.ThreefoldRepetition => "by threefold repetition",
                Termination.FiftyMoveRule => "by the 50-move rule",
                Termination.DrawAgreement => "by agreement",
                Termination.TimeoutVsInsufficientMaterial => "by timeout vs insufficient material",
                Termination.Aborted => "— game aborted",
                _ => "",
            };
            return Result switch
            {
                GameResult.WhiteWins => $"White wins {how}",
                GameResult.BlackWins => $"Black wins {how}",
                GameResult.Draw => Termination == Termination.Aborted ? "Game aborted" : $"Draw {how}",
                _ => "In progress",
            };
        }
    }

    private void End(GameResult result, Termination termination)
    {
        if (IsOver) return;
        Result = result;
        Termination = termination;
        _legalCache = null;
    }

    private void UpdateResultFromPosition()
    {
        if (IsOver) return;
        if (!MoveGenerator.HasLegalMove(_position))
        {
            if (_position.InCheck)
                End(_position.SideToMove == Color.White ? GameResult.BlackWins : GameResult.WhiteWins, Termination.Checkmate);
            else
                End(GameResult.Draw, Termination.Stalemate);
        }
        else if (_position.IsInsufficientMaterial())
        {
            End(GameResult.Draw, Termination.InsufficientMaterial);
        }
        else if (AutoDrawRules && _position.RepetitionCount() >= 2)
        {
            End(GameResult.Draw, Termination.ThreefoldRepetition);
        }
        else if (AutoDrawRules && _position.HalfmoveClock >= 100)
        {
            End(GameResult.Draw, Termination.FiftyMoveRule);
        }
    }
}
