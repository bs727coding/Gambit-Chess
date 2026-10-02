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

/// <summary>
/// Free analysis: move both sides, engine lines with an eval bar, FEN/PGN import and export. Moves
/// played from an earlier position become variations (<see cref="MoveTree"/>); the page shows one
/// line at a time as a <see cref="Game"/> and offers the alternatives at each position.
/// </summary>
public sealed partial class AnalysisPage : Page
{
    private readonly AnalysisEngine _engine = new();
    private MoveTree _tree = new();
    private List<MoveNode> _line;       // root first: the line on display
    private Game _game = NewGame(null); // _line replayed (move list, openings, navigation)
    private Dictionary<string, string> _tags = [];
    private int _viewPly = -1;
    private bool _editing;
    private readonly Piece[] _editBoard = new Piece[64];
    private Piece _editPiece = Piece.WhiteQueen;
    private readonly List<Button> _paletteButtons = [];
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public AnalysisPage()
    {
        _line = MoveTree.LineThrough(_tree.Root);
        InitializeComponent();
        _engine.InfoUpdated += Engine_InfoUpdated;
        _toastTimer.Tick += (_, _) =>
        {
            Toast.IsOpen = false;
            _toastTimer.Stop();
        };
        Board.BoardSizeChanged += (_, size) => Eval.Height = size;
        Board.Interaction = BoardInteraction.Both;
        Board.SquareClicked += Board_SquareClicked;
        BuildPalette();

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
        AchievementService.Instance.Unlock("analyst");
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
        _tree = MoveTree.FromGame(game);
        _tags = new Dictionary<string, string>(game.Tags);
        ShowLine(MoveTree.LineThrough(_tree.Root)[^1]);
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
        _tree = new MoveTree(pos.ToFen());
        _tags = [];
        ShowLine(_tree.Root);
    }

    // ------------------------------------------------------------------ board + history

    private MoveNode CurrentNode => _line[_viewPly < 0 ? _line.Count - 1 : _viewPly];

    /// <summary>Displays the line through <paramref name="node"/> with the board at <paramref name="viewAt"/> (default: the node).</summary>
    private void ShowLine(MoveNode node, MoveNode? viewAt = null, bool animate = false)
    {
        _line = MoveTree.LineThrough(node);
        _game = _tree.ToGame(_line);
        foreach (var (name, value) in _tags) _game.Tags[name] = value;
        int ply = (viewAt ?? node).Ply;
        _viewPly = ply >= _game.Moves.Count ? -1 : ply;
        Refresh(animate);
    }

    private void Board_MoveRequested(object? sender, BoardMoveEventArgs e)
    {
        // A move from an earlier position starts (or follows) a variation; the old line is kept.
        MoveNode at = CurrentNode;
        if (!MoveGenerator.LegalMoves(at.Position).Contains(e.Move)) return;
        MoveNode child = _tree.Play(at, e.Move);
        if (at.Ply + 1 < _line.Count && ReferenceEquals(_line[at.Ply + 1], child)) ShowPly(at.Ply + 1);
        else ShowLine(child, animate: true);
        SoundService.PlayFor(_game.Moves[at.Ply]);
    }

