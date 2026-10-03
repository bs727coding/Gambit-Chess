using Gambit.App.Controls;
using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Openings;
using Gambit.Engine.Review;
using Gambit.Engine.Search;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using WColor = Windows.UI.Color;

namespace Gambit.App.Pages;

/// <summary>What to review: a finished game and how to label/orient it.</summary>
public sealed record ReviewRequest(Game Game, string WhiteName, string BlackName, Color Perspective = Color.White)
{
    /// <summary>Loads a game from an archived PGN file.</summary>
    public static ReviewRequest? FromPgnFile(string path, string playerColor)
    {
        PgnGame pgn = Pgn.ReadOne(File.ReadAllText(path));
        Game game = pgn.ToGame(out _);
        return new ReviewRequest(game, pgn.Tag("White") ?? "White", pgn.Tag("Black") ?? "Black",
            playerColor == "black" ? Color.Black : Color.White);
    }
}

/// <summary>Game review: accuracy per side, move-by-move classification, eval graph, best-move arrows.</summary>
public sealed partial class ReviewPage : Page
{
    private static readonly Dictionary<MoveClass, (string Symbol, string Hex, string Label)> Styles = new()
    {
        [MoveClass.Brilliant] = ("!!", "#1BACA6", "Brilliant"),
        [MoveClass.Great] = ("!", "#5C8BB0", "Great"),
        [MoveClass.Best] = ("★", "#81B64C", "Best"),
        [MoveClass.Excellent] = ("✓", "#81B64C", "Excellent"),
        [MoveClass.Good] = ("✓", "#95B776", "Good"),
        [MoveClass.Book] = ("≡", "#A88865", "Book"),
        [MoveClass.Forced] = ("→", "#8A8A8A", "Forced"),
        [MoveClass.Inaccuracy] = ("?!", "#F7C045", "Inaccuracy"),
        [MoveClass.Mistake] = ("?", "#FFA459", "Mistake"),
        [MoveClass.Miss] = ("✕", "#FF7769", "Miss"),
        [MoveClass.Blunder] = ("??", "#FA412D", "Blunder"),
    };

    private Game? _game;
    private GameReview? _review;
    private int _ply;
    private CancellationTokenSource? _cts;
    private Color _perspective = Color.White;

    public ReviewPage()
    {
        InitializeComponent();
        Board.Interaction = BoardInteraction.None;
        Board.BoardSizeChanged += (_, size) => Eval.Height = size;
        AddAccelerator(VirtualKey.Left, () => ShowPly(_ply - 1));
        AddAccelerator(VirtualKey.Right, () => ShowPly(_ply + 1));
        AddAccelerator(VirtualKey.Home, () => ShowPly(0));
        AddAccelerator(VirtualKey.End, () => ShowPly(int.MaxValue));
        ApplySettings();
    }

