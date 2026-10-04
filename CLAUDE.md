# CLAUDE.md — Gambit (native Windows 11 chess app)

Gambit is a WinUI 3 / .NET 10 chess app for Windows 11 on Arm64 (Snapdragon X Elite): bots of many
strengths, rated puzzles, lessons, game review, achievements, statistics, themes, and online play
through a self-hostable server. Product name "Gambit" is a working title (`src/Gambit.App/AppInfo.cs`).

## Work happens in sessions — follow this protocol

The project is built incrementally across ~5-hour Claude Code sessions (usage limits can also cut a
session short at any moment, so **checkpoint often**).

1. **Start:** read `docs/PROGRESS.md` (current state, next steps, known issues) and
   `docs/ROADMAP.md`. Run `./build.ps1 test` to confirm a green baseline.
2. **Work:** take the next items from PROGRESS "Next steps" / unchecked roadmap items. Keep the app
   building. After each working milestone: run tests, `git commit` (the user approved local
   milestone commits; never push).
3. **Checkpoint:** update `docs/PROGRESS.md` whenever a milestone lands, not only at the end.
4. **End (last ~20 min):** tick items in `ROADMAP.md`, write the session log + "Next steps" in
   `PROGRESS.md`, commit.

## Toolchain & commands (PowerShell)

* .NET SDK 10.0.4xx (Arm64) at `C:\Program Files\dotnet`. No Visual Studio — everything builds with
  the dotnet CLI (the WinUI XAML compiler runs from the Windows App SDK NuGet package).
* `./build.ps1` — build everything (Debug)          `./build.ps1 test` — run all tests
* `./build.ps1 install` / `uninstall` — per-user install + Start menu shortcut (only run when the user asks)
* `./build.ps1 run` — build (Release) + launch the app  `./build.ps1 publish` — self-contained app in `artifacts/`
* `./build.ps1 server` — online server on :5080 (all interfaces; may trigger a firewall prompt —
  for local testing bind to 127.0.0.1 with `dotnet run --project src/Gambit.Server -c Release --urls http://127.0.0.1:5080`)
* Puzzles: the bundled set comes from the Lichess puzzle DB (CC0): `python tools/import_lichess_puzzles.py artifacts/lichess_db_puzzle.csv.zst src/Gambit.Core/Puzzles/puzzles.csv`
  (needs the ~300 MB download from database.lichess.org). `tools/Gambit.PuzzleGen` (engine-generated,
  heuristic ratings) is for experiments — don't mix its output into the bundled set.
* Online bot: `dotnet run -c Release --project tools/Gambit.OnlineBot -- http://localhost:5080 <bot-id> <tc|code> [games] [--invite CODE]`
  (plays as the account `<Name>Bot`; password in `GAMBIT_BOT_PASSWORD`; `--invite` creates the account once).
* Server admin: `dotnet run --project src/Gambit.Server -- <invite|users|reset-password|ban|backup|help>`
  works on `gambit.db` (set `GAMBIT_DATA` to the server's data folder). A new server logs the invite
  code for the first (admin) account. Accounts are invite-only by default (docs/ONLINE.md).
  Restoring: put the backup in the data folder as `restore.db` and restart the server.
* Engine speed: `dotnet run -c Release tools/bench.cs [depth] [rounds]` searches fixed positions to a
  fixed depth. Its node count is the search signature (pinned by `Search_signature_is_unchanged`):
  a pure speed-up keeps it. This machine's clock speed drifts (up to 2x between runs), so compare
  timings by running the old and new builds alternately, never against an earlier run.
  `tools/bench.cs smp [threads] [depth]` compares the analysis board's multi-threaded search
  (`ParallelSearcher`, Lazy SMP) with one thread. Only analysis is multi-threaded: bots, hints,
  review workers and the bench stay single-threaded and deterministic.
* Bot ladder: `dotnet run -c Release --project tools/Gambit.BotArena -- <minutes> artifacts/arena.csv <ids,...>`
  (see docs/BOT-CALIBRATION.md; bump `BotMoveProvider.Revision` when bot move choice changes)
* Sounds: `python tools/gen_sounds.py`; icon: `tools/make-icon.ps1`.
* `python` here is the Microsoft Store build: its view of `%LOCALAPPDATA%` is virtualized (it once
  reported the real `Gambit\settings.json` as missing). Touch the user's profile with PowerShell.
* Edit files with the Edit/Write tools, `sed` or Python — in Windows PowerShell 5.1 `Set-Content -Encoding utf8`
  adds a BOM and `Get-Content` without `-Encoding utf8` reads UTF-8 as ANSI (mangles —, →, ≥).

**Network quirk:** one of api.nuget.org's CDN IPs is unreachable from this machine (TCP timeouts of
21 s). Restores usually still succeed (build.ps1 retries), but a restore can stall for a long time:
when no packages changed, `dotnet test tests/Gambit.Tests/Gambit.Tests.csproj --no-restore` skips it
(~15 s for the whole suite). If NuGet fails repeatedly, switch
`NuGet.config` to the v2 endpoint noted in that file. winget cannot refresh its source here either —
download Microsoft installers directly from builds.dotnet.microsoft.com and verify hashes.

