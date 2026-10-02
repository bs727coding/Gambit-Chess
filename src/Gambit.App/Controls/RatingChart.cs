using Gambit.App.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Gambit.App.Controls;

/// <summary>Minimal line chart of a rating over time (puzzle rating history).</summary>
public sealed partial class RatingChart : UserControl
{
    private readonly Canvas _canvas = new();
    private IReadOnlyList<int> _values = [];

    public RatingChart()
    {
        Content = new Border { Child = _canvas, CornerRadius = new CornerRadius(6), Background = Ui.NeutralFill(14) };
        _canvas.SizeChanged += (_, _) => Redraw();
    }

    public void SetValues(IReadOnlyList<int> values)
    {
        _values = values;
        Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        if (_values.Count < 2)
        {
            _canvas.Children.Add(new TextBlock { Text = "Solve a few rated puzzles to see your rating history.", Opacity = 0.6, Margin = new Thickness(12) });
            return;
        }

        int min = _values.Min(), max = _values.Max();
        if (max - min < 50)
        {
            min -= 25;
            max += 25;
        }
        const double padTop = 18, padBottom = 18, padLeft = 44, padRight = 10;
        double X(int i) => padLeft + i * (w - padLeft - padRight) / (_values.Count - 1);
        double Y(int v) => padTop + (max - v) * (h - padTop - padBottom) / (max - min);

        foreach (int level in new[] { min, (min + max) / 2, max })
        {
            double y = Y(level);
            _canvas.Children.Add(new Line { X1 = padLeft, X2 = w - padRight, Y1 = y, Y2 = y, Stroke = Ui.NeutralFill(60), StrokeThickness = 1 });
            var label = new TextBlock { Text = level.ToString(), FontSize = 11, Opacity = 0.6 };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 8);
            _canvas.Children.Add(label);
        }

        var line = new Polyline { Stroke = Ui.AccentBrush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        for (int i = 0; i < _values.Count; i++) line.Points.Add(new Point(X(i), Y(_values[i])));
        _canvas.Children.Add(line);

        var dot = new Ellipse { Width = 8, Height = 8, Fill = Ui.AccentBrush, Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 1.5 };
        Canvas.SetLeft(dot, X(_values.Count - 1) - 4);
        Canvas.SetTop(dot, Y(_values[^1]) - 4);
        _canvas.Children.Add(dot);
    }
}
