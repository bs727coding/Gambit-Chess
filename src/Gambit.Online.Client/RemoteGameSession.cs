using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Sessions;

namespace Gambit.Online.Client;

/// <summary>Where a finished game's rematch negotiation stands.</summary>
public enum RematchStatus
{
    None,
    /// <summary>We asked; waiting for the opponent.</summary>
    Offered,
    /// <summary>The opponent asked; offering back accepts.</summary>
    Received,
    /// <summary>The opponent declined our offer.</summary>
    Declined,
    /// <summary>The opponent left, started another game, or withdrew their offer.</summary>
    Unavailable,
}

/// <summary>
/// An online game. Local moves are applied optimistically and sent to the server; the server is
/// authoritative — any disagreement triggers a resync from the server's move list. Server events
/// are marshalled to the synchronization context that created the session (the UI thread).
/// A spectator session (<see cref="IsSpectator"/>) only mirrors the game: it never moves or offers.
/// </summary>
public sealed class RemoteGameSession : IGameSession
{
    private readonly OnlineClient _client;
    private readonly SynchronizationContext? _sync;
    private readonly Color _me;
    private bool _finished;
    private bool _disposed;

    public RemoteGameSession(OnlineClient client, GameStartDto start)
    {
        _client = client;
        _sync = SynchronizationContext.Current;
        GameId = start.GameId;
        IsSpectator = start.YourColor == "spectator";
        _me = start.YourColor == "black" ? Color.Black : Color.White;
        TimeControl = new TimeControl(TimeSpan.FromSeconds(start.TimeControl.InitialSeconds), TimeSpan.FromSeconds(start.TimeControl.IncrementSeconds));
        Clock = new ChessClock(TimeControl);
        WhiteDto = start.White;
        BlackDto = start.Black;
        White = ToInfo(start.White, Color.White);
        Black = ToInfo(start.Black, Color.Black);
        Game = BuildGame(start);
        ApplyClock(start.Clock);

        _client.MovePlayed += OnMovePlayed;
        _client.GameOver += OnGameOver;
        _client.DrawOffered += OnDrawOffered;
        _client.DrawDeclined += OnDrawDeclined;
        _client.OpponentConnection += OnOpponentConnection;
        _client.Notice += OnNotice;
        _client.Resync += OnResync;
        _client.RematchOffered += OnRematchOffered;
        _client.RematchDeclined += OnRematchDeclined;
        _client.ChatMessage += OnChatMessage;
    }

    public string GameId { get; }
    public TimeControl TimeControl { get; }
    public PlayerDto WhiteDto { get; }
    public PlayerDto BlackDto { get; }

    /// <summary>The local player's colour (White for spectators, who watch from White's side).</summary>
    public Color LocalColor => _me;

    /// <summary>Watching someone else's game: no moves, offers or rematches.</summary>
    public bool IsSpectator { get; }

    public Game Game { get; private set; }
    public PlayerInfo White { get; }
    public PlayerInfo Black { get; }
    public ChessClock? Clock { get; }

    /// <summary>"Thinking" = waiting for the opponent's move.</summary>
    public bool IsOpponentThinking => !IsSpectator && !_finished && !Game.IsOver && Game.SideToMove != _me;

    public bool CanTakeback => false;
    public bool CanOfferDraw => !IsSpectator && !_finished && !Game.IsOver && Game.Moves.Count >= 2;

    /// <summary>Rating changes reported by the server when the game ended.</summary>
    public (int? White, int? Black) RatingChanges { get; private set; }

    /// <summary>Rematch negotiation after the game; the server starts the new game (a new session) once both agree.</summary>
    public RematchStatus Rematch { get; private set; }

    public event EventHandler? RematchChanged;

    public event EventHandler<MovePlayedEventArgs>? MovePlayed;
    public event EventHandler<GameEndedEventArgs>? GameEnded;
    public event EventHandler? StateReset;
    public event EventHandler? ThinkingChanged;
    public event EventHandler<ChatEventArgs>? ChatReceived;

    /// <summary>A chat line between the players (yours too, as the server confirms it).</summary>
    public event EventHandler<ChatDto>? PlayerChat;
    public event EventHandler<bool>? DrawOfferAnswered;
    public event EventHandler? DrawOfferReceived;

    public bool IsLocalSide(Color side) => !IsSpectator && side == _me;

