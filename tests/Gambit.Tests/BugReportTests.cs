using System.IO.Compression;
using Gambit.ViewModels;

namespace Gambit.Tests;

public sealed class BugReportTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("gambit-report-test-").FullName;

    public void Dispose() => Directory.Delete(_data, recursive: true);

    [Fact]
    public void A_report_has_the_recent_logs_and_settings_but_no_secrets_or_progress()
    {
        DateTime now = DateTime.Now;
        Directory.CreateDirectory(Path.Combine(_data, "logs"));
        Directory.CreateDirectory(Path.Combine(_data, "games"));
        string old = Path.Combine(_data, "logs", "gambit-2026-09-01.log");
        File.WriteAllText(old, "long ago");
        File.SetLastWriteTime(old, now.AddDays(-30));
        string today = Path.Combine(_data, "logs", "gambit-2026-10-05.log");
        File.WriteAllText(today, "10:00:00.000 [INFO] Gambit starting");
        File.WriteAllText(Path.Combine(_data, "settings.json"), """{ "boardTheme": "blue", "onlineToken": "secret-1" }""");
        File.WriteAllText(Path.Combine(_data, "profile.json"), """{ "name": "Sam" }""");
        File.WriteAllText(Path.Combine(_data, "games", "a.pgn"), "1. e4 e5 *");

        using var report = new MemoryStream();
        // Today's log is still open for writing, as it is while the app runs.
        using (new FileStream(today, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            BugReport.Write(report, _data, "Gambit 1.2.3", now);

        report.Position = 0;
        using var zip = new ZipArchive(report);
        Assert.Equal(["about.txt", "logs/gambit-2026-10-05.log", "settings.json"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        string Read(string name) => new StreamReader(zip.GetEntry(name)!.Open()).ReadToEnd();
        Assert.Equal("Gambit 1.2.3", Read("about.txt"));
        Assert.Contains("Gambit starting", Read("logs/gambit-2026-10-05.log"));
        Assert.Contains("blue", Read("settings.json"));
        Assert.DoesNotContain("secret", Read("settings.json"));
    }
}
