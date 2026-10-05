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
    /// <summary>The theme whose square colors the user picks (Settings: CustomLightSquare, CustomDarkSquare).</summary>
    public const string CustomId = "custom";

    private static Color Hex(string hex, byte alpha = 255)
    {
        hex = hex.TrimStart('#');
        return ColorHelper.FromArgb(alpha,
            Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    /// <summary>Parses "#RRGGBB" (the # is optional); null if <paramref name="hex"/> isn't a color.</summary>
    public static Color? TryParse(string? hex)
    {
        hex = hex?.Trim().TrimStart('#');
        if (hex is not { Length: 6 } || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) return null;
        return ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    /// <summary>"#RRGGBB" for <paramref name="color"/> (alpha left out).</summary>
    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Colors for the last-move and selection highlight; "theme" (no color) keeps the board theme's own.</summary>
    public static IReadOnlyList<(string Id, string Name, Color? Color)> Highlights { get; } =
    [
        ("theme", "Match the board", null),
        ("yellow", "Yellow", Hex("#FFFF33", 120)),
        ("green", "Green", Hex("#9BC700", 115)),
        ("blue", "Blue", Hex("#4CC2FF", 115)),
        ("purple", "Purple", Hex("#B388FF", 135)),
        ("orange", "Orange", Hex("#FF9F1C", 125)),
        ("red", "Red", Hex("#FF5252", 105)),
    ];

    /// <summary>The custom theme with the given square colors (unreadable ones fall back to defaults).</summary>
    public static BoardTheme Custom(string? light, string? dark) =>
        new(CustomId, "Custom", TryParse(light) ?? Hex("#EAE4D3"), TryParse(dark) ?? Hex("#4E8C87"), Hex("#FFFF33", 120));

    /// <summary>The theme the settings describe: built-in or custom, with the chosen highlight color.</summary>
    public static BoardTheme ForSettings(string themeId, string? customLight, string? customDark, string? highlight)
    {
        BoardTheme theme = themeId == CustomId ? Custom(customLight, customDark) : Get(themeId);
        Color? color = Highlights.FirstOrDefault(h => h.Id == highlight).Color;
        return color is Color c ? theme with { Highlight = c } : theme;
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
