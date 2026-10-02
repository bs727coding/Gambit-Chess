using System.Reflection;

namespace Gambit.App;

/// <summary>Product identity in one place (rename the app here).</summary>
public static class AppInfo
{
    public const string DisplayName = "Gambit";
    public const string Tagline = "Play, solve, learn.";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "0.1.0";

    public static string Architecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
}
