using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Gambit.App.Controls;

/// <summary>Lays children out left to right like words in a paragraph, wrapping when a line is full.</summary>
public sealed partial class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 2;

    public double VerticalSpacing { get; set; } = 2;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in Children) child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Layout(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, arrange: true);
        return finalSize;
    }

    private Size Layout(double width, bool arrange)
    {
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (UIElement child in Children)
        {
            Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
            widest = Math.Max(widest, x - HorizontalSpacing);
        }
        return new Size(double.IsInfinity(width) ? widest : Math.Min(widest, width), y + lineHeight);
    }
}
