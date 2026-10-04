# Gambit — Online play

## How it works

```
Gambit app ──HTTPS: sign-in, invites (/api)──► Gambit.Server ──► gambit.db (SQLite: accounts, ratings, games)
           ──SignalR (WebSockets): games ────►   GameManager (rooms, clocks, matchmaking, challenges)
   RemoteGameSession : IGameSession              Gambit.Core (rules) + Glicko-2 ratings
```

* **The server is authoritative.** Every move is checked with `Gambit.Core` before it is broadcast;
  clocks, flag falls, first-move aborts (45 s) and abandonment (60 s to reconnect) are decided on
  the server.
* **Walkouts:** not making the first move, resigning before both sides have moved, or leaving a game
  counts as a walkout. Three within 30 minutes pause that player's quick pairing for 10 minutes
  after the last one (games with friends still work). Kept in memory: a restart forgives.
* **The app plays online games through the same game page as bot games** — only the session type
  differs (`RemoteGameSession` instead of `LocalGameSession`). Your moves are applied instantly and
  confirmed by the server; if anything disagrees the app resyncs from the server's move list.
* **Accounts:** a username and a password (no email). Servers are **invite-only** by default: a new
  account needs an invite code. Passwords are stored as salted PBKDF2 hashes; signing in gives the
  app a session key, which it keeps in Windows Credential Manager (never in settings or backups)
  and the server keeps only as a hash. Five wrong passwords pause sign-in for that name for ten
  minutes, and sign-in requests are rate-limited per address.
* **Ratings:** Glicko-2, separately for bullet / blitz / rapid / classical.
* **Storage:** everything lives in one SQLite file, `gambit.db` in the data folder: accounts,
  sign-in sessions, invites, ratings and every finished game with its PGN.
* **Fair play:** hints, takebacks and the engine are disabled in online games.

## Accounts and invites

