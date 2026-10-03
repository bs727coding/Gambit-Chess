using Gambit.App.Helpers;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gambit.App.Controls;

/// <summary>
/// The analysis board's move list: the main line in two columns, and every variation inline right
/// after the move it is an alternative to (numbered like PGN, deeper ones in parentheses). Each move
/// is a button: click it (or Enter) to go to that position.
/// </summary>
public sealed partial class MoveTreeView : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty = new()
    {
        Text = "Moves will appear here. Moves from an earlier position start a variation.",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8),
        Opacity = 0.6,
    };
    private readonly Dictionary<MoveNode, (Button Button, TextBlock Text)> _moves = [];
    private MoveNode? _current;

    public MoveTreeView()
    {
        _scroll = new ScrollViewer
        {
            Content = _rows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var grid = new Grid();
        grid.Children.Add(_empty);
        grid.Children.Add(_scroll);
        Content = grid;
        // Keep the current move in view when the list gets shorter or taller (window size, other cards).
        _scroll.SizeChanged += (_, _) =>
        {
            if (_current != null && _moves.TryGetValue(_current, out var now)) Ui.CenterInView(_scroll, _rows, now.Button);
        };
    }

    /// <summary>Raised with the move the user picked.</summary>
    public event EventHandler<MoveNode>? NodeSelected;

    /// <summary>Redraws the whole tree and highlights <paramref name="current"/> (the root: nothing).</summary>
    public void SetTree(MoveTree tree, MoveNode current)
    {
        _rows.Children.Clear();
        _moves.Clear();
        List<MoveNode> main = tree.MainLine();
        _empty.Visibility = main.Count <= 1 ? Visibility.Visible : Visibility.Collapsed;

        Grid? row = null; // the main-line row waiting for Black's move
        foreach (MoveNode node in main.Skip(1))
        {
            if (node.Side == Color.White)
            {
                row = Row(node.MoveNumber);
                AddCell(row, 1, node);
            }
            else
            {
                if (row == null)
                {
                    row = Row(node.MoveNumber);
                    AddPlaceholder(row, 1);
                }
                AddCell(row, 2, node);
                row = null;
            }

            // Alternatives to this move, each on its own indented line.
            foreach (MoveNode alternative in node.Parent!.Children.Skip(1))
            {
                _rows.Children.Add(Variation(alternative));
                row = null; // Black's reply then starts a new row ("12. … Nf6")
            }
        }

        _current = null;
        Highlight(current);
    }

    public void Highlight(MoveNode node)
    {
        if (_current != null && _moves.TryGetValue(_current, out var old))
        {
            old.Button.Background = new SolidColorBrush(Colors.Transparent);
            old.Text.ClearValue(TextBlock.ForegroundProperty);
        }
        _current = node;
        if (_moves.TryGetValue(node, out var now))
        {
            now.Button.Background = Ui.AccentBrush;
            now.Text.Foreground = new SolidColorBrush(Colors.White);
            Ui.CenterInView(_scroll, _rows, now.Button);
        }
    }

    private Grid Row(int number)
    {
        var row = new Grid { ColumnSpacing = 4, Padding = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (number % 2 == 0) row.Background = Ui.NeutralFill(14);
        row.Children.Add(new TextBlock { Text = $"{number}.", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        _rows.Children.Add(row);
        return row;
    }

    private void AddCell(Grid row, int column, MoveNode node)
    {
        Button button = MoveButton(node, node.San, FontWeights.SemiBold, new Thickness(8, 4, 8, 4));
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private static void AddPlaceholder(Grid row, int column)
    {
        var dots = new TextBlock { Text = "…", Opacity = 0.5, Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(dots, column);
        row.Children.Add(dots);
    }

    /// <summary>One variation, indented under the main line, with its own sub-variations in parentheses.</summary>
    private Border Variation(MoveNode first)
    {
        var flow = new WrapPanel();
        AppendLine(flow, first);
        return new Border
        {
            Child = flow,
            Margin = new Thickness(44, 0, 4, 2),
            Padding = new Thickness(6, 2, 2, 2),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = Ui.NeutralFill(70),
        };
    }

    /// <summary>
    /// Writes the line starting at <paramref name="start"/> (following first children) the way PGN
    /// does: White's moves numbered, Black's numbered after a break, alternatives in parentheses
    /// right after the move they replace.
    /// </summary>
    private void AppendLine(WrapPanel flow, MoveNode start)
    {
        bool number = true;
        for (MoveNode? node = start; node != null; node = node.Children.Count > 0 ? node.Children[0] : null)
        {
            string text = number || node.Side == Color.White
                ? $"{node.MoveNumber}{(node.Side == Color.White ? "." : "…")} {node.San}"
                : node.San;
            flow.Children.Add(MoveButton(node, text, FontWeights.Normal, new Thickness(4, 1, 4, 2)));
            number = false;

            // The alternatives to this move (its parent's other children) follow it in parentheses;
            // the start's own siblings are drawn by the caller.
            if (!ReferenceEquals(node, start) && node.Parent is MoveNode parent && ReferenceEquals(parent.Children[0], node) && parent.Children.Count > 1)
            {
                foreach (MoveNode alternative in parent.Children.Skip(1))
                {
                    flow.Children.Add(Bracket("("));
                    AppendLine(flow, alternative);
                    flow.Children.Add(Bracket(")"));
                }
                number = true;
            }
        }
    }

    private static TextBlock Bracket(string text) => new() { Text = text, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) };

    private Button MoveButton(MoveNode node, string text, Windows.UI.Text.FontWeight weight, Thickness padding)
    {
        var label = new TextBlock { Text = text, FontWeight = weight };
        var button = new Button
        {
            Content = label,
            Padding = padding,
            MinWidth = 0,
            MinHeight = 0,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        string side = node.Side == Color.White ? "White" : "Black";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Move {node.MoveNumber} {side} {node.San}{(node.IsMainLine ? "" : ", variation")}");
        button.Click += (_, _) => NodeSelected?.Invoke(this, node);
        _moves[node] = (button, label);
        return button;
    }
}
