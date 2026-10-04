using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gambit.App.Services;

/// <summary>
/// Where Gambit keeps its data: %LOCALAPPDATA%\Gambit (per Windows user, unpackaged app). Setting the
/// GAMBIT_DATA_DIR environment variable points the app at another folder, e.g. a throwaway profile
/// for testing that leaves the real one untouched.
/// </summary>
public static class AppPaths
{
    private static readonly string? Custom = Environment.GetEnvironmentVariable("GAMBIT_DATA_DIR") is { Length: > 0 } dir ? dir : null;

    public static string Root { get; } = Directory.CreateDirectory(
        Custom ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gambit")).FullName;

    /// <summary>Running on a throwaway profile (GAMBIT_DATA_DIR): the title bar says so.</summary>
    public static bool IsTestProfile => Custom != null;

    /// <summary>A short id for this data folder (8 hex digits), for names that must differ per profile.</summary>
    public static string RootId { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Root.ToLowerInvariant())))[..8];

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Profile => Path.Combine(Root, "profile.json");
    public static string Games { get; } = Directory.CreateDirectory(Path.Combine(Root, "games")).FullName;
    public static string Logs { get; } = Directory.CreateDirectory(Path.Combine(Root, "logs")).FullName;

    /// <summary>
    /// Set once the data folder was restored or reset and the app is restarting: saves are skipped,
    /// so the old state still in memory can't write over the new files.
    /// </summary>
    public static bool Frozen { get; set; }
}

/// <summary>Small helper for atomic JSON load/save with sensible defaults when files are missing or corrupt.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception ex)
        {
            // Keep the broken file for diagnosis, start fresh.
            Log.Warn($"Could not read {path}: {ex.Message}");
            try { File.Copy(path, path + ".corrupt", overwrite: true); } catch { }
        }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        if (AppPaths.Frozen) return;
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Minimal file logger (crashes and warnings) — %LOCALAPPDATA%\Gambit\logs.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                string file = Path.Combine(AppPaths.Logs, $"gambit-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