    private void UpdateVariations()
    {
        MoveNode at = CurrentNode;
        MoveNode? next = at.Ply + 1 < _line.Count ? _line[at.Ply + 1] : null;
        List<MoveNode> others = at.Children.Where(c => !ReferenceEquals(c, next)).ToList();
        AlternativeButtons.Children.Clear();
        foreach (MoveNode alternative in others)
        {
            var button = new Button { Content = MoveLabel(alternative), Padding = new Thickness(8, 2, 8, 3), FontSize = 12 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Variation {MoveLabel(alternative)}");
            ToolTipService.SetToolTip(button, "Switch to this line");
            button.Click += (_, _) => ShowLine(alternative, animate: true);
            AlternativeButtons.Children.Add(button);
        }
        AlternativesRow.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        MoveNode? branch = _line[^1].BranchPoint;
        SideLineRow.Visibility = branch != null ? Visibility.Visible : Visibility.Collapsed;
        if (branch != null) SideLineText.Text = $"Side line from {MoveLabel(branch)}";
        VariationPanel.Visibility = others.Count > 0 || branch != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string MoveLabel(MoveNode n) => $"{n.MoveNumber}{(n.Side == Color.White ? "." : "…")} {n.San}";

    private void PromoteLine_Click(object sender, RoutedEventArgs e)
    {
        MoveNode view = CurrentNode;
        MoveTree.Promote(_line[^1]);
        ShowLine(_line[^1], view);
        ShowToast("Main line updated", "This line is now the main line.", InfoBarSeverity.Success);
    }

    private void DeleteLine_Click(object sender, RoutedEventArgs e)
    {
        if (_line[^1].BranchPoint is not MoveNode branch || branch.Parent is not MoveNode parent) return;
        MoveTree.Remove(branch);
        ShowLine(parent);
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
        UpdateVariations();
    }

    private void Refresh(bool animate)
    {
        MoveList.SetMoves(_game.Moves, _viewPly < 0 ? _game.Moves.Count : _viewPly);
        ShowBoard(animate);
        UpdateVariations();
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
        _tree = new MoveTree();
        _tags = [];
        ShowLine(_tree.Root);
    }

    private void LoadFen_Click(object sender, RoutedEventArgs e) => LoadFen(FenBox.Text);

    private void CopyFen_Click(object sender, RoutedEventArgs e)
    {
        CopyText(Board.Position.ToFen());
        ShowToast("FEN copied", "The current position is on the clipboard.", InfoBarSeverity.Success);
    }

    private void CopyPgn_Click(object sender, RoutedEventArgs e)
    {
        Game main = _tree.ToGame(_tree.MainLine());
        foreach (var (name, value) in _tags) main.Tags[name] = value;
        CopyText(Pgn.Write(main, _tree));
        bool hasVariations = HasVariations(_tree.Root);
        ShowToast("PGN copied", hasVariations ? "The game and its variations are on the clipboard." : "The game is on the clipboard.", InfoBarSeverity.Success);
    }

    private static bool HasVariations(MoveNode n) => n.Children.Count > 1 || n.Children.Any(HasVariations);

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

    // ------------------------------------------------------------------ position editor

    private void BuildPalette()
    {
        Palette.Children.Clear();
        _paletteButtons.Clear();
        Piece[] pieces =
        [
            Piece.WhiteKing, Piece.WhiteQueen, Piece.WhiteRook, Piece.WhiteBishop, Piece.WhiteKnight, Piece.WhitePawn, Piece.None,
            Piece.BlackKing, Piece.BlackQueen, Piece.BlackRook, Piece.BlackBishop, Piece.BlackKnight, Piece.BlackPawn,
        ];
        foreach (Piece p in pieces)
        {
            var button = new Button
            {
                Width = 44,
                Height = 44,
                Padding = new Thickness(4),
                Tag = p,
                Content = p == Piece.None ? new FontIcon { Glyph = "\uE75C", FontSize = 18 } : Board.PieceSet.Create(p),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, p == Piece.None ? "Eraser" : p.ToString());
            ToolTipService.SetToolTip(button, p == Piece.None ? "Eraser" : p.ToString());
            button.Click += (_, _) =>
            {
                _editPiece = p;
                UpdatePaletteSelection();
            };
            _paletteButtons.Add(button);
            Palette.Children.Add(button);
        }
        UpdatePaletteSelection();
    }

    private void UpdatePaletteSelection()
    {
        foreach (Button b in _paletteButtons)
        {
            bool selected = b.Tag is Piece p && p == _editPiece;
            b.BorderBrush = selected ? Helpers.Ui.AccentBrush : null;
            b.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        Position current = Board.Position;
        for (int sq = 0; sq < 64; sq++) _editBoard[sq] = current.PieceAt(sq);
        EditSide.SelectedIndex = current.SideToMove == Color.White ? 0 : 1;
        CastleWK.IsChecked = current.Castling.HasFlag(CastlingRights.WhiteKingSide);
        CastleWQ.IsChecked = current.Castling.HasFlag(CastlingRights.WhiteQueenSide);
        CastleBK.IsChecked = current.Castling.HasFlag(CastlingRights.BlackKingSide);
        CastleBQ.IsChecked = current.Castling.HasFlag(CastlingRights.BlackQueenSide);
        SetEditing(true);
    }

    private void SetEditing(bool editing)
    {
        _editing = editing;
        EditorCard.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        MovesCard.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        Board.Interaction = editing ? BoardInteraction.None : BoardInteraction.Both;
        if (editing)
        {
            _engine.Stop();
            Lines.Children.Clear();
            EngineInfo.Text = "Paused while editing";
            BuildPalette();
            RenderEditor();
        }
        else
        {
            ShowBoard(animate: false);
        }
    }

    private void Board_SquareClicked(object? sender, int sq)
    {
        if (!_editing) return;
        _editBoard[sq] = _editBoard[sq] == _editPiece ? Piece.None : _editPiece;
        // Pawns can't stand on the first or last rank.
        if (_editBoard[sq].Type() == PieceType.Pawn && Square.Rank(sq) is 0 or 7) _editBoard[sq] = Piece.None;
        RenderEditor();
    }

    private string EditorFen(bool withRights)
    {
        var sb = new System.Text.StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                Piece p = _editBoard[Square.Make(file, rank)];
                if (p == Piece.None)
                {
                    empty++;
                    continue;
                }
                if (empty > 0) sb.Append(empty);
                empty = 0;
                sb.Append(p.ToFenChar());
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }
        sb.Append(EditSide.SelectedIndex == 1 ? " b " : " w ");
        string castling = withRights
            ? (CastleWK.IsChecked == true ? "K" : "") + (CastleWQ.IsChecked == true ? "Q" : "") + (CastleBK.IsChecked == true ? "k" : "") + (CastleBQ.IsChecked == true ? "q" : "")
            : "";
        sb.Append(castling.Length == 0 ? "-" : castling).Append(" - 0 1");
        return sb.ToString();
    }

    private void RenderEditor()
    {
        try
        {
            Board.SetPosition(Position.FromFen(EditorFen(withRights: false)));
            Board.SetMarkers([]);
            FenBox.Text = EditorFen(withRights: true);
        }
        catch (FenException)
        {
            // The editor only produces syntactically valid FEN; ignore transient states.
        }
    }

    private void EditStart_Click(object sender, RoutedEventArgs e)
    {
        Position start = Position.Start();
        for (int sq = 0; sq < 64; sq++) _editBoard[sq] = start.PieceAt(sq);
        EditSide.SelectedIndex = 0;
        CastleWK.IsChecked = CastleWQ.IsChecked = CastleBK.IsChecked = CastleBQ.IsChecked = true;
        RenderEditor();
    }

    private void EditClear_Click(object sender, RoutedEventArgs e)
    {
        Array.Clear(_editBoard);
        CastleWK.IsChecked = CastleWQ.IsChecked = CastleBK.IsChecked = CastleBQ.IsChecked = false;
        RenderEditor();
    }

    private void EditCancel_Click(object sender, RoutedEventArgs e) => SetEditing(false);

    private void EditDone_Click(object sender, RoutedEventArgs e)
    {
        string fen = EditorFen(withRights: true);
        if (!Fen.TryParse(fen, out Position? pos, out string? error) || pos == null)
        {
            ShowToast("This position isn't legal", error ?? "Check the kings and pawns.", InfoBarSeverity.Warning);
            return;
        }
        _editing = false;
        EditorCard.Visibility = Visibility.Collapsed;
        MovesCard.Visibility = Visibility.Visible;
        Board.Interaction = BoardInteraction.Both;
        LoadFen(pos.ToFen());
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
