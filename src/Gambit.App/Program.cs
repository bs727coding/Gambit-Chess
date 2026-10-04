using Gambit.App.Services;
using Velopack;

namespace Gambit.App;

/// <summary>
/// Entry point. Velopack goes first: when the installer, an update or the uninstaller starts the app
/// with its special arguments, it does its work (shortcuts, clean-up) and exits right here, before any
/// window opens. Then a second copy on the same data folder hands over to the open one
/// (<see cref="SingleInstance"/>); otherwise the usual WinUI startup follows.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (SingleInstance.HandOffToRunningCopy()) return;
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
