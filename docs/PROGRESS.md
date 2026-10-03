# Gambit — Progress log

## Current state (update at every milestone)

Sessions 1–3 and 7 of the roadmap are complete, most of 4–6 and part of 8. The app builds and runs
natively on Arm64.

| Area | Status |
|---|---|
| Toolchain | .NET SDK 10.0.401 Arm64 (machine-wide). Windows App SDK 2.5.1 via NuGet. No Visual Studio needed. |
| Gambit.Core | Board, legal movegen (perft-verified, also king-less lesson positions), FEN/SAN/UCI/PGN (incl. variations), Game + draw rules, `MoveTree`, clock, Glicko-2, sessions (`IGameSession`), premove rules, openings (~150, embedded TSV), puzzles (embedded CSV), lessons (embedded JSON) |
| Gambit.Engine | PVS + TT + QS/SEE + null-move + LMR + MultiPV; PeSTO eval; 13 bots with opening books; `GameReviewer` (parallel, move classes, accuracy) |
| Gambit.App | Home, Play (bots, pass-and-play, online), Game (clocks, premoves, hints, takebacks, resume after restart, sounds, online rematch), Game Review (with plain-language explanations), Analysis (engine lines, position editor, variations), Puzzles (rated, Rush, Survival, daily, themes), Learn (5 courses / 49 lessons), Online lobby, Profile (stats, rating chart, openings, ~50 achievements), Settings (themes, pieces, sounds, premoves) |
| Online | `Gambit.Server` (ASP.NET Core + SignalR, authoritative, rematches, spectators, rate limits), `Gambit.Online.Client` (`RemoteGameSession`), Dockerfile, docs/ONLINE.md; `./build.ps1 server` for LAN play |
| Tools | `import_lichess_puzzles.py`, `Gambit.PuzzleGen` (experiments), `Gambit.BotArena` (ladder measurement), `Gambit.OnlineBot` (plays online, accepts rematches), `ui.ps1`, `ui-scroll.ps1`, `screenshot-quiet.ps1` |
| Content | 20,928 puzzles from the Lichess puzzle DB (CC0; `tools/import_lichess_puzzles.py`): 400–1,100 per 100-point band from 400 to 2999, 49 practice themes with ≥ 120 each; 49 lessons; tactic/mate solutions audited by the engine in tests |
| Install | `./build.ps1 install` → `%LOCALAPPDATA%\Programs\Gambit` + Start menu shortcut (tested into a scratch folder; not installed for real, the user decides) |
| Tests | 118 passing (`./build.ps1 test`) incl. in-process server + real clients (games, challenges, rematches, spectating, rate limits) |

## Next steps

**Priority (from the user, 2026-10-02): a functional, clean, correct app over bot tuning.**

1. **QA pass, remaining:** play through a few of the newer lessons in the sandbox profile; the
   pages themselves were checked in light and dark and at the minimum window size (see the log).
2. **Puzzles, later:** opening-specific practice (Lichess `OpeningTags` are dropped by the import
   today), a link to the source game on lichess.org.
3. **Clean-up:** move `GamePage`'s game-flow logic (local / online / spectator / hot-seat,
   premoves, rematch) into a testable view model; share the board-settings code duplicated across
   four pages.
4. **Online:** stays local for now (the user's call, 2026-10-02); hosting is ready for when they
   want it (Fly.io: four commands, docs/ONLINE.md). Later: accounts/sign-in, friends list, chat.
5. **Polish:** keyboard play on the board, Narrator pass, inline variations in the analysis move
   list, localization; MSIX packaging if wanted (`./build.ps1 install` covers local install).
6. **Parked unless asked:** bot ladder tuning (BOT-CALIBRATION.md), lessons on zwischenzug /
   X-ray / triangulation.

## Known issues / notes

* Engine speed is modest (~0.7M nodes/s single-threaded). Ideas: incremental eval, pawn hash,
  staged movegen, cheaper SEE.
* `GamePage` keeps its logic in code-behind; consider a view model if it grows further.
* Online accounts are guest tokens (secret in settings). No sign-in, no moderation yet.
* UI tests: `tools/ui.ps1` drives the app through UI Automation. Board squares are invokable
  elements (`sq-e4`), so moves can be played without the mouse:
  `./tools/ui.ps1 invoke -Name "Play"`, `./tools/ui.ps1 board -Moves "e2e4,g1f3"`.
  **The user plays Gambit while sessions run** — check it isn't in use before driving it, and
  test in a sandbox profile (`./build.ps1 run -DataDir artifacts/qa-profile`).
* The user's real profile still holds a few test games from session 2 (played before the sandbox
  existed); ask before cleaning them up.
* api.nuget.org has an unreachable CDN edge from this network (see CLAUDE.md); restores still work.
* Windows PowerShell 5.1 mangles UTF-8 in `Get-Content`/`Set-Content` (see CLAUDE.md) — edit
  files with the Edit/Write tools, `sed` or Python.

## Next session kickoff (paste this into a new Claude Code session in this folder)

> Continue building Gambit. Read CLAUDE.md, docs/PROGRESS.md and docs/ROADMAP.md, run
> `./build.ps1 test`, then work through the "Next steps" list. Commit at milestones and keep
> PROGRESS.md updated as you go.

---

## Session log

