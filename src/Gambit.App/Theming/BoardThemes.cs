using Windows.UI;
using Microsoft.UI;

namespace Gambit.App.Theming;

/// <summary>Colors for the board squares and overlays.</summary>
public sealed record BoardTheme(string Id, string Name, Color Light, Color Dark, Color Highlight)
{
    /// <summary>Coordinate text drawn on light squares uses the dark square color, and vice versa.</summary>
    public Color CoordinateOnLight => Dark;
    public Color CoordinateOnDark => Light;

    /// <summary>Translucent overlay for legal-move dots and capture rings.</summary>
    public Color HintColor { get; init; } = ColorHelper.FromArgb(40, 0, 0, 0);

    public Color CheckColor { get; init; } = ColorHelper.FromArgb(255, 235, 64, 52);
}

public static class BoardThemes
{
    private static Color Hex(string hex, byte alpha = 255)
    {
        hex = hex.TrimStart('#');
        return ColorHelper.FromArgb(alpha,
            Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    public static IReadOnlyList<BoardTheme> All { get; } =
    [
        new("green", "Tournament green", Hex("#EBECD0"), Hex("#739552"), Hex("#FFFF33", 120)),
        new("brown", "Walnut", Hex("#F0D9B5"), Hex("#B58863"), Hex("#9BC700", 110)),
        new("blue", "Glacier", Hex("#DEE3E6"), Hex("#8CA2AD"), Hex("#9BC700", 110)),
        new("slate", "Slate", Hex("#E3E6EA"), Hex("#7A8696"), Hex("#4CC2FF", 100)),
        new("purple", "Amethyst", Hex("#F0F1F0"), Hex("#8476BA"), Hex("#FFFF33", 105)),
        new("coral", "Coral", Hex("#F4E3D7"), Hex("#D08B6D"), Hex("#FFE14D", 120)),
        new("midnight", "Midnight", Hex("#A4ADBD"), Hex("#4C566A"), Hex("#88C0D0", 130)),
        new("mono", "Graphite", Hex("#D9D9D9"), Hex("#8C8C8C"), Hex("#FFD54F", 120)),
    ];

    public static BoardTheme Get(string id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
