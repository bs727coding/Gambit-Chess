# Gambit — Progress log

## Current state (update at every milestone)

**Session 1 complete; Session 2 mostly complete** (sounds, opening book, Game Review, hints, resume). The app builds, runs natively on Arm64 and is
playable end to end: pick one of 13 bots, play with clocks, resign/draw/takeback, see the result,
review the game in the analysis board with a live engine.

| Area | Status |
|---|---|
| Toolchain | .NET SDK 10.0.401 Arm64 (machine-wide). Windows App SDK 2.5.1 via NuGet. No Visual Studio needed. |
| Gambit.Core | Board, legal movegen (perft-verified), FEN/SAN/UCI/PGN, Game + draw rules, clock, Glicko-2, sessions |
| Gambit.Engine | PVS + TT + QS/SEE + null-move + LMR + MultiPV; PeSTO eval; ~700 knps search / ~8–10 Mnps perft (Release) |
| Bots | 13 bots, Acorn 250 → Monolith (max). Ratings are estimates, not yet calibrated |
| Gambit.App | Home, Play (bot picker), Game (hints, resume after restart), Game Review, Analysis, Profile, Settings, Puzzles/Learn previews |
| Content | ~150 named openings (embedded TSV, transposition-aware) used for names + bot books; synthesized sounds |
| Tests | 86 passing (`./build.ps1 test`) |

## Next steps (start of Session 2)

1. **Puzzles (Session 3)** — in progress: engine-generated puzzle set (tools/Gambit.PuzzleGen mines
   tactics from bot self-play; no download needed), puzzle trainer with Glicko-2 rating, Puzzle Rush,
   daily puzzle, themes. Optional upgrade: import the Lichess CC0 puzzle DB (~250 MB download — ask first).
2. Remaining Session 2 items: board editor + variations for analysis, premoves, bot calibration by
   self-play, bot style personalities beyond the opening book.

## Known issues / notes

* Captured-piece strip uses Segoe UI Symbol glyphs; on dark theme black pieces look white. Replace
  with mini vector pieces.
* Engine nps is modest (~0.7M/s). Ideas: incremental eval/material, cheaper SEE in move ordering,
  pawn hash table, staged move generation.
* `GamePage` keeps its logic in code-behind (pragmatic); consider a view model when online play lands.
* UI tests: `tools/ui.ps1` drives the app through UI Automation. Board squares are exposed as
  invokable elements (`sq-e4`), so moves can be played without the mouse:
  `./tools/ui.ps1 invoke -Name "Play"`, `./tools/ui.ps1 board -Moves "e2e4,g1f3"`.
* api.nuget.org has an unreachable CDN edge from this network (see CLAUDE.md); restores still work.

## Next session kickoff (paste this into a new Claude Code session in this folder)

> Continue building Gambit. Read CLAUDE.md, docs/PROGRESS.md and docs/ROADMAP.md, run
> `./build.ps1 test`, then work through the next unchecked roadmap items. Commit at milestones and
> keep PROGRESS.md updated as you go.

---

## Session log

### Session 1 — 2026-10-02
* Chose the stack: WinUI 3 (Windows App SDK 2.5) + .NET 10, own C# engine, SignalR server later.
* Installed .NET 10 SDK (winget could not reach its source; used the identical Microsoft installer
  from builds.dotnet.microsoft.com, SHA-512 + Authenticode verified).
* Core library, engine, 13 bots, 72 tests (perft suite to depth 5–6 on six reference positions).
* WinUI app: Mica + custom title bar + NavigationView; chess board control (click/drag, hints,
  highlights, promotion picker, animation, arrows, UI Automation peers); original vector piece sets;
  8 board themes; Play/Game/Analysis/Home/Profile/Settings pages; JSON settings + profile + PGN archive.
* Verified visually via screenshots and UI Automation: game vs Acorn, check, resign → game-over dialog,
  analysis engine lines, settings preview, profile stats.
* Session 2 work (same day): synthesized sounds, opening book/names, Game Review (parallel engine
  analysis, move classes, accuracy, eval graph), hints, resume unfinished games after restart.
