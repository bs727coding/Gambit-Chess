using Velopack;

namespace Gambit.App.Services;

/// <summary>
/// App updates (Velopack) from the Gambit server you play on: its /releases folder holds the update
/// packages (see docs/RELEASING.md). Only installed copies update themselves; a development build or a
/// copy run from a plain folder reports <see cref="IsInstalled"/> false and never checks.
/// </summary>
public sealed class UpdateService
{
    private UpdateManager? _manager;
    private string? _managerFeed;

    public static UpdateService Instance { get; } = new();

    /// <summary>An update found by the last check, ready to download and install.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>Raised (on the calling thread) when <see cref="Available"/> changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Where new versions come from: the server the build was made for, or else the online server from
    /// the Online page. Its /releases folder is the feed.
    /// </summary>
    public static string Server => (AppInfo.HomeServer ?? App.Settings.Current.OnlineServerUrl).Trim().TrimEnd('/');

    private static string Feed => Server + "/releases";

    public bool IsInstalled => Manager()?.IsInstalled == true;

    public string? AvailableVersion => Available?.TargetFullRelease.Version.ToString();

    /// <summary>Why updating has to wait (an online game in progress), or null.</summary>
    public static string? WaitReason => Pages.GamePage.HasActiveOnlineGame
        ? "Finish your online game first: updating restarts Gambit."
        : null;

    /// <summary>Looks for a newer version; returns it, or null (none, not installed, or the server can't be reached).</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            if (Manager() is not UpdateManager manager || !manager.IsInstalled) return null;
            Available = await manager.CheckForUpdatesAsync();
            if (Available != null) Log.Info($"Update available: {AvailableVersion}");
        }
        catch (Exception ex)
        {
            Log.Info($"Update check skipped: {ex.Message}");
            Available = null;
        }
        Changed?.Invoke();
        return Available;
    }

    /// <summary>Downloads the available update, then closes Gambit, installs it and starts the new version.</summary>
    public async Task UpdateAndRestartAsync(Action<int>? progress = null)
    {
        if (Available is not UpdateInfo update || Manager() is not UpdateManager manager) return;
        await manager.DownloadUpdatesAsync(update, progress);
        Log.Info($"Installing update {AvailableVersion}");
        manager.ApplyUpdatesAndRestart(update);
    }

    private UpdateManager? Manager()
    {
        string feed = Feed;
        if (_manager == null || _managerFeed != feed)
        {
            try
            {
                _manager = new UpdateManager(feed);
                _managerFeed = feed;
            }
            catch (Exception ex)
            {
                Log.Info($"Updates unavailable: {ex.Message}");
                _manager = null;
            }
        }
        return _manager;
    }
}
