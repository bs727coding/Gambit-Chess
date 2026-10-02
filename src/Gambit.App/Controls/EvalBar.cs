using Gambit.Engine.Search;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gambit.App.Controls;

/// <summary>Vertical evaluation bar (White's share grows from the bottom unless flipped).</summary>
public sealed partial class EvalBar : UserControl
{
    private readonly Grid _grid = new() { CornerRadius = new CornerRadius(4) };
    private readonly RowDefinition _top = new();
    private readonly RowDefinition _bottom = new();
    private readonly Border _topFill = new();
    private readonly Border _bottomFill = new();
    private readonly TextBlock _label = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private bool _flipped;
    private int _whiteScore;

    public EvalBar()
    {
        _grid.RowDefinitions.Add(_top);
        _grid.RowDefinitions.Add(_bottom);
        Grid.SetRow(_bottomFill, 1);
        _grid.Children.Add(_topFill);
        _grid.Children.Add(_bottomFill);
        Grid.SetRowSpan(_label, 2);
        _grid.Children.Add(_label);
        Content = _grid;
        Width = 26;
        Apply();
    }

    public bool Flipped
    {
        get => _flipped;
        set
        {
            _flipped = value;
            Apply();
        }
    }

    /// <summary>Evaluation from White's point of view (centipawns or mate score).</summary>
    public void SetScore(int whiteScore)
    {
        _whiteScore = whiteScore;
        Apply();
    }

    private void Apply()
    {
        double whiteShare = Searcher.IsMateScore(_whiteScore)
            ? (_whiteScore > 0 ? 1 : 0)
            : 1 / (1 + Math.Exp(-_whiteScore / 260.0));
        whiteShare = Math.Clamp(whiteShare, 0.03, 0.97);

        var white = new SolidColorBrush(ColorHelper.FromArgb(255, 240, 240, 240));
        var black = new SolidColorBrush(ColorHelper.FromArgb(255, 52, 52, 52));
        if (_flipped)
        {
            _top.Height = new GridLength(whiteShare, GridUnitType.Star);
            _bottom.Height = new GridLength(1 - whiteShare, GridUnitType.Star);
            _topFill.Background = white;
            _bottomFill.Background = black;
        }
        else
        {
            _top.Height = new GridLength(1 - whiteShare, GridUnitType.Star);
            _bottom.Height = new GridLength(whiteShare, GridUnitType.Star);
            _topFill.Background = black;
            _bottomFill.Background = white;
        }

        bool whiteAhead = _whiteScore >= 0;
        string text = Searcher.IsMateScore(_whiteScore)
            ? $"M{Math.Abs(Searcher.MateIn(_whiteScore))}"
            : $"{Math.Abs(_whiteScore) / 100.0:0.0}";
        _label.Text = text;
        // Print the number inside the leading side's color, at its end of the bar.
        bool atBottom = whiteAhead != _flipped;
        _label.VerticalAlignment = atBottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _label.Margin = new Thickness(0, 4, 0, 4);
        _label.Foreground = new SolidColorBrush(whiteAhead ? ColorHelper.FromArgb(255, 40, 40, 40) : ColorHelper.FromArgb(255, 235, 235, 235));
    }
}
