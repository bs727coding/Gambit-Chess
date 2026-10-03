# Gambit — Progress log

## Current state (update at every milestone)

Sessions 1–3 and 7 of the roadmap are complete, most of 4–6 and part of 8. The app builds and runs
natively on Arm64.

| Area | Status |
|---|---|
| Toolchain | .NET SDK 10.0.401 Arm64 (machine-wide). Windows App SDK 2.5.1 via NuGet. No Visual Studio needed. |
| Gambit.Core | Board, legal movegen (perft-verified, also king-less lesson positions), FEN/SAN/UCI/PGN (incl. variations), Game + draw rules, `MoveTree`, clock, Glicko-2, sessions (`IGameSession`), premove rules, openings (~150, embedded TSV), puzzles (embedded CSV), lessons (embedded JSON) |
| Gambit.Engine | PVS + TT + QS/SEE + null-move + LMR + MultiPV; PeSTO eval; 13 bots with opening books; `GameReviewer` (parallel, move classes, accuracy) |
| Gambit.App | Welcome screen (first run), Home, Play (bots, pass-and-play, online), Game (clocks, premoves, hints, takebacks, resume after restart, sounds, online rematch), Game Review (with plain-language explanations), Analysis (engine lines, position editor, variations), Puzzles (rated, Rush, Survival, daily, themes, openings), Learn (5 courses / 49 lessons, opening review), Online lobby, Profile (stats, rating chart, openings, ~50 achievements), Settings (themes, pieces, sounds, premoves) |
| Online | `Gambit.Server` (ASP.NET Core + SignalR, authoritative, rematches, spectators, rate limits), `Gambit.Online.Client` (`RemoteGameSession`), Dockerfile, docs/ONLINE.md; `./build.ps1 server` for LAN play |
| Tools | `import_lichess_puzzles.py`, `Gambit.PuzzleGen` (experiments), `Gambit.BotArena` (ladder measurement), `Gambit.OnlineBot` (plays online, accepts rematches), `ui.ps1`, `ui-scroll.ps1`, `screenshot-quiet.ps1` |
| Content | 21,162 puzzles from the Lichess puzzle DB (CC0; `tools/import_lichess_puzzles.py`): 400–1,100 per 100-point band from 400 to 2999, 49 practice themes with ≥ 120 each, 25 practice openings with ≥ 50 each; 49 lessons; tactic/mate solutions audited by the engine in tests |
| Install | `./build.ps1 install` → `%LOCALAPPDATA%\Programs\Gambit` + Start menu shortcut (tested into a scratch folder; not installed for real, the user decides) |
| Tests | 153 passing (`./build.ps1 test`) incl. in-process server + real clients (games, challenges, rematches, spectating, rate limits) |

## Next steps

**Priority (from the user, 2026-10-02): a functional, clean, correct app over bot tuning.**
The user's list of 2026-10-03 is done (QA pass, GamePage view model, inline variations, puzzles
by opening, faster engine, onboarding) and the "Left" tooltip bug is fixed (confirmed by the user).

