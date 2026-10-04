using System.Collections.Concurrent;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Online;
using Gambit.Server.Data;
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

    /// <summary>The player whose walkout ended the game (no first move, an early resignation, or leaving), if any.</summary>
    public Player? WalkedOut { get; set; }

    /// <summary>"white"/"black" while that player's rematch offer is pending (finished games only).</summary>
    public string? RematchOfferBy { get; set; }
    public bool RematchStarted { get; set; }

    /// <summary>Connection ids of spectators (they share the room's SignalR group with the players).</summary>
    public HashSet<string> Spectators { get; } = [];

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
public sealed class GameManager(IHubContext<GameHub, IGameClient> hub, PlayerRegistry players, RatingStore ratings, GameArchive archive,
    Conduct conduct, ILogger<GameManager> log, ServerOptions options)
{
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly ConcurrentDictionary<string, string> _activeRoomByPlayer = new();
    private readonly ConcurrentDictionary<string, (Player Creator, TimeControlDto Tc, string Color, DateTimeOffset At, long Seq)> _challenges = new();
    private long _challengeSeq;
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
        if (conduct.PairingPause(p.PublicId) is TimeSpan wait)
        {
            int minutes = (int)Math.Ceiling(wait.TotalMinutes);
            throw new HubException($"You left several games early, so quick pairing is paused for {(minutes == 1 ? "1 more minute" : $"{minutes} more minutes")}. Games with friends still work.");
        }
        await WithdrawRematchOffersAsync(p);
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
        _challenges[code] = (p, tc, color, DateTimeOffset.UtcNow, Interlocked.Increment(ref _challengeSeq));

        // Keep only the newest few codes per player.
        var mine = _challenges.Where(c => ReferenceEquals(c.Value.Creator, p)).OrderBy(c => c.Value.Seq).ToList();
        foreach (var old in mine.Take(Math.Max(0, mine.Count - options.MaxOpenChallenges))) _challenges.TryRemove(old.Key, out _);
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
        await WithdrawRematchOffersAsync(white);
        await WithdrawRematchOffersAsync(black);

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
            if (room.Game.Moves.Count < 2)
            {
                room.Game.Abort();
                room.WalkedOut = p;
            }
            else
            {
                room.Game.Resign(c);
            }
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

    // ------------------------------------------------------------------ rematches

    /// <summary>Offers a rematch of a finished game, or accepts the opponent's pending offer. Colors swap.</summary>
    public async Task OfferRematchAsync(Player p, string gameId)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room)) return;
        Player opponent;
        bool available, start = false;
        lock (room.Gate)
        {
            if (!room.Finished || room.RematchStarted || room.ColorOf(p) is not Color c) return;
            opponent = room.PlayerOf(c.Opposite());
            string me = c == Color.White ? "white" : "black";
            available = opponent.Connected && ActiveRoom(opponent) == null && ActiveRoom(p) == null;
            if (!available)
            {
                room.RematchOfferBy = null;
            }
            else if (room.RematchOfferBy == me)
            {
                return; // already offered
            }
            else if (room.RematchOfferBy != null)
            {
                room.RematchStarted = true; // both want it
                start = true;
            }
            else
            {
                room.RematchOfferBy = me;
            }
        }

        if (!available)
        {
            if (p.ConnectionId is string conn) await hub.Clients.Client(conn).RematchDeclined(room.Id, true);
        }
        else if (start)
        {
            CancelSeek(p);
            CancelSeek(opponent);
            await StartGameAsync(room.Black, room.White, room.TimeControl, "white");
        }
        else if (opponent.ConnectionId is string conn)
        {
            await hub.Clients.Client(conn).RematchOffered(room.Id);
        }
    }

    /// <summary>Declines the opponent's rematch offer, or withdraws one's own; the other player is told.</summary>
    public async Task DeclineRematchAsync(Player p, string gameId, bool unavailable = false)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room)) return;
        Player opponent;
        lock (room.Gate)
        {
            if (room.RematchStarted || room.RematchOfferBy == null || room.ColorOf(p) is not Color c) return;
            room.RematchOfferBy = null;
            opponent = room.PlayerOf(c.Opposite());
        }
        if (opponent.ConnectionId is string conn) await hub.Clients.Client(conn).RematchDeclined(room.Id, unavailable);
    }

    // ------------------------------------------------------------------ spectators

    /// <summary>Games in progress, most-watched first.</summary>
    public IReadOnlyList<LiveGameDto> ListGames()
    {
        var list = new List<LiveGameDto>();
        foreach (GameRoom room in _rooms.Values)
        {
            lock (room.Gate)
            {
                if (room.Finished || room.Game.IsOver) continue;
                list.Add(new LiveGameDto(room.Id, players.ToDto(room.White, room.Category), players.ToDto(room.Black, room.Category),
                    room.TimeControl, room.Game.Moves.Count, room.Spectators.Count));
            }
        }
        return list.OrderByDescending(g => g.Spectators).ThenByDescending(g => g.Plies).Take(50).ToList();
    }

    /// <summary>Adds a spectator to a running game; returns its current state, or null.</summary>
    public async Task<GameStartDto?> WatchAsync(Player p, string connectionId, string gameId)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room)) return null;
        lock (room.Gate)
        {
            if (room.Finished) return null;
            if (room.ColorOf(p) == null) room.Spectators.Add(connectionId);
        }
        await hub.Groups.AddToGroupAsync(connectionId, room.Id);
        return StartDto(room, p);
    }

    public async Task UnwatchAsync(string connectionId, string gameId)
    {
        if (!_rooms.TryGetValue(gameId, out GameRoom? room)) return;
        bool removed;
        lock (room.Gate) removed = room.Spectators.Remove(connectionId);
        if (removed) await hub.Groups.RemoveFromGroupAsync(connectionId, room.Id);
    }

    /// <summary>Forgets a closed connection's spectator seats (SignalR drops its groups itself).</summary>
    public void RemoveSpectator(string connectionId)
    {
        foreach (GameRoom room in _rooms.Values)
            lock (room.Gate) room.Spectators.Remove(connectionId);
    }

    /// <summary>Cancels pending rematch offers involving <paramref name="p"/> (they left or started another game).</summary>
    private async Task WithdrawRematchOffersAsync(Player p)
    {
        foreach (GameRoom room in _rooms.Values)
            if (room.Finished && room.RematchOfferBy != null && room.ColorOf(p) != null)
                await DeclineRematchAsync(p, room.Id, unavailable: true);
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
        await WithdrawRematchOffersAsync(p);
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
                    room.WalkedOut = room.White;
                    end = true;
                }
                else if (room.WhiteGoneSince is DateTimeOffset w && now - w > options.ReconnectGrace)
                {
                    if (room.Game.Moves.Count < 2) room.Game.Abort();
                    else room.Game.Abandon(Color.White);
                    room.WalkedOut = room.White;
                    end = true;
                }
                else if (room.BlackGoneSince is DateTimeOffset b && now - b > options.ReconnectGrace)
                {
                    if (room.Game.Moves.Count < 2) room.Game.Abort();
                    else room.Game.Abandon(Color.Black);
                    room.WalkedOut = room.Black;
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
        bool rated = false;
        lock (room.Gate)
        {
            if (room.Finished) return;
            room.Finished = true;
            room.Clock.Stop();
            Game g = room.Game;
            if (g.Termination != Termination.Aborted && g.Moves.Count >= 2)
            {
                rated = true;
                double whiteScore = g.Result switch { GameResult.WhiteWins => 1, GameResult.BlackWins => 0, _ => 0.5 };
                (whiteChange, blackChange) = ratings.Rate(room.White.PublicId, room.Black.PublicId, room.Category, whiteScore);
            }
            result = g.ResultString;
            description = g.ResultDescription;
        }

        _activeRoomByPlayer.TryRemove(room.White.PublicId, out _);
        _activeRoomByPlayer.TryRemove(room.Black.PublicId, out _);
        if (room.WalkedOut is Player walkedOut) conduct.RecordWalkout(walkedOut.PublicId);
        Archive(room, rated, whiteChange, blackChange);
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
                room.ColorOf(forPlayer) switch { Color.White => "white", Color.Black => "black", _ => "spectator" },
                room.TimeControl,
                room.Game.StartFen,
                room.Game.Moves.Select(m => m.Uci).ToList(),
                room.ClockDto());
        }
    }

    private static TimeCategory Category(TimeControlDto tc) =>
        new TimeControl(TimeSpan.FromSeconds(tc.InitialSeconds), TimeSpan.FromSeconds(tc.IncrementSeconds)).Category;

    /// <summary>Stores the finished game (aborted ones too, unrated) in the database.</summary>
    private void Archive(GameRoom room, bool rated, int? whiteChange, int? blackChange)
    {
        try
        {
            string pgn;
            lock (room.Gate) pgn = Pgn.Write(room.Game);
            archive.Save(room.Id, room.White.PublicId, room.Black.PublicId, room.TimeControl.Key, room.Game.ResultString,
                room.Game.Termination.ToString(), rated, whiteChange, blackChange, room.CreatedAt, DateTimeOffset.UtcNow, pgn);
        }
        catch (Exception ex)
        {
            log.LogWarning("Saving game {Id} failed: {Message}", room.Id, ex.Message);
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
