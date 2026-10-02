namespace Gambit.Online;

/// <summary>Hub route and protocol version (bump when messages change incompatibly).</summary>
public static class OnlineProtocol
{
    public const string HubPath = "/hub/game";
    public const int Version = 1;
}

/// <summary>A player as seen by others.</summary>
public sealed record PlayerDto(string Id, string Name, int Rating, bool Provisional);

/// <summary>Time control in seconds (0/0 = unlimited is not offered online).</summary>
public sealed record TimeControlDto(int InitialSeconds, int IncrementSeconds)
{
    public string Key => $"{InitialSeconds / 60}+{IncrementSeconds}";
}

/// <summary>Sent to both players when a game starts (also when rejoining a running game).</summary>
public sealed record GameStartDto(
    string GameId,
    PlayerDto White,
    PlayerDto Black,
    string YourColor,          // "white" | "black"
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
}

/// <summary>Client → server calls (implemented by the hub).</summary>
public interface IGameServer
{
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
}
