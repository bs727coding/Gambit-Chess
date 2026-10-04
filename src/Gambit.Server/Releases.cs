using System.Net;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Gambit.Server;

/// <summary>
/// App downloads and updates from the server: the files `./build.ps1 package` makes (Velopack
/// installers, update packages and feeds) go in the data folder's releases/, which is served at
/// /releases (the app's update feed). /download is a small page for people who don't have Gambit yet.
/// </summary>
public static class Releases
{
    public const string Folder = "releases";

    public static void MapReleases(this WebApplication app, ServerOptions options)
    {
        string folder = Directory.CreateDirectory(Path.Combine(options.DataDirectory, Folder)).FullName;
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".nupkg"] = "application/octet-stream";
        types.Mappings[".exe"] = "application/octet-stream";
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(folder),
            RequestPath = "/" + Folder,
            ContentTypeProvider = types,
        });
        app.MapGet("/download", () => Results.Content(DownloadPage(folder), "text/html; charset=utf-8"));
    }

    /// <summary>Links to the installers in the releases folder, with which PC each one is for.</summary>
    private static string DownloadPage(string folder)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Download Gambit</title>
            <style>body{font-family:'Segoe UI',system-ui,sans-serif;max-width:640px;margin:40px auto;padding:0 16px;line-height:1.5;color:#1b1b1b}
            a.button{display:inline-block;margin:6px 0;padding:10px 16px;border-radius:6px;background:#2e7d32;color:#fff;text-decoration:none}
            small{color:#555}</style></head><body>
            <h1>Gambit</h1><p>Chess for Windows: bots, puzzles, lessons, and online games on this server.</p>
            """);
        string[] installers = [.. Directory.GetFiles(folder, "*-Setup.exe").Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)];
        if (installers.Length == 0)
        {
            html.Append("<p>No installers have been put on this server yet.</p>");
        }
        else
        {
            foreach (string file in installers)
            {
                string label = file.Contains("arm64", StringComparison.OrdinalIgnoreCase)
                    ? "Windows on Arm (Snapdragon PCs, Surface Pro X)"
                    : "Windows on Intel or AMD (most PCs)";
                html.Append($"<p><a class=\"button\" href=\"/{Folder}/{WebUtility.UrlEncode(file)}\">Download for {label}</a></p>");
            }
            html.Append("""
                <p><small>Not sure which? Settings &gt; System &gt; About shows "ARM-based processor" on Arm PCs. Windows may warn
                that the installer is from an unknown publisher: choose <b>More info</b>, then <b>Run anyway</b>. Gambit installs
                for you alone (no administrator needed), keeps itself up to date from this server, and can be removed in Settings &gt; Apps.</small></p>
                """);
        }
        return html.Append("</body></html>").ToString();
    }
}
