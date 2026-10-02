using Gambit.App.Helpers;
using Gambit.Core.Games;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gambit.App.Controls;

/// <summary>Two-column SAN move list. Click a move to view that position.</summary>
public sealed partial class MoveListView : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly ScrollViewer _scroll;
    private readonly List<Border> _cells = [];
    private readonly TextBlock _empty = new()
    {
        Text = "Moves will appear here.",
        Margin = new Thickness(8),
        Opacity = 0.6,
    };
    private int _current = -1;

    public MoveListView()
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
    }

    /// <summary>Raised with the ply to show (1 = position after White's first move).</summary>
    public event EventHandler<int>? PlySelected;

    public void SetMoves(IReadOnlyList<GameMove> moves, int currentPly)
    {
        _rows.Children.Clear();
        _cells.Clear();
        _empty.Visibility = moves.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        int i = 0;
        // A game starting with Black to move gets a "1... move" first row.
        if (moves.Count > 0 && moves[0].Side == Core.Board.Color.Black)
        {
            _rows.Children.Add(Row(moves[0].MoveNumber, null, moves[0]));
            i = 1;
        }
        for (; i < moves.Count; i += 2)
        {
            GameMove white = moves[i];
            GameMove? black = i + 1 < moves.Count ? moves[i + 1] : null;
            _rows.Children.Add(Row(white.MoveNumber, white, black));
        }

        _current = -1;
        Highlight(currentPly);
        ScrollToEnd();
    }

    public void Highlight(int ply)
    {
        _current = ply;
        foreach (Border b in _cells)
        {
            bool on = b.Tag is int p && p == ply;
            b.Background = on ? Ui.AccentBrush : null;
            if (b.Child is TextBlock t)
            {
                if (on) t.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                else t.ClearValue(TextBlock.ForegroundProperty);
            }
        }
    }

    private void ScrollToEnd()
    {
        _scroll.UpdateLayout();
        _scroll.ChangeView(null, _scroll.ScrollableHeight, null, disableAnimation: true);
    }

    private Grid Row(int number, GameMove? white, GameMove? black)
    {
        var row = new Grid { ColumnSpacing = 4, Padding = new Thickness(4, 0, 4, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (number % 2 == 0) row.Background = Ui.NeutralFill(14);
        row.CornerRadius = new CornerRadius(4);

        var num = new TextBlock { Text = $"{number}.", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        row.Children.Add(num);
        if (white != null) AddCell(row, 1, white);
        else AddPlaceholder(row, 1);
        if (black != null) AddCell(row, 2, black);
        return row;
    }

    private void AddCell(Grid row, int column, GameMove move)
    {
        var text = new TextBlock { Text = move.San, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var cell = new Border
        {
            Child = text,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            Tag = move.Ply,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        cell.PointerEntered += (_, _) =>
        {
            if ((int)cell.Tag != _current) cell.Background = Ui.NeutralFill(40);
        };
        cell.PointerExited += (_, _) =>
        {
            if ((int)cell.Tag != _current) cell.Background = null;
        };
        cell.Tapped += (_, _) => PlySelected?.Invoke(this, move.Ply);
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
        _cells.Add(cell);
    }

    private static void AddPlaceholder(Grid row, int column)
    {
        var t = new TextBlock { Text = "…", Opacity = 0.5, Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(t, column);
        row.Children.Add(t);
    }
}
