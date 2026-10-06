using Gambit.App.Helpers;
using Gambit.App.Theming;
using Gambit.Core.Board;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gambit.App.Controls;

/// <summary>Strip shown above/below the board: avatar, name, rating, captured material and clock.</summary>
public sealed partial class PlayerBar : UserControl
{
    private readonly Grid _avatarHost = new() { Width = 40, Height = 40 };
    private readonly TextBlock _name = new() { FontWeight = FontWeights.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _rating = new() { Opacity = 0.7, FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly ProgressRing _thinking = new() { Width = 14, Height = 14, IsActive = false, Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0) };
    private readonly StackPanel _captured = new() { Orientation = Orientation.Horizontal, Spacing = -7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _advantage = new() { FontSize = 13, Opacity = 0.75, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _clock = new() { CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 4, 14, 4), MinWidth = 104, Visibility = Visibility.Collapsed };
    private readonly TextBlock _clockText = new()
    {
        FontSize = 24,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Right,
        FontFamily = new FontFamily("Segoe UI Variable Display"), // equal-width digits: a running clock doesn't jiggle
    };

    public PlayerBar()
    {
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(_name);
        nameRow.Children.Add(_rating);
        nameRow.Children.Add(_thinking);

        var capturedRow = new StackPanel { Orientation = Orientation.Horizontal, Height = 22 };
        capturedRow.Children.Add(_captured);
        capturedRow.Children.Add(_advantage);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        info.Children.Add(nameRow);
        info.Children.Add(capturedRow);

        _clock.Child = _clockText;

        var grid = new Grid { Height = 52 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(info, 1);
        Grid.SetColumn(_clock, 2);
        _avatarHost.VerticalAlignment = VerticalAlignment.Center;
        _clock.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_avatarHost);
        grid.Children.Add(info);
        grid.Children.Add(_clock);
        Content = grid;
        SetClockState(active: false, low: false);
    }

    public void SetPlayer(string name, string? rating, string monogram, string colorHex)
    {
        _name.Text = name;
        _rating.Text = rating is null ? "" : $"({rating})";
        _avatarHost.Children.Clear();
        _avatarHost.Children.Add(Ui.Avatar(monogram, colorHex, 40));
    }

    public void SetThinking(bool thinking)
    {
        _thinking.IsActive = thinking;
        _thinking.Visibility = thinking ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Pieces this player has captured (drawn with the current piece set) and their material lead.</summary>
    public void SetCaptured(IEnumerable<Piece> captured, int advantage, PieceSet set)
    {
        _captured.Children.Clear();
        PieceType? previous = null;
        foreach (Piece p in captured.OrderBy(p => p.Type()))
        {
            FrameworkElement v = set.Create(p);
            v.Width = v.Height = 20;
            // Small gap between groups of different piece types, overlap within a group.
            if (previous is PieceType t && t != p.Type()) v.Margin = new Thickness(9, 0, 0, 0);
            previous = p.Type();
            _captured.Children.Add(v);
        }
        _advantage.Text = advantage > 0 ? $"+{advantage}" : "";
    }

    public void SetClock(TimeSpan? remaining, bool active)
    {
        if (remaining is not TimeSpan t)
        {
            _clock.Visibility = Visibility.Collapsed;
            return;
        }
        _clock.Visibility = Visibility.Visible;
        _clockText.Text = Ui.FormatClock(t);
        SetClockState(active, low: t < TimeSpan.FromSeconds(20));
    }

    private void SetClockState(bool active, bool low)
    {
        if (low && active)
        {
            _clock.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 196, 43, 28));
            _clockText.Foreground = new SolidColorBrush(Colors.White);
        }
        else if (active)
        {
            _clock.Background = Ui.AccentBrush;
            _clockText.Foreground = new SolidColorBrush(Colors.White);
        }
        else
        {
            _clock.Background = Ui.NeutralFill(36);
            _clockText.ClearValue(TextBlock.ForegroundProperty);
        }
    }
}
