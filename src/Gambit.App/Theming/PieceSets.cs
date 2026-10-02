using Gambit.Core.Board;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WColor = Windows.UI.Color;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Gambit.App.Theming;

/// <summary>Creates the visual for a piece. Each call returns a new element (XAML elements cannot be shared).</summary>
public abstract class PieceSet
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract FrameworkElement Create(Piece piece);
}

public static class PieceSets
{
    public static IReadOnlyList<PieceSet> All { get; } =
    [
        new VectorPieceSet("gambit", "Gambit",
            white: new PiecePalette(Hex("#FAFAFA"), Hex("#262626"), Hex("#262626")),
            black: new PiecePalette(Hex("#383838"), Hex("#101010"), Hex("#D8D8D8"))),
        new VectorPieceSet("ivory", "Ivory & ebony",
            white: new PiecePalette(Hex("#F3E9D2"), Hex("#4A3B2A"), Hex("#4A3B2A")),
            black: new PiecePalette(Hex("#2E2620"), Hex("#120E0B"), Hex("#C9B48F"))),
        new VectorPieceSet("frost", "Frost",
            white: new PiecePalette(Hex("#FFFFFF"), Hex("#3A6EA5"), Hex("#3A6EA5")),
            black: new PiecePalette(Hex("#25466B"), Hex("#0D2440"), Hex("#BFD9F2"))),
        new GlyphPieceSet(),
    ];

    public static PieceSet Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];

    internal static WColor Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return ColorHelper.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }
}

public sealed record PiecePalette(WColor Body, WColor Outline, WColor Detail);

/// <summary>
/// Original vector piece set drawn in a 100×100 box. Each piece is a stack of layers:
/// body shapes (filled + outlined), detail shapes (filled with the detail color) and detail lines.
/// </summary>
public sealed class VectorPieceSet : PieceSet
{
    private enum Kind { Body, Detail, Line }

    private readonly record struct Layer(Kind Kind, string Data);

    private readonly PiecePalette _white;
    private readonly PiecePalette _black;
    private readonly Dictionary<WColor, SolidColorBrush> _brushes = [];

    public VectorPieceSet(string id, string name, PiecePalette white, PiecePalette black)
    {
        Id = id;
        Name = name;
        _white = white;
        _black = black;
    }

    public override string Id { get; }
    public override string Name { get; }

    private const double Stroke = 3.2;

    private static string Base(double left, double right) =>
        FormattableString.Invariant($"M {left + 3},84 H {right - 3} Q {right},84 {right},87 V 90 Q {right},92 {right - 3},92 H {left + 3} Q {left},92 {left},90 V 87 Q {left},84 {left + 3},84 Z");

    private static string Circle(double cx, double cy, double r) =>
        FormattableString.Invariant($"M {cx - r},{cy} A {r},{r} 0 1 1 {cx + r},{cy} A {r},{r} 0 1 1 {cx - r},{cy} Z");

    private static string Ellipse(double cx, double cy, double rx, double ry) =>
        FormattableString.Invariant($"M {cx - rx},{cy} A {rx},{ry} 0 1 1 {cx + rx},{cy} A {rx},{ry} 0 1 1 {cx - rx},{cy} Z");

