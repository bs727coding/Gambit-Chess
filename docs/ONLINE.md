# Gambit — Online play

## How it works

```
Gambit app ──SignalR (WebSockets)──► Gambit.Server ──► Gambit.Core (rules) + Glicko-2 ratings
   RemoteGameSession : IGameSession       GameManager (rooms, clocks, matchmaking, challenges)
```

* **The server is authoritative.** Every move is checked with `Gambit.Core` before it is broadcast;
  clocks, flag falls, first-move aborts (45 s) and abandonment (60 s to reconnect) are decided on
  the server.
* **The app plays online games through the same game page as bot games** — only the session type
  differs (`RemoteGameSession` instead of `LocalGameSession`). Your moves are applied instantly and
  confirmed by the server; if anything disagrees the app resyncs from the server's move list.
* **Accounts:** the app creates a random secret token on first use (stored in your settings) — that
  token *is* your guest account and your ratings follow it. Other players only see a public id
  derived from it. (Proper accounts are on the roadmap.)
* **Ratings:** Glicko-2, separately for bullet / blitz / rapid / classical, saved in `ratings.json`.
* **Fair play:** hints, takebacks and the engine are disabled in online games.
* Finished games are stored as PGN in the server's data folder (`games/`).

## Play with friends on your network (easiest)

1. On one PC run: `./build.ps1 server` (allow it through the Windows firewall when asked).
2. Find that PC's address: `ipconfig` → IPv4 address, e.g. `192.168.1.20`.
3. Everyone opens **Online** in Gambit, enters `http://192.168.1.20:5080` and connects.
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
2. **Give it a persistent volume at `/data`.** Ratings (`ratings.json`) and the PGN archive are
   written there; without a volume they reset whenever the container is replaced.
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
| `GAMBIT_DATA` | `./data` next to the server | Ratings + PGN archive |
| `Gambit:ReconnectGrace` | `00:01:00` | appsettings.json or env `Gambit__ReconnectGrace` |
| `Gambit:FirstMoveTimeout` | `00:00:45` | Game aborted if White doesn't move |
| `Gambit:CallsPerSecond` / `Gambit:CallBurst` | `10` / `30` | Hub calls per connection (token bucket); excess calls fail with "Too many requests" |
| `Gambit:ConnectionsPerMinute` | `60` | Hub HTTP requests per client IP (a connect is ~2); excess gets HTTP 429 |
| `Gambit:MaxOpenChallenges` | `5` | Friend codes per player; creating more drops the oldest |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | unset | Set to `true` behind a reverse proxy (Azure Container Apps, Fly.io, nginx) so limits see the real client IP |

Health check: `GET /health` → `ok`. `GET /` shows players online and games in progress.

## Protocol (Gambit.Online.Contracts)

Client → server: `Hello(name, version)`, `Seek(tc)`, `CancelSeek()`, `CreateChallenge(tc, color)`,
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

Real accounts (sign-in), PostgreSQL storage, friends list,
chat with moderation, and a public deployment.
