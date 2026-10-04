using CommunityToolkit.Mvvm.ComponentModel;

namespace Gambit.App.Services;

public enum AppThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Persisted user preferences. Observable so the board and pages react live to changes.</summary>
public sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty] public partial AppThemeMode Theme { get; set; } = AppThemeMode.System;
    [ObservableProperty] public partial string BoardTheme { get; set; } = "green";
    [ObservableProperty] public partial string PieceSet { get; set; } = "gambit";
    [ObservableProperty] public partial bool ShowLegalMoves { get; set; } = true;
    [ObservableProperty] public partial bool ShowCoordinates { get; set; } = true;
    [ObservableProperty] public partial bool HighlightLastMove { get; set; } = true;
    [ObservableProperty] public partial bool AnimateMoves { get; set; } = true;
    [ObservableProperty] public partial bool AutoQueen { get; set; }
    [ObservableProperty] public partial bool Premoves { get; set; } = true;
    [ObservableProperty] public partial bool ConfirmResign { get; set; } = true;
    [ObservableProperty] public partial bool SoundEnabled { get; set; } = true;

    // Last choices on the Play page.
    [ObservableProperty] public partial string LastBotId { get; set; } = "acorn";
    [ObservableProperty] public partial string LastColor { get; set; } = "white";
    [ObservableProperty] public partial string LastTimeControl { get; set; } = "unlimited";
    [ObservableProperty] public partial bool AllowTakebacks { get; set; } = true;

    // Online play (the sign-in itself is kept in Windows Credential Manager: OnlineCredentials).
    [ObservableProperty] public partial string OnlineServerUrl { get; set; } = "http://localhost:5080";
    [ObservableProperty] public partial string LastOnlineTimeControl { get; set; } = "3+2";
}

/// <summary>Loads settings at startup and saves them (debounced) whenever a property changes.</summary>
public sealed class SettingsService
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveTimer;

    public SettingsService()
    {
        Current = JsonStore.Load<AppSettings>(AppPaths.Settings);
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (queue != null)
        {
            _saveTimer = queue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => Save();
        }
        Current.PropertyChanged += (_, _) =>
        {
            if (_saveTimer != null)
            {
                _saveTimer.Stop();
                _saveTimer.Start();
            }
            else
            {
                Save();
            }
        };
    }

    public AppSettings Current { get; }

    public void Save()
    {
        try
        {
            JsonStore.Save(AppPaths.Settings, Current);
        }
        catch (Exception ex)
        {
            Log.Warn($"Saving settings failed: {ex.Message}");
        }
    }
}