Proposed to the user on 2026-10-03, in this order (waiting for their pick):
1. **Single-player polish (the user's pick, in progress):** progress tools in Settings ✔; spaced
   repetition for the opening drills ✔; a multi-threaded analysis board (bots stay single-threaded
   and unchanged). The puzzle source link was dropped (the user's call).
2. **Server ready to go online, built and tested locally (not deployed):** accounts and sign-in
   instead of guest tokens, a database instead of ratings.json, handling for abandoned games, name
   moderation, backups. Hosting itself is ready (Fly.io, docs/ONLINE.md).
3. **Distribution:** version numbers and an update check, MSIX or a signed installer (buying a
   signing certificate is the user's call), an x64 build test.
4. **Parked unless asked:** bot ladder tuning (BOT-CALIBRATION.md), lessons on zwischenzug /
   X-ray / triangulation, localization, accessibility (keyboard play, Narrator; the user said "no
   accessibility" when asking for next steps, 2026-10-03).

## Known issues / notes

* Engine: ~1.5M nodes/s on one thread (Release, `tools/bench.cs`). Ideas left: Lazy SMP for the
  analysis board (the big one; makes analysis nondeterministic), incremental piece-square sums and
  a pawn hash (~5% each). Staged move generation would change move order (and the signature).
* Online accounts are guest tokens (secret in settings). No sign-in, no moderation yet.
* UI tests: `tools/ui.ps1` drives the app through UI Automation. Board squares are invokable
  elements (`sq-e4`), so moves can be played without the mouse:
  `./tools/ui.ps1 invoke -Name "Play"`, `./tools/ui.ps1 board -Moves "e2e4,g1f3"`.
  **The user plays Gambit while sessions run** — check it isn't in use before driving it, and
  test in a sandbox profile (`./build.ps1 run -Configuration Debug -DataDir artifacts/qa-profile`).
* The user's real profile was reset to a clean slate on 2026-10-03 at their request (backup in
  `%LOCALAPPDATA%\Gambit-backup-2026-10-03`); appearance and gameplay settings were kept.
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
* Lessons played through in the app (`tools/play-lessons.ps1`, new): all 38 lessons of Tactics,
  Openings, Endgames and Mates finish with three stars. Fixed: quiz answers were cut off (no
  wrapping); keyboard focus fell to "All lessons" when Continue disabled itself, so a second Enter
  left the lesson (focus now follows the step); three inaccurate lesson texts (removing the
  defender, trapped piece, decoy).
* Test windows: a test-profile window is titled "Gambit (test profile)", opens behind other windows
  without focus (the user's keystrokes once landed in one), and is the only window the tools/
  scripts will drive.
* Game page restructured: its game flow moved into `GameViewModel` in the new UI-free project
  `src/Gambit.ViewModels` (with `GameSetup`, `SavedGame` and `IGameHost`, implemented by the app's
  `GameHost`). GamePage went from ~800 to ~400 lines of rendering, dialogs and toasts. 10 new tests
  (`GameViewModelTests`) cover bot replies and saving, history browsing, takebacks, resigning and
  recording, pass-and-play, premoves, resuming with the low-time warning, captured material and
  watching. Five pages now share `board.ApplyUserSettings()`. Checked in the app: a bot game,
  history, resign dialog, post-game buttons, rematch, and resuming after a restart.
* Analysis board: the move list shows the whole move tree (new `MoveTreeView`): the main line in
  two columns and each variation inline under the move it replaces, numbered like PGN, with deeper
  variations in parentheses; every move is a button (keyboard and screen-reader friendly), and
  clicking one switches the board to its line. The separate "also tried here" buttons are gone.
  Both move lists now keep the current move centered (a rebuilt list scrolled before it had its
  final size), engine lines take one line each (full line in the tooltip) and the engine card
  keeps its height between moves, so the move list stops jumping.
* Puzzles by opening: the import keeps each puzzle's opening family (Lichess `OpeningTags`) and
  tops up the 24 most common to ≥ 80 puzzles (21,162 puzzles in all). The Puzzles page has a
  "Practice by opening" section (25 openings with ≥ 50 puzzles, named as in the opening book:
  "Queen's Gambit Declined", "Petrov's Defense"); practice is rated like theme practice, tiles show
  your success rate, and a solved puzzle names its opening. Tests cover the CSV column, the names
  and the coverage. Verified in the app: a Sicilian puzzle (Lichess #dA7r3) solved, rated and
  counted on its tile.
* Faster engine, same moves. New `tools/bench.cs` + `Bench` (12 positions at a fixed depth): the
  node count is the search signature, pinned by a test and unchanged (478,571 at depth 8), so every
  search visits exactly the same tree as before. Changes: evaluation rewritten for speed (branch-free
  piece-square sums, per-piece-type mobility, king squares looked up once) — same scores, ~1.6x
  faster per call; `Position` keeps its bitboards and board inline; magic lookups packed per square;
  a 1 MB static-evaluation cache in the searcher (27% hits). Alternating old and new builds: 1.2x
  faster search with tiered PGO (as the app runs), 1.28x without. Startup: the magic bitboard
  factors were searched for on every launch (0.9 s Release, 1.9 s Debug, despite a comment saying
  "a few milliseconds"); they are now constants, checked while the tables are built (24 ms).
  Depth- and node-limited bots (Acorn to Iris) play exactly as before; time-limited searches (hints,
  review, analysis, Jade and up) get ~1.2x more nodes. Verified: all tests, analysis in the app.
* First-run welcome screen: name + "how much chess have you played?" (new to chess, beginner,
  casual, club, strong). The level sets the first suggested bot (Acorn, Clover, Ember, Gale, Iris),
  the starting puzzle rating (600-2000, still provisional) and the Play page's preselected bot; Home
  then suggests the weakest unbeaten bot at or above that level. New players land on Learn. Skip
  keeps the defaults. Profiles that already have games, puzzles or lessons never see it (so the
  user's reset profile will, once the Release build is rebuilt). Logic in `Gambit.ViewModels`
  (`Onboarding`, 4 tests). Verified in the app on fresh data dirs: casual (name, Ember preselected,
  puzzles 1200?), new to chess (Learn, Acorn, Home text), skip, relaunch, and an existing profile.
  `tools/ui.ps1 type` sets text boxes.
* Fixed (reported by the user): a small "Left" box hovered over the board during games. The
  game, analysis and review pages attach their arrow-key shortcuts to the whole page, and WinUI
  shows the first shortcut's key as a tooltip wherever the pointer rests; those pages now hide it
  (`KeyboardAcceleratorPlacementMode.Hidden`). The keys still work; the move buttons keep their
  own tooltips. Present since the first app version.
* Progress tools in Settings ("Your progress"): back up to a zip (profile, games, unfinished game,
  settings; never the online sign-in token), restore from one (keeps the current token), and reset.
  Restore and reset first save an automatic copy in `<data>/backups` (newest ten kept), then the
  app restarts itself; saves are switched off meanwhile (`AppPaths.Frozen`) so nothing in memory
  writes over the new files. Saved games are found by file name when a profile from another
  computer stores paths that don't exist here. Logic in `Gambit.ViewModels/ProgressBackup` (5
  tests, incl. no writes outside the data folder). Verified in the app through the real file
  dialogs (`tools/file-dialog.ps1`, new): back up, reset (restart → welcome screen), restore
  (restart → 6 games back). A test backup first landed in the user's OneDrive Documents (the
  dialog's default folder); it was moved out at once, and the tool now refuses bare file names.
* Opening review (spaced repetition): the 15 multi-move drills of the opening lessons are review
  lines. Finishing a lesson puts its lines in box 1 (due the next day); a clean review (no
  mistake, no hint) moves a line up a box (3, 7, 14, 30, 60, 120 days), a slip sends it back to
  tomorrow. Learn shows an "Opening review" card (due count or "all caught up, next due …"); the
  session runs on the lesson page in review mode and saves each line as it is finished (a restart
  can't wipe a slip, a line is never counted twice). Home's Lessons card mentions due lines.
  Schedule in `Gambit.ViewModels/OpeningReview` (8 tests). Verified in the app with backdated
  lessons: three lines (one with a deliberate mistake → "comes back tomorrow"), a clean first
  review (→ "in 3 days", box 2), the summary dialog and both cards.
