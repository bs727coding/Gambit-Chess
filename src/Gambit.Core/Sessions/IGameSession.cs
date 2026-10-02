using Gambit.Core.Board;
using Gambit.Core.Games;

namespace Gambit.Core.Sessions;

public enum PlayerKind
{
    /// <summary>A human at this device.</summary>
    LocalHuman,

    /// <summary>A computer opponent running on this device.</summary>
    Bot,

    /// <summary>A human connected through the online server.</summary>
    Remote,
}

/// <summary>Display + identity information about one side of a game.</summary>
public sealed record PlayerInfo(string Name, PlayerKind Kind, int? Rating = null)
{
    /// <summary>Stable id (bot id, local profile id, or online user id).</summary>
    public string Id { get; init; } = Name;

    /// <summary>Short tagline under the name (bot description, title, country...).</summary>
    public string? Subtitle { get; init; }

    /// <summary>Key the UI uses to pick an avatar (bot avatars, profile picture id...).</summary>
    public string? AvatarKey { get; init; }

    public bool IsLocalHuman => Kind == PlayerKind.LocalHuman;
}

/// <summary>Everything a move provider (bot) needs to choose a move. The position is a private copy.</summary>
public sealed record GameSnapshot(Position Position, TimeSpan? OwnTime, TimeSpan? OpponentTime, TimeSpan Increment, int Ply);

/// <summary>Something that can choose moves for one side — a bot today, maybe a remote engine later.</summary>
public interface IMoveProvider
{
    /// <summary>Choose a move. Called on a background thread; honour cancellation promptly.</summary>
    Task<Move> ChooseMoveAsync(GameSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Respond to a draw offer. Default: decline.</summary>
    ValueTask<bool> ConsiderDrawOfferAsync(GameSnapshot snapshot, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);

    /// <summary>Optional short line the provider wants to "say" after a move (bot personality).</summary>
    string? Chat(GameEvent evt, Game game) => null;
}

public enum GameEvent
{
    GameStarted,
    MovePlayed,
    OpponentBlundered,
    GaveCheck,
    WonGame,
    LostGame,
    DrawnGame,
}

public sealed class MovePlayedEventArgs(GameMove move, bool byLocalPlayer) : EventArgs
{
    public GameMove Move { get; } = move;
    public bool ByLocalPlayer { get; } = byLocalPlayer;
}

public sealed class GameEndedEventArgs(GameResult result, Termination termination, string description) : EventArgs
{
    public GameResult Result { get; } = result;
    public Termination Termination { get; } = termination;
    public string Description { get; } = description;
}

public sealed class ChatEventArgs(PlayerInfo from, string text) : EventArgs
{
    public PlayerInfo From { get; } = from;
    public string Text { get; } = text;
}

/// <summary>
/// The single seam between the game UI and whoever is on the other side of the board. Local bot
/// games, hot-seat games and (later) online games all implement this, so the game page never needs
/// to know which one it is showing. Events are raised on the thread/sync-context that created the
/// session (the UI thread in the app).
/// </summary>
public interface IGameSession : IDisposable
{
    Game Game { get; }
    PlayerInfo White { get; }
    PlayerInfo Black { get; }

    /// <summary>Null for untimed games.</summary>
    ChessClock? Clock { get; }

    /// <summary>True while a bot/remote opponent is choosing a move.</summary>
    bool IsOpponentThinking { get; }

    /// <summary>Can the user of this device move pieces of <paramref name="side"/>?</summary>
    bool IsLocalSide(Color side);

    bool CanTakeback { get; }

    bool CanOfferDraw { get; }

    event EventHandler<MovePlayedEventArgs>? MovePlayed;
    event EventHandler<GameEndedEventArgs>? GameEnded;

    /// <summary>Moves were taken back or the state was replaced (resync) — redraw everything.</summary>
    event EventHandler? StateReset;

    event EventHandler? ThinkingChanged;
    event EventHandler<ChatEventArgs>? ChatReceived;

    /// <summary>A draw offer was declined (or accepted, in which case GameEnded follows).</summary>
    event EventHandler<bool>? DrawOfferAnswered;

    void Start();

    /// <summary>Submit a move for a local side. Returns false if it is not legal or not this side's turn.</summary>
    bool TrySubmitMove(Move move);

    /// <summary>The local player resigns (in hot-seat games: the side to move resigns).</summary>
    void Resign();

    void OfferDraw();

    /// <summary>Take back the last local move (and the opponent's reply in bot games).</summary>
    bool Takeback();
}
