using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;

namespace Gambit.Tests;

public class NotationTests
{
    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData(PerftTests.Kiwipete)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [InlineData("rnbqkbnr/pp1ppppp/8/2pP4/8/8/PPP1PPPP/RNBQKBNR w KQkq c6 0 3")]
    [InlineData("4k3/8/8/8/8/8/8/4K2R b K - 12 40")]
    public void Fen_round_trips(string fen)
    {
        Assert.Equal(fen, Position.FromFen(fen).ToFen());
    }

    [Fact]
    public void Fen_drops_en_passant_square_when_no_capture_is_possible()
    {
        var pos = Position.FromFen("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1");
        Assert.Equal(Square.None, pos.EnPassantSquare);
    }

    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "Nf3", "g1f3")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e4", "e2e4")]
    [InlineData("r1bqkbnr/pppp1ppp/2n5/4p3/3PP3/5N2/PPP2PPP/RNBQKB1R b KQkq - 0 3", "exd4", "e5d4")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", "O-O-O", "e1c1")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", "O-O", "e1g1")]
    [InlineData("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1", "b8=Q+", "b7b8q")]
    [InlineData("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1", "b8=N", "b7b8n")]
    [InlineData("3k4/8/8/8/8/8/4K3/R6R w - - 0 1", "Rab1", "a1b1")]
    [InlineData("3k4/8/8/8/8/R7/8/R2K4 w - - 0 1", "R1a2", "a1a2")]
    [InlineData("8/8/1k6/8/7Q/8/8/K3Q2Q w - - 0 1", "Qh1e4", "h1e4")]
    public void San_format_and_parse(string fen, string san, string uci)
    {
        var pos = Position.FromFen(fen);
        Move m = Uci.Parse(pos, uci);
        Assert.False(m.IsNone);
        Assert.Equal(san, San.Format(pos, m));
        Assert.True(San.TryParse(pos, san, out var parsed));
        Assert.Equal(m, parsed);
    }

    [Theory]
    [InlineData("0-0", "e1g1")]
    [InlineData("O-O!?", "e1g1")]
    [InlineData("Ra1-b1", "a1b1")]
    [InlineData("h1h2", "h1h2")]
    public void San_parse_is_lenient(string text, string uci)
    {
        var pos = Position.FromFen("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1");
        Assert.True(San.TryParse(pos, text, out var m));
        Assert.Equal(uci, m.ToUci());
    }

    [Fact]
    public void Pgn_round_trip_preserves_moves_and_result()
    {
        var game = new Game();
        foreach (string san in new[] { "e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6", "Qxf7#" }) Assert.NotNull(game.TryPlay(san));
        game.Tags["White"] = "Alice";
        game.Tags["Black"] = "Bob";

        string pgn = Pgn.Write(game);
        Assert.Contains("1. e4 e5 2. Bc4 Nc6 3. Qh5 Nf6 4. Qxf7# 1-0", pgn);

        var parsed = Pgn.ReadOne(pgn);
        Assert.Equal("Alice", parsed.Tag("White"));
        Assert.Equal("1-0", parsed.Result);
        var replay = parsed.ToGame(out string? error);
        Assert.Null(error);
        Assert.Equal(GameResult.WhiteWins, replay.Result);
        Assert.Equal(Termination.Checkmate, replay.Termination);
    }

    [Fact]
    public void Pgn_reader_skips_comments_variations_and_nags()
    {
        const string text = """
            [Event "Test"]
            [Result "*"]

            1. e4 {best by test} e5 (1... c5 2. Nf3 (2. c3) d6) 2. Nf3 $1 Nc6 3.Bb5 a6 ; Ruy Lopez
            4. Ba4 *
            """;
        var games = Pgn.ReadAll(text);
        Assert.Single(games);
        Assert.Equal(new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4" }, games[0].Moves);
        Assert.Equal("best by test", games[0].Comments[0]);
        var game = games[0].ToGame(out var err);
        Assert.Null(err);
        Assert.Equal(7, game.Moves.Count);
    }

    [Fact]
    public void Pgn_reads_multiple_games_and_fen_setups()
    {
        const string text = """
            [Event "A"]
            [Result "1-0"]

            1. f3 e5 2. g4 Qh4# 0-1

            [Event "B"]
            [SetUp "1"]
            [FEN "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1"]
            [Result "*"]

            1. e4 Kd7 *
            """;
        var games = Pgn.ReadAll(text);
        Assert.Equal(2, games.Count);
        Assert.Equal(GameResult.BlackWins, games[0].ToGame(out _).Result);
        var g2 = games[1].ToGame(out var err);
        Assert.Null(err);
        Assert.Equal("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", g2.StartFen);
        Assert.Equal(2, g2.Moves.Count);
    }
}
