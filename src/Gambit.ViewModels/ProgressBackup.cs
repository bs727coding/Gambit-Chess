using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gambit.ViewModels;

/// <summary>What a backup file holds, from its manifest.</summary>
public sealed record BackupInfo(DateTimeOffset Created, string AppVersion, int Games);

/// <summary>
/// Backs up, restores and resets the player's progress in the app's data folder: profile.json,
/// games/*.pgn, current-game.json and settings.json. A backup is a zip file. The online sign-in
/// token is never written to one (backups get copied and shared), and a restore keeps the current one.
/// </summary>
public static class ProgressBackup
{
    public const int FormatVersion = 1;

    /// <summary>Automatic copies taken before a restore or reset, inside the data folder.</summary>
    public const string AutomaticFolder = "backups";

    private const int AutomaticKept = 10;
    private const string ManifestFile = "backup.json";
    private const string ProfileFile = "profile.json";
    private const string SettingsFile = "settings.json";
    private const string CurrentGameFile = "current-game.json";
    private const string GamesFolder = "games";
    private const string TokenProperty = "onlineToken"; // the guest sign-in that settings.json held before accounts

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record Manifest(string App, int FormatVersion, string AppVersion, DateTimeOffset Created, int Games);

    /// <summary>Writes the progress in <paramref name="dataDir"/> to a new zip file (replacing <paramref name="zipPath"/>).</summary>
    public static void Create(string dataDir, string zipPath, string appVersion)
    {
        if (!File.Exists(Path.Combine(dataDir, ProfileFile))) throw new FileNotFoundException("There is no profile to back up yet.");
        string tmp = zipPath + ".tmp";
        File.Delete(tmp);
        using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Path.Combine(dataDir, ProfileFile), ProfileFile);
            if (File.Exists(Path.Combine(dataDir, CurrentGameFile)))
                zip.CreateEntryFromFile(Path.Combine(dataDir, CurrentGameFile), CurrentGameFile);
            if (ReadSettings(dataDir) is JsonObject settings)
            {
                settings.Remove(TokenProperty);
                WriteText(zip, SettingsFile, settings.ToJsonString(Json));
            }

            int games = 0;
            string gamesDir = Path.Combine(dataDir, GamesFolder);
            if (Directory.Exists(gamesDir))
            {
                foreach (string pgn in Directory.GetFiles(gamesDir, "*.pgn").Order(StringComparer.Ordinal))
                {
                    zip.CreateEntryFromFile(pgn, $"{GamesFolder}/{Path.GetFileName(pgn)}");
                    games++;
                }
            }
            WriteText(zip, ManifestFile, JsonSerializer.Serialize(new Manifest("Gambit", FormatVersion, appVersion, DateTimeOffset.Now, games), Json));
        }
        File.Move(tmp, zipPath, overwrite: true);
    }

    /// <summary>The backup's details, or null (with the reason) if it can't be restored.</summary>
    public static BackupInfo? Read(string zipPath, out string? problem)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry? manifestEntry = zip.GetEntry(ManifestFile);
            ZipArchiveEntry? profileEntry = zip.GetEntry(ProfileFile);
            Manifest? manifest = manifestEntry == null ? null : JsonSerializer.Deserialize<Manifest>(ReadText(manifestEntry), Json);
            if (manifest?.App != "Gambit" || profileEntry == null)
            {
                problem = "This file isn't a Gambit backup.";
                return null;
            }
            if (manifest.FormatVersion > FormatVersion)
            {
                problem = $"This backup was made by a newer version of Gambit ({manifest.AppVersion}).";
                return null;
            }
            using (JsonDocument.Parse(ReadText(profileEntry))) { } // the profile must at least be valid JSON
            problem = null;
            return new BackupInfo(manifest.Created, manifest.AppVersion, manifest.Games);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            problem = "This file isn't a Gambit backup, or it is damaged.";
            return null;
        }
        catch (IOException ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Replaces the progress in <paramref name="dataDir"/> with the backup's. Settings come from the
    /// backup too, except the online sign-in token, which stays as it is.
    /// </summary>
    public static void Restore(string zipPath, string dataDir)
    {
        if (Read(zipPath, out string? problem) == null) throw new InvalidDataException(problem);
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        string? token = ReadSettings(dataDir)?[TokenProperty]?.GetValue<string>();

        ClearProgress(dataDir);
        Directory.CreateDirectory(Path.Combine(dataDir, GamesFolder));
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            switch (entry.FullName)
            {
                case ProfileFile or CurrentGameFile:
                    entry.ExtractToFile(Path.Combine(dataDir, entry.FullName), overwrite: true);
                    break;
                case SettingsFile:
                    if (JsonNode.Parse(ReadText(entry)) is JsonObject settings)
                    {
                        if (string.IsNullOrEmpty(token)) settings.Remove(TokenProperty);
                        else settings[TokenProperty] = token;
                        File.WriteAllText(Path.Combine(dataDir, SettingsFile), settings.ToJsonString(Json));
                    }
                    break;
                default:
                    // Only plain file names inside games/ (nothing can be written outside the data folder).
                    string name = entry.FullName.StartsWith(GamesFolder + "/", StringComparison.Ordinal) ? entry.FullName[(GamesFolder.Length + 1)..] : "";
                    if (name.EndsWith(".pgn", StringComparison.OrdinalIgnoreCase) && name == Path.GetFileName(name))
                        entry.ExtractToFile(Path.Combine(dataDir, GamesFolder, name), overwrite: true);
                    break;
            }
        }
    }

    /// <summary>Deletes all progress (profile, games, the unfinished game). Settings stay.</summary>
    public static void Reset(string dataDir) => ClearProgress(dataDir);

    /// <summary>
    /// Copies the current progress into the data folder's backups/ (before a restore or reset) and keeps
    /// the newest ten. Returns the file, or null if there is no profile yet.
    /// </summary>
    public static string? SaveAutomaticCopy(string dataDir, string reason, string appVersion)
    {
        if (!File.Exists(Path.Combine(dataDir, ProfileFile))) return null;
        string dir = Directory.CreateDirectory(Path.Combine(dataDir, AutomaticFolder)).FullName;
        string path = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd HHmmss} {reason}.zip");
        Create(dataDir, path, appVersion);
        foreach (string old in Directory.GetFiles(dir, "*.zip").OrderDescending(StringComparer.Ordinal).Skip(AutomaticKept))
            File.Delete(old);
        return path;
    }

    private static void ClearProgress(string dataDir)
    {
        File.Delete(Path.Combine(dataDir, ProfileFile));
        File.Delete(Path.Combine(dataDir, CurrentGameFile));
        string gamesDir = Path.Combine(dataDir, GamesFolder);
        if (Directory.Exists(gamesDir))
            foreach (string pgn in Directory.GetFiles(gamesDir, "*.pgn")) File.Delete(pgn);
    }

    private static JsonObject? ReadSettings(string dataDir)
    {
        string path = Path.Combine(dataDir, SettingsFile);
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
