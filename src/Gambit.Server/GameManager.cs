using System.Collections.Concurrent;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Online;
using Microsoft.AspNetCore.SignalR;

namespace Gambit.Server;

/// <summary>One authoritative game between two players. Mutate only while holding <see cref="Gate"/>.</summary>
public sealed class GameRoom(string id, Player white, Player black, TimeControlDto timeControl)
{
    public object Gate { get; } = new();
    public string Id { get; } = id;
    public Player White { get; } = white;
    public Player Black { get; } = black;
    public TimeControlDto TimeControl { get; } = timeControl;
    public Game Game { get; } = new() { AutoDrawRules = true };
    public ChessClock Clock { get; } = new(new TimeControl(TimeSpan.FromSeconds(timeControl.InitialSeconds), TimeSpan.FromSeconds(timeControl.IncrementSeconds)));
    public TimeCategory Category => Clock.Control.Category;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? WhiteGoneSince { get; set; }
    public DateTimeOffset? BlackGoneSince { get; set; }
    public string? DrawOfferBy { get; set; }
    public bool Finished { get; set; }

    public Color? ColorOf(Player p) => ReferenceEquals(p, White) ? Color.White : ReferenceEquals(p, Black) ? Color.Black : null;
    public Player PlayerOf(Color c) => c == Color.White ? White : Black;

    public ClockDto ClockDto() => new(
        (long)Clock.Remaining(Color.White).TotalMilliseconds,
        (long)Clock.Remaining(Color.Black).TotalMilliseconds,
        Clock.Running switch { Color.White => "white", Color.Black => "black", _ => null },
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

/// <summary>
/// Owns rooms, matchmaking and challenges. Every move is validated with Gambit.Core before it is
/// broadcast; clocks, flags, aborts and abandonment are decided here, never by clients.
/// </summary>
public sealed class GameManager(IHubContext<GameHub, IGameClient> hub, PlayerRegistry players, RatingStore ratings, ServerOptions options, ILogger<GameManager> log)
{
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly ConcurrentDictionary<string, string> _activeRoomByPlayer = new();
    private readonly ConcurrentDictionary<string, (Player Creator, TimeControlDto Tc, string Color, DateTimeOffset At)> _challenges = new();
    private readonly object _seekGate = new();
    private readonly List<(Player Player, TimeControlDto Tc)> _seeks = [];

    public int GamesInProgress => _rooms.Values.Count(r => !r.Finished);

    public LobbyStatsDto Stats()
    {
        lock (_seekGate)
        {
            return new LobbyStatsDto(players.OnlineCount, GamesInProgress,
                _seeks.GroupBy(s => s.Tc.Key).ToDictionary(g => g.Key, g => g.Count()));
        }
    }

    public static bool IsValid(TimeControlDto tc) => tc.InitialSeconds is >= 30 and <= 10800 && tc.IncrementSeconds is >= 0 and <= 180;

    // ------------------------------------------------------------------ matchmaking

    public async Task SeekAsync(Player p, TimeControlDto tc)
    {
        if (!IsValid(tc) || ActiveRoom(p) != null) return;
        Player? opponent = null;
        lock (_seekGate)
        {
            _seeks.RemoveAll(s => ReferenceEquals(s.Player, p));
            int i = _seeks.FindIndex(s => s.Tc == tc && !ReferenceEquals(s.Player, p) && s.Player.Connected);
            if (i >= 0)
            {
                opponent = _seeks[i].Player;
                _seeks.RemoveAt(i);
            }
            else
            {
                _seeks.Add((p, tc));
            }
        }
        if (opponent != null) await StartGameAsync(opponent, p, tc, "random");
    }

    public void CancelSeek(Player p)
    {
        lock (_seekGate) _seeks.RemoveAll(s => ReferenceEquals(s.Player, p));
    }

    public ChallengeDto CreateChallenge(Player p, TimeControlDto tc, string color)
    {
        if (!IsValid(tc)) throw new HubException("Invalid time control.");
        string code;
        do code = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3));
        while (_challenges.ContainsKey(code));
        color = color is "white" or "black" ? color : "random";
        _challenges[code] = (p, tc, color, DateTimeOffset.UtcNow);
        return new ChallengeDto(code, tc, color, players.ToDto(p, Category(tc)));
    }

