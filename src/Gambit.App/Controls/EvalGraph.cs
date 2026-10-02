using Gambit.Engine.Review;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WColor = Windows.UI.Color;

namespace Gambit.App.Controls;

/// <summary>
/// Area chart of White's winning chances across a game (white area = White's share). Click or drag
/// to jump to a move; key moments are marked with colored dots.
/// </summary>
public sealed partial class EvalGraph : UserControl
{
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 58, 58, 58)) };
    private IReadOnlyList<int> _evals = [];
    private IReadOnlyList<(int Ply, WColor Color)> _markers = [];
    private int _current;

    public EvalGraph()
    {
        var border = new Border { CornerRadius = new CornerRadius(6), Child = _canvas };
        Content = border;
        _canvas.SizeChanged += (_, _) => Redraw();
        _canvas.PointerPressed += OnPointer;
        _canvas.PointerMoved += (s, e) =>
        {
            if (e.Pointer.IsInContact) OnPointer(s, e);
        };
    }

    public event EventHandler<int>? PlySelected;

    /// <summary>Evaluations (centipawns, White's view) for ply 0..N, plus colored key-moment markers.</summary>
    public void SetData(IReadOnlyList<int> whiteEvals, IReadOnlyList<(int Ply, WColor Color)> markers)
    {
        _evals = whiteEvals;
        _markers = markers;
        Redraw();
    }

    public void SetCurrent(int ply)
    {
        _current = ply;
        Redraw();
    }

    private void OnPointer(object sender, PointerRoutedEventArgs e)
    {
        if (_evals.Count < 2) return;
        double x = e.GetCurrentPoint(_canvas).Position.X;
        int ply = (int)Math.Round(x / Math.Max(1, _canvas.ActualWidth) * (_evals.Count - 1));
        PlySelected?.Invoke(this, Math.Clamp(ply, 0, _evals.Count - 1));
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
        if (w <= 0 || h <= 0 || _evals.Count == 0) return;

        int n = Math.Max(1, _evals.Count - 1);
        double X(int i) => i * w / n;
        double Y(int i) => h * (1 - GameReviewer.WinPercent(_evals[i]) / 100.0);

        var area = new PathFigure { StartPoint = new Point(0, h), IsClosed = true, IsFilled = true };
        for (int i = 0; i < _evals.Count; i++) area.Segments.Add(new LineSegment { Point = new Point(X(i), Y(i)) });
        area.Segments.Add(new LineSegment { Point = new Point(w, h) });
        var geometry = new PathGeometry();
        geometry.Figures.Add(area);
        _canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 236, 236, 236)) });

        _canvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h / 2, Y2 = h / 2, Stroke = new SolidColorBrush(ColorHelper.FromArgb(90, 128, 128, 128)), StrokeThickness = 1 });

        foreach (var (ply, color) in _markers)
        {
            if (ply < 0 || ply >= _evals.Count) continue;
            var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 1 };
            Canvas.SetLeft(dot, X(ply) - 4);
            Canvas.SetTop(dot, Math.Clamp(Y(ply), 4, h - 4) - 4);
            _canvas.Children.Add(dot);
        }

        double cx = X(Math.Clamp(_current, 0, _evals.Count - 1));
        _canvas.Children.Add(new Line { X1 = cx, X2 = cx, Y1 = 0, Y2 = h, Stroke = new SolidColorBrush(ColorHelper.FromArgb(255, 76, 194, 255)), StrokeThickness = 2 });
    }
}