    private static readonly Dictionary<PieceType, Layer[]> Shapes = new()
    {
        [PieceType.Pawn] =
        [
            new(Kind.Body, "M 27,84 C 29,72 38,64 42,56 L 58,56 C 62,64 71,72 73,84 Z"),
            new(Kind.Body, Base(19, 81)),
            new(Kind.Body, "M 35,52 Q 50,47 65,52 Q 67,56 63,58.5 Q 50,55.5 37,58.5 Q 33,56 35,52 Z"),
            new(Kind.Body, Circle(50, 36, 13)),
        ],
        [PieceType.Rook] =
        [
            new(Kind.Body, "M 31,38 H 69 L 72,76 H 28 Z"),
            new(Kind.Body, "M 23,76 H 77 V 84 H 23 Z"),
            new(Kind.Body, Base(15, 85)),
            new(Kind.Body, "M 25,31 V 14 H 35 V 21 H 45 V 14 H 55 V 21 H 65 V 14 H 75 V 31 Z"),
            new(Kind.Body, "M 25,30 H 75 V 38 H 25 Z"),
        ],
        [PieceType.Knight] =
        [
            new(Kind.Body, "M 30,84 C 30,74 38,66 46,60 C 38,62 30,63 24,63 C 17,63 12,59 12,53 C 12,48 15,45 19,42 C 24,37 30,30 36,24 C 39,20 42,17 45,15 L 47,6 L 54,14 C 70,16 82,32 82,54 C 82,66 80,76 80,84 Z"),
            new(Kind.Body, Base(17, 85)),
            new(Kind.Detail, Ellipse(37, 32, 3.2, 2.6)),
            new(Kind.Detail, Ellipse(18.5, 49, 1.9, 1.6)),
            new(Kind.Line, "M 55,21 C 67,25 75,39 75,57"),
            new(Kind.Line, "M 13.5,56 Q 17,57.5 21,56.5"),
        ],
        [PieceType.Bishop] =
        [
            new(Kind.Body, "M 50,22 C 63,32 70,46 66,60 Q 64,68 60,74 H 40 Q 36,68 34,60 C 30,46 37,32 50,22 Z"),
            new(Kind.Body, "M 33,74 H 67 Q 70,79 67,84 H 33 Q 30,79 33,74 Z"),
            new(Kind.Body, Base(19, 81)),
            new(Kind.Body, Circle(50, 16, 6)),
            new(Kind.Detail, "M 55,35 L 59,39 L 49,52 L 45,48 Z"),
            new(Kind.Line, "M 37,64 Q 50,60 63,64"),
        ],
        [PieceType.Queen] =
        [
            new(Kind.Body, "M 28,68 Q 50,63 72,68 L 78,84 H 22 Z"),
            new(Kind.Body, Base(17, 83)),
            new(Kind.Body, "M 22,66 L 15,30 L 31,52 L 34,24 L 45,49 L 50,20 L 55,49 L 66,24 L 69,52 L 85,30 L 78,66 Q 50,60 22,66 Z"),
            new(Kind.Body, Circle(15, 28, 4.5)),
            new(Kind.Body, Circle(34, 22, 4.5)),
            new(Kind.Body, Circle(50, 17, 4.5)),
            new(Kind.Body, Circle(66, 22, 4.5)),
            new(Kind.Body, Circle(85, 28, 4.5)),
            new(Kind.Line, "M 27,70 Q 50,65 73,70"),
        ],
        [PieceType.King] =
        [
            new(Kind.Body, "M 47,6 H 53 V 15 H 60 V 21 H 53 V 42 H 47 V 21 H 40 V 15 H 47 Z"),
            new(Kind.Body, "M 30,66 Q 50,61 70,66 L 77,84 H 23 Z"),
            new(Kind.Body, Base(16, 84)),
            new(Kind.Body, "M 30,66 C 20,56 18,40 30,34 C 38,30 46,34 50,41 C 54,34 62,30 70,34 C 82,40 80,56 70,66 Q 50,60 30,66 Z"),
            new(Kind.Line, "M 50,42 V 61"),
            new(Kind.Line, "M 30,72 Q 50,66.5 70,72"),
        ],
    };

    public override FrameworkElement Create(Piece piece)
    {
        PiecePalette pal = piece.Color() == Core.Board.Color.White ? _white : _black;
        var canvas = new Canvas { Width = 100, Height = 100 };
        foreach (Layer layer in Shapes[piece.Type()])
        {
            var path = new Path
            {
                Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), layer.Data),
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            switch (layer.Kind)
            {
                case Kind.Body:
                    path.Fill = Brush(pal.Body);
                    path.Stroke = Brush(pal.Outline);
                    path.StrokeThickness = Stroke;
                    break;
                case Kind.Detail:
                    path.Fill = Brush(pal.Detail);
                    break;
                case Kind.Line:
                    path.Stroke = Brush(pal.Detail);
                    path.StrokeThickness = Stroke * 0.8;
                    break;
            }
            canvas.Children.Add(path);
        }
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }

    private SolidColorBrush Brush(WColor c)
    {
        if (!_brushes.TryGetValue(c, out var b)) _brushes[c] = b = new SolidColorBrush(c);
        return b;
    }
}

/// <summary>Classic look using the chess glyphs of the Segoe UI Symbol font (filled shape + outline on top).</summary>
public sealed class GlyphPieceSet : PieceSet
{
    private static readonly FontFamily Font = new("Segoe UI Symbol");

    public override string Id => "classic";
    public override string Name => "Classic glyphs";

    public override FrameworkElement Create(Piece piece)
    {
        int index = (int)piece.Type() switch
        {
            1 => 5, // pawn
            2 => 4, // knight
            3 => 3, // bishop
            4 => 2, // rook
            5 => 1, // queen
            _ => 0, // king
        };
        string outline = char.ConvertFromUtf32(0x2654 + index);
        string filled = char.ConvertFromUtf32(0x265A + index);
        bool white = piece.Color() == Core.Board.Color.White;

        var grid = new Grid { Width = 100, Height = 100 };
        grid.Children.Add(Glyph(filled, white ? PieceSets.Hex("#FFFFFF") : PieceSets.Hex("#1E1E1E")));
        grid.Children.Add(Glyph(outline, white ? PieceSets.Hex("#1A1A1A") : PieceSets.Hex("#000000")));
        return new Viewbox { Child = grid, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    }

    private static TextBlock Glyph(string text, WColor color) => new()
    {
        Text = text,
        FontFamily = Font,
        FontSize = 84,
        IsColorFontEnabled = false,
        Foreground = new SolidColorBrush(color),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextLineBounds = TextLineBounds.Tight,
        Margin = new Thickness(0, 0, 0, 4),
    };
}