    public async Task<bool> AcceptChallengeAsync(Player p, string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!_challenges.TryRemove(code, out var ch)) return false;
        if (ReferenceEquals(ch.Creator, p) || !ch.Creator.Connected) return false;
        string creatorColor = ch.Color;
        await StartGameAsync(ch.Creator, p, ch.Tc, creatorColor);
        return true;
    }

    private async Task StartGameAsync(Player first, Player second, TimeControlDto tc, string firstColor)
    {
        bool firstIsWhite = firstColor switch
        {
            "white" => true,
            "black" => false,
            _ => Random.Shared.Next(2) == 0,
        };
        Player white = firstIsWhite ? first : second, black = firstIsWhite ? second : first;
        var room = new GameRoom(Guid.NewGuid().ToString("N")[..12], white, black, tc);
        room.Game.Tags["Event"] = "Gambit online game";
        room.Game.Tags["Site"] = "Gambit server";
        room.Game.Tags["White"] = white.Name;
        room.Game.Tags["Black"] = black.Name;
        room.Game.Tags["TimeControl"] = $"{tc.InitialSeconds}+{tc.IncrementSeconds}";
        _rooms[room.Id] = room;
        _activeRoomByPlayer[white.PublicId] = room.Id;
        _activeRoomByPlayer[black.PublicId] = room.Id;
        log.LogInformation("Game {Id}: {White} vs {Black} ({Tc})", room.Id, white.Name, black.Name, tc.Key);

        foreach (Player pl in new[] { white, black })
        {
            if (pl.ConnectionId is not string conn) continue;
            await hub.Groups.AddToGroupAsync(conn, room.Id);
            await hub.Clients.Client(conn).GameStarted(StartDto(room, pl));
        }
    }

    // ------------------------------------------------------------------ in-game actions

    public async Task<bool> MakeMoveAsync(Player p, string gameId, int ply, string uci)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room)) return false;
        MoveDto dto;
        bool ended;
        lock (room.Gate)
        {
            if (room.Finished || room.Game.IsOver) return false;
            Color? color = room.ColorOf(p);
            if (color != room.Game.SideToMove) return false;
            if (ply != room.Game.Moves.Count + 1) return false;
            Move move = Uci.Parse(room.Game.Position, uci);
            if (move.IsNone) return false;

            if (room.Clock.Running == color && room.Clock.IsFlagged(color.Value))
            {
                // Out of time before the move arrived.
                room.Game.Timeout(color.Value);
                dto = null!;
                ended = true;
            }
            else
            {
                // The clock starts with Black's clock after White's first move.
                TimeSpan? think = null;
                if (room.Clock.Running == null) room.Clock.Start(color.Value.Opposite());
                else think = room.Clock.Switch();
                GameMove gm = room.Game.Play(move, room.Clock.Remaining(color.Value), think);
                room.DrawOfferBy = null;
                dto = new MoveDto(room.Id, gm.Ply, gm.Uci, gm.San, room.ClockDto());
                ended = room.Game.IsOver;
            }
        }

        if (dto != null) await hub.Clients.Group(room.Id).MovePlayed(dto);
        if (ended) await FinishAsync(room);
        return dto != null;
    }

    public async Task ResignAsync(Player p, string gameId)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room)) return;
        lock (room.Gate)
        {
            if (room.Finished || room.Game.IsOver || room.ColorOf(p) is not Color c) return;
            if (room.Game.Moves.Count < 2) room.Game.Abort();
            else room.Game.Resign(c);
        }
        await FinishAsync(room);
    }

    public async Task OfferDrawAsync(Player p, string gameId)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room)) return;
        string? by;
        lock (room.Gate)
        {
            if (room.Finished || room.Game.IsOver || room.ColorOf(p) is not Color c) return;
            by = c == Color.White ? "white" : "black";
            if (room.DrawOfferBy == by) return;
            room.DrawOfferBy = by;
        }
        await hub.Clients.Group(room.Id).DrawOffered(room.Id, by);
    }

    public async Task RespondToDrawAsync(Player p, string gameId, bool accept)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room)) return;
        bool agreed = false;
        lock (room.Gate)
        {
            if (room.Finished || room.Game.IsOver || room.ColorOf(p) is not Color c || room.DrawOfferBy == null) return;
            string me = c == Color.White ? "white" : "black";
            if (room.DrawOfferBy == me) return;
            if (accept)
            {
                room.Game.AgreeDraw();
                agreed = true;
            }
            room.DrawOfferBy = null;
        }
        if (agreed) await FinishAsync(room);
        else await hub.Clients.Group(room.Id).DrawDeclined(room.Id);
    }

    public async Task<bool> RejoinAsync(Player p, string gameId)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room) || room.ColorOf(p) == null || p.ConnectionId is not string conn) return false;
        await hub.Groups.AddToGroupAsync(conn, room.Id);
        await hub.Clients.Client(conn).Resync(StartDto(room, p));
        return true;
    }

    // ------------------------------------------------------------------ connections

    public async Task OnConnectedAsync(Player p)
    {
        if (ActiveRoom(p) is not GameRoom room) return;
        lock (room.Gate)
        {
            if (ReferenceEquals(p, room.White)) room.WhiteGoneSince = null;
            else room.BlackGoneSince = null;
        }
        await RejoinAsync(p, room.Id);
        await hub.Clients.Group(room.Id).OpponentConnection(room.Id, true, 0);
    }

    public async Task OnDisconnectedAsync(Player p)
    {
        CancelSeek(p);
        if (ActiveRoom(p) is not GameRoom room) return;
        lock (room.Gate)
        {
            if (ReferenceEquals(p, room.White)) room.WhiteGoneSince = DateTimeOffset.UtcNow;
            else room.BlackGoneSince = DateTimeOffset.UtcNow;
        }
        await hub.Clients.Group(room.Id).OpponentConnection(room.Id, false, (int)options.ReconnectGrace.TotalSeconds);
    }

    // ------------------------------------------------------------------ periodic checks

    /// <summary>Flags, first-move aborts and abandonment. Called a few times per second.</summary>
    public async Task TickAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (GameRoom room in _rooms.Values)
        {
            bool end = false;
            lock (room.Gate)
            {
                if (room.Finished || room.Game.IsOver)
                {
                    end = !room.Finished;
                }
                else if (room.Clock.Running is Color side && room.Clock.IsFlagged(side))
                {
                    room.Game.Timeout(side);
                    end = true;
                }
                else if (room.Game.Moves.Count == 0 && now - room.CreatedAt > options.FirstMoveTimeout)
                {
                    room.Game.Abort();
                    end = true;
                }
                else if (room.WhiteGoneSince is DateTimeOffset w && now - w > options.ReconnectGrace)
                {
                    if (room.Game.Moves.Count < 2) room.Game.Abort();
                    else room.Game.Abandon(Color.White);
                    end = true;
                }
                else if (room.BlackGoneSince is DateTimeOffset b && now - b > options.ReconnectGrace)
                {
                    if (room.Game.Moves.Count < 2) room.Game.Abort();
                    else room.Game.Abandon(Color.Black);
                    end = true;
                }
            }
            if (end) await FinishAsync(room);

            // Forget finished rooms after a while (clients may still ask for a resync briefly).
            if (room.Finished && now - room.CreatedAt > TimeSpan.FromHours(6)) _rooms.TryRemove(room.Id, out _);
        }

        foreach (var (code, ch) in _challenges)
            if (now - ch.At > TimeSpan.FromHours(1) || !ch.Creator.Connected && now - ch.Creator.LastSeen > TimeSpan.FromMinutes(5))
                _challenges.TryRemove(code, out _);
    }

    private async Task FinishAsync(GameRoom room)
    {
        int? whiteChange = null, blackChange = null;
        string result, description;
        lock (room.Gate)
        {
            if (room.Finished) return;
            room.Finished = true;
            room.Clock.Stop();
            Game g = room.Game;
            if (g.Termination != Termination.Aborted && g.Moves.Count >= 2)
            {
                double whiteScore = g.Result switch { GameResult.WhiteWins => 1, GameResult.BlackWins => 0, _ => 0.5 };
                (whiteChange, blackChange) = ratings.Rate(room.White.PublicId, room.Black.PublicId, room.Category, whiteScore);
            }
            result = g.ResultString;
            description = g.ResultDescription;
        }

        _activeRoomByPlayer.TryRemove(room.White.PublicId, out _);
        _activeRoomByPlayer.TryRemove(room.Black.PublicId, out _);
        SavePgn(room);
        log.LogInformation("Game {Id} over: {Result}", room.Id, description);
        await hub.Clients.Group(room.Id).GameOver(new GameOverDto(room.Id, result, room.Game.Termination.ToString(), description, whiteChange, blackChange));
    }

    // ------------------------------------------------------------------ helpers

    private GameRoom? ActiveRoom(Player p) =>
        _activeRoomByPlayer.TryGetValue(p.PublicId, out string? id) && _rooms.TryGetValue(id, out GameRoom? room) && !room.Finished ? room : null;

    private GameStartDto StartDto(GameRoom room, Player forPlayer)
    {
        lock (room.Gate)
        {
            return new GameStartDto(
                room.Id,
                players.ToDto(room.White, room.Category),
                players.ToDto(room.Black, room.Category),
                ReferenceEquals(forPlayer, room.White) ? "white" : "black",
                room.TimeControl,
                room.Game.StartFen,
                room.Game.Moves.Select(m => m.Uci).ToList(),
                room.ClockDto());
        }
    }

    private static TimeCategory Category(TimeControlDto tc) =>
        new TimeControl(TimeSpan.FromSeconds(tc.InitialSeconds), TimeSpan.FromSeconds(tc.IncrementSeconds)).Category;

    private void SavePgn(GameRoom room)
    {
        try
        {
            string dir = Directory.CreateDirectory(Path.Combine(options.DataDirectory, "games")).FullName;
            File.WriteAllText(Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{room.Id}.pgn"), Pgn.Write(room.Game));
        }
        catch (Exception ex)
        {
            log.LogWarning("Saving PGN failed: {Message}", ex.Message);
        }
    }
}

/// <summary>Drives <see cref="GameManager.TickAsync"/> (flags, aborts, abandonment).</summary>
public sealed class GameClockService(GameManager games, ILogger<GameClockService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await games.TickAsync();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Tick failed");
            }
        }
    }
}
