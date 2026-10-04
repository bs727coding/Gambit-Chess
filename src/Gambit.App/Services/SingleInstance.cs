using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;

namespace Gambit.App.Services;

/// <summary>
/// One Gambit per data folder. Two copies on the same profile would each save their own in-memory
/// settings, profile and puzzle progress over the other's (a game recorded in one would vanish when
/// the other saved), so a second launch brings the open window forward and exits. A test profile is
/// another folder, so it runs next to the real one.
/// </summary>
public static partial class SingleInstance
{
    private static readonly string Key = $"Gambit ({AppPaths.RootId})";

    /// <summary>
    /// Registers this copy for its data folder. Returns true when another copy already has it: that
    /// window is brought to the front (except on a test profile, which stays in the background) and
    /// this copy should exit.
    /// </summary>
    public static bool HandOffToRunningCopy()
    {
        try
        {
            AppInstance owner = AppInstance.FindOrRegisterForKey(Key);
            if (owner.IsCurrent) return false;
            Log.Info($"Gambit is already open on this data folder (process {owner.ProcessId}); this copy exits");
            if (!AppPaths.IsTestProfile) BringToFront((int)owner.ProcessId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Single-instance check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Lets a new copy take over the data folder (<see cref="App.Restart"/>).</summary>
    public static void Release()
    {
        try
        {
            AppInstance.GetCurrent().UnregisterKey();
        }
        catch (Exception ex)
        {
            Log.Warn($"Single-instance release failed: {ex.Message}");
        }
    }

    private static void BringToFront(int processId)
    {
        try
        {
            IntPtr window = Process.GetProcessById(processId).MainWindowHandle;
            if (window == IntPtr.Zero) return;
            const int Restore = 9; // SW_RESTORE
            if (IsIconic(window)) ShowWindow(window, Restore);
            SetForegroundWindow(window);
        }
        catch (Exception ex)
        {
            Log.Info($"Couldn't show the open Gambit window: {ex.Message}");
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr window);
}
