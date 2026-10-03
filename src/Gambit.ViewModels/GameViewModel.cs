using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Openings;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;
using Gambit.Engine.Search;
using Gambit.Online.Client;

namespace Gambit.ViewModels;

/// <summary>Who may move pieces on the board right now.</summary>
public enum MoveInput
{
    None,
    White,
    Black,
    Both,
}

/// <summary>Sounds the game asks the app to play (moves have their own, see <see cref="IGameHost.PlayMoveSound"/>).</summary>
public enum GameCue
{
    Start,
    Win,
    End,
    LowTime,
}

public enum NoticeKind
{
    Info,
    Success,
}

/// <summary>A short message for the game page's toast. One with an action stays until answered.</summary>
public sealed record GameNotice(string Title, string Message, NoticeKind Kind = NoticeKind.Info, string? ActionLabel = null, Action? Action = null);

/// <summary>What the result dialog shows.</summary>
public sealed record GameOverSummary(string Title, string Description, IReadOnlyList<string> Details, string? RematchLabel);

/// <summary>A player as the name bars above and below the board show them.</summary>
public sealed record PlayerBadge(string Name, string? Rating, string Monogram, string ColorHex);

/// <summary>The opponent card beside the board.</summary>
public sealed record OpponentCard(string Name, string Tagline, string Monogram, string ColorHex);

/// <summary>Pieces one side has captured and its material lead (negative when behind).</summary>
public sealed record Captures(IReadOnlyList<Piece> Pieces, int Advantage);

/// <summary>A finished game for the profile: stats, archive and achievements.</summary>
public sealed record FinishedGame(Game Game, string Opponent, BotProfile? Bot, int? OpponentRating, Color? PlayerColor, TimeControl TimeControl);

/// <summary>What the game view model needs from the app: the profile, storage and sounds.</summary>
public interface IGameHost
{
    /// <summary>The app's name, for the PGN "Site" tag.</summary>
    string AppName { get; }

    string PlayerName { get; }

    /// <summary>The user's premove setting.</summary>
    bool PremovesEnabled { get; }

    /// <summary>Keeps the unfinished game so it can be resumed after a restart.</summary>
    void SaveUnfinished(SavedGame game);

    void ClearUnfinished();

    void RecordFinished(FinishedGame game);

    /// <summary>The user's record against a bot (after <see cref="RecordFinished"/>).</summary>
    (int Wins, int Losses, int Draws) RecordAgainst(string botId);

    void PlayMoveSound(GameMove move);

    void Play(GameCue cue);
}

/// <summary>
/// The game page's logic without UI types. It starts and resumes games, follows the session (bot,
/// pass-and-play, online, spectating — always through <see cref="IGameSession"/>), and exposes what
/// the page renders: the position shown while browsing history, who may move, status, which actions
/// are available, captured material, players and clocks. Things the page can't simply re-read are
/// events: sounds go to the host; toasts, the draw question and the result dialog are raised.
/// Lives on the UI thread (sessions raise their events there).
/// </summary>
public sealed class GameViewModel : IDisposable
{
    private readonly IGameHost _host;
    private readonly Func<BotProfile, IMoveProvider> _createBot;
    private bool _recorded;
    private bool _lowTimeWarned;

    public GameViewModel(IGameHost host, Func<BotProfile, IMoveProvider>? createBot = null)
    {
        _host = host;
        _createBot = createBot ?? (bot => new BotMoveProvider(bot));
    }

    public IGameSession? Session { get; private set; }

    public GameSetup? Setup { get; private set; }

    /// <summary>The ply shown while browsing history; -1 follows the live position.</summary>
    public int ViewPly { get; private set; } = -1;

    /// <summary>A new session is attached: reset the board, players and move list.</summary>
    public event EventHandler? GameStarted;

    /// <summary>Anything the page renders may have changed (status, buttons, clocks, players).</summary>
    public event EventHandler? Changed;

    /// <summary>The shown position changed: redraw the board from <see cref="Position"/>.</summary>
    public event EventHandler<BoardUpdate>? BoardChanged;

    /// <summary>Moves were added or taken back: redraw the move list.</summary>
    public event EventHandler? MovesChanged;

    public event EventHandler<GameNotice>? Notice;

    public event EventHandler<string>? ChatReceived;

    /// <summary>The opponent offers a draw: ask the user, then call <see cref="RespondToDraw"/>.</summary>
    public event EventHandler? DrawOfferReceived;

