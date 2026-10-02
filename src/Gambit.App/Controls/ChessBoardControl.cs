using System.Numerics;
using Gambit.App.Theming;
using Gambit.Core.Board;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using WColor = Windows.UI.Color;

namespace Gambit.App.Controls;

/// <summary>Which colors the user may move on the board.</summary>
[Flags]
public enum BoardInteraction
{
    None = 0,
    White = 1,
    Black = 2,
    Both = White | Black,
}

public sealed class BoardMoveEventArgs(Move move, bool dragged) : EventArgs
{
    public Move Move { get; } = move;

    /// <summary>True if the piece was dragged (no animation needed when the position updates).</summary>
    public bool Dragged { get; } = dragged;
}

/// <summary>A colored square highlight or arrow drawn on top of the board (hints, lessons, analysis).</summary>
public readonly record struct BoardMarker(int From, int To, WColor Color)
{
    public bool IsArrow => From != To;
    public static BoardMarker Square(int sq, WColor color) => new(sq, sq, color);
    public static BoardMarker Arrow(int from, int to, WColor color) => new(from, to, color);
}

/// <summary>
/// Interactive chess board. Display-only state: the owner sets the position and reacts to
/// <see cref="MoveRequested"/>; the board validates gestures against the legal moves of the
/// position it shows. Supports click-to-move, drag-and-drop, promotion picker, flip, coordinates,
/// legal-move hints, last-move/check highlights, move animation and right-click arrows.
/// </summary>
public sealed partial class ChessBoardControl : UserControl
{
    private static readonly TimeSpan MoveAnimation = TimeSpan.FromMilliseconds(170);
    private static readonly WColor UserArrowColor = ColorHelper.FromArgb(200, 255, 170, 0);
    private static readonly WColor UserCircleColor = ColorHelper.FromArgb(200, 255, 170, 0);

