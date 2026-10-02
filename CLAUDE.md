# CLAUDE.md — Gambit (native Windows 11 chess app)

Gambit is a WinUI 3 / .NET 10 chess app for Windows 11 on Arm64 (Snapdragon X Elite): bots of many
strengths, rated puzzles, lessons, achievements, statistics, themes — and an architecture that makes
online multiplayer an additive feature. Product name "Gambit" is a working title (display name lives
in `src/Gambit.App/AppInfo.cs`).

## Work happens in sessions — follow this protocol

The project is built incrementally across ~5-hour Claude Code sessions (usage limits can also cut a
session short at any moment, so **checkpoint often**).

1. **Start:** read `docs/PROGRESS.md` (current state, next steps, known issues) and the current
   session in `docs/ROADMAP.md`. Run `./build.ps1 test` to confirm a green baseline.
2. **Work:** take the first unchecked roadmap items. Keep the app building. After each working
   milestone: run tests, `git commit` (the user approved local milestone commits; never push).
3. **Checkpoint:** update `docs/PROGRESS.md` whenever a milestone lands, not only at the end — a
   usage limit can end the session without warning.
4. **End (last ~20 min):** tick items in `ROADMAP.md`, write the session log + "Next steps" in
   `PROGRESS.md`, commit.

## Toolchain & commands (PowerShell)

* .NET SDK 10.0.4xx (Arm64) at `C:\Program Files\dotnet`. No Visual Studio is installed — everything
  builds with the dotnet CLI (WinUI XAML compiler runs from the Windows App SDK NuGet package).
* `./build.ps1` — build everything (Debug)          `./build.ps1 test` — run unit tests
* `./build.ps1 run` — build + launch the app (Arm64)  `./build.ps1 publish` — self-contained Release to `artifacts/`
* `./build.ps1 perft` — quick move-generator benchmark
* Visual check of the running app: `tools/screenshot.ps1` writes a PNG of the Gambit window that
  you can open with the Read tool.

**Network quirk:** one of api.nuget.org's CDN IPs is unreachable from this machine (TCP timeouts of
21 s). Restores usually still succeed (build.ps1 retries). If NuGet fails repeatedly, switch
`NuGet.config` to the v2 endpoint noted in that file. winget cannot refresh its source here either —
download Microsoft installers directly from builds.dotnet.microsoft.com and verify hashes.

## Layout & rules

```
src/Gambit.Core     net10.0  rules, notation (FEN/SAN/UCI/PGN), Game, clocks, Glicko-2, sessions
src/Gambit.Engine   net10.0  evaluation, search, bots (BotProfile/BotRoster/BotMoveProvider)
src/Gambit.App      WinUI 3 app (unpackaged, self-contained, win-arm64/x64)
tests/Gambit.Tests  xUnit — perft, notation, rules, engine, content validation
docs/               ROADMAP.md, PROGRESS.md, ARCHITECTURE.md
tools/              helper scripts (screenshot, data import, sound generation)
```

* `Gambit.Core` and `Gambit.Engine` stay platform-neutral (no Windows/UI references) — the future
  ASP.NET Core server reuses them.
* Squares are ints, a1 = 0 … h8 = 63. `Move` is a 16-bit struct (from | to << 6 | flag << 12).
* The game page talks only to `IGameSession` (`LocalGameSession` today, `RemoteGameSession` later).
  Never special-case "bot game" in UI code; ask the session (`IsLocalSide`, events).
* Content (bots, puzzles, lessons, achievements, themes) is data + a validation test.
* App: MVVM with CommunityToolkit.Mvvm; theme resources only (no hard-coded colors outside
  board/piece themes); Windows 11 look (Mica, NavigationView, Settings cards).
* Match existing style: file-scoped namespaces, nullable enabled, XML doc comments on public types.
* Move generation changes must keep the perft tests green.
