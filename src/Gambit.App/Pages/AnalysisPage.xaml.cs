using Gambit.App.Controls;
using Gambit.App.Services;
using Gambit.App.Theming;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Openings;
using Gambit.Engine.Search;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Gambit.App.Pages;

/// <summary>What to open in the analysis board.</summary>
public sealed record AnalysisRequest(string? Fen = null, string? PgnFile = null, string? Pgn = null);

/// <summary>Free analysis: move both sides, engine lines with an eval bar, FEN/PGN import and export.</summary>
public sealed partial class AnalysisPage : Page
{
    private readonly AnalysisEngine _engine = new();
    private Game _game = NewGame(null);
    private int _viewPly = -1;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public AnalysisPage()
    {
        InitializeComponent();
        _engine.InfoUpdated += Engine_InfoUpdated;
        _toastTimer.Tick += (_, _) =>
        {
            Toast.IsOpen = false;
            _toastTimer.Stop();
        };
        Board.BoardSizeChanged += (_, size) => Eval.Height = size;
        Board.Interaction = BoardInteraction.Both;

        AddAccelerator(VirtualKey.Left, () => StepPly(-1));
        AddAccelerator(VirtualKey.Right, () => StepPly(+1));
        AddAccelerator(VirtualKey.Home, () => ShowPly(0));
        AddAccelerator(VirtualKey.End, () => ShowPly(int.MaxValue));

        App.Settings.Current.PropertyChanged += (_, _) => ApplySettings();
        ApplySettings();
        Refresh(animate: false);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AnalysisRequest req) Load(req);
        else if (EngineSwitch.IsOn) _engine.Analyze(Board.Position);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _engine.Stop(); // don't burn CPU in the background
    }

    private static Game NewGame(string? fen) => new(fen) { AutoDrawRules = false };

    private void Load(AnalysisRequest req)
    {
        try
        {
            if (req.PgnFile != null) LoadPgn(File.ReadAllText(req.PgnFile));
            else if (req.Pgn != null) LoadPgn(req.Pgn);
            else if (req.Fen != null) LoadFen(req.Fen);
        }
        catch (Exception ex)
        {
            ShowToast("Couldn't open that game", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void LoadPgn(string text)
    {
        PgnGame pgn = Pgn.ReadOne(text);
        Game game = pgn.ToGame(out string? error);
        game.AutoDrawRules = false;
        _game = game;
        _viewPly = -1;
        Refresh(animate: false);
        if (error != null) ShowToast("PGN partially loaded", error, InfoBarSeverity.Warning);
        else ShowToast("Game loaded", $"{pgn.Tag("White") ?? "?"} vs {pgn.Tag("Black") ?? "?"} · {game.Moves.Count} plies", InfoBarSeverity.Success);
    }

    private void LoadFen(string fen)
    {
        if (!Fen.TryParse(fen, out Position? pos, out string? error) || pos == null)
        {
            ShowToast("Invalid FEN", error ?? "That position could not be read.", InfoBarSeverity.Error);
            return;
        }
        _game = NewGame(pos.ToFen());
        _viewPly = -1;
        Refresh(animate: false);
    }

    // ------------------------------------------------------------------ board + history

    private void Board_MoveRequested(object? sender, BoardMoveEventArgs e)
    {
        // Moving from an earlier position replaces the rest of the line.
        if (_viewPly >= 0)
        {
            while (_game.Moves.Count > _viewPly) _game.Undo();
            _viewPly = -1;
        }
        if (_game.IsOver || !_game.IsLegal(e.Move)) return;
        SoundService.PlayFor(_game.Play(e.Move));
        Refresh(animate: true);
    }

    private void MoveList_PlySelected(object? sender, int ply) => ShowPly(ply);

    private void StepPly(int delta)
    {
        int current = _viewPly < 0 ? _game.Moves.Count : _viewPly;
        ShowPly(current + delta);
    }

    private void ShowPly(int ply)
    {
        int count = _game.Moves.Count;
        ply = Math.Clamp(ply, 0, count);
        int current = _viewPly < 0 ? count : _viewPly;
        if (ply == current) return;
        bool forwardOne = ply == current + 1;
        _viewPly = ply == count ? -1 : ply;
        ShowBoard(animate: forwardOne);
        MoveList.Highlight(ply);
    }

    private void Refresh(bool animate)
    {
        MoveList.SetMoves(_game.Moves, _viewPly < 0 ? _game.Moves.Count : _viewPly);
        ShowBoard(animate);
    }

    private void ShowBoard(bool animate)
    {
        int ply = _viewPly < 0 ? _game.Moves.Count : _viewPly;
        Position pos = ply == _game.Moves.Count ? _game.Position : _game.PositionAt(ply);
        Move last = ply > 0 ? _game.Moves[ply - 1].Move : Move.None;
        Board.SetPosition(pos, last, animate);
        FenBox.Text = pos.ToFen();
        Opening? opening = _game.StartsFromStandardPosition ? OpeningBook.IdentifyAt(_game, ply) : OpeningBook.ForPosition(pos);
        OpeningText.Text = opening == null ? "" : $"{opening.Eco} · {opening.Name}";

        var probe = new Game(pos.ToFen()) { AutoDrawRules = false };
        StatusText.Text = probe.IsOver ? probe.ResultDescription : $"{pos.SideToMove.Name()} to move";

        Lines.Children.Clear();
        EngineInfo.Text = EngineSwitch.IsOn ? "Thinking…" : "Off";
        if (EngineSwitch.IsOn && !probe.IsOver) _engine.Analyze(pos);
        else
        {
            _engine.Stop();
            if (probe.IsOver)
            {
                EngineInfo.Text = probe.ResultDescription;
                Eval.SetScore(probe.Winner switch { Color.White => Searcher.Mate, Color.Black => -Searcher.Mate, _ => 0 });
            }
        }
    }

    private void Engine_InfoUpdated(Position root, SearchInfo info)
    {
        if (root.Key != Board.Position.Key || !EngineSwitch.IsOn) return;
        EngineInfo.Text = $"Depth {info.Depth} · {info.NodesPerSecond / 1000:N0} kN/s";
        if (info.Best is PvLine best)
            Eval.SetScore(root.SideToMove == Color.White ? best.Score : -best.Score);

        Lines.Children.Clear();
        foreach (PvLine line in info.Lines)
        {
            int whiteScore = root.SideToMove == Color.White ? line.Score : -line.Score;
            string score = Searcher.FormatScore(whiteScore);
            List<string> san = San.FormatLine(root, line.Pv.Take(12));
            string moves = FormatSanLine(root, san);

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chip = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(whiteScore >= 0 ? Microsoft.UI.ColorHelper.FromArgb(255, 238, 238, 238) : Microsoft.UI.ColorHelper.FromArgb(255, 48, 48, 48)),
                Child = new TextBlock
                {
                    Text = score,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(whiteScore >= 0 ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White),
                },
                VerticalAlignment = VerticalAlignment.Top,
            };
            var text = new TextBlock { Text = moves, TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(text, 1);
            row.Children.Add(chip);
            row.Children.Add(text);

            Move first = line.Move;
            var button = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(button, "Play this move");
            button.Click += (_, _) => Board_MoveRequested(this, new BoardMoveEventArgs(first, false));
            Lines.Children.Add(button);
        }
    }

    private static string FormatSanLine(Position root, List<string> san)
    {
        var sb = new System.Text.StringBuilder();
        int number = root.FullmoveNumber;
        bool white = root.SideToMove == Color.White;
        for (int i = 0; i < san.Count; i++)
        {
            if (white) sb.Append(number).Append(". ");
            else if (i == 0) sb.Append(number).Append("… ");
            sb.Append(san[i]).Append(' ');
            if (!white) number++;
            white = !white;
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ buttons

    private void EngineSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ShowBoard(animate: false);
    }

    private void First_Click(object sender, RoutedEventArgs e) => ShowPly(0);
    private void Prev_Click(object sender, RoutedEventArgs e) => StepPly(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => StepPly(+1);
    private void Last_Click(object sender, RoutedEventArgs e) => ShowPly(int.MaxValue);

    private void Flip_Click(object sender, RoutedEventArgs e)
    {
        Board.Flipped = !Board.Flipped;
        Eval.Flipped = Board.Flipped;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _game = NewGame(null);
        _viewPly = -1;
        Refresh(animate: false);
    }

    private void LoadFen_Click(object sender, RoutedEventArgs e) => LoadFen(FenBox.Text);

    private void CopyFen_Click(object sender, RoutedEventArgs e)
    {
        CopyText(Board.Position.ToFen());
        ShowToast("FEN copied", "The current position is on the clipboard.", InfoBarSeverity.Success);
    }

    private void CopyPgn_Click(object sender, RoutedEventArgs e)
    {
        CopyText(Pgn.Write(_game));
        ShowToast("PGN copied", "The game is on the clipboard.", InfoBarSeverity.Success);
    }

    private async void PastePgn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                ShowToast("Clipboard is empty", "Copy a PGN or FEN first.", InfoBarSeverity.Warning);
                return;
            }
            string text = (await content.GetTextAsync()).Trim();
            if (Fen.TryParse(text, out _, out _)) LoadFen(text);
            else LoadPgn(text);
        }
        catch (Exception ex)
        {
            ShowToast("Couldn't read the clipboard", ex.Message, InfoBarSeverity.Error);
        }
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void ShowToast(string title, string message, InfoBarSeverity severity)
    {
        Toast.Title = title;
        Toast.Message = message;
        Toast.Severity = severity;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ApplySettings()
    {
        AppSettings s = App.Settings.Current;
        if (Board.Theme.Id != s.BoardTheme) Board.Theme = BoardThemes.Get(s.BoardTheme);
        if (Board.PieceSet.Id != s.PieceSet) Board.PieceSet = PieceSets.Get(s.PieceSet);
        Board.ShowLegalMoves = s.ShowLegalMoves;
        Board.ShowCoordinates = s.ShowCoordinates;
        Board.HighlightLastMove = s.HighlightLastMove;
        Board.AnimateMoves = s.AnimateMoves;
        Board.AutoQueen = s.AutoQueen;
    }

    private void AddAccelerator(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key };
        accelerator.Invoked += (_, args) =>
        {
            if (FocusManager.GetFocusedElement(XamlRoot) is TextBox) return; // let the FEN box keep its arrows
            action();
            args.Handled = true;
        };
        KeyboardAccelerators.Add(accelerator);
    }
}
