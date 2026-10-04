using System.Text.RegularExpressions;

namespace Gambit.Server.Data;

/// <summary>What makes a username or a password acceptable. Messages are shown to the player as is.</summary>
public static partial class NameRules
{
    public const int MinUsername = 3, MaxUsername = 20, MinPassword = 8, MaxPassword = 128;

    /// <summary>Names that would look official or confusing.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "moderator", "mod", "gambit", "system", "server", "support", "staff",
        "guest", "anonymous", "root", "owner", "official", "help", "null", "undefined", "spectator",
    };

    /// <summary>Very common passwords that are guessed first.</summary>
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "passw0rd", "12345678", "123456789", "1234567890",
        "qwertyui", "qwerty123", "qwertyuiop", "11111111", "00000000", "abcd1234", "abc12345", "iloveyou",
        "letmein1", "welcome1", "sunshine", "football", "baseball", "superman", "trustno1", "chess123",
        "chessmaster", "checkmate", "gambit123", "admin123", "asdfghjk", "zaq12wsx", "1q2w3e4r", "changeme",
    };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex UsernamePattern();

    /// <summary>Null if <paramref name="username"/> can be registered, otherwise why not.</summary>
    public static string? CheckUsername(string? username)
    {
        username ??= "";
        if (username.Length < MinUsername || username.Length > MaxUsername)
            return $"Usernames are {MinUsername} to {MaxUsername} characters long.";
        if (!UsernamePattern().IsMatch(username))
            return "Use letters, digits, - and _ only, starting with a letter or digit.";
        if (Reserved.Contains(username))
            return "That username is reserved. Please pick another.";
        return null;
    }

    /// <summary>Null if <paramref name="password"/> is acceptable for <paramref name="username"/>, otherwise why not.</summary>
    public static string? CheckPassword(string? password, string username)
    {
        password ??= "";
        if (password.Length < MinPassword) return $"Passwords need at least {MinPassword} characters.";
        if (password.Length > MaxPassword) return $"Passwords can be at most {MaxPassword} characters.";
        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase)) return "Your password can't be your username.";
        if (CommonPasswords.Contains(password) || password.Distinct().Count() <= 2) return "That password is too easy to guess.";
        return null;
    }
}
