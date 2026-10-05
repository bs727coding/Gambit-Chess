namespace Gambit.Online;

/// <summary>Hub route and protocol version (bump when messages change incompatibly).</summary>
public static class OnlineProtocol
{
    public const string HubPath = "/hub/game";

    /// <summary>2: accounts (sign-in, invites) replaced guest tokens. 3: friends, direct challenges, chat.</summary>
    public const int Version = 3;
}

/// <summary>A player as seen by others.</summary>
public sealed record PlayerDto(string Id, string Name, int Rating, bool Provisional);

/// <summary>Time control in seconds (0/0 = unlimited is not offered online).</summary>
public sealed record TimeControlDto(int InitialSeconds, int IncrementSeconds)
{
    public string Key => $"{InitialSeconds / 60}+{IncrementSeconds}";
}

/// <summary>Sent to both players when a game starts (also when rejoining a running game), and to spectators.</summary>
public sealed record GameStartDto(
    string GameId,
    PlayerDto White,
    PlayerDto Black,
    string YourColor,          // "white" | "black" | "spectator"
    TimeControlDto TimeControl,
    string StartFen,
    IReadOnlyList<string> Moves,
    ClockDto Clock);

/// <summary>Remaining clock times (ms) and whose clock is running; ServerTime lets clients correct for latency.</summary>
public sealed record ClockDto(long WhiteMs, long BlackMs, string? Running, long ServerTimeMs);

/// <summary>A move accepted by the server. Ply is 1-based (the n-th half-move of the game).</summary>
public sealed record MoveDto(string GameId, int Ply, string Uci, string San, ClockDto Clock);

public sealed record GameOverDto(string GameId, string Result, string Termination, string Description,
    int? WhiteRatingChange, int? BlackRatingChange);

public sealed record ChallengeDto(string Code, TimeControlDto TimeControl, string Color, PlayerDto Creator);

public sealed record LobbyStatsDto(int PlayersOnline, int GamesInProgress, IReadOnlyDictionary<string, int> Seeking);

/// <summary>A game in progress that can be watched.</summary>
public sealed record LiveGameDto(string GameId, PlayerDto White, PlayerDto Black, TimeControlDto TimeControl, int Plies, int Spectators);

/// <summary>A chat line between the two players of a game (offensive words already masked by the server).</summary>
public sealed record ChatDto(string GameId, string From, string Text, DateTimeOffset At);

/// <summary>Server → client calls.</summary>
public interface IGameClient
{
    Task Welcome(PlayerDto me, LobbyStatsDto stats);
    Task GameStarted(GameStartDto game);
    Task MovePlayed(MoveDto move);
    Task GameOver(GameOverDto result);
    Task DrawOffered(string gameId, string byColor);
    Task DrawDeclined(string gameId);
    Task OpponentConnection(string gameId, bool connected, int graceSeconds);
    Task Resync(GameStartDto game);
    Task Notice(string message);

    /// <summary>The opponent of a finished game wants a rematch.</summary>
    Task RematchOffered(string gameId);

    /// <summary>A rematch offer was declined or withdrawn; <paramref name="unavailable"/> = the other player left or is busy.</summary>
    Task RematchDeclined(string gameId, bool unavailable);

    /// <summary>The friend list changed (a request, an answer, a friend came online or started a game): fetch it again.</summary>
    Task FriendsChanged();

    /// <summary>A friend challenges you; accept with AcceptChallenge(code), or DeclineChallenge(code).</summary>
    Task ChallengeReceived(ChallengeDto challenge);

    /// <summary>The friend you challenged declined (or couldn't play).</summary>
    Task ChallengeDeclined(string code, string byName);

    Task ChatMessage(ChatDto message);
}

/// <summary>Client → server calls (implemented by the hub).</summary>
public interface IGameServer
{
    /// <summary>First call on a connection. The player is the signed-in account; <paramref name="name"/> is ignored.</summary>
    Task<PlayerDto> Hello(string name, int protocolVersion);
    Task Seek(TimeControlDto timeControl);
    Task CancelSeek();
    Task<ChallengeDto> CreateChallenge(TimeControlDto timeControl, string color);
    Task<bool> AcceptChallenge(string code);
    Task<bool> MakeMove(string gameId, int ply, string uci);
    Task Resign(string gameId);
    Task OfferDraw(string gameId);
    Task RespondToDraw(string gameId, bool accept);
    Task<bool> Rejoin(string gameId);

    /// <summary>Offers a rematch of a finished game, or accepts the opponent's offer (colors swap).</summary>
    Task OfferRematch(string gameId);

    /// <summary>Declines the opponent's rematch offer, or withdraws one's own.</summary>
    Task DeclineRematch(string gameId);

    /// <summary>Games in progress, most-watched first.</summary>
    Task<IReadOnlyList<LiveGameDto>> ListGames();

    /// <summary>Starts watching a game: returns its state (YourColor = "spectator") and streams its moves; null if it isn't running.</summary>
    Task<GameStartDto?> Watch(string gameId);

    Task Unwatch(string gameId);

    /// <summary>Challenges a friend who is online: they are asked straight away (no code to share).</summary>
    Task<ChallengeDto> ChallengeFriend(string username, TimeControlDto timeControl, string color);

    /// <summary>Turns down a friend's challenge.</summary>
    Task DeclineChallenge(string code);

    /// <summary>Sends a chat line to the other player of a game you play in.</summary>
    Task SendChat(string gameId, string text);
}
