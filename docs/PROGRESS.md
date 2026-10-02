# Gambit — Progress log

## Current state (update at every milestone)

**Session 1 in progress.** Core rules library written and compiling; engine evaluation + TT written;
search, bots, app shell not yet done.

| Area | Status |
|---|---|
| Toolchain | .NET SDK 10.0.401 Arm64 installed machine-wide; git repo initialised |
| Gambit.Core | Board/movegen/FEN/SAN/UCI/PGN/Game/Clock/Glicko-2/Sessions written, builds clean |
| Gambit.Engine | Evaluator + TranspositionTable written; Searcher + bots pending |
| Tests | Perft suite written, not yet run |
| Gambit.App | Not started |

## Next steps

1. Write `Gambit.Engine/Search/Searcher.cs` (+ limits/results, SEE) and `Bots/*`.
2. Run `./build.ps1 test` (perft must pass), commit "Core + engine".
3. Create the WinUI 3 app (see Session 1 in ROADMAP.md).

## Known issues / notes

* api.nuget.org has an unreachable CDN edge from this network (see CLAUDE.md).

## Next session kickoff (paste this into a new Claude Code session in this folder)

> Continue building Gambit. Read CLAUDE.md, docs/PROGRESS.md and docs/ROADMAP.md, run the tests,
> then work through the next unchecked roadmap items. Commit at milestones and keep PROGRESS.md
> updated as you go.

---

## Session log

### Session 1 — 2026-10-02
* Chose stack: WinUI 3 (Windows App SDK 2.x) + .NET 10, own C# engine, SignalR server later.
* Installed .NET 10 SDK (winget could not reach its source; used the identical Microsoft installer
  from builds.dotnet.microsoft.com, SHA-512 + signature verified).
* Wrote roadmap, architecture, Core library.
