using System.IO.Compression;
using System.Text;

namespace Gambit.ViewModels;

/// <summary>
/// A bug report to send to whoever runs your Gambit server: a zip with the recent logs, the settings
/// and a note about the build. Sign-ins aren't in it (they live in Windows Credential Manager), and
/// neither are games, the profile or progress.
/// </summary>
public static class BugReport
{
    /// <summary>Logs older than this are left out.</summary>
    public const int LogDays = 14;

    public static void Write(Stream output, string dataDir, string about, DateTime now)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        using (var writer = new StreamWriter(zip.CreateEntry("about.txt").Open(), new UTF8Encoding(false)))
            writer.Write(about);

        string logs = Path.Combine(dataDir, "logs");
        if (Directory.Exists(logs))
            foreach (string file in Directory.GetFiles(logs, "gambit-*.log").Order())
                if (File.GetLastWriteTime(file) >= now.AddDays(-LogDays))
                    Add(zip, file, "logs/" + Path.GetFileName(file));

        if (ProgressBackup.SafeSettings(dataDir) is string settings)
            using (var writer = new StreamWriter(zip.CreateEntry("settings.json").Open(), new UTF8Encoding(false)))
                writer.Write(settings);
    }

    /// <summary>Copies a file that the app may still be writing to (today's log).</summary>
    private static void Add(ZipArchive zip, string path, string name)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using Stream entry = zip.CreateEntry(name).Open();
        source.CopyTo(entry);
    }
}
