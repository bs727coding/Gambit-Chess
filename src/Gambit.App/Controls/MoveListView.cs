using Gambit.App.Helpers;
using Gambit.Core.Games;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WColor = Windows.UI.Color;

namespace Gambit.App.Controls;

/// <summary>A small colored badge drawn next to a move (game review classifications).</summary>
public readonly record struct MoveBadge(string Symbol, WColor Color, string Description);

/// <summary>Two-column SAN move list with optional per-move badges. Click a move to view that position.</summary>
public sealed partial class MoveListView : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<int, (Border Cell, TextBlock Text)> _cells = [];
    private readonly TextBlock _empty = new()
    {
        Text = "Moves will appear here.",
        Margin = new Thickness(8),
        Opacity = 0.6,
    };
    private IReadOnlyDictionary<int, MoveBadge>? _badges;
    private IReadOnlyList<GameMove> _moves = [];
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

    /// <summary>Per-ply badges (null = none). Takes effect on the next <see cref="SetMoves"/>.</summary>
    public void SetBadges(IReadOnlyDictionary<int, MoveBadge>? badges)
    {
        _badges = badges;
        SetMoves(_moves, _current);
    }

    public void SetMoves(IReadOnlyList<GameMove> moves, int currentPly)
    {
        _moves = moves;
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
        if (currentPly >= moves.Count) ScrollToEnd();
    }

    public void Highlight(int ply)
    {
        if (_cells.TryGetValue(_current, out var old))
        {
            old.Cell.Background = null;
            old.Text.ClearValue(TextBlock.ForegroundProperty);
        }
        _current = ply;
        if (_cells.TryGetValue(ply, out var now))
        {
            now.Cell.Background = Ui.AccentBrush;
            now.Text.Foreground = new SolidColorBrush(Colors.White);
            now.Cell.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.5 });
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
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (_badges != null && _badges.TryGetValue(move.Ply, out MoveBadge badge))
        {
            var dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(badge.Color),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = badge.Symbol,
                    FontSize = badge.Symbol.Length > 1 ? 9 : 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTipService.SetToolTip(dot, badge.Description);
            content.Children.Add(dot);
        }
        content.Children.Add(text);

        var cell = new Border
        {
            Child = content,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        int ply = move.Ply;
        cell.PointerEntered += (_, _) =>
        {
            if (ply != _current) cell.Background = Ui.NeutralFill(40);
        };
        cell.PointerExited += (_, _) =>
        {
            if (ply != _current) cell.Background = null;
        };
        cell.Tapped += (_, _) => PlySelected?.Invoke(this, ply);
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
        _cells[ply] = (cell, text);
    }

    private static void AddPlaceholder(Grid row, int column)
    {
        var t = new TextBlock { Text = "…", Opacity = 0.5, Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(t, column);
        row.Children.Add(t);
    }
}
