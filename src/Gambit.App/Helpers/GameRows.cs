using Gambit.App.Pages;
using Gambit.App.Services;
using Gambit.Engine.Bots;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Gambit.App.Helpers;

/// <summary>Builds the "recent game" rows used on Home and Profile.</summary>
public static class GameRows
{
    public static FrameworkElement Create(GameRecord g)
    {
        var grid = new Grid
        {
            ColumnSpacing = 14,
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(6),
            Background = Ui.NeutralFill(18),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        BotProfile? bot = g.BotId != null ? BotRoster.Bots.FirstOrDefault(b => b.Id == g.BotId) : null;
        grid.Children.Add(bot != null ? Ui.Avatar(bot.Monogram, bot.Color, 36) : Ui.Avatar("⇄", "#5C6BC0", 36));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string rating = g.OpponentRating is int r && bot is { IsMaxStrength: false } ? $" ({r})" : bot?.IsMaxStrength == true ? " (Max)" : "";
        text.Children.Add(new TextBlock { Text = $"vs {g.Opponent}{rating}", FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = $"{g.Termination} · {g.Moves} moves · {g.TimeControl}{(g.Opening is string o ? " · " + o : "")} · {Ui.RelativeTime(g.PlayedAt)}",
            Opacity = 0.7,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var (label, color) = g.Outcome switch
        {
            "win" => ("Win", ColorHelper.FromArgb(255, 46, 125, 50)),
            "loss" => ("Loss", ColorHelper.FromArgb(255, 198, 40, 40)),
            _ => (g.PlayerColor == "both" ? g.Result : "Draw", ColorHelper.FromArgb(255, 110, 110, 110)),
        };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 2, 10, 2),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = label, Foreground = new SolidColorBrush(Colors.White), FontSize = 12, FontWeight = FontWeights.SemiBold },
        };
        Grid.SetColumn(chip, 2);
        grid.Children.Add(chip);

        if (File.Exists(g.PgnFile))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var review = new Button
            {
                Content = new FontIcon { Glyph = "\uE9F9", FontSize = 14 },
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(review, "Game review");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(review, "Game review");
            review.Click += (_, _) =>
            {
                try
                {
                    if (ReviewRequest.FromPgnFile(g.PgnFile, g.PlayerColor) is ReviewRequest req) App.Window.Navigate(typeof(ReviewPage), req);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Opening review failed: {ex.Message}");
                }
            };
            Grid.SetColumn(review, 4);
            grid.Children.Add(review);

            var open = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(open, "Open in analysis board");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, "Open in analysis board");
            open.Click += (_, _) => App.Window.Navigate(typeof(AnalysisPage), new AnalysisRequest(PgnFile: g.PgnFile), "analysis");
            Grid.SetColumn(open, 3);
            grid.Children.Add(open);
        }
        return grid;
    }
}
