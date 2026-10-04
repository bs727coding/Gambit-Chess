using System.Reflection;

namespace Gambit.App;

/// <summary>Product identity in one place (rename the app here).</summary>
public static class AppInfo
{
    public const string DisplayName = "Gambit";
    public const string Tagline = "Play, solve, learn.";

    private static string Informational =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>"0.2.0" (Directory.Build.props).</summary>
    public static string Version => Informational.Split('+')[0];

    /// <summary>The git commit the build came from (7 characters), or "" (useful in bug reports).</summary>
    public static string Commit => Informational.Split('+') is [_, string sha, ..] ? sha[..Math.Min(7, sha.Length)] : "";

    public static string Architecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>
    /// The server this build was made for (`./build.ps1 package -ServerUrl ...`), or null: installed
    /// copies update from it and new profiles connect to it. Development builds have none.
    /// </summary>
    public static string? HomeServer { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GambitServerUrl")?.Value is { Length: > 0 } url ? url : null;
}
