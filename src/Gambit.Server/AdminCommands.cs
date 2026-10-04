using System.Globalization;
using Gambit.Server.Data;

namespace Gambit.Server;

/// <summary>
/// Server administration from the command line: <c>Gambit.Server invite</c>, <c>users</c>, <c>ban</c>, ...
/// They work on the data folder's database directly, also while the server is running (on Fly.io:
/// <c>fly ssh console -C "dotnet /app/Gambit.Server.dll users"</c>).
/// </summary>
public static class AdminCommands
{
    private const string Usage = """
        Gambit server administration (the server itself starts when no command is given):

          invite [--uses N] [--days D] [--admin] [--note TEXT]   create an invite code (default: 1 use, 14 days; --days 0 = no expiry)
          invites                                                list unused invite codes
          revoke-invite CODE                                     cancel an invite code
          users                                                  list accounts
          reset-password USER                                    set a new random password (printed) and sign USER out
          ban USER REASON...                                     suspend an account and sign it out
          unban USER                                             lift a suspension
          rename USER NEWNAME                                    change a username
          make-admin USER | remove-admin USER                    grant or remove admin rights
          delete-user USER --yes                                 delete an account (its finished games stay)
          backup FILE                                            copy the database to FILE (safe while running)

        The data folder is GAMBIT_DATA (or Gambit:DataDirectory in appsettings.json, default ./data).
        """;

    private static readonly HashSet<string> Commands =
    [
        "invite", "invites", "revoke-invite", "users", "reset-password", "ban", "unban", "rename",
        "make-admin", "remove-admin", "delete-user", "backup", "help", "--help", "-h",
    ];

    public static bool IsCommand(string arg) => Commands.Contains(arg);

    /// <summary>Runs one command; returns the process exit code.</summary>
    public static int Run(string[] args, ServerOptions options, TextWriter output)
    {
        string command = args[0];
        if (command is "help" or "--help" or "-h")
        {
            output.WriteLine(Usage);
            return 0;
        }

        var db = new ServerDatabase(options);
        var accounts = new AccountStore(db, options);
        string? Arg(int i) => args.Length > i ? args[i] : null;
        string? Flag(string name) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        switch (command)
        {
            case "invite":
            {
                int uses = int.TryParse(Flag("--uses"), out int u) && u > 0 ? u : 1;
                int? days = Flag("--days") is string d ? int.TryParse(d, out int dd) && dd > 0 ? dd : null : options.InviteDays;
                Invite invite = accounts.CreateInvite(null, uses, days is int n ? TimeSpan.FromDays(n) : null, args.Contains("--admin"), Flag("--note"));
                output.WriteLine($"Invite code: {invite.Code}");
                output.WriteLine($"  {Describe(invite)}");
                return 0;
            }
            case "invites":
            {
                IReadOnlyList<Invite> open = accounts.OpenInvites();
                if (open.Count == 0) output.WriteLine("No unused invite codes.");
                foreach (Invite i in open)
                {
                    string by = i.CreatedBy == null ? "admin" : accounts.FindById(i.CreatedBy)?.Username ?? i.CreatedBy;
                    output.WriteLine($"{i.Code}  by {by}  {Describe(i)}");
                }
                return 0;
            }
            case "revoke-invite" when Arg(1) is string code:
                return Report(output, accounts.DeleteInvite(code), $"Invite {AccountStore.FormatCode(code)} cancelled.", "No such invite code.");
            case "users":
            {
                IReadOnlyList<Account> all = accounts.All();
                output.WriteLine(all.Count == 1 ? "1 account" : $"{all.Count} accounts");
                foreach (Account a in all)
                {
                    string flags = (a.IsAdmin ? " admin" : "") + (a.IsBanned ? $" SUSPENDED ({a.BanReason})" : "");
                    output.WriteLine($"{a.Username,-20} since {a.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{flags}");
                }
                return 0;
            }
            case "reset-password" when Arg(1) is string user:
            {
                if (accounts.ResetPassword(user) is not string password) return Report(output, false, "", $"No account named {user}.");
                output.WriteLine($"New password for {user}: {password}");
                output.WriteLine("They are signed out everywhere; give them this password privately.");
                return 0;
            }
            case "ban" when Arg(1) is string user && args.Length > 2:
                return Report(output, accounts.SetBanned(user, string.Join(' ', args[2..])), $"{user} is suspended and signed out.", $"No account named {user}.");
            case "unban" when Arg(1) is string user:
                return Report(output, accounts.SetBanned(user, null), $"{user} can play again.", $"No account named {user}.");
            case "rename" when Arg(1) is string user && Arg(2) is string newName:
            {
                string? error = accounts.Rename(user, newName);
                return Report(output, error == null, $"{user} is now {newName} (on their next connection).", error ?? "");
            }
            case "make-admin" when Arg(1) is string user:
                return Report(output, accounts.SetAdmin(user, true), $"{user} is an admin.", $"No account named {user}.");
            case "remove-admin" when Arg(1) is string user:
                return Report(output, accounts.SetAdmin(user, false), $"{user} is no longer an admin.", $"No account named {user}.");
            case "delete-user" when Arg(1) is string user:
                if (!args.Contains("--yes"))
                {
                    output.WriteLine($"This deletes {user}'s account, ratings and sign-ins for good. Add --yes to confirm.");
                    return 1;
                }
                return Report(output, accounts.Delete(user), $"{user} was deleted.", $"No account named {user}.");
            case "backup" when Arg(1) is string file:
                db.BackupTo(file);
                output.WriteLine($"Database copied to {Path.GetFullPath(file)}.");
                return 0;
            default:
                output.WriteLine(Usage);
                return 1;
        }
    }

    private static string Describe(Invite i) =>
        (i.UsesLeft == 1 ? "1 use" : $"{i.UsesLeft} uses")
        + (i.ExpiresAt is DateTimeOffset e ? $", valid until {e.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}" : ", no expiry")
        + (i.MakesAdmin ? ", makes an admin" : "")
        + (i.Note is { Length: > 0 } note ? $" ({note})" : "");

    private static int Report(TextWriter output, bool ok, string success, string failure)
    {
        output.WriteLine(ok ? success : failure);
        return ok ? 0 : 1;
    }
}
