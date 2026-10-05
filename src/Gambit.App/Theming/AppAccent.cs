using Gambit.App.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Gambit.App.Theming;

/// <summary>
/// The app's accent color, taken from the board theme: buttons, selections and highlights match the
/// board. It replaces the Windows accent in the app's resources (SystemAccentColor and its three
/// lighter and darker steps), which the Fluent brushes are built from.
/// </summary>
public static class AppAccent
{
    /// <summary>The accent in use.</summary>
    public static Color Current { get; private set; } = Colors.SeaGreen;

    /// <summary>
    /// Sets the accent and its steps in the app's resources. Brushes read them when they load or when
    /// the theme is next applied (MainWindow re-applies it after a change).
    /// </summary>
    public static void Apply(Color accent)
    {
        Current = accent;
        ResourceDictionary resources = Application.Current.Resources;
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorLight1"] = Shade(accent, 0.10);
        resources["SystemAccentColorLight2"] = Shade(accent, 0.20);
        resources["SystemAccentColorLight3"] = Shade(accent, 0.32);
        resources["SystemAccentColorDark1"] = Shade(accent, -0.07);
        resources["SystemAccentColorDark2"] = Shade(accent, -0.15);
        resources["SystemAccentColorDark3"] = Shade(accent, -0.23);
        Ui.AccentBrush.Color = accent;
    }

    /// <summary>
    /// An accent made from a board's dark squares (for custom themes): the same hue, brought to a
    /// middle lightness and enough saturation to read as a color on buttons in light and dark mode.
    /// </summary>
    public static Color FromSquare(Color square)
    {
        var (h, s, l) = ToHsl(square);
        return FromHsl(h, Math.Max(s, 0.35), Math.Clamp(l, 0.36, 0.48));
    }

    /// <summary><paramref name="color"/> with its lightness moved by <paramref name="delta"/> (HSL, 0 to 1).</summary>
    public static Color Shade(Color color, double delta)
    {
        var (h, s, l) = ToHsl(color);
        return FromHsl(h, s, Math.Clamp(l + delta, 0.08, 0.92));
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return ((h * 60 + 360) % 360, s, l);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return ColorHelper.FromArgb(255, B(r + m), B(g + m), B(b + m));
    }
}
