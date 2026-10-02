using Gambit.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Gambit.App.Helpers;

/// <summary>Shows ContentDialogs with the app's current theme, one at a time.</summary>
public static class Dialogs
{
    private static bool _open;

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog, XamlRoot? root)
    {
        if (_open || root == null) return ContentDialogResult.None;
        _open = true;
        try
        {
            dialog.XamlRoot = root;
            if (root.Content is FrameworkElement fe) dialog.RequestedTheme = fe.ActualTheme;
            if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out object? style) && style is Style s)
                dialog.Style = s;
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Warn($"Dialog failed: {ex.Message}");
            return ContentDialogResult.None;
        }
        finally
        {
            _open = false;
        }
    }
}
