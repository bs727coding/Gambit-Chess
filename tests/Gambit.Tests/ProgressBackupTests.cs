using System.IO.Compression;
using System.Text.Json.Nodes;
using Gambit.ViewModels;

namespace Gambit.Tests;

public sealed class ProgressBackupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("gambit-backup-test-").FullName;

    public ProgressBackupTests()
    {
        Directory.CreateDirectory(Path.Combine(Data, "games"));
        File.WriteAllText(Path.Combine(Data, "profile.json"), """{ "name": "Sam", "gamesPlayed": 2 }""");
        File.WriteAllText(Path.Combine(Data, "settings.json"), """{ "boardTheme": "blue", "onlineToken": "secret-1" }""");
        File.WriteAllText(Path.Combine(Data, "current-game.json"), """{ "moves": ["e2e4"] }""");
        File.WriteAllText(Path.Combine(Data, "games", "a.pgn"), "1. e4 e5 *");
        File.WriteAllText(Path.Combine(Data, "games", "b.pgn"), "1. d4 d5 *");
    }

    private string Data => Path.Combine(_root, "data");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_backup_holds_the_progress_but_never_the_online_token()
    {
        string zip = Path.Combine(_root, "backup.zip");
        ProgressBackup.Create(Data, zip, "1.2.3");

        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            Assert.Equal(new[] { "backup.json", "current-game.json", "games/a.pgn", "games/b.pgn", "profile.json", "settings.json" },
                archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
            string settings = new StreamReader(archive.GetEntry("settings.json")!.Open()).ReadToEnd();
            Assert.DoesNotContain("secret", settings);
            Assert.Contains("blue", settings);
        }

        BackupInfo? info = ProgressBackup.Read(zip, out string? problem);
        Assert.Null(problem);
        Assert.Equal(2, info!.Games);
        Assert.Equal("1.2.3", info.AppVersion);
    }

    [Fact]
    public void Restore_brings_progress_back_and_keeps_the_current_token()
    {
        string zip = Path.Combine(_root, "backup.zip");
        ProgressBackup.Create(Data, zip, "1.0");

        // Later: different progress, a new game, no unfinished game, a new sign-in token.
        File.WriteAllText(Path.Combine(Data, "profile.json"), """{ "name": "Other" }""");
        File.WriteAllText(Path.Combine(Data, "games", "c.pgn"), "1. c4 *");
        File.Delete(Path.Combine(Data, "current-game.json"));
        File.WriteAllText(Path.Combine(Data, "settings.json"), """{ "boardTheme": "green", "onlineToken": "secret-2" }""");

        ProgressBackup.Restore(zip, Data);

        Assert.Contains("Sam", File.ReadAllText(Path.Combine(Data, "profile.json")));
        Assert.Equal(new[] { "a.pgn", "b.pgn" }, Directory.GetFiles(Path.Combine(Data, "games")).Select(f => Path.GetFileName(f)).Order());
        Assert.True(File.Exists(Path.Combine(Data, "current-game.json")));
        JsonObject settings = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!.AsObject();
        Assert.Equal("blue", (string?)settings["boardTheme"]);
        Assert.Equal("secret-2", (string?)settings["onlineToken"]);
    }

    [Fact]
    public void Reset_clears_progress_keeps_settings_and_saves_a_copy_first()
    {
        string? copy = ProgressBackup.SaveAutomaticCopy(Data, "before reset", "1.0");
        ProgressBackup.Reset(Data);

        Assert.False(File.Exists(Path.Combine(Data, "profile.json")));
        Assert.False(File.Exists(Path.Combine(Data, "current-game.json")));
        Assert.Empty(Directory.GetFiles(Path.Combine(Data, "games")));
        Assert.Contains("secret-1", File.ReadAllText(Path.Combine(Data, "settings.json")));

        Assert.NotNull(copy);
        Assert.StartsWith(Path.Combine(Data, ProgressBackup.AutomaticFolder), copy);
        Assert.NotNull(ProgressBackup.Read(copy, out _));
        Assert.Null(ProgressBackup.SaveAutomaticCopy(Data, "nothing left", "1.0")); // no profile any more
    }

    [Fact]
    public void Only_gambit_backups_can_be_restored()
    {
        string notZip = Path.Combine(_root, "notes.zip");
        File.WriteAllText(notZip, "hello");
        Assert.Null(ProgressBackup.Read(notZip, out string? problem));
        Assert.NotNull(problem);

        string otherZip = Path.Combine(_root, "other.zip");
        using (ZipArchive zip = ZipFile.Open(otherZip, ZipArchiveMode.Create)) zip.CreateEntry("profile.json");
        Assert.Null(ProgressBackup.Read(otherZip, out problem));
        Assert.Throws<InvalidDataException>(() => ProgressBackup.Restore(otherZip, Data));
        Assert.Contains("Sam", File.ReadAllText(Path.Combine(Data, "profile.json"))); // nothing was touched
    }

    [Fact]
    public void Restore_never_writes_outside_the_data_folder()
    {
        string zip = Path.Combine(_root, "backup.zip");
        ProgressBackup.Create(Data, zip, "1.0");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(archive.CreateEntry("games/../../evil.pgn").Open());
            writer.Write("x");
        }

        ProgressBackup.Restore(zip, Data);

        Assert.False(File.Exists(Path.Combine(_root, "evil.pgn")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(Data, "games")).Length);
    }
}
