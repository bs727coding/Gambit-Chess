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

    // Offensive names. The first list is refused anywhere in a name, also spelled with look-alike
    // digits (0 for o, 3 for e, ...) or split up with - and _. The short words of the second list are
    // refused only as a separate word ("Big-Dick", "BigDick"), so names such as Cassandra, Dickens,
    // Peacock or Therapist stay possible. Admins rename anything that slips through (Gambit.Server rename).
    private static readonly string[] OffensiveAnywhere =
    [
        "fuck", "shit", "cunt", "nigger", "nigga", "faggot", "retard", "whore", "bitch", "pussy", "porn",
        "penis", "vagina", "hitler", "tranny", "twat", "wanker", "bastard", "molest", "dickhead",
        "asshole", "slut", "jizz", "cumshot", "kkk",
    ];

    private static readonly HashSet<string> OffensiveWords = new(StringComparer.Ordinal)
    {
        "ass", "fag", "dick", "cock", "rape", "rapist", "nazi", "kike", "spic", "chink", "dyke", "pedo", "sex", "cum",
        "tit", "tits", "piss", "gook", "homo", "nonce", "wank",
    };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex UsernamePattern();

    /// <summary>Words of a name: "BigDick_99" → Big, Dick, 99.</summary>
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex WordPattern();

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
        if (IsOffensive(username))
            return "Please pick a different username.";
        return null;
    }

    /// <summary>True if the name contains an offensive word (see the lists above).</summary>
    public static bool IsOffensive(string username)
    {
        // A 1 can stand for i or l, so read the name both ways.
        foreach (char one in "il")
        {
            string mapped = string.Concat(username.Select(c => c switch
            {
                '0' => 'o', '1' => one, '3' => 'e', '4' => 'a', '5' => 's', '7' => 't', '8' => 'b', '9' => 'g', _ => c,
            }));
            string letters = string.Concat(mapped.Where(char.IsLetter)).ToLowerInvariant();
            if (OffensiveAnywhere.Any(letters.Contains)) return true;
            if (WordPattern().Matches(mapped).Any(m => OffensiveWords.Contains(m.Value.ToLowerInvariant()))) return true;
        }
        return false;
    }

    /// <summary>A chat line with every word that would be refused in a username replaced by asterisks.</summary>
    public static string MaskOffensive(string text) =>
        ChatWordPattern().Replace(text, m => IsOffensive(m.Value) ? new string('*', m.Value.Length) : m.Value);

    [GeneratedRegex(@"[\p{L}\p{N}_-]+")]
    private static partial Regex ChatWordPattern();

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