    public event EventHandler<GameOverSummary>? GameOver;

    /// <summary>The opponent just moved: a queued premove can be played (<see cref="PlayPremove"/>).</summary>
    public event EventHandler? OpponentMoved;

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Starts a game against a bot (or pass-and-play), optionally replaying saved moves and clock times.</summary>
    public void StartLocal(GameSetup setup, IReadOnlyList<string>? resumeMoves = null, TimeSpan? whiteTime = null, TimeSpan? blackTime = null)
    {
        EndSession();
        if (resumeMoves == null) _host.ClearUnfinished();
        var game = new Game(setup.StartFen);
        foreach (string uci in resumeMoves ?? [])
        {
            Move m = Core.Notation.Uci.Parse(game.Position, uci);
            if (m.IsNone || game.IsOver) break;
            game.Play(m);
        }
        var human = new PlayerInfo(_host.PlayerName, PlayerKind.LocalHuman) { Id = "me" };
        IMoveProvider? whiteBot = null, blackBot = null;
        PlayerInfo white, black;
        if (setup.Bot is BotProfile bp)
        {
            IMoveProvider bot = _createBot(bp);
            var botInfo = new PlayerInfo(bp.Name, PlayerKind.Bot, bp.Rating) { Id = bp.Id, Subtitle = bp.Tagline, AvatarKey = bp.Id };
            if (setup.HumanColor == Color.White)
            {
                (white, black, blackBot) = (human, botInfo, bot);
            }
            else
            {
                (white, black, whiteBot) = (botInfo, human, bot);
            }
        }
        else
        {
            white = new PlayerInfo("White", PlayerKind.LocalHuman) { Id = "white" };
            black = new PlayerInfo("Black", PlayerKind.LocalHuman) { Id = "black" };
        }

        game.Tags["Event"] = setup.IsHotSeat ? "Pass and play" : "Casual game vs computer";
        game.Tags["Site"] = $"{_host.AppName} for Windows";
        game.Tags["Date"] = DateTime.Now.ToString("yyyy.MM.dd");
        game.Tags["White"] = white.Name;
        game.Tags["Black"] = black.Name;
        if (white.Rating is int wr) game.Tags["WhiteElo"] = wr.ToString();
        if (black.Rating is int br) game.Tags["BlackElo"] = br.ToString();
        game.Tags["TimeControl"] = setup.TimeControl.PgnTag;

        var session = new LocalGameSession(game, white, black, whiteBot, blackBot, setup.TimeControl, setup.AllowTakebacks);
        if (session.Clock != null && whiteTime is TimeSpan wt && blackTime is TimeSpan bt) session.Clock.Set(wt, bt);
        Attach(session, setup, playStartSound: resumeMoves == null);
    }

    /// <summary>Continues the game saved when the app was last closed.</summary>
    public void Resume(SavedGame saved)
    {
        TimeSpan? white = saved.WhiteMs is double w ? TimeSpan.FromMilliseconds(w) : null;
        TimeSpan? black = saved.BlackMs is double b ? TimeSpan.FromMilliseconds(b) : null;
        StartLocal(saved.ToSetup(), saved.Moves, white, black);
        Notice?.Invoke(this, new GameNotice("Welcome back", "Your unfinished game has been restored."));
    }

    /// <summary>Shows an online game (played or watched): the session talks to the server.</summary>
    public void StartOnline(RemoteGameSession remote)
    {
        EndSession();
        var setup = new GameSetup(null, remote.LocalColor, remote.TimeControl, AllowTakebacks: false) { IsOnline = true, IsSpectating = remote.IsSpectator };
        Attach(remote, setup, playStartSound: remote.Game.Moves.Count == 0);
    }

