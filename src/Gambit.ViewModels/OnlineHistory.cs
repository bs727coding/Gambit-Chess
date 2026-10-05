using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Online;

namespace Gambit.ViewModels;

/// <summary>
/// Bringing your online games from the server into the local game list: which ones are missing
/// (played on another PC), and each as a finished <see cref="Game"/>.
/// </summary>
public static class OnlineHistory
{
    /// <summary>A game in the local list: its server id (null for games kept before ids were), the opponent and when it ended.</summary>
    public sealed record LocalGame(string? OnlineId, string Opponent, DateTimeOffset PlayedAt);

    /// <summary>
    /// The server's games that the local list lacks: not known by id, and not an older local record of
    /// the same game (same opponent, ended within 15 minutes of each other).
    /// </summary>
    public static IReadOnlyList<GameRecordDto> Missing(IEnumerable<GameRecordDto> server, IReadOnlyCollection<LocalGame> local, string myName)
    {
        var known = local.Where(l => l.OnlineId != null).Select(l => l.OnlineId!).ToHashSet();
        var missing = new List<GameRecordDto>();
        foreach (GameRecordDto g in server)
        {
            if (known.Contains(g.Id)) continue;
            string opponent = Opponent(g, myName);
            bool keptBefore = local.Any(l => l.OnlineId == null && string.Equals(l.Opponent, opponent, StringComparison.OrdinalIgnoreCase)
                && (l.PlayedAt - g.EndedAt).Duration() <= TimeSpan.FromMinutes(15));
            if (!keptBefore) missing.Add(g);
        }
        return missing;
    }

    public static bool PlayedWhite(GameRecordDto g, string myName) => string.Equals(g.White, myName, StringComparison.OrdinalIgnoreCase);

    public static string Opponent(GameRecordDto g, string myName) => PlayedWhite(g, myName) ? g.Black : g.White;

    /// <summary>The game with its moves, result and how it ended, or null if its PGN can't be read.</summary>
    public static Game? ToGame(GameRecordDto g)
    {
        try
        {
            PgnGame pgn = Pgn.ReadOne(g.Pgn);
            var game = new Game(pgn.Tags.FirstOrDefault(t => t.Key == "FEN").Value);
            foreach (var (key, value) in pgn.Tags) game.Tags[key] = value;
            foreach (string san in pgn.Moves) game.Play(San.Parse(game.Position, san));
            if (!game.IsOver && Enum.TryParse(g.Termination, out Termination how))
            {
                Color loser = g.Result == "1-0" ? Color.Black : Color.White;
                switch (how)
                {
                    case Termination.Resignation: game.Resign(loser); break;
                    case Termination.Timeout or Termination.TimeoutVsInsufficientMaterial: game.Timeout(loser); break;
                    case Termination.Abandoned: game.Abandon(loser); break;
                    case Termination.DrawAgreement: game.AgreeDraw(); break;
                    case Termination.Aborted: game.Abort(); break;
                }
            }
            return game;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"5+0" (minutes + seconds, as the server writes it) as a time control.</summary>
    public static TimeControl ParseTimeControl(string key)
    {
        string[] p = key.Split('+');
        int minutes = int.TryParse(p[0], out int m) ? m : 0;
        int increment = p.Length > 1 && int.TryParse(p[1], out int i) ? i : 0;
        return new TimeControl(TimeSpan.FromMinutes(minutes), TimeSpan.FromSeconds(increment));
    }
}
