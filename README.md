# Gambit

A native Windows 11 chess app built for Arm64 (Snapdragon X Elite) with WinUI 3 and .NET 10.
Play computer opponents from total beginner to full engine strength, analyse positions, and — as
the roadmap progresses — solve rated puzzles, take interactive lessons, earn achievements and play
online.

## What works today

* **Play vs 13 bots** (Acorn ≈250 → Monolith, full strength), with clocks, takebacks, draw offers,
  resignation, pass-and-play for two people on one PC, and a game-over summary.
* **Analysis board** with a live multi-line engine, evaluation bar, FEN/PGN paste and copy.
* **Windows 11 design**: Mica, Fluent controls, light/dark theme, accent colors.
* **Themes**: 8 board themes and 4 piece sets (three original vector sets + classic glyphs).
* **Profile**: win/loss record per bot, streaks, and a PGN archive of every finished game.
* Accessible board: every square is a UI Automation element ("e4, white pawn").

See [docs/ROADMAP.md](docs/ROADMAP.md) for what's next (puzzles, lessons, achievements, online play)
and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how it fits together.

## Build and run

Requirements: Windows 11 (Arm64 or x64) and the .NET 10 SDK. Visual Studio is not required.

```powershell
./build.ps1 run       # build (Release) and launch
./build.ps1 test      # run the unit tests (perft, notation, rules, engine)
./build.ps1 publish   # self-contained build in ./artifacts/win-arm64
```

The published folder runs without installing .NET or the Windows App SDK. Your data (settings,
profile, game archive) lives in `%LOCALAPPDATA%\Gambit`.

## Project layout

| Path | What |
|---|---|
| `src/Gambit.Core` | Chess rules, notation, games, clocks, ratings, game sessions (platform-neutral) |
| `src/Gambit.Engine` | Search, evaluation and the bot roster (platform-neutral) |
| `src/Gambit.App` | The WinUI 3 desktop app |
| `tests/Gambit.Tests` | xUnit tests |
| `tools/` | Dev scripts: screenshots, UI automation, icon generation |
