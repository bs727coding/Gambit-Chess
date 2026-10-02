using Gambit.Core.Board;
using Gambit.Core.Games;

namespace Gambit.Core.Sessions;

/// <summary>
/// A game played entirely on this device: human vs bot, human vs human (hot-seat), or bot vs bot.
/// Bots think on the thread pool against a cloned position; results are marshalled back to the
/// synchronization context that created the session, so <see cref="Game"/> is only ever mutated
/// on that thread.
/// </summary>
public sealed class LocalGameSession : IGameSession
{
    private readonly SynchronizationContext? _sync;
    private readonly IMoveProvider? _whiteBot;
    private readonly IMoveProvider? _blackBot;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private CancellationTokenSource? _thinkCts;
    private ITimer? _flagTimer;
    private int _generation;
    private bool _started;
    private bool _finished;
    private bool _disposed;

    public LocalGameSession(
        Game game,
        PlayerInfo white,
        PlayerInfo black,
        IMoveProvider? whiteBot,
        IMoveProvider? blackBot,
        TimeControl timeControl = default,
        bool allowTakebacks = true,
        TimeProvider? time = null)
    {
        Game = game;
        White = white;
        Black = black;
        _whiteBot = whiteBot;
        _blackBot = blackBot;
        _time = time ?? TimeProvider.System;
        AllowTakebacks = allowTakebacks;
        Clock = timeControl.IsUnlimited ? null : new ChessClock(timeControl, _time);
        _sync = SynchronizationContext.Current;
    }

    public Game Game { get; }
    public PlayerInfo White { get; }
    public PlayerInfo Black { get; }
    public ChessClock? Clock { get; }
    public bool AllowTakebacks { get; }
    public bool IsOpponentThinking { get; private set; }

    public bool CanTakeback => AllowTakebacks && !_finished && Game.Moves.Count > 0 && LocalSideHasMoved();

    public bool CanOfferDraw => !Game.IsOver && (_whiteBot != null || _blackBot != null) && Game.Moves.Count >= 2;

    public event EventHandler<MovePlayedEventArgs>? MovePlayed;
    public event EventHandler<GameEndedEventArgs>? GameEnded;
    public event EventHandler? StateReset;
    public event EventHandler? ThinkingChanged;
    public event EventHandler<ChatEventArgs>? ChatReceived;
    public event EventHandler<bool>? DrawOfferAnswered;

    public bool IsLocalSide(Color side) => ProviderFor(side) == null;

    public IMoveProvider? ProviderFor(Color side) => side == Color.White ? _whiteBot : _blackBot;

    public PlayerInfo PlayerFor(Color side) => side == Color.White ? White : Black;

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        if (Game.IsOver)
        {
            FinishGame();
            return;
        }

        foreach (Color side in new[] { Color.White, Color.Black })
            Say(side, GameEvent.GameStarted);

        if (Clock != null)
        {
            Clock.Start(Game.SideToMove);
            _flagTimer = _time.CreateTimer(_ => Post(CheckFlag), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        }

        RequestBotMoveIfNeeded();
    }

    public bool TrySubmitMove(Move move)
    {
        if (!_started || _finished || Game.IsOver) return false;
        if (!IsLocalSide(Game.SideToMove) || !Game.IsLegal(move)) return false;
        ApplyMove(move, byLocal: true);
        return true;
    }

    public void Resign()
    {
        if (_finished || Game.IsOver) return;
        Color loser = HumanColors() switch
        {
            [var only] => only,
            _ => Game.SideToMove,
        };
        Game.Resign(loser);
        FinishGame();
    }

    public void OfferDraw()
    {
        if (_finished || Game.IsOver) return;

        Color opponent = HumanColors() is [var human] ? human.Opposite() : Game.SideToMove.Opposite();
        IMoveProvider? bot = ProviderFor(opponent);
        if (bot == null)
        {
            // Both players are at this device: an offer is an agreement.
            Game.AgreeDraw();
            DrawOfferAnswered?.Invoke(this, true);
            FinishGame();
            return;
        }

        int gen = _generation;
        GameSnapshot snapshot = MakeSnapshot(opponent);
        Task.Run(async () =>
        {
            bool accept;
            try
            {
                accept = await bot.ConsiderDrawOfferAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                accept = false;
            }
            Post(() =>
            {
                if (_finished || Game.IsOver) return;
                if (accept && gen == _generation)
                {
                    CancelThinking();
                    Game.AgreeDraw();
                    DrawOfferAnswered?.Invoke(this, true);
                    FinishGame();
                }
                else
                {
                    DrawOfferAnswered?.Invoke(this, false);
                }
            });
        });
    }