    public void Start()
    {
        ThinkingChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TrySubmitMove(Move move)
    {
        if (IsSpectator || _finished || Game.IsOver || Game.SideToMove != _me || !Game.IsLegal(move)) return false;
        int ply = Game.Moves.Count + 1;
        GameMove gm = Game.Play(move);
        MovePlayed?.Invoke(this, new MovePlayedEventArgs(gm, byLocalPlayer: true));
        ThinkingChanged?.Invoke(this, EventArgs.Empty);
        _ = SendMoveAsync(ply, move.ToUci());
        return true;
    }

    private async Task SendMoveAsync(int ply, string uci)
    {
        try
        {
            if (!await _client.MakeMoveAsync(GameId, ply, uci)) await _client.RejoinAsync(GameId);
        }
        catch (Exception ex)
        {
            Post(() => ChatReceived?.Invoke(this, new ChatEventArgs(ServerVoice, $"Connection problem: {ex.Message}")));
        }
    }

    public void Resign()
    {
        if (!IsSpectator) Fire(() => _client.ResignAsync(GameId));
    }

    public void OfferDraw()
    {
        if (!IsSpectator) Fire(() => _client.OfferDrawAsync(GameId));
    }

    public void RespondToDraw(bool accept)
    {
        if (!IsSpectator) Fire(() => _client.RespondToDrawAsync(GameId, accept));
    }

    public bool Takeback() => false;

    /// <summary>Asks for a rematch, or accepts the opponent's request. Only after the game has ended.</summary>
    public void OfferRematch()
    {
        if (IsSpectator || !_finished || Rematch == RematchStatus.Offered) return;
        if (Rematch != RematchStatus.Received) SetRematch(RematchStatus.Offered);
        Fire(() => _client.OfferRematchAsync(GameId));
    }

    /// <summary>Declines the opponent's request, or withdraws our own.</summary>
    public void DeclineRematch()
    {
        if (Rematch is not (RematchStatus.Offered or RematchStatus.Received)) return;
        SetRematch(RematchStatus.None);
        Fire(() => _client.DeclineRematchAsync(GameId));
    }

    // ------------------------------------------------------------------ server events

    private void OnMovePlayed(MoveDto dto)
    {
        if (dto.GameId != GameId) return;
        Post(() =>
        {
            ApplyClock(dto.Clock);
            if (dto.Ply == Game.Moves.Count && Game.Moves[^1].Uci == dto.Uci) return; // our own move, confirmed
            if (dto.Ply != Game.Moves.Count + 1)
            {
                _ = _client.RejoinAsync(GameId);
                return;
            }
            Move m = Uci.Parse(Game.Position, dto.Uci);
            if (m.IsNone)
            {
                _ = _client.RejoinAsync(GameId);
                return;
            }
            GameMove gm = Game.Play(m);
            MovePlayed?.Invoke(this, new MovePlayedEventArgs(gm, byLocalPlayer: false));
            ThinkingChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnGameOver(GameOverDto dto)
    {
        if (dto.GameId != GameId) return;
        Post(() =>
        {
            if (_finished) return;
            _finished = true;
            Clock?.Stop();
            RatingChanges = (dto.WhiteRatingChange, dto.BlackRatingChange);
            if (!Game.IsOver) ApplyResult(dto);
            ThinkingChanged?.Invoke(this, EventArgs.Empty);
            GameEnded?.Invoke(this, new GameEndedEventArgs(Game.Result, Game.Termination, dto.Description));
        });
    }

    private void OnDrawOffered(string gameId, string byColor)
    {
        if (gameId != GameId || IsSpectator) return;
        bool mine = byColor == (_me == Color.White ? "white" : "black");
        if (!mine) Post(() => DrawOfferReceived?.Invoke(this, EventArgs.Empty));
    }

    private void OnDrawDeclined(string gameId)
    {
        if (gameId == GameId && !IsSpectator) Post(() => DrawOfferAnswered?.Invoke(this, false));
    }

    private void OnOpponentConnection(string gameId, bool connected, int graceSeconds)
    {
        if (gameId != GameId) return;
        string who = IsSpectator ? "A player" : "Your opponent";
        Post(() => ChatReceived?.Invoke(this, new ChatEventArgs(ServerVoice, connected
            ? $"{who} is connected."
            : $"{who} disconnected and has {graceSeconds} seconds to come back.")));
    }

    /// <summary>Server announcements (e.g. "signed in somewhere else") show up like the server's other game messages.</summary>
    private void OnNotice(string message) => Post(() => ChatReceived?.Invoke(this, new ChatEventArgs(ServerVoice, message)));

    private void OnChatMessage(ChatDto message)
    {
        if (message.GameId == GameId) Post(() => PlayerChat?.Invoke(this, message));
    }

    /// <summary>Sends a chat line to the other player. Throws with the server's reason (too fast, not your game).</summary>
    public Task SendChatAsync(string text) => IsSpectator ? Task.CompletedTask : _client.SendChatAsync(GameId, text);

    private void OnResync(GameStartDto dto)
    {
        if (dto.GameId != GameId) return;
        Post(() =>
        {
            Game = BuildGame(dto);
            ApplyClock(dto.Clock);
            StateReset?.Invoke(this, EventArgs.Empty);
            ThinkingChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnRematchOffered(string gameId)
    {
        if (gameId == GameId) Post(() => SetRematch(RematchStatus.Received));
    }

    private void OnRematchDeclined(string gameId, bool unavailable)
    {
        if (gameId != GameId) return;
        Post(() => SetRematch(Rematch == RematchStatus.Offered && !unavailable ? RematchStatus.Declined : RematchStatus.Unavailable));
    }

    private void SetRematch(RematchStatus status)
    {
        if (Rematch == status) return;
        Rematch = status;
        RematchChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ helpers

    private static readonly PlayerInfo ServerVoice = new("Server", PlayerKind.Remote) { Id = "server" };

    private PlayerInfo ToInfo(PlayerDto dto, Color side) =>
        new(dto.Name, IsLocalSide(side) ? PlayerKind.LocalHuman : PlayerKind.Remote, dto.Rating)
        {
            Id = dto.Id,
            Subtitle = dto.Provisional ? $"{dto.Rating}? (provisional)" : dto.Rating.ToString(),
        };

    private Game BuildGame(GameStartDto start)
    {
        var game = new Game(start.StartFen);
        foreach (string uci in start.Moves)
        {
            Move m = Uci.Parse(game.Position, uci);
            if (m.IsNone || game.IsOver) break;
            game.Play(m);
        }
        game.Tags["Event"] = "Gambit online game";
        game.Tags["White"] = start.White.Name;
        game.Tags["Black"] = start.Black.Name;
        game.Tags["WhiteElo"] = start.White.Rating.ToString();
        game.Tags["BlackElo"] = start.Black.Rating.ToString();
        game.Tags["TimeControl"] = $"{start.TimeControl.InitialSeconds}+{start.TimeControl.IncrementSeconds}";
        return game;
    }

    private void ApplyClock(ClockDto c)
    {
        if (Clock == null) return;
        // Account for the time the message spent in transit.
        long latency = Math.Clamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - c.ServerTimeMs, 0, 2000);
        long white = c.WhiteMs - (c.Running == "white" ? latency : 0);
        long black = c.BlackMs - (c.Running == "black" ? latency : 0);
        Clock.Stop();
        Clock.Set(TimeSpan.FromMilliseconds(Math.Max(0, white)), TimeSpan.FromMilliseconds(Math.Max(0, black)));
        if (c.Running == "white") Clock.Start(Color.White);
        else if (c.Running == "black") Clock.Start(Color.Black);
    }

    private void ApplyResult(GameOverDto dto)
    {
        Color? winner = dto.Result switch { "1-0" => Color.White, "0-1" => Color.Black, _ => null };
        switch (dto.Termination)
        {
            case nameof(Termination.Resignation) when winner is Color w:
                Game.Resign(w.Opposite());
                break;
            case nameof(Termination.Timeout) or nameof(Termination.TimeoutVsInsufficientMaterial):
                Game.Timeout(winner?.Opposite() ?? Game.SideToMove);
                break;
            case nameof(Termination.Abandoned) when winner is Color w:
                Game.Abandon(w.Opposite());
                break;
            case nameof(Termination.Aborted):
                Game.Abort();
                break;
            default:
                if (winner is Color win) Game.Resign(win.Opposite());
                else Game.AgreeDraw();
                break;
        }
    }

    private void Fire(Func<Task> call)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await call();
            }
            catch (Exception ex)
            {
                Post(() => ChatReceived?.Invoke(this, new ChatEventArgs(ServerVoice, $"Connection problem: {ex.Message}")));
            }
        });
    }

    private void Post(Action action)
    {
        if (_disposed) return;
        if (_sync != null) _sync.Post(_ => { if (!_disposed) action(); }, null);
        else action();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsSpectator) Fire(() => _client.UnwatchAsync(GameId));
        _client.MovePlayed -= OnMovePlayed;
        _client.GameOver -= OnGameOver;
        _client.DrawOffered -= OnDrawOffered;
        _client.DrawDeclined -= OnDrawDeclined;
        _client.OpponentConnection -= OnOpponentConnection;
        _client.Notice -= OnNotice;
        _client.Resync -= OnResync;
        _client.RematchOffered -= OnRematchOffered;
        _client.RematchDeclined -= OnRematchDeclined;
        _client.ChatMessage -= OnChatMessage;
    }
}
