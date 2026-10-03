# Gambit

A native Windows 11 chess app built for Arm64 (Snapdragon X Elite) with WinUI 3 and .NET 10 —
play bots from total beginner to full engine strength, solve rated puzzles, take interactive
lessons, review your games, earn achievements, and play online against friends.

## Features

* **Play vs 13 bots** (Acorn ≈250 → Monolith, full strength) with personalities and opening books;
  clocks, premoves, hints, takebacks, draw offers, resignation, pass-and-play, and resume after restarting.
* **Game Review** — every position analysed in parallel on all cores: Brilliant/Great/Best …
  Mistake/Blunder classifications with plain-language reasons ("this hangs your knight", "allows
  mate in 2"), accuracy for both sides, eval graph, best-move and refutation arrows.
* **Puzzles** — about 21,000 puzzles from the Lichess puzzle database, rated 400 to 3000: rated
  (Glicko-2) with ranks, Puzzle Rush (3 min / 5 min / Survival), a daily puzzle with streaks, and
  practice by 49 themes (mate patterns, forks, pins, skewers, deflection, endgame types…).
* **Lessons** — 5 courses, 49 interactive lessons: how pieces move (with star-collecting
  mini-games), checkmate patterns, tactics, openings (Italian, Ruy Lopez, Scotch, Sicilian,
  French, Caro-Kann, Queen's Gambit, London, King's Indian, English) and endgames (two-bishop and bishop-knight mates, opposition, key squares, Lucena, Philidor…).
* **Online play** — quick pairing by time control or private games with a friend code (with rematches), against a
  self-hostable server (ASP.NET Core + SignalR, authoritative, rated). See [docs/ONLINE.md](docs/ONLINE.md).
* **Analysis board** with a multi-line engine, evaluation bar, opening names, a position editor and FEN/PGN import/export.
* **Profile & achievements** — ~50 achievements, per-bot records, puzzle rating history, openings
  you play, and a PGN archive of every game.
* **Windows 11 design** — Mica, Fluent controls, light/dark theme, accent color, 8 board themes,
  4 piece sets (three original vector sets), synthesized sound effects.
* **Accessible board** — every square is a UI Automation element ("e4, white pawn").

See [docs/ROADMAP.md](docs/ROADMAP.md) for what's next and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
for how it fits together.

## Build and run

Requirements: Windows 11 (Arm64 or x64) and the .NET 10 SDK. Visual Studio is not required.

```powershell
./build.ps1 run       # build (Release) and launch the app
./build.ps1 install   # install for your user + Start menu shortcut (re-run to update)
./build.ps1 test      # run the unit + integration tests
./build.ps1 server    # run the online server on port 5080 for LAN play
./build.ps1 publish   # self-contained app in ./artifacts/win-arm64
```

`install` copies the self-contained app to `%LOCALAPPDATA%\Programs\Gambit` and adds "Gambit" to
the Start menu — no admin rights or certificates needed; `./build.ps1 uninstall` removes it. The
published folder runs without installing .NET or the Windows App SDK. Your data (settings,
profile, puzzle progress, game archive) lives in `%LOCALAPPDATA%\Gambit` and survives reinstalls.

## Project layout

| Path | What |
|---|---|
| `src/Gambit.Core` | Rules, notation, games, clocks, ratings, sessions, openings, puzzles, lessons (platform-neutral) |
| `src/Gambit.Engine` | Search, evaluation, bots, game review (platform-neutral) |
| `src/Gambit.App` | The WinUI 3 desktop app |
| `src/Gambit.Online.Contracts` | Online protocol (DTOs + hub interfaces) |
| `src/Gambit.Online.Client` | SignalR client + `RemoteGameSession` |
| `src/Gambit.Server` | Online server (Dockerfile at the repo root) |
| `tests/Gambit.Tests` | xUnit tests (perft, rules, engine, content validation, online end-to-end) |
| `tools/` | Lichess puzzle import, puzzle generator, online bot, screenshots, UI automation, icon and sound generators |

## Credits

Puzzles come from the [Lichess puzzle database](https://database.lichess.org/#puzzles), released
under [CC0](https://creativecommons.org/publicdomain/zero/1.0/) — thank you, Lichess and its players.
`tools/import_lichess_puzzles.py` rebuilds the collection from a fresh download.