* **The first account:** a new server prints a one-time invite code in its log ("No accounts yet …
  use invite code ABCD-EFGH"). Create your own account with it: that account is the admin.
* **Inviting people:** in Gambit, **Online → Invite a friend** makes a code that works once and for
  14 days (up to 5 unused at a time; admins: no limit). Your friend enters the server address, then
  **Create an account** with the code. Admins can also make codes on the server (below).
* **Open sign-ups** (anyone who can reach the server): set `Gambit:SignUps` to `Open`.

### Administration

Run the server binary with a command; it works on the same database, also while the server runs:

```
dotnet run --project src/Gambit.Server -- invite --uses 3 --note "chess club"
dotnet run --project src/Gambit.Server -- users
```

| Command | Does |
|---|---|
| `invite [--uses N] [--days D] [--admin] [--note TEXT]` | new invite code (default 1 use, 14 days; `--days 0` never expires) |
| `invites` / `revoke-invite CODE` | list unused codes / cancel one |
| `users` | list accounts (admins and suspensions marked) |
| `reset-password USER` | sets and prints a new random password; signs the account out everywhere |
| `ban USER REASON…` / `unban USER` | suspend (signs out at once, sign-in refused with the reason) / lift |
| `rename USER NEWNAME` | change a username (shown from their next connection) |
| `make-admin USER` / `remove-admin USER` | admin rights (admins' invites have no limit) |
| `delete-user USER --yes` | delete an account, its ratings and sign-ins (finished games keep its id) |
| `backup FILE` | consistent copy of `gambit.db` (safe while running) |

On Fly.io: `fly ssh console -C "dotnet /app/Gambit.Server.dll users"`. Locally the data folder is
`GAMBIT_DATA`, or `data/` next to the server binary.

## Play with friends on your network (easiest)

1. On one PC run: `./build.ps1 server` (allow it through the Windows firewall when asked). Note the
   invite code it prints the first time.
2. Find that PC's address: `ipconfig` → IPv4 address, e.g. `192.168.1.20`.
3. Open **Online** in Gambit, enter `http://192.168.1.20:5080`, **Connect**, then **Create an
   account** with that invite code. Use **Invite a friend** to get a code for each friend.
4. Use **Play a friend** → *Create* and share the 6-character code, or **Quick pairing**.

## Play over the internet

Pick one:

* **Tailscale (no router changes):** install Tailscale on your PC and your friends' PCs, run the
  server, and connect to `http://<your-tailscale-ip>:5080`.
* **Port forwarding:** forward TCP 5080 on your router to the server PC and give friends your
  public IP. Prefer the cloud options below for anything long-running.
* **Cloud (recommended for a public server):** the repo has a `Dockerfile` and a `fly.toml`.
  You need your own account with the provider; nothing is deployed automatically.

### Hosting rules that matter

1. **Run exactly one instance.** Games, clocks, seeks and challenge codes live in the server's
   memory. Two instances would split players between them, so turn off autoscaling and
   scale-to-zero (scaling to zero would also end every game in progress).
2. **Give it a persistent volume at `/data`.** The database (`gambit.db`: accounts, ratings, games)
   lives there; without a volume everything resets whenever the container is replaced.
3. **Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` behind the host's proxy**, so the per-IP
   connection limit sees real client addresses instead of the proxy's.
4. WebSockets must be allowed (they are on all the options below). Health check: `GET /health`.

### Fly.io (simplest)

```
fly auth login
# edit `app` in fly.toml (names are global), then:
fly launch --copy-config --no-deploy
fly volumes create gambit_data --size 1
fly deploy --ha=false
```

`fly.toml` already encodes the rules above (one machine that never auto-stops, the `/data` volume,
forwarded headers, a `/health` check). The app is then at `https://<app>.fly.dev`.

### Azure Container Apps

```
az login
az containerapp up --name gambit --resource-group gambit --location eastus --source . --ingress external --target-port 8080 --env-vars ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
az containerapp update --name gambit --resource-group gambit --min-replicas 1 --max-replicas 1
```

The second command is essential (Container Apps scales between zero and several replicas by
default). For ratings that survive restarts, add an Azure Files share mounted at `/data`
(Microsoft's guide: *Use storage mounts in Azure Container Apps*).

### Any Docker host

```
docker build -t gambit-server .
docker run -d --restart unless-stopped -p 8080:8080 -v gambit-data:/data gambit-server
```

Put a TLS reverse proxy (Caddy, nginx) in front for an `https://` address, with WebSockets enabled
and forwarded headers on.

## Configuration

| Setting | Default | Notes |
|---|---|---|
| `ASPNETCORE_URLS` | `http://localhost:5000` (`http://+:8080` in Docker) | Listen address |
| `GAMBIT_DATA` | `./data` next to the server | Where `gambit.db` lives |
| `Gambit:SignUps` | `Invite` | `Open` lets anyone create an account |
| `Gambit:MembersCanInvite` / `Gambit:InvitesPerMember` / `Gambit:InviteDays` | `true` / `5` / `14` | Invites made in the app |
| `Gambit:SessionDays` | `180` | A sign-in ends after this many days unused |
| `Gambit:AccountRequestsPerMinute` | `20` | Sign-in and sign-up requests per client IP |
| `Gambit:WalkoutsBeforePause` / `Gambit:WalkoutWindow` / `Gambit:PairingPause` | `3` / `00:30:00` / `00:10:00` | Quick-pairing pause for repeat walkouts |
| `Gambit:ReconnectGrace` | `00:01:00` | appsettings.json or env `Gambit__ReconnectGrace` |
| `Gambit:FirstMoveTimeout` | `00:00:45` | Game aborted if White doesn't move |
| `Gambit:CallsPerSecond` / `Gambit:CallBurst` | `10` / `30` | Hub calls per connection (token bucket); excess calls fail with "Too many requests" |
| `Gambit:ConnectionsPerMinute` | `60` | Hub HTTP requests per client IP (a connect is ~2); excess gets HTTP 429 |
| `Gambit:MaxOpenChallenges` | `5` | Friend codes per player; creating more drops the oldest |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | unset | Set to `true` behind a reverse proxy (Azure Container Apps, Fly.io, nginx) so limits see the real client IP |

Health check: `GET /health` → `ok`. `GET /` shows players online and games in progress.

## Protocol (Gambit.Online.Contracts)

Accounts (HTTP + JSON, `AccountApi`): `GET /api/info`, `POST /api/register`, `POST /api/signin`
(→ session key), and with `Authorization: Bearer <key>`: `POST /api/signout`, `GET /api/me`,
`POST /api/password`, `POST|GET /api/invites`. The hub requires the same key (SignalR sends it as
`?access_token=`); an unknown, expired or suspended session gets HTTP 401.

Client → server: `Hello(name, version)` (the player is the signed-in account), `Seek(tc)`, `CancelSeek()`, `CreateChallenge(tc, color)`,
`AcceptChallenge(code)`, `MakeMove(gameId, ply, uci)`, `Resign`, `OfferDraw`, `RespondToDraw`, `Rejoin`,
`OfferRematch(gameId)`, `DeclineRematch(gameId)`, `ListGames()`, `Watch(gameId)`, `Unwatch(gameId)`.

Server → client: `Welcome`, `GameStarted`, `MovePlayed`, `GameOver`, `DrawOffered`, `DrawDeclined`,
`OpponentConnection`, `Resync`, `Notice`, `RematchOffered`, `RematchDeclined(gameId, unavailable)`.

Moves travel as UCI strings with a ply number, so duplicates and out-of-order messages are detected.
Bump `OnlineProtocol.Version` for incompatible changes (older apps are told to update).

**Rematches:** after a game, either player may call `OfferRematch`; when the other one does too, the
server starts a new game with colors swapped and the same time control (once per game). Offers are
withdrawn automatically when a player seeks, starts another game or disconnects.

**Spectators:** `ListGames` returns the games in progress (most-watched first). `Watch` returns the
game's state with `YourColor = "spectator"` and adds the connection to the game's group, so it
receives the same move and result messages as the players; draw offers are ignored client-side.
The client re-watches after a reconnect.

## Next steps (roadmap Session 8)

Name moderation, backups off the server, friends list, chat with moderation, and a public
deployment.