    public bool Takeback()
    {
        if (!CanTakeback) return false;
        CancelThinking();

        // Undo until it is a local human's turn again (at least one ply).
        bool undone = Game.Undo();
        while (undone && Game.Moves.Count > 0 && !IsLocalSide(Game.SideToMove)) Game.Undo();
        _generation++;
        if (Clock != null) Clock.Start(Game.SideToMove);

        StateReset?.Invoke(this, EventArgs.Empty);
        RequestBotMoveIfNeeded();
        return undone;
    }

    // ------------------------------------------------------------------ internals

    private void ApplyMove(Move move, bool byLocal)
    {
        Color mover = Game.SideToMove;
        TimeSpan? think = null, clockAfter = null;
        if (Clock != null)
        {
            think = Clock.Switch();
            clockAfter = Clock.Remaining(mover);
        }

        GameMove gm = Game.Play(move, clockAfter, think);
        _generation++;
        MovePlayed?.Invoke(this, new MovePlayedEventArgs(gm, byLocal));

        if (!byLocal) Say(mover, gm.GivesCheck ? GameEvent.GaveCheck : GameEvent.MovePlayed);

        if (Game.IsOver)
        {
            FinishGame();
            return;
        }
        RequestBotMoveIfNeeded();
    }

    private void RequestBotMoveIfNeeded()
    {
        if (_finished || Game.IsOver || _disposed) return;
        Color side = Game.SideToMove;
        IMoveProvider? bot = ProviderFor(side);
        if (bot == null) return;

        CancelThinking();
        var cts = new CancellationTokenSource();
        _thinkCts = cts;
        int gen = _generation;
        GameSnapshot snapshot = MakeSnapshot(side);
        SetThinking(true);

        Task.Run(async () =>
        {
            Move move;
            try
            {
                move = await bot.ChooseMoveAsync(snapshot, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                move = Move.None;
            }

            Post(() =>
            {
                if (cts.IsCancellationRequested || gen != _generation || _finished || Game.IsOver) return;
                SetThinking(false);
                if (!Game.IsLegal(move)) move = Game.LegalMoves[0];
                ApplyMove(move, byLocal: false);
            });
        }, cts.Token);
    }

    private GameSnapshot MakeSnapshot(Color side) => new(
        Game.Position.Clone(),
        Clock?.Remaining(side),
        Clock?.Remaining(side.Opposite()),
        Clock?.Control.Increment ?? TimeSpan.Zero,
        Game.Moves.Count);

    private void CheckFlag()
    {
        if (_finished || Clock?.Running is not Color side || !Clock.IsFlagged(side)) return;
        CancelThinking();
        Game.Timeout(side);
        FinishGame();
    }

    private void FinishGame()
    {
        if (_finished) return;
        _finished = true;
        Clock?.Stop();
        _flagTimer?.Dispose();
        _flagTimer = null;
        CancelThinking();

        foreach (Color side in new[] { Color.White, Color.Black })
        {
            if (ProviderFor(side) == null) continue;
            GameEvent evt = Game.Winner == side ? GameEvent.WonGame
                : Game.Winner == side.Opposite() ? GameEvent.LostGame
                : GameEvent.DrawnGame;
            Say(side, evt);
        }

        GameEnded?.Invoke(this, new GameEndedEventArgs(Game.Result, Game.Termination, Game.ResultDescription));
    }

    private void Say(Color side, GameEvent evt)
    {
        IMoveProvider? bot = ProviderFor(side);
        string? text = bot?.Chat(evt, Game);
        if (!string.IsNullOrWhiteSpace(text)) ChatReceived?.Invoke(this, new ChatEventArgs(PlayerFor(side), text));
    }

    private void CancelThinking()
    {
        _thinkCts?.Cancel();
        _thinkCts = null;
        SetThinking(false);
    }

    private void SetThinking(bool value)
    {
        if (IsOpponentThinking == value) return;
        IsOpponentThinking = value;
        ThinkingChanged?.Invoke(this, EventArgs.Empty);
    }

    private Color[] HumanColors() =>
        new[] { Color.White, Color.Black }.Where(IsLocalSide).ToArray();

    private bool LocalSideHasMoved() =>
        Game.Moves.Any(m => IsLocalSide(m.Side));

    private void Post(Action action)
    {
        if (_disposed) return;
        if (_sync != null)
        {
            _sync.Post(_ =>
            {
                if (!_disposed) action();
            }, null);
        }
        else
        {
            lock (_gate)
            {
                if (!_disposed) action();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CancelThinking();
        _disposed = true;
        _flagTimer?.Dispose();
        _flagTimer = null;
    }
}
