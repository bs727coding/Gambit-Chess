# Gambit — Progress log

## Current state (update at every milestone)

**Session 1 complete** (plus several Session 2 items). The app builds, runs natively on Arm64 and is
playable end to end: pick one of 13 bots, play with clocks, resign/draw/takeback, see the result,
review the game in the analysis board with a live engine.

| Area | Status |
|---|---|
| Toolchain | .NET SDK 10.0.401 Arm64 (machine-wide). Windows App SDK 2.5.1 via NuGet. No Visual Studio needed. |
| Gambit.Core | Board, legal movegen (perft-verified), FEN/SAN/UCI/PGN, Game + draw rules, clock, Glicko-2, sessions |
| Gambit.Engine | PVS + TT + QS/SEE + null-move + LMR + MultiPV; PeSTO eval; ~700 knps search / ~8–10 Mnps perft (Release) |
| Bots | 13 bots, Acorn 250 → Monolith (max). Ratings are estimates, not yet calibrated |
| Gambit.App | Home, Play (bot picker), Game, Analysis, Profile, Settings, Puzzles/Learn previews |
| Tests | 72 passing (`./build.ps1 test`) |

## Next steps (start of Session 2)

1. Sounds (generate WAVs with a script — no downloads needed), move/capture/check/castle/game-end.
2. Opening book + opening-name detection; bot personalities (book choices, styles).
3. Game review (move classification + accuracy + eval graph) — reuse `AnalysisEngine`.
4. Board editor for the analysis page; analysis variations (currently a single line).
5. Calibrate bot strength by self-play between adjacent bots (script in tools/).
6. Persist an unfinished game across app restarts.

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
