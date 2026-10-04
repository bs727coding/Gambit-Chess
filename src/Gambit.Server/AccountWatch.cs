using Gambit.Online;
using Gambit.Server.Data;
using Microsoft.AspNetCore.SignalR;

namespace Gambit.Server;

/// <summary>
/// Applies account changes made with the admin commands (another process) to connected players within
/// <see cref="Interval"/>: suspended or deleted accounts are told why and disconnected (a game in
/// progress then counts as abandoned if they don't come back), renamed ones play on under the new name.
/// </summary>
public sealed class AccountWatch(PlayerRegistry players, AccountStore accounts, IHubContext<GameHub, IGameClient> hub, ILogger<AccountWatch> log)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckAsync();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Account check failed");
            }
        }
    }

    public async Task CheckAsync()
    {
        foreach (Player p in players.Connected())
        {
            Account? account = accounts.FindById(p.PublicId);
            if (account == null || account.IsBanned)
            {
                string reason = account == null ? "This account was deleted." : AccountStore.BannedMessage(account);
                log.LogInformation("Disconnecting {Name}: {Reason}", p.Name, reason);
                if (p.ConnectionId is string connection)
                {
                    await hub.Clients.Client(connection).Notice(reason);
                    await Task.Delay(500); // let the message reach them before the connection closes
                }
                p.Kick?.Invoke();
            }
            else if (account.Username != p.Name)
            {
                p.Name = account.Username;
            }
        }
    }
}