## Verifying the UI without disturbing the user

The user works on this PC while sessions run — and sometimes plays Gambit itself. **Don't foreground
the app repeatedly**, and before driving it with UI Automation check that the user isn't using it
(e.g. it's on a page you didn't open, or a game you didn't start); if they are, stop and verify
through tests instead.
* **Test in a sandbox profile, Debug build:** `./build.ps1 run -Configuration Debug -DataDir artifacts/qa-profile`
  (sets `GAMBIT_DATA_DIR`), so test games, puzzle attempts and settings never land in the user's
  real profile (`%LOCALAPPDATA%\Gambit`). The user's desktop shortcut runs the Release build in
  `src/Gambit.App/bin/ARM64/Release/...`: while it's open a Release build can't overwrite it, so test
  with Debug. A test-profile window is titled "Gambit (test profile)", opens behind other windows
  without taking focus, and is the only window the tools/ scripts drive (`-ProcessId` overrides).
* After UI changes, read the log (`<data dir>/logs/gambit-<date>.log`): unhandled UI exceptions are
  logged and swallowed, so a page that throws while navigating simply doesn't open.
* `tools/ui.ps1` drives the app via UI Automation without the mouse: `invoke -Name "Play"`,
  `board -Moves "e2e4,g1f3"` (board squares are invokable elements `sq-e4`), `type -Name "Your name" -Text "Sam"`, `list`.
* A new, empty data dir opens on the welcome screen (onboarding); finish or `invoke -Name "Skip"` it
  first. Profiles with any games, puzzles or lessons never see it.
* File dialogs (Settings → back up / restore): `tools/file-dialog.ps1 -Path <full path>` fills in the
  Save/Open dialog the test window opened. Always give a full path under artifacts/: the dialog
  starts in the user's (OneDrive) Documents, and one test backup once landed there.
  `tools/ui-scroll.ps1 -Percent 100` scrolls the current page.
* Lessons: `dotnet run tools/lesson-moves.cs > artifacts/lesson-moves.json`, then
  `./tools/play-lessons.ps1 -From tactics/fork -Count 10 [-Shots dir]` plays them start to finish.
* `tools/screenshot-quiet.ps1 -Out x.png` captures an on-screen window in place (never moves it);
  a minimized one is restored behind other windows, captured and re-minimized. (`tools/screenshot.ps1` is the foreground version.)
* Read state through UIA by AutomationId (x:Name), e.g. `StatusText`, `CodeText`, `FeedbackText`.

## Layout & rules

```
src/Gambit.Core             net10.0  rules, notation, Game, clocks, Glicko-2, sessions, openings/puzzles/lessons data
src/Gambit.Engine           net10.0  evaluation, search, bots, GameReviewer
src/Gambit.App              WinUI 3 app (unpackaged, self-contained, win-arm64/x64)
src/Gambit.Online.Contracts net10.0  protocol DTOs + hub interfaces (bump OnlineProtocol.Version on breaking changes)
src/Gambit.Online.Client    net10.0  OnlineClient (SignalR) + RemoteGameSession : IGameSession
src/Gambit.Server           ASP.NET Core + SignalR, authoritative GameManager (Dockerfile at repo root)
src/Gambit.ViewModels       net10.0  page logic without UI types (GameViewModel for GamePage), unit-tested
tests/Gambit.Tests          xUnit — perft, notation, rules, engine, content validation, online end-to-end
tools/                      Lichess puzzle import, PuzzleGen, BotArena, OnlineBot, ui.ps1, screenshots, sound/icon generators
docs/                       ROADMAP.md, PROGRESS.md, ARCHITECTURE.md, ONLINE.md
```

* `Gambit.Core` and `Gambit.Engine` stay platform-neutral (no Windows/UI references).
* Squares are ints, a1 = 0 … h8 = 63. `Move` is a 16-bit struct (from | to << 6 | flag << 12).
* The game page renders `GameViewModel`, which talks only to `IGameSession` (`LocalGameSession` or
  `RemoteGameSession`). Never special-case the opponent type beyond `GameSetup.IsOnline` cosmetics;
  game-flow changes go in the view model, with a test in `GameViewModelTests`.
* Boards take the user's settings through `board.ApplyUserSettings()` (Helpers/BoardSettings.cs).
* Content (openings TSV, puzzles CSV, lessons JSON) is embedded in Core and validated by tests —
  add content, run `./build.ps1 test`.
* App: theme resources in XAML; in code-built UI use `Ui.AccentBrush` / `Ui.NeutralFill` and leave
  text Foreground unset (never set it to null) so light/dark both work.
* Match existing style: file-scoped namespaces, nullable enabled, XML doc comments on public types.
* Move generation changes must keep the perft tests green.
