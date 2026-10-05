using Gambit.Core.Games;
using Gambit.Online;
using Gambit.ViewModels;

namespace Gambit.Tests;

public class OnlineHistoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static GameRecordDto Game(string id, string white, string black, DateTimeOffset ended, string result = "0-1",
        string termination = "Resignation", string pgn = "") =>
        new(id, white, black, "5+0", result, termination, true, -8, 8, ended, pgn);

    [Fact]
    public void Only_games_the_local_list_lacks_are_missing()
    {
        GameRecordDto known = Game("a1", "Me", "Ann", Noon);
        GameRecordDto keptBeforeIds = Game("b2", "Bob", "me", Noon.AddHours(1)); // names are case-insensitive
        GameRecordDto fromAnotherPc = Game("c3", "Me", "Ann", Noon.AddHours(2));
        var local = new[]
        {
            new OnlineHistory.LocalGame("a1", "Ann", Noon),
            new OnlineHistory.LocalGame(null, "Bob", Noon.AddHours(1).AddMinutes(2)),
        };
        Assert.Equal([fromAnotherPc], OnlineHistory.Missing([known, keptBeforeIds, fromAnotherPc], local, "Me"));
        Assert.Equal("Bob", OnlineHistory.Opponent(keptBeforeIds, "Me"));
        Assert.False(OnlineHistory.PlayedWhite(keptBeforeIds, "Me"));
    }

    [Fact]
    public void A_server_game_reads_back_with_how_it_ended()
    {
        const string pgn = "[Event \"Gambit online game\"]\n[White \"Me\"]\n[Black \"Ann\"]\n\n1. e4 {[%clk 0:05:00]} e5 {[%clk 0:05:00]} 0-1\n";
        Game game = OnlineHistory.ToGame(Game("x", "Me", "Ann", Noon, pgn: pgn))!;
        Assert.Equal(2, game.Moves.Count);
        Assert.Equal(GameResult.BlackWins, game.Result);
        Assert.Equal("Black wins by resignation", game.ResultDescription);
        Assert.Equal("Ann", game.Tags["Black"]);
        Assert.Null(OnlineHistory.ToGame(Game("y", "Me", "Ann", Noon, pgn: "1. e4 Ke3")));
        Assert.Equal(TimeSpan.FromMinutes(5), OnlineHistory.ParseTimeControl("5+0").Initial);
    }
}