    private readonly Grid _host = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly Grid _board = new() { Background = new SolidColorBrush(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _surface = new();
    private readonly Canvas _squareLayer = new();
    private readonly Canvas _highlightLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _coordLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _hintLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _pieceLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _markerLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _overlayLayer = new();

    private readonly Rectangle[] _squareRects = new Rectangle[64];
    private readonly TextBlock[] _fileLabels = new TextBlock[8];
    private readonly TextBlock[] _rankLabels = new TextBlock[8];
    private readonly FrameworkElement?[] _pieceVisuals = new FrameworkElement?[64];
    private readonly Piece[] _visualPieces = new Piece[64];

    private Position _position = Position.Start();
    private List<Move> _legal = [];
    private Move _lastMove;
    private double _sq;
    private int _selected = -1;
    private BoardTheme _theme = BoardThemes.All[0];
    private PieceSet _pieceSet = PieceSets.All[0];
    private bool _flipped;
    private IReadOnlyList<BoardMarker> _markers = [];
    private readonly List<BoardMarker> _userMarkers = [];

    // Pointer state
    private bool _pressed;
    private bool _dragging;
    private int _dragFrom = -1;
    private Point _pressPoint;
    private int _rightFrom = -1;
    private Move _pendingUserMove;
    private bool _pendingDragged;
    private int _hoverSquare = -1;

    // Promotion picker state
    private List<Move>? _promotionChoices;
    private bool _promotionDragged;

    public ChessBoardControl()
    {
        IsTabStop = true;
        _surface.Children.Add(_squareLayer);
        _surface.Children.Add(_highlightLayer);
        _surface.Children.Add(_coordLayer);
        _surface.Children.Add(_hintLayer);
        _board.Children.Add(_surface);
        _board.Children.Add(_pieceLayer);
        _board.Children.Add(_markerLayer);
        _board.Children.Add(_overlayLayer);
        _host.Children.Add(_board);
        Content = _host;

        for (int sq = 0; sq < 64; sq++)
        {
            _squareRects[sq] = new Rectangle();
            _squareLayer.Children.Add(_squareRects[sq]);
        }
        for (int i = 0; i < 8; i++)
        {
            _fileLabels[i] = CoordLabel();
            _rankLabels[i] = CoordLabel();
            _coordLayer.Children.Add(_fileLabels[i]);
            _coordLayer.Children.Add(_rankLabels[i]);
        }

        _host.SizeChanged += (_, _) => UpdateLayoutAndRedraw();
        _board.PointerPressed += OnPointerPressed;
        _board.PointerMoved += OnPointerMoved;
        _board.PointerReleased += OnPointerReleased;
        _board.PointerCaptureLost += (_, _) => CancelDrag();
        _board.RightTapped += (_, e) => e.Handled = true;
        KeyDown += OnKeyDown;
        _legal = MoveGenerator.LegalMoves(_position);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new BoardAutomationPeer(this);

    /// <summary>
    /// Exposes the board to UI Automation: the board plus one invokable element per square (named
    /// like "e4, white pawn"), so screen-reader users can play by activating squares and UI tests
    /// can make moves without the mouse.
    /// </summary>
    private sealed partial class BoardAutomationPeer(ChessBoardControl owner) : FrameworkElementAutomationPeer(owner)
    {
        private List<AutomationPeer>? _squares;

        protected override string GetClassNameCore() => "ChessBoard";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string GetNameCore() =>
            $"Chess board, {(((ChessBoardControl)Owner)._position.SideToMove == Color.White ? "White" : "Black")} to move";

        protected override IList<AutomationPeer> GetChildrenCore()
        {
            if (_squares == null)
            {
                _squares = [];
                for (int rank = 7; rank >= 0; rank--)
                    for (int file = 0; file < 8; file++)
                        _squares.Add(new SquareAutomationPeer((ChessBoardControl)Owner, Core.Board.Square.Make(file, rank)));
            }
            return _squares;
        }
    }

    private sealed partial class SquareAutomationPeer(ChessBoardControl board, int square) : AutomationPeer, IInvokeProvider
    {
        protected override string GetNameCore()
        {
            Piece p = board._position.PieceAt(square);
            string name = Core.Board.Square.Name(square);
            if (p == Piece.None) return name;
            return $"{name}, {(p.Color() == Color.White ? "white" : "black")} {p.Type().ToString().ToLowerInvariant()}";
        }

        protected override string GetAutomationIdCore() => "sq-" + Core.Board.Square.Name(square);
        protected override string GetClassNameCore() => "BoardSquare";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override object? GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : null;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsEnabledCore() => true;
        protected override bool IsOffscreenCore() => false;

        public void Invoke() => board.ActivateSquare(square);
    }

    /// <summary>Click-equivalent for a square: select a piece, or move the selected piece there.</summary>
    public void ActivateSquare(int sq)
    {
        if (_promotionChoices != null) CancelPromotion();
        if (_selected >= 0 && _selected != sq && TryCompleteMove(_selected, sq, dragged: false)) return;
        if (CanMovePieceOn(sq))
        {
            _selected = sq;
            RedrawOverlays();
        }
        else
        {
            ClearSelection();
        }
        SquareClicked?.Invoke(this, sq);
    }

    // ------------------------------------------------------------------ public API

    /// <summary>The user completed a legal move gesture (already validated, promotion chosen).</summary>
    public event EventHandler<BoardMoveEventArgs>? MoveRequested;

    /// <summary>The user clicked a square (after move handling) — used by puzzles/lessons.</summary>
    public event EventHandler<int>? SquareClicked;

    /// <summary>The drawn board's edge length changed (lets pages align player bars with the board).</summary>
    public event EventHandler<double>? BoardSizeChanged;

    public double BoardSize => 8 * _sq;

    public Position Position => _position;

    public BoardInteraction Interaction { get; set; } = BoardInteraction.Both;

    /// <summary>Allow moving pieces of the side that is not to move (e.g. board editor). Default false.</summary>
    public bool IgnoreTurn { get; set; }

    public bool ShowLegalMoves { get; set; } = true;
    public bool HighlightLastMove { get; set; } = true;
    public bool AnimateMoves { get; set; } = true;
    public bool AutoQueen { get; set; }

    public bool ShowCoordinates
    {
        get => _coordLayer.Visibility == Visibility.Visible;
        set => _coordLayer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool Flipped
    {
        get => _flipped;
        set
        {
            if (_flipped == value) return;
            _flipped = value;
            RebuildPieces();
            UpdateLayoutAndRedraw();
        }
    }

    public BoardTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            UpdateLayoutAndRedraw();
        }
    }

    public PieceSet PieceSet
    {
        get => _pieceSet;
        set
        {
            _pieceSet = value;
            RebuildPieces();
            PositionPieces();
        }
    }

    /// <summary>Shows a new position. Animates <paramref name="lastMove"/> when requested and enabled.</summary>
    public void SetPosition(Position position, Move lastMove = default, bool animate = false)
    {
        CloseGesture();
        _position = position.Clone();
        _lastMove = lastMove;
        _legal = MoveGenerator.LegalMoves(_position);
        _selected = -1;
        _userMarkers.Clear();

        bool skipAnimation = _pendingDragged && lastMove == _pendingUserMove;
        _pendingUserMove = Move.None;
        _pendingDragged = false;

        SyncPieces();
        PositionPieces();
        if (animate && AnimateMoves && !skipAnimation && !lastMove.IsNone) AnimateMove(lastMove);
        RedrawOverlays();
    }

    /// <summary>Programmatic highlights and arrows (cleared by passing an empty list).</summary>
    public void SetMarkers(IReadOnlyList<BoardMarker> markers)
    {
        _markers = markers;
        DrawMarkers();
    }

    public void ClearSelection()
    {
        _selected = -1;
        RedrawOverlays();
    }

    // ------------------------------------------------------------------ layout

    private void UpdateLayoutAndRedraw()
    {
        double w = _host.ActualWidth, h = _host.ActualHeight;
        if (w <= 0 || h <= 0) return;
        double size = Math.Floor(Math.Min(w, h) / 8) * 8;
        if (size < 64) size = 64;
        _sq = size / 8;
        _board.Width = _board.Height = size;
        foreach (Canvas c in new[] { _squareLayer, _highlightLayer, _coordLayer, _hintLayer, _pieceLayer, _markerLayer, _overlayLayer })
        {
            c.Width = size;
            c.Height = size;
        }

        ApplyRoundedClip(size);
        BoardSizeChanged?.Invoke(this, size);

        var light = new SolidColorBrush(_theme.Light);
        var dark = new SolidColorBrush(_theme.Dark);
        for (int sq = 0; sq < 64; sq++)
        {
            Rectangle r = _squareRects[sq];
            r.Width = r.Height = _sq + 0.5; // overlap a hair to avoid seams
            Point p = SquareOrigin(sq);
            Canvas.SetLeft(r, p.X);
            Canvas.SetTop(r, p.Y);
            r.Fill = Square.IsLight(sq) ? light : dark;
        }

        double fontSize = Math.Max(9, _sq * 0.17);
        for (int i = 0; i < 8; i++)
        {
            // Files along the bottom edge (right-aligned), ranks along the left edge (top-aligned).
            int file = _flipped ? 7 - i : i;
            int bottomSquare = Core.Board.Square.Make(file, _flipped ? 7 : 0);
            TextBlock f = _fileLabels[i];
            f.Text = ((char)('a' + file)).ToString();
            f.FontSize = fontSize;
            f.Foreground = new SolidColorBrush(Square.IsLight(bottomSquare) ? _theme.CoordinateOnLight : _theme.CoordinateOnDark);
            Canvas.SetLeft(f, i * _sq + _sq - fontSize * 0.75);
            Canvas.SetTop(f, 8 * _sq - fontSize * 1.35);

            int rank = _flipped ? i : 7 - i;
            int leftSquare = Core.Board.Square.Make(_flipped ? 7 : 0, rank);
            TextBlock r = _rankLabels[i];
            r.Text = (rank + 1).ToString();
            r.FontSize = fontSize;
            r.Foreground = new SolidColorBrush(Square.IsLight(leftSquare) ? _theme.CoordinateOnLight : _theme.CoordinateOnDark);
            Canvas.SetLeft(r, fontSize * 0.25);
            Canvas.SetTop(r, i * _sq + fontSize * 0.1);
        }

        PositionPieces();
        RedrawOverlays();
        if (_promotionChoices != null) ShowPromotionPicker(_promotionChoices, _promotionDragged);
    }

    private void ApplyRoundedClip(double size)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(_surface);
        Compositor compositor = visual.Compositor;
        CompositionRoundedRectangleGeometry geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new Vector2((float)size, (float)size);
        geometry.CornerRadius = new Vector2(6, 6);
        visual.Clip = compositor.CreateGeometricClip(geometry);
    }

    private static TextBlock CoordLabel() => new()
    {
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        IsHitTestVisible = false,
    };

    private Point SquareOrigin(int sq)
    {
        int f = Square.File(sq), r = Square.Rank(sq);
        return _flipped ? new Point((7 - f) * _sq, r * _sq) : new Point(f * _sq, (7 - r) * _sq);
    }

    private Point SquareCenter(int sq)
    {
        Point o = SquareOrigin(sq);
        return new Point(o.X + _sq / 2, o.Y + _sq / 2);
    }

    private int SquareAt(Point p)
    {
        if (_sq <= 0 || p.X < 0 || p.Y < 0 || p.X >= 8 * _sq || p.Y >= 8 * _sq) return -1;
        int col = (int)(p.X / _sq), row = (int)(p.Y / _sq);
        return _flipped ? Core.Board.Square.Make(7 - col, row) : Core.Board.Square.Make(col, 7 - row);
    }

    // ------------------------------------------------------------------ pieces

    private void RebuildPieces()
    {
        for (int sq = 0; sq < 64; sq++) RemoveVisual(sq);
        SyncPieces();
    }

    private void SyncPieces()
    {
        for (int sq = 0; sq < 64; sq++)
        {
            Piece p = _position.PieceAt(sq);
            if (p == _visualPieces[sq] && (_pieceVisuals[sq] != null || p == Piece.None)) continue;
            RemoveVisual(sq);
            if (p == Piece.None) continue;
            FrameworkElement v = _pieceSet.Create(p);
            v.RenderTransform = new TranslateTransform();
            _pieceVisuals[sq] = v;
            _visualPieces[sq] = p;
            _pieceLayer.Children.Add(v);
        }
    }

    private void RemoveVisual(int sq)
    {
        if (_pieceVisuals[sq] is FrameworkElement v) _pieceLayer.Children.Remove(v);
        _pieceVisuals[sq] = null;
        _visualPieces[sq] = Piece.None;
    }

    private void PositionPieces()
    {
        if (_sq <= 0) return;
        double inset = _sq * 0.04;
        for (int sq = 0; sq < 64; sq++)
        {
            if (_pieceVisuals[sq] is not FrameworkElement v) continue;
            Point o = SquareOrigin(sq);
            v.Width = v.Height = _sq - 2 * inset;
            Canvas.SetLeft(v, o.X + inset);
            Canvas.SetTop(v, o.Y + inset);
            Canvas.SetZIndex(v, 0);
            if (v.RenderTransform is TranslateTransform t && !(_dragging && sq == _dragFrom))
            {
                t.X = 0;
                t.Y = 0;
            }
        }
    }

    private void AnimateMove(Move m)
    {
        AnimateFromTo(m.From, m.To);
        if (m.Flag == MoveFlag.KingCastle) AnimateFromTo(m.To + 1, m.To - 1);
        else if (m.Flag == MoveFlag.QueenCastle) AnimateFromTo(m.To - 2, m.To + 1);
    }

    private void AnimateFromTo(int from, int to)
    {
        if (_pieceVisuals[to] is not FrameworkElement v || v.RenderTransform is not TranslateTransform t) return;
        Point a = SquareOrigin(from), b = SquareOrigin(to);
        Canvas.SetZIndex(v, 10);
        Animate(t, a.X - b.X, a.Y - b.Y);
    }

    private static void Animate(TranslateTransform t, double fromX, double fromY)
    {
        t.X = 0;
        t.Y = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var ax = new DoubleAnimation { From = fromX, To = 0, Duration = MoveAnimation, EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var ay = new DoubleAnimation { From = fromY, To = 0, Duration = MoveAnimation, EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        Storyboard.SetTarget(ax, t);
        Storyboard.SetTargetProperty(ax, "X");
        Storyboard.SetTarget(ay, t);
        Storyboard.SetTargetProperty(ay, "Y");
        var sb = new Storyboard();
        sb.Children.Add(ax);
        sb.Children.Add(ay);
        sb.Begin();
    }

    // ------------------------------------------------------------------ overlays

    private void RedrawOverlays()
    {
        DrawHighlights();
        DrawHints();
        DrawMarkers();
    }

    private void DrawHighlights()
    {
        _highlightLayer.Children.Clear();
        if (_sq <= 0) return;
        var hl = new SolidColorBrush(_theme.Highlight);

        if (HighlightLastMove && !_lastMove.IsNone)
        {
            AddSquareFill(_lastMove.From, hl);
            AddSquareFill(_lastMove.To, hl);
        }
        if (_selected >= 0) AddSquareFill(_selected, hl);

        if (_position.InCheck)
        {
            int k = _position.KingSquare(_position.SideToMove);
            Point o = SquareOrigin(k);
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop { Color = _theme.CheckColor, Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(170, _theme.CheckColor.R, _theme.CheckColor.G, _theme.CheckColor.B), Offset = 0.35 });
            brush.GradientStops.Add(new GradientStop { Color = ColorHelper.FromArgb(0, _theme.CheckColor.R, _theme.CheckColor.G, _theme.CheckColor.B), Offset = 1 });
            var e = new Ellipse { Width = _sq, Height = _sq, Fill = brush };
            Canvas.SetLeft(e, o.X);
            Canvas.SetTop(e, o.Y);
            _highlightLayer.Children.Add(e);
        }

        if (_dragging && _hoverSquare >= 0)
        {
            Point o = SquareOrigin(_hoverSquare);
            var r = new Rectangle
            {
                Width = _sq,
                Height = _sq,
                Stroke = new SolidColorBrush(ColorHelper.FromArgb(170, 255, 255, 255)),
                StrokeThickness = Math.Max(2, _sq * 0.05),
            };
            Canvas.SetLeft(r, o.X);
            Canvas.SetTop(r, o.Y);
            _highlightLayer.Children.Add(r);
        }
    }

    private void AddSquareFill(int sq, Brush brush)
    {
        Point o = SquareOrigin(sq);
        var r = new Rectangle { Width = _sq + 0.5, Height = _sq + 0.5, Fill = brush };
        Canvas.SetLeft(r, o.X);
        Canvas.SetTop(r, o.Y);
        _highlightLayer.Children.Add(r);
    }

    private void DrawHints()
    {
        _hintLayer.Children.Clear();
        if (_selected < 0 || !ShowLegalMoves || _sq <= 0) return;
        var brush = new SolidColorBrush(_theme.HintColor);
        foreach (int to in _legal.Where(m => m.From == _selected).Select(m => m.To).Distinct())
        {
            Point c = SquareCenter(to);
            bool capture = _position.PieceAt(to) != Piece.None || _legal.Any(m => m.From == _selected && m.To == to && m.IsEnPassant);
            Ellipse e;
            if (capture)
            {
                double d = _sq * 0.92, thick = _sq * 0.085;
                e = new Ellipse { Width = d, Height = d, Stroke = brush, StrokeThickness = thick };
                Canvas.SetLeft(e, c.X - d / 2);
                Canvas.SetTop(e, c.Y - d / 2);
            }
            else
            {
                double d = _sq * 0.32;
                e = new Ellipse { Width = d, Height = d, Fill = brush };
                Canvas.SetLeft(e, c.X - d / 2);
                Canvas.SetTop(e, c.Y - d / 2);
            }
            _hintLayer.Children.Add(e);
        }
    }

    private void DrawMarkers()
    {
        _markerLayer.Children.Clear();
        if (_sq <= 0) return;
        foreach (BoardMarker m in _markers.Concat(_userMarkers))
        {
            if (m.IsArrow) _markerLayer.Children.Add(CreateArrow(m.From, m.To, m.Color));
            else _markerLayer.Children.Add(CreateCircle(m.From, m.Color));
        }
    }

    private Ellipse CreateCircle(int sq, WColor color)
    {
        Point c = SquareCenter(sq);
        double thick = _sq * 0.07, d = _sq - thick;
        var e = new Ellipse { Width = d, Height = d, Stroke = new SolidColorBrush(color), StrokeThickness = thick };
        Canvas.SetLeft(e, c.X - d / 2);
        Canvas.SetTop(e, c.Y - d / 2);
        return e;
    }

    private Microsoft.UI.Xaml.Shapes.Path CreateArrow(int from, int to, WColor color)
    {
        Point a = SquareCenter(from), b = SquareCenter(to);
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double ux = dx / len, uy = dy / len, px = -uy, py = ux;
        double shaft = _sq * 0.09, head = _sq * 0.24, headLen = _sq * 0.42;
        double startOffset = _sq * 0.28;
        Point s = new(a.X + ux * startOffset, a.Y + uy * startOffset);
        Point hb = new(b.X - ux * headLen, b.Y - uy * headLen);

        var fig = new PathFigure { StartPoint = new Point(s.X + px * shaft, s.Y + py * shaft), IsClosed = true, IsFilled = true };
        foreach (Point p in new[]
        {
            new Point(hb.X + px * shaft, hb.Y + py * shaft),
            new Point(hb.X + px * head, hb.Y + py * head),
            b,
            new Point(hb.X - px * head, hb.Y - py * head),
            new Point(hb.X - px * shaft, hb.Y - py * shaft),
            new Point(s.X - px * shaft, s.Y - py * shaft),
        })
        {
            fig.Segments.Add(new LineSegment { Point = p });
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(fig);
        return new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = new SolidColorBrush(color) };
    }

    // ------------------------------------------------------------------ input

    private bool CanMovePieceOn(int sq)
    {
        Piece p = _position.PieceAt(sq);
        if (p == Piece.None) return false;
        Color c = p.Color();
        if (!IgnoreTurn && c != _position.SideToMove) return false;
        return c == Color.White ? Interaction.HasFlag(BoardInteraction.White) : Interaction.HasFlag(BoardInteraction.Black);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        PointerPoint pt = e.GetCurrentPoint(_board);
        int sq = SquareAt(pt.Position);

        if (_promotionChoices != null)
        {
            // Clicks while the picker is open are handled by the picker; anything else cancels.
            CancelPromotion();
            e.Handled = true;
            return;
        }

        if (pt.Properties.IsRightButtonPressed)
        {
            _rightFrom = sq;
            _selected = -1;
            RedrawOverlays();
            e.Handled = true;
            return;
        }

        if (!pt.Properties.IsLeftButtonPressed) return;

        if (_userMarkers.Count > 0)
        {
            _userMarkers.Clear();
            DrawMarkers();
        }

        if (sq < 0)
        {
            ClearSelection();
            return;
        }

        if (_selected >= 0 && _selected != sq && TryCompleteMove(_selected, sq, dragged: false))
        {
            e.Handled = true;
            return;
        }

        if (CanMovePieceOn(sq))
        {
            _selected = sq;
            _pressed = true;
            _dragFrom = sq;
            _pressPoint = pt.Position;
            _board.CapturePointer(e.Pointer);
            RedrawOverlays();
        }
        else
        {
            ClearSelection();
        }

        SquareClicked?.Invoke(this, sq);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed || _dragFrom < 0) return;
        Point p = e.GetCurrentPoint(_board).Position;

        if (!_dragging)
        {
            double dx = p.X - _pressPoint.X, dy = p.Y - _pressPoint.Y;
            if (dx * dx + dy * dy < 16) return;
            _dragging = true;
        }

        if (_pieceVisuals[_dragFrom] is FrameworkElement v && v.RenderTransform is TranslateTransform t)
        {
            Point center = SquareCenter(_dragFrom);
            t.X = p.X - center.X;
            t.Y = p.Y - center.Y;
            Canvas.SetZIndex(v, 100);
        }

        int hover = SquareAt(p);
        if (hover != _hoverSquare)
        {
            _hoverSquare = hover;
            DrawHighlights();
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint pt = e.GetCurrentPoint(_board);
        int sq = SquareAt(pt.Position);

        if (_rightFrom >= 0)
        {
            ToggleUserMarker(_rightFrom, sq < 0 ? _rightFrom : sq);
            _rightFrom = -1;
            e.Handled = true;
            return;
        }

        if (!_pressed) return;

        // Snapshot and reset the gesture *before* releasing capture: ReleasePointerCapture raises
        // PointerCaptureLost synchronously, which would otherwise cancel the drop.
        bool wasDragging = _dragging;
        int from = _dragFrom;
        _pressed = false;
        _dragging = false;
        _dragFrom = -1;
        _hoverSquare = -1;
        _board.ReleasePointerCapture(e.Pointer);

        if (wasDragging)
        {
            if (sq >= 0 && sq != from && TryCompleteMove(from, sq, dragged: true))
            {
                e.Handled = true;
                return;
            }
            PositionPieces(); // snap back
            DrawHighlights();
        }
        e.Handled = true;
    }

    private void CancelDrag()
    {
        if (!_dragging && !_pressed) return;
        _dragging = false;
        _pressed = false;
        _dragFrom = -1;
        _hoverSquare = -1;
        PositionPieces();
        DrawHighlights();
    }

    private void CloseGesture()
    {
        _dragging = false;
        _pressed = false;
        _dragFrom = -1;
        _hoverSquare = -1;
        _rightFrom = -1;
        _promotionChoices = null;
        _overlayLayer.Children.Clear();
    }

    private void ToggleUserMarker(int from, int to)
    {
        if (from < 0) return;
        var color = from == to ? UserCircleColor : UserArrowColor;
        int existing = _userMarkers.FindIndex(m => m.From == from && m.To == to);
        if (existing >= 0) _userMarkers.RemoveAt(existing);
        else _userMarkers.Add(new BoardMarker(from, to, color));
        DrawMarkers();
    }

    private bool TryCompleteMove(int from, int to, bool dragged)
    {
        if (!CanMovePieceOn(from)) return false;
        var candidates = _legal.Where(m => m.From == from && m.To == to).ToList();
        if (candidates.Count == 0) return false;

        if (candidates.Count > 1 && candidates.All(m => m.IsPromotion))
        {
            if (AutoQueen)
            {
                Commit(candidates.First(m => m.PromotionType == PieceType.Queen), dragged);
            }
            else
            {
                if (dragged && _pieceVisuals[from] is FrameworkElement v && v.RenderTransform is TranslateTransform t)
                {
                    Point a = SquareOrigin(from), b = SquareOrigin(to);
                    t.X = b.X - a.X;
                    t.Y = b.Y - a.Y;
                }
                ShowPromotionPicker(candidates, dragged);
            }
            return true;
        }

        Commit(candidates[0], dragged);
        return true;
    }

    private void Commit(Move move, bool dragged)
    {
        _selected = -1;
        _pendingUserMove = move;
        _pendingDragged = dragged;
        MoveRequested?.Invoke(this, new BoardMoveEventArgs(move, dragged));
        if (_pendingUserMove == move)
        {
            // The owner did not accept the move (e.g. not our turn) — restore the board.
            _pendingUserMove = Move.None;
            _pendingDragged = false;
            PositionPieces();
            RedrawOverlays();
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            if (_promotionChoices != null) CancelPromotion();
            ClearSelection();
            _userMarkers.Clear();
            DrawMarkers();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ promotion picker

    private void ShowPromotionPicker(List<Move> choices, bool dragged)
    {
        _promotionChoices = choices;
        _promotionDragged = dragged;
        _overlayLayer.Children.Clear();

        var dim = new Rectangle
        {
            Width = 8 * _sq,
            Height = 8 * _sq,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(90, 0, 0, 0)),
        };
        dim.PointerPressed += (_, e) =>
        {
            CancelPromotion();
            e.Handled = true;
        };
        _overlayLayer.Children.Add(dim);

        int to = choices[0].To;
        Color mover = _position.PieceAt(choices[0].From).Color();
        Point origin = SquareOrigin(to);
        bool downward = origin.Y < 4 * _sq; // picker grows toward the board's center
        PieceType[] order = [PieceType.Queen, PieceType.Knight, PieceType.Rook, PieceType.Bishop];

        var panel = new StackPanel
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(250, 250, 250, 250)),
            CornerRadius = new CornerRadius(6),
            Width = _sq,
        };
        foreach (PieceType type in downward ? order : order.Reverse())
        {
            Move m = choices.First(c => c.PromotionType == type);
            var cell = new Grid
            {
                Width = _sq,
                Height = _sq,
                Background = new SolidColorBrush(Colors.Transparent),
            };
            FrameworkElement piece = _pieceSet.Create(type.Of(mover));
            piece.Margin = new Thickness(_sq * 0.08);
            cell.Children.Add(piece);
            cell.PointerEntered += (_, _) => cell.Background = new SolidColorBrush(ColorHelper.FromArgb(60, 0, 120, 215));
            cell.PointerExited += (_, _) => cell.Background = new SolidColorBrush(Colors.Transparent);
            cell.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                bool wasDragged = _promotionDragged;
                _promotionChoices = null;
                _overlayLayer.Children.Clear();
                Commit(m, wasDragged);
            };
            panel.Children.Add(cell);
        }

        panel.Shadow = new ThemeShadow();
        panel.Translation = new Vector3(0, 0, 32);
        Canvas.SetLeft(panel, origin.X);
        Canvas.SetTop(panel, downward ? origin.Y : origin.Y - 3 * _sq);
        _overlayLayer.Children.Add(panel);
    }

    private void CancelPromotion()
    {
        _promotionChoices = null;
        _overlayLayer.Children.Clear();
        _selected = -1;
        PositionPieces();
        RedrawOverlays();
    }
}
