// Gambit online bot: plays on a Gambit server as one of the built-in bots.
//   dotnet run --project tools/Gambit.OnlineBot -- <server-url> <bot-id> <time-control | challenge-code> [games]
//   e.g. dotnet run --project tools/Gambit.OnlineBot -- http://localhost:5080 harbor 3+2
// With a challenge code and games > 1 the bot accepts rematch offers (waiting up to 2 minutes for one).
using Gambit.Core.Board;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;
using Gambit.Online;
using Gambit.Online.Client;

string url = args.Length > 0 ? args[0] : "http://localhost:5080";
BotProfile profile = BotRoster.Get(args.Length > 1 ? args[1] : "harbor");
string target = args.Length > 2 ? args[2] : "3+2";
int gamesToPlay = args.Length > 3 && int.TryParse(args[3], out int n) ? n : 1;
bool isCode = !target.Contains('+');

var client = new OnlineClient();
var bot = new BotMoveProvider(profile) { HumanLikeDelay = true };
var done = new TaskCompletionSource();
int played = 0, started = 0;
RemoteGameSession? session = null;

client.GameStarted += start =>
{
    started++;
    session?.Dispose();
    session = new RemoteGameSession(client, start);
    RemoteGameSession s = session;
    Console.WriteLine($"Game {start.GameId}: {start.White.Name} vs {start.Black.Name} ({start.TimeControl.Key}), playing {start.YourColor}");
    s.MovePlayed += (_, e) =>
    {
        Console.WriteLine($"  {e.Move.MoveNumber}{(e.Move.Side == Color.White ? "." : "...")} {e.Move.San}");
        _ = MaybeMove(s);
    };
    s.GameEnded += async (_, e) =>
    {
        Console.WriteLine($"Game over: {e.Description}");
        bot.NewGame();
        if (++played >= gamesToPlay) done.TrySetResult();
        else if (isCode) await WaitForRematchAsync(started);
        else await client.SeekAsync(Parse(target));
    };
    s.DrawOfferReceived += (_, _) => s.RespondToDraw(false);
    s.RematchChanged += (_, _) =>
    {
        if (s.Rematch != RematchStatus.Received) return;
        Console.WriteLine("Rematch requested — accepting.");
        s.OfferRematch();
    };
    s.Start();
    _ = MaybeMove(s);
};

await client.ConnectAsync(url, $"bot-{profile.Id}-{Environment.MachineName}-{Guid.NewGuid():N}", $"{profile.Name} (bot)");
Console.WriteLine($"Connected to {url} as {client.Me?.Name}");
if (isCode)
{
    if (!await client.AcceptChallengeAsync(target)) Console.WriteLine($"Challenge code {target} not found.");
}
else
{
    await client.SeekAsync(Parse(target));
    Console.WriteLine($"Seeking a {target} game…");
}
await done.Task;
await client.DisposeAsync();

async Task WaitForRematchAsync(int gamesSoFar)
{
    Console.WriteLine("Waiting for a rematch offer…");
    await Task.Delay(TimeSpan.FromMinutes(2));
    if (started == gamesSoFar) done.TrySetResult();
}

async Task MaybeMove(RemoteGameSession s)
{
    if (s.Game.IsOver || !s.IsLocalSide(s.Game.SideToMove)) return;
    var snapshot = new GameSnapshot(s.Game.Position.Clone(), s.Clock?.Remaining(s.LocalColor), s.Clock?.Remaining(s.LocalColor.Opposite()),
        s.TimeControl.Increment, s.Game.Moves.Count);
    Move m = await bot.ChooseMoveAsync(snapshot, CancellationToken.None);
    if (!m.IsNone && s.Game.Position.Key == snapshot.Position.Key) s.TrySubmitMove(m);
}

static TimeControlDto Parse(string key)
{
    string[] p = key.Split('+');
    return new TimeControlDto(int.Parse(p[0]) * 60, p.Length > 1 ? int.Parse(p[1]) : 0);
}
