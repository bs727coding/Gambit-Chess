using Gambit.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gambit.App.Helpers;

/// <summary>Asks for the two players' names before a pass-and-play game; remembers them for next time.</summary>
public static class PassAndPlayDialog
{
    /// <summary>The players' names, or null when cancelled.</summary>
    public static async Task<(string White, string Black)?> AskAsync(XamlRoot root)
    {
        AppSettings s = App.Settings.Current;
        var white = new TextBox { Header = "White", MaxLength = 24, Text = s.LastWhiteName.Length > 0 ? s.LastWhiteName : App.Profile.Profile.Name };
        var black = new TextBox { Header = "Black", MaxLength = 24, Text = s.LastBlackName.Length > 0 ? s.LastBlackName : "Guest" };
        var flip = new CheckBox { Content = "Turn the board after each move", IsChecked = s.PassAndPlayFlip };
        var panel = new StackPanel { Spacing = 12, MinWidth = 300 };
        panel.Children.Add(white);
        panel.Children.Add(black);
        panel.Children.Add(flip);
        var dialog = new ContentDialog
        {
            Title = "Pass and play",
            Content = panel,
            PrimaryButtonText = "Start",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await Dialogs.ShowAsync(dialog, root) != ContentDialogResult.Primary) return null;

        string whiteName = Clean(white.Text, "White"), blackName = Clean(black.Text, "Black");
        s.LastWhiteName = whiteName;
        s.LastBlackName = blackName;
        s.PassAndPlayFlip = flip.IsChecked == true;
        return (whiteName, blackName);
    }

    private static string Clean(string text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
}
