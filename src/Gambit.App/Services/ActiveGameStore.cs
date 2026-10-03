using Gambit.ViewModels;

namespace Gambit.App.Services;

/// <summary>Persists the game in progress (current-game.json) after every move.</summary>
public static class ActiveGameStore
{
    private static string FilePath => Path.Combine(AppPaths.Root, "current-game.json");

    public static bool Exists => File.Exists(FilePath);

    public static void Save(SavedGame saved)
    {
        try
        {
            JsonStore.Save(FilePath, saved);
        }
        catch (Exception ex)
        {
            Log.Warn($"Saving the current game failed: {ex.Message}");
        }
    }

    public static SavedGame? Load()
    {
        if (!Exists) return null;
        SavedGame saved = JsonStore.Load<SavedGame>(FilePath);
        return saved.Moves.Count == 0 && saved.BotId == null ? null : saved;
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Log.Warn($"Clearing the saved game failed: {ex.Message}");
        }
    }
}
