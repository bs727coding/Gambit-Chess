using Gambit.Server.Data;

namespace Gambit.Server;

/// <summary>
/// Keeps a daily copy of the database in the data folder's backups/ (the newest <see cref="Kept"/>), a
/// quick way back from a mistake. Copies that survive losing the server itself are the host's volume
/// snapshots and the downloads admins make from the app (GET <see cref="Gambit.Online.AccountApi.Backup"/>).
/// </summary>
public sealed class DatabaseBackups(ServerDatabase db, ServerOptions options, ILogger<DatabaseBackups> log, TimeProvider? clock = null)
    : BackgroundService
{
    public const int Kept = 7;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string Folder => Path.Combine(options.DataDirectory, "backups");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // not while the server is starting up
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                if (MakeDailyCopy() is string file) log.LogInformation("Database copied to {File}", file);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Daily database copy failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Writes today's copy unless it exists, and removes all but the newest <see cref="Kept"/>. Returns the new file, or null.</summary>
    public string? MakeDailyCopy()
    {
        Directory.CreateDirectory(Folder);
        string file = Path.Combine(Folder, $"gambit-{_clock.GetUtcNow():yyyy-MM-dd}.db");
        if (File.Exists(file)) return null;
        db.BackupTo(file);
        foreach (string old in Directory.GetFiles(Folder, "gambit-*.db").OrderDescending(StringComparer.Ordinal).Skip(Kept))
            File.Delete(old);
        return file;
    }
}
