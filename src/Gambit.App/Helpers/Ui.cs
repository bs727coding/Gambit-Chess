using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Engine.Bots;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;

namespace Gambit.App.Helpers;

public static class Ui
{
    public static WColor ParseColor(string hex, byte alpha = 255)
    {
        hex = hex.TrimStart('#');
        return ColorHelper.FromArgb(alpha, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    public static SolidColorBrush Brush(string hex, byte alpha = 255) => new(ParseColor(hex, alpha));

    /// <summary>The first element named <paramref name="name"/> below <paramref name="root"/> in the visual tree (template parts too).</summary>
    public static FrameworkElement? FindDescendant(DependencyObject root, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return fe;
            if (FindDescendant(child, name) is FrameworkElement found) return found;
        }
        return null;
    }

    /// <summary>
    /// Theme-neutral helpers for code-built UI. (Looking up theme brushes from Application resources in
    /// code follows the *system* theme, not the app's chosen theme, so code-built elements use the
    /// accent color, translucent neutral fills and inherited text color instead.)
    /// </summary>
    public static SolidColorBrush AccentBrush { get; } = new(AccentColor());

    public static SolidColorBrush NeutralFill(byte alpha) => new(ColorHelper.FromArgb(alpha, 128, 128, 128));

    private static WColor AccentColor()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
        }
        catch
        {
            return ColorHelper.FromArgb(255, 0, 120, 212);
        }
    }

    /// <summary>A round avatar with a monogram, in the bot's (or player's) color.</summary>
    public static Border Avatar(string monogram, string colorHex, double size)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = Brush(colorHex),
            Child = new TextBlock
            {
                Text = monogram,
                FontSize = size * 0.42,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    public static string FormatClock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        if (t.TotalSeconds < 10) return $"{t.Seconds}.{t.Milliseconds / 100}";
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    /// <summary>Chess glyph for a piece type (filled style) — used for captured-piece strips.</summary>
    public static string Glyph(PieceType t) => t switch
    {
        PieceType.Pawn => "♟",
        PieceType.Knight => "♞",
        PieceType.Bishop => "♝",
        PieceType.Rook => "♜",
        PieceType.Queen => "♛",
        _ => "♚",
    };

    public static FontIcon Icon(string glyph, double size = 16) => new() { Glyph = glyph, FontSize = size };

    public static string RelativeTime(DateTimeOffset when)
    {
        TimeSpan ago = DateTimeOffset.Now - when;
        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalHours < 1) return $"{(int)ago.TotalMinutes} min ago";
        if (ago.TotalDays < 1) return $"{(int)ago.TotalHours} h ago";
        if (ago.TotalDays < 7) return $"{(int)ago.TotalDays} d ago";
        return when.ToString("MMM d, yyyy");
    }

    /// <summary>
    /// Scrolls <paramref name="scroll"/> so <paramref name="element"/> (inside <paramref name="content"/>)
    /// sits in the middle of the view. A freshly built element is centered once it is loaded (laid
    /// out); StartBringIntoView on it right after rebuilding a list does nothing.
    /// </summary>
    public static void CenterInView(ScrollViewer scroll, UIElement content, FrameworkElement element)
    {
        void Center()
        {
            if (element.XamlRoot == null) return;
            scroll.UpdateLayout();
            double top = element.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            double target = top - (scroll.ViewportHeight - element.ActualHeight) / 2;
            scroll.ChangeView(null, Math.Clamp(target, 0, scroll.ScrollableHeight), null, disableAnimation: true);
        }
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            element.Loaded -= OnLoaded;
            Center();
        }
        if (element.IsLoaded) Center();
        else element.Loaded += OnLoaded;
    }

    /// <summary>
    /// Keeps a wrap grid's tiles filling the width of its parent panel (no ragged gap on the right):
    /// as many columns of at least <paramref name="minWidth"/> as fit, sharing the width equally.
    /// The parent is measured, not the grid: a wrap grid is only as wide as its tiles, so sizing
    /// from its own width would ratchet down. The parent is looked up once the grid is loaded
    /// (while a page is still being built, <see cref="FrameworkElement.Parent"/> is null).
    /// </summary>
    public static void StretchTiles(VariableSizedWrapGrid grid, double minWidth)
    {
        FrameworkElement? parent = null;
        void Fit()
        {
            if (parent == null) return;
            Thickness pad = parent switch { StackPanel s => s.Padding, Grid g => g.Padding, Border b => b.Padding, _ => default };
            double available = parent.ActualWidth - pad.Left - pad.Right - grid.Margin.Left - grid.Margin.Right;
            if (available <= 0) return;
            int columns = Math.Max(1, (int)(available / minWidth));
            // 2 px of slack: layout rounding to physical pixels can leave the grid a fraction of a
            // pixel narrower than computed here, which would wrap the last column.
            double width = Math.Floor((available - 2) / columns);
            if (grid.ItemWidth != width) grid.ItemWidth = width;
        }
        grid.Loaded += (_, _) =>
        {
            if (parent == null && VisualTreeHelper.GetParent(grid) is FrameworkElement p)
            {
                parent = p;
                parent.SizeChanged += (_, _) => Fit();
            }
            Fit();
        };
    }
}