    /// <summary>Follows <paramref name="session"/> (any kind) and starts it.</summary>
    public void Attach(IGameSession session, GameSetup setup, bool playStartSound)
    {
        EndSession();
        Setup = setup;
        Session = session;
        _recorded = false;
        _lowTimeWarned = false;
        ViewPly = -1;
        session.MovePlayed += Session_MovePlayed;
        session.GameEnded += Session_GameEnded;
        session.StateReset += Session_StateReset;
        session.ThinkingChanged += Session_ThinkingChanged;
        session.ChatReceived += Session_ChatReceived;
        session.DrawOfferAnswered += Session_DrawOfferAnswered;
        session.DrawOfferReceived += Session_DrawOfferReceived;
        if (session is RemoteGameSession remote) remote.RematchChanged += Remote_RematchChanged;

        GameStarted?.Invoke(this, EventArgs.Empty);
        if (playStartSound) _host.Play(GameCue.Start);
        session.Start();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops following the current session (declining any pending online rematch) and disposes it.</summary>
    public void EndSession()
    {
        if (Session is not IGameSession session) return;
        session.MovePlayed -= Session_MovePlayed;
        session.GameEnded -= Session_GameEnded;
        session.StateReset -= Session_StateReset;
        session.ThinkingChanged -= Session_ThinkingChanged;
        session.ChatReceived -= Session_ChatReceived;
        session.DrawOfferAnswered -= Session_DrawOfferAnswered;
        session.DrawOfferReceived -= Session_DrawOfferReceived;
        if (session is RemoteGameSession remote)
        {
            remote.RematchChanged -= Remote_RematchChanged;
            remote.DeclineRematch(); // moving on: answer or withdraw any pending rematch offer
        }
        session.Dispose();
        Session = null;
    }

    public void Dispose() => EndSession();

    // ------------------------------------------------------------------ state the page renders

    public bool IsGameOver => Session?.Game.IsOver == true;

    /// <summary>A game is on and not finished.</summary>
    public bool IsLive => Session != null && !Session.Game.IsOver;

    /// <summary>An unfinished game of the user's (not one they're watching).</summary>
    public bool HasUnfinishedGame => IsLive && Setup?.IsSpectating != true;

    /// <summary>Taking part in the game rather than watching it.</summary>
    public bool IsPlaying => Setup?.IsSpectating != true;

    public int MoveCount => Session?.Game.Moves.Count ?? 0;

    /// <summary>The ply on the board: the live one unless browsing history.</summary>
    public int DisplayedPly => ViewPly < 0 ? MoveCount : ViewPly;

    public bool IsBrowsingHistory => ViewPly >= 0;

    /// <summary>The position on the board.</summary>
    public Position? Position => Session is { } s ? (ViewPly < 0 ? s.Game.Position : s.Game.PositionAt(ViewPly)) : null;

    /// <summary>The move that led to <see cref="Position"/>.</summary>
    public Move LastMove => Session is { } s && DisplayedPly > 0 ? s.Game.Moves[DisplayedPly - 1].Move : Move.None;

    /// <summary>The board starts from the human's side (Black at the bottom when playing Black).</summary>
    public bool StartFlipped => Setup is { IsHotSeat: false, HumanColor: Color.Black };

    public MoveInput Input
    {
        get
        {
            if (Session == null || Session.Game.IsOver || ViewPly >= 0 || Setup == null || Setup.IsSpectating) return MoveInput.None;
            if (Setup.IsHotSeat) return MoveInput.Both;
            return Setup.HumanColor == Color.White ? MoveInput.White : MoveInput.Black;
        }
    }

    public bool AllowPremoves => Input != MoveInput.None && Setup is { IsHotSeat: false } && _host.PremovesEnabled;

    public string Status
    {
        get
        {
            if (Session == null) return "";
            Game g = Session.Game;
            if (g.IsOver) return g.ResultDescription;
            if (ViewPly >= 0) return "Viewing an earlier position";
            if (Session.IsOpponentThinking) return $"{OpponentName} is thinking…";
            if (Setup?.IsSpectating == true) return $"Watching · {(g.SideToMove == Color.White ? Session.White : Session.Black).Name} to move";
            if (Setup?.IsHotSeat == true) return $"{g.SideToMove.Name()} to move";
            return g.Position.InCheck ? "Check! Your move" : "Your move";
        }
    }

    /// <summary>"C20 · King's Pawn Game" for the shown position, or empty.</summary>
    public string OpeningName
    {
        get
        {
            if (Session == null) return "";
            Opening? o = OpeningBook.IdentifyAt(Session.Game, DisplayedPly);
            return o == null ? "" : $"{o.Eco} · {o.Name}";
        }
    }

    public string OpponentName
    {
        get
        {
            if (Setup?.Bot is BotProfile bot) return bot.Name;
            if (Setup?.IsOnline == true && Session != null) return (Setup.HumanColor == Color.White ? Session.Black : Session.White).Name;
            return "Your opponent";
        }
    }

    public bool ShowTakeback => Setup?.AllowTakebacks != false;

    public bool ShowDrawAndResign => IsPlaying;

    /// <summary>The in-game actions (undo, draw, resign); the post-game ones replace them.</summary>
    public bool ShowGameActions => !IsGameOver && (ShowTakeback || ShowDrawAndResign);

    public bool ShowPostGame => IsGameOver;

    public bool CanTakeback => IsLive && Session!.CanTakeback;

    public bool CanOfferDraw => IsLive && Session!.CanOfferDraw;

    public bool CanResign => IsLive && IsPlaying && Session!.Game.Moves.Count > 0;

    public bool CanHint => IsLive && Setup?.IsOnline != true && ViewPly < 0 && !Session!.IsOpponentThinking && Session.IsLocalSide(Session.Game.SideToMove);

    public bool CanGoBack => DisplayedPly > 0;

    public bool CanGoForward => DisplayedPly < MoveCount;

    public bool CanReview => MoveCount > 0;

    /// <summary>Whose point of view the review takes.</summary>
    public Color ReviewPerspective => Setup is { IsHotSeat: false } s ? s.HumanColor : Color.White;

    public bool ShowRematch => IsPlaying;

    /// <summary>The rematch button's text (online games follow the rematch handshake); null when watching.</summary>
    public string? RematchLabel => !IsPlaying ? null : (Session as RemoteGameSession)?.Rematch switch
    {
        RematchStatus.Offered => "Rematch offered…",
        RematchStatus.Received => "Accept rematch",
        _ => "Rematch",
    };

    public bool CanRematch => IsPlaying && (Session as RemoteGameSession)?.Rematch != RematchStatus.Offered;

    /// <summary>The online opponent asked for a rematch (the result dialog's button turns into "Accept rematch").</summary>
    public bool IsRematchRequested => (Session as RemoteGameSession)?.Rematch == RematchStatus.Received;

    public PlayerBadge Badge(Color side)
    {
        if (Session == null) return new PlayerBadge("", null, "", "#6B7A8F");
        PlayerInfo p = side == Color.White ? Session.White : Session.Black;
        if (p.Kind == PlayerKind.Bot)
        {
            BotProfile bp = BotRoster.Get(p.Id);
            return new PlayerBadge(bp.Name, bp.RatingText, bp.Monogram, bp.Color);
        }
        return new PlayerBadge(p.Name, p.Rating?.ToString(), Monogram(p.Name), side == Color.White ? "#6B7A8F" : "#2F3B4C");
    }

    public OpponentCard Opponent
    {
        get
        {
            if (Setup?.Bot is BotProfile bp) return new OpponentCard($"{bp.Name} ({bp.RatingText})", bp.Tagline, bp.Monogram, bp.Color);
            if (Setup?.IsOnline == true && Session != null)
            {
                PlayerInfo opp = Setup.HumanColor == Color.White ? Session.Black : Session.White;
                string tagline = Setup.IsSpectating
                    ? $"Watching {Session.White.Name} vs {Session.Black.Name} · {Setup.TimeControl.DisplayName}"
                    : $"Online · {Setup.TimeControl.DisplayName} · rated";
                return new OpponentCard(opp.Rating is int r ? $"{opp.Name} ({r})" : opp.Name, tagline, Monogram(opp.Name), "#0F6CBD");
            }
            return new OpponentCard("Pass and play", "Two players, one device. Take turns moving.", "⇄", "#5C6BC0");
        }
    }

    /// <summary>What <paramref name="by"/> has captured in the shown position, and its material lead.</summary>
    public Captures CapturesBy(Color by)
    {
        if (Position is not Position pos) return new Captures([], 0);
        int[] start = [0, 8, 2, 2, 2, 1, 0];
        var pieces = new List<Piece>();
        int mine = 0, theirs = 0;
        Color them = by.Opposite();
        for (int t = 1; t <= 5; t++)
        {
            var type = (PieceType)t;
            int ours = pos.Count(by, type), others = pos.Count(them, type);
            mine += ours * type.NominalValue();
            theirs += others * type.NominalValue();
            for (int i = others; i < start[t]; i++) pieces.Add(type.Of(them));
        }
        return new Captures(pieces, mine - theirs);
    }

    public TimeSpan? Remaining(Color side) => Session?.Clock?.Remaining(side);

    public bool IsClockRunning(Color side) => Session?.Clock?.Running == side;

    /// <summary>Called by the page's clock timer: warns once when the user's own time runs low.</summary>
    public void TickClock()
    {
        if (_lowTimeWarned || Session?.Clock is not ChessClock clock || Setup is not { IsHotSeat: false, IsSpectating: false } setup) return;
        if (clock.Running == setup.HumanColor && clock.Remaining(setup.HumanColor) < TimeSpan.FromSeconds(10))
        {
            _lowTimeWarned = true;
            _host.Play(GameCue.LowTime);
        }
    }

    // ------------------------------------------------------------------ commands

    /// <summary>A move made on the board. Ignored while browsing history.</summary>
    public bool SubmitMove(Move move) => Session != null && ViewPly < 0 && Session.TrySubmitMove(move);

    /// <summary>Plays a queued premove if it's legal now that it's the user's turn; otherwise it's dropped.</summary>
    public bool PlayPremove(int from, int to)
    {
        if (Session == null || Session.Game.IsOver || ViewPly >= 0 || !Session.IsLocalSide(Session.Game.SideToMove)) return false;
        Move move = Premoves.Resolve(Session.Game.Position, from, to);
        return !move.IsNone && Session.TrySubmitMove(move);
    }

    /// <summary>Shows ply <paramref name="ply"/> (clamped); the last one returns to the live position.</summary>
    public void ShowPly(int ply)
    {
        if (Session == null) return;
        int count = MoveCount;
        ply = Math.Clamp(ply, 0, count);
        int current = DisplayedPly;
        if (ply == current) return;
        ViewPly = ply == count ? -1 : ply;
        BoardChanged?.Invoke(this, new BoardUpdate(Animate: ply == current + 1, ClearPremove: ViewPly >= 0, ClearMarkers: false));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void StepPly(int delta) => ShowPly(DisplayedPly + delta);

    public void Takeback()
    {
        if (Session?.Takeback() == true) Notice?.Invoke(this, new GameNotice("Move taken back", "Your last move was undone."));
    }

    public void OfferDraw()
    {
        if (Session == null) return;
        Notice?.Invoke(this, new GameNotice("Draw offered", $"Waiting for {OpponentName}…"));
        Session.OfferDraw();
    }

    /// <summary>Resigns (the page asks for confirmation first, if the user wants that).</summary>
    public void Resign()
    {
        if (Session == null || Session.Game.IsOver) return;
        Session.Resign();
    }

    public void RespondToDraw(bool accept) => Session?.RespondToDraw(accept);

    /// <summary>Online: offers (or accepts) a rematch, and the server opens the new game. Local: starts the same game again.</summary>
    public void Rematch()
    {
        if (Session is RemoteGameSession remote) remote.OfferRematch();
        else if (Setup != null) StartLocal(Setup);
    }

    /// <summary>The engine's suggestion for the user's move, or null if the position changed meanwhile.</summary>
    public async Task<Move?> FindHintAsync()
    {
        if (!CanHint) return null;
        Position pos = Session!.Game.Position.Clone();
        ulong key = pos.Key;
        SearchResult result = await Task.Run(() => new Searcher(16).Search(pos,
            new SearchLimits { MaxDepth = 14, SoftTime = TimeSpan.FromMilliseconds(500), HardTime = TimeSpan.FromMilliseconds(900) }));
        if (Session == null || Session.Game.Position.Key != key || result.BestMove.IsNone) return null;
        return result.BestMove;
    }

    /// <summary>The result dialog's contents for the finished game (call after it ended).</summary>
    public GameOverSummary Summary(string? description = null)
    {
        if (Session == null || Setup == null) return new GameOverSummary("Game over", "", [], null);
        Game g = Session.Game;
        bool aborted = g.Termination == Termination.Aborted;
        string title = aborted ? "Game aborted"
            : Setup.IsNeutralView ? g.Winner switch { Color.White => "White wins", Color.Black => "Black wins", _ => "Draw" }
            : g.Winner == Setup.HumanColor ? "You won!" : g.Winner == null ? "Draw" : "You lost";
        description = aborted ? "The game ended before both players had moved, so it doesn't count." : (description ?? g.ResultDescription) + ".";
        var details = new List<string>();
        if (Setup.Bot is BotProfile bp && !aborted)
        {
            (int wins, int losses, int draws) = _host.RecordAgainst(bp.Id);
            details.Add($"Your record vs {bp.Name}: {wins} W · {losses} L · {draws} D");
            if (g.Winner == Setup.HumanColor && wins == 1) details.Add($"First win against {bp.Name}! Try the next bot up.");
        }
        if (Session is RemoteGameSession { IsSpectator: false } remote)
        {
            int? change = Setup.HumanColor == Color.White ? remote.RatingChanges.White : remote.RatingChanges.Black;
            if (change is int d) details.Add($"Rating change: {(d >= 0 ? "+" : "")}{d}");
        }
        return new GameOverSummary(title, description, details, RematchLabel);
    }

    // ------------------------------------------------------------------ session events

    private void Session_MovePlayed(object? sender, MovePlayedEventArgs e)
    {
        ViewPly = -1;
        _host.PlayMoveSound(e.Move);
        if (!Session!.Game.IsOver && Setup is { IsOnline: false }) _host.SaveUnfinished(SavedGame.From(Setup, Session.Game, Session.Clock));
        MovesChanged?.Invoke(this, EventArgs.Empty);
        BoardChanged?.Invoke(this, new BoardUpdate(Animate: true, ClearPremove: false, ClearMarkers: true));
        Changed?.Invoke(this, EventArgs.Empty);
        if (!e.ByLocalPlayer) OpponentMoved?.Invoke(this, EventArgs.Empty);
    }

    private void Session_GameEnded(object? sender, GameEndedEventArgs e)
    {
        Record();
        if (Setup?.IsOnline != true) _host.ClearUnfinished();
        bool won = Setup is { IsNeutralView: false } setup && Session?.Game.Winner == setup.HumanColor;
        _host.Play(won ? GameCue.Win : GameCue.End);
        Changed?.Invoke(this, EventArgs.Empty);
        GameOver?.Invoke(this, Summary(e.Description));
    }

    private void Session_StateReset(object? sender, EventArgs e)
    {
        ViewPly = -1;
        if (Setup is { IsOnline: false } && Session != null) _host.SaveUnfinished(SavedGame.From(Setup, Session.Game, Session.Clock));
        MovesChanged?.Invoke(this, EventArgs.Empty);
        BoardChanged?.Invoke(this, new BoardUpdate(Animate: false, ClearPremove: true, ClearMarkers: true));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Session_ThinkingChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void Session_ChatReceived(object? sender, ChatEventArgs e) => ChatReceived?.Invoke(this, e.Text);

    private void Session_DrawOfferReceived(object? sender, EventArgs e) => DrawOfferReceived?.Invoke(this, EventArgs.Empty);

    private void Session_DrawOfferAnswered(object? sender, bool accepted)
    {
        if (!accepted) Notice?.Invoke(this, new GameNotice("Draw declined", $"{OpponentName} wants to keep playing."));
    }

    private void Remote_RematchChanged(object? sender, EventArgs e)
    {
        if (sender is not RemoteGameSession remote || !ReferenceEquals(remote, Session)) return;
        string name = OpponentName;
        GameNotice? notice = remote.Rematch switch
        {
            RematchStatus.Offered => new GameNotice("Rematch offered", $"Waiting for {name}…"),
            RematchStatus.Received => new GameNotice("Rematch?", $"{name} wants a rematch.", NoticeKind.Success, "Accept", remote.OfferRematch),
            RematchStatus.Declined => new GameNotice("Rematch declined", $"{name} doesn't want a rematch right now."),
            RematchStatus.Unavailable => new GameNotice("No rematch", $"{name} is no longer available for a rematch."),
            _ => null,
        };
        if (notice != null) Notice?.Invoke(this, notice);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records the finished game in the profile, once; watched games aren't the user's.</summary>
    private void Record()
    {
        if (_recorded || Session == null || Setup == null || Setup.IsSpectating) return;
        _recorded = true;
        Game g = Session.Game;
        if (g.Moves.Count == 0) return;
        if (OpeningBook.Identify(g) is Opening opening)
        {
            g.Tags["ECO"] = opening.Eco;
            g.Tags["Opening"] = opening.Name;
        }
        PlayerInfo? onlineOpponent = Setup.IsOnline ? (Setup.HumanColor == Color.White ? Session.Black : Session.White) : null;
        _host.RecordFinished(new FinishedGame(g,
            Opponent: Setup.Bot?.Name ?? onlineOpponent?.Name ?? "Pass and play",
            Bot: Setup.Bot,
            OpponentRating: Setup.Bot?.Rating ?? onlineOpponent?.Rating,
            PlayerColor: Setup.IsHotSeat ? null : Setup.HumanColor,
            TimeControl: Setup.TimeControl));
    }

    private static string Monogram(string name) => name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
}

/// <summary>How to redraw the board after <see cref="GameViewModel.BoardChanged"/>.</summary>
public sealed record BoardUpdate(bool Animate, bool ClearPremove, bool ClearMarkers);
