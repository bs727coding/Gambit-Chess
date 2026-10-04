using System.Security.Cryptography;
using System.Text;
using Windows.Security.Credentials;

namespace Gambit.App.Services;

/// <summary>
/// Saved sign-ins, one per server, in Windows Credential Manager: encrypted for this Windows user and
/// never written to settings.json or progress backups. Kept apart per data folder, so a test profile
/// can't see or replace the real profile's sign-in.
/// </summary>
public static class OnlineCredentials
{
    public sealed record SavedSignIn(string Username, string Token);

    private static readonly string Resource =
        $"Gambit online ({Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.Root.ToLowerInvariant())))[..8]})";

    public static SavedSignIn? Get(string serverUrl)
    {
        try
        {
            PasswordCredential credential = new PasswordVault().Retrieve(Resource, Key(serverUrl));
            credential.RetrievePassword();
            string[] parts = credential.Password.Split('\n', 2);
            return parts.Length == 2 ? new SavedSignIn(parts[0], parts[1]) : null;
        }
        catch (Exception)
        {
            return null; // Retrieve throws when there is nothing saved
        }
    }

    public static void Save(string serverUrl, string username, string token)
    {
        Remove(serverUrl);
        new PasswordVault().Add(new PasswordCredential(Resource, Key(serverUrl), $"{username}\n{token}"));
    }

    public static void Remove(string serverUrl)
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(Resource, Key(serverUrl)));
        }
        catch (Exception)
        {
            // Nothing saved.
        }
    }

    private static string Key(string serverUrl) => serverUrl.Trim().TrimEnd('/').ToLowerInvariant();
}