    public static MoveBadge BadgeFor(MoveClass cls)
    {
        var (symbol, hex, label) = Styles[cls];
        return new MoveBadge(symbol, Ui.ParseColor(hex), label);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ReviewRequest req) _ = StartAsync(req);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cts?.Cancel();
    }

    private async Task StartAsync(ReviewRequest req)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _game = req.Game;
        _review = null;

        _perspective = req.Perspective;
        Board.Flipped = req.Perspective == Color.Black;
        Eval.Flipped = Board.Flipped;
        WhiteName.Text = req.WhiteName;
        BlackName.Text = req.BlackName;
        Opening? opening = OpeningBook.Identify(_game);
        OpeningText.Text = opening == null ? "" : $"{opening.Eco} · {opening.Name}";

        ProgressPanel.Visibility = Visibility.Visible;
        SummaryGrid.Visibility = Visibility.Collapsed;
        CountsGrid.Visibility = Visibility.Collapsed;
        Progress.Value = 0;
        MoveList.SetBadges(null);
        MoveList.SetMoves(_game.Moves, 0);
        Graph.SetData([], []);
        _ply = -1;
        ShowPly(0);

        var progress = new Progress<double>(p =>
        {
            Progress.Value = p;
            ProgressText.Text = $"Analysing {_game.Moves.Count + 1} positions on {Environment.ProcessorCount} cores… {p:P0}";
        });
        try
        {
            _review = await new GameReviewer().ReviewAsync(_game, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error("Game review failed", ex);
            ProgressText.Text = "The review could not be completed.";
            return;
        }
        if (cts.IsCancellationRequested || !ReferenceEquals(_game, req.Game)) return;
        Populate();
    }

    private void Populate()
    {
        if (_review == null || _game == null) return;
        ProgressPanel.Visibility = Visibility.Collapsed;
        SummaryGrid.Visibility = Visibility.Visible;
        CountsGrid.Visibility = Visibility.Visible;
        WhiteAccuracy.Text = $"{_review.WhiteAccuracy:0.0}";
        BlackAccuracy.Text = $"{_review.BlackAccuracy:0.0}";

        BuildCounts();
        MoveList.SetBadges(_review.Moves.ToDictionary(m => m.Ply, m => BadgeFor(m.Class)));
        var markers = _review.Moves.Where(m => m.IsKeyMoment)
            .Select(m => (m.Ply, Ui.ParseColor(Styles[m.Class].Hex)))
            .ToList();
        Graph.SetData(_review.EvalCurve, markers);
        AchievementService.Instance.OnReview(_review, _game, _perspective);
        int ply = _ply;
        _ply = -1;
        ShowPly(ply);
    }

    private void BuildCounts()
    {
        CountsGrid.Children.Clear();
        CountsGrid.RowDefinitions.Clear();
        CountsGrid.ColumnDefinitions.Clear();
        // Two groups of five rows ("white count | class | black count") keep the card short enough
        // for the move list and buttons to fit on a laptop-sized window.
        for (int group = 0; group < 2; group++)
        {
            if (group == 1) CountsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            CountsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            CountsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            CountsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (int r = 0; r < 5; r++) CountsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        MoveClass[] shown = [MoveClass.Brilliant, MoveClass.Great, MoveClass.Best, MoveClass.Excellent, MoveClass.Good,
            MoveClass.Book, MoveClass.Inaccuracy, MoveClass.Mistake, MoveClass.Miss, MoveClass.Blunder];
        for (int i = 0; i < shown.Length; i++)
        {
            MoveClass cls = shown[i];
            int row = i % 5, column = i < 5 ? 0 : 4;
            int w = _review!.Count(Color.White, cls), b = _review.Count(Color.Black, cls);
            var (symbol, hex, label) = Styles[cls];

            var wt = new TextBlock { Text = w.ToString(), HorizontalAlignment = HorizontalAlignment.Right, Opacity = w == 0 ? 0.35 : 1, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var bt = new TextBlock { Text = b.ToString(), HorizontalAlignment = HorizontalAlignment.Left, Opacity = b == 0 ? 0.35 : 1, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var mid = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Width = 104 };
            mid.Children.Add(new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Ui.ParseColor(hex)),
                Child = new TextBlock { Text = symbol, FontSize = 8, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            mid.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.85 });

            Grid.SetRow(wt, row);
            Grid.SetColumn(wt, column);
            Grid.SetRow(mid, row);
            Grid.SetColumn(mid, column + 1);
            Grid.SetRow(bt, row);
            Grid.SetColumn(bt, column + 2);
            CountsGrid.Children.Add(wt);
            CountsGrid.Children.Add(mid);
            CountsGrid.Children.Add(bt);
        }
    }

    private void ShowPly(int ply)
    {
        if (_game == null) return;
        ply = Math.Clamp(ply, 0, _game.Moves.Count);
        if (ply == _ply) return;
        bool forwardOne = ply == _ply + 1;
        _ply = ply;

        Position pos = _game.PositionAt(ply);
        Move last = ply > 0 ? _game.Moves[ply - 1].Move : Move.None;
        Board.SetPosition(pos, last, animate: forwardOne);
        if (forwardOne && ply > 0) SoundService.PlayFor(_game.Moves[ply - 1]);
        MoveList.Highlight(ply);
        Graph.SetCurrent(ply);
        UpdateVerdict();
    }

    private void UpdateVerdict()
    {
        if (_game == null) return;
        var markers = new List<BoardMarker>();

        if (_review == null)
        {
            SetVerdict("…", "#808080", _ply == 0 ? "Starting position" : $"{_game.Moves[_ply - 1].San}", "Waiting for the engine…");
        }
        else
        {
            Eval.SetScore(_review.EvalCurve[_ply]);
            if (_ply == 0)
            {
                SetVerdict("●", "#808080", "Starting position", $"Evaluation {Searcher.FormatScore(_review.EvalCurve[0])}");
            }
            else
            {
                MoveReview r = _review.Moves[_ply - 1];
                var (symbol, hex, label) = Styles[r.Class];
                string san = r.Move.San;
                string title = r.Class switch
                {
                    MoveClass.Brilliant => $"{san} is brilliant!",
                    MoveClass.Great => $"{san} is a great move",
                    MoveClass.Best => $"{san} is the best move",
                    MoveClass.Excellent => $"{san} is excellent",
                    MoveClass.Good => $"{san} is good",
                    MoveClass.Book => $"{san} is a book move",
                    MoveClass.Forced => $"{san} was forced",
                    MoveClass.Inaccuracy => $"{san} is an inaccuracy",
                    MoveClass.Mistake => $"{san} is a mistake",
                    MoveClass.Miss => $"{san} misses a chance",
                    _ => $"{san} is a blunder",
                };
                bool showBest = r.Class is MoveClass.Good or MoveClass.Inaccuracy or MoveClass.Mistake or MoveClass.Miss or MoveClass.Blunder;
                string detail = $"Evaluation {Searcher.FormatScore(r.EvalBefore)} → {Searcher.FormatScore(r.EvalAfter)}";
                // "Best was …" unless the explanation already names the better move.
                if (showBest && r.BestSan.Length > 0 && r.Explanation?.Contains(r.BestSan, StringComparison.Ordinal) != true)
                    detail = $"Best was {r.BestSan} ({Searcher.FormatScore(r.BestEval)}). " + detail;
                if (r.Explanation != null) detail = r.Explanation + " " + detail;
                SetVerdict(symbol, hex, title, detail);

                if (showBest && !r.BestMove.IsNone)
                    markers.Add(BoardMarker.Arrow(r.BestMove.From, r.BestMove.To, Ui.ParseColor("#81B64C", 210)));
                if (!r.Refutation.IsNone)
                    markers.Add(BoardMarker.Arrow(r.Refutation.From, r.Refutation.To, Ui.ParseColor("#E15241", 200))); // the punishing reply
                markers.Add(BoardMarker.Square(r.Move.Move.To, Ui.ParseColor(hex, 230)));
            }
        }
        Board.SetMarkers(markers);
    }

    private void SetVerdict(string symbol, string hex, string title, string detail)
    {
        VerdictSymbol.Text = symbol;
        VerdictBadge.Background = new SolidColorBrush(Ui.ParseColor(hex));
        VerdictTitle.Text = title;
        VerdictDetail.Text = detail;
    }

    private void ApplySettings() => Board.ApplyUserSettings();

    private void Graph_PlySelected(object? sender, int ply) => ShowPly(ply);
    private void MoveList_PlySelected(object? sender, int ply) => ShowPly(ply);
    private void First_Click(object sender, RoutedEventArgs e) => ShowPly(0);
    private void Prev_Click(object sender, RoutedEventArgs e) => ShowPly(_ply - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => ShowPly(_ply + 1);
    private void Last_Click(object sender, RoutedEventArgs e) => ShowPly(int.MaxValue);

    private void NextKey_Click(object sender, RoutedEventArgs e)
    {
        if (_review == null) return;
        MoveReview? next = _review.Moves.FirstOrDefault(m => m.Ply > _ply && m.IsKeyMoment)
            ?? _review.Moves.FirstOrDefault(m => m.IsKeyMoment);
        if (next != null) ShowPly(next.Ply);
    }

    private void Flip_Click(object sender, RoutedEventArgs e)
    {
        Board.Flipped = !Board.Flipped;
        Eval.Flipped = Board.Flipped;
    }

    private void AddAccelerator(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        KeyboardAccelerators.Add(accelerator);
    }
}