### Session 1 — 2026-10-02
* Chose the stack: WinUI 3 (Windows App SDK 2.5) + .NET 10, own C# engine, SignalR server.
* Installed .NET 10 SDK (winget could not reach its source; used the identical Microsoft installer
  from builds.dotnet.microsoft.com, SHA-512 + Authenticode verified).
* Core library, engine, 13 bots, perft-verified move generation; WinUI app with board control,
  play vs bots, analysis board, profile, settings.
* Same day, continued: sounds, opening book, Game Review, hints, resume after restart, puzzles
  (generator + trainer + Rush + daily + themes), lessons (5 courses), achievements, profile stats,
  online multiplayer (server, client, lobby, Docker, docs).
* Verified visually via screenshots + UI Automation: games vs bots, check/resign/game over,
  review, analysis, puzzles hub/trainer, settings, profile.
* Later the same day: position editor on the analysis board; online play verified in the app
  against `tools/Gambit.OnlineBot`; a second puzzle-generation pass with 1400–2200 bots.

### Session 2 — 2026-10-02 (after a usage-limit break)
* Online rematches (protocol, server, `RemoteGameSession`, game page) — verified in the app against
  the OnlineBot: Rematch in the result dialog started a new rated game with colours swapped.
* Premoves (Core rules, board, game page, Settings toggle) — verified in the app: they fired the
  instant the bot replied.
* Analysis board variations (`MoveTree`, PGN import and export with nested variations) — verified
  in the app.
* Server abuse protection (per-connection call rate limit, per-IP connection limit, challenge cap).
* Spectating: list live games, watch one (re-watched after reconnects), spectator mode on the game
  page (no moves, offers, rematch or profile stats). Verified in the app watching two OnlineBots:
  live moves, "Watching · X to move", neutral "Black wins" result, nothing saved to the archive.
* `./build.ps1 install` / `uninstall`; zero build warnings; ARCHITECTURE.md rewritten to match code.
* Lessons 36 → 49: tactics (double check, removing the defender, overloading, trapped pieces,
  decoy, deflection), openings (Scotch, English, QGA, Slav, Alapin), endgames (two-bishop and
  bishop+knight mates, key squares, wrong bishop, rooks behind passed pawns). A new engine audit
  in `LessonTests` found and fixed four flawed older steps.
* `tools/Gambit.BotArena` and the first ladder measurement (docs/BOT-CALIBRATION.md); bots now
  convert won endgames (low-end draws fell from ~50% to ~1%).
* Puzzles 564 → 689 (two passes with stronger bots) with unique ids. Fixed a double-encoded PROGRESS.md (PowerShell 5.1).
* Bot play styles (revision 3): aggressive, solid and tricky bots now play differently (style
  bonuses on their candidate moves); shown on the Play page's bot details. Arena check: ladder
  unchanged within the margins.
* Game Review explains key moments in plain language ("This allows mate: Qh4#", "The queen on g5
  can simply be taken: Bxg5", "Qxf7# was mate in one", forks) and draws the punishing reply in
  red. Logic unit-tested; the review page's new text and arrow weren't checked visually yet.
* Hosting: `fly.toml` and a rewritten hosting guide (exactly one instance, persistent `/data`,
  forwarded headers); removed the Dockerfile's `wget` health check (the aspnet image has no wget).
* The user was playing Gambit during the session; `screenshot-quiet.ps1` no longer moves an
  on-screen window, and CLAUDE.md says to check before driving the app.
* The user asked to step back from bot tuning: priorities are now a functional, clean, correct app.
* QA pass of every page in the running app (sandbox profile via `GAMBIT_DATA_DIR` /
  `./build.ps1 run -DataDir`), light and dark, default and minimum window size. Fixed:
  * achievement toast that never hid (its timer was garbage-collected before firing; new
    `Helpers.Delay` keeps one-shot timers alive; also used by lessons and puzzles);
  * see-through toasts on the game and analysis pages (now solid with a border);
  * Game Review panel overflowing at the default size (move counts in two groups);
  * post-game buttons: Rematch on its own row; the in-game Undo/Draw/Resign row hides when the
    game ends and lays out only its visible buttons (no gap where Undo is hidden online);
  * truncated labels ("Take back" → "Undo", "Tournament green" theme tile), plurals ("1 move",
    "1 puzzle", "1 game"), aborted games shown as "Aborted" rather than a loss;
  * bot play style shown on the Play page; puzzle "Skip" no longer styled as the main action;
  * tile grids (lessons, puzzle themes, achievements, online time controls) stretch to fill the
    width instead of leaving a ragged gap; Profile records use the same W · L · D order as Play.
  * A first version of the tile stretching threw while pages were built (Puzzles, Learn, Online and
    Profile would not open) — caught from the app log and fixed before committing; CLAUDE.md now
    says to read the log after UI changes.
* Puzzles: replaced the 689 engine-generated puzzles (heuristic ratings, only 19 rated 1600+) with
  20,928 from the Lichess puzzle database (CC0, downloaded with the user's approval):
  `tools/import_lichess_puzzles.py` streams the 6.2M-puzzle archive and keeps well-rated, popular,
  much-played puzzles, stratified by rating, one per source game, no duplicate positions, every
  practice theme topped up to ≥ 120. The Puzzles page now groups 49 practice themes (checkmate
  patterns, tactics, game phases); names for every Lichess theme; Puzzle Rush no longer falls back
  to the easiest puzzles once it passes the hardest. Tests check rating and theme coverage.
  Verified in the app: today's daily puzzle (Lichess #wNt3h) solved end to end, theme practice.
