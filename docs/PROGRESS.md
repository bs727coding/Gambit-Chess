# Gambit — Progress log

## Current state (update at every milestone)

Sessions 1–5 and 7 of the roadmap are complete, most of 6 and 8. Version 0.3.0 (2026-10-05): the app
builds and runs natively on Arm64 (and x64), with Velopack installers and updates from the server.
Since then (unreleased, for 0.3.1): the "update required" message on the Online page and a UI rework
(Inter, accent from the board theme, icon-rail menu, Play tabs, new Home, tidier game page, Settings
in sections).

| Area | Status |
|---|---|
| Toolchain | .NET SDK 10.0.401 Arm64 (machine-wide). Windows App SDK 2.5.1 via NuGet. No Visual Studio needed. |
| Gambit.Core | Board, legal movegen (perft-verified, also king-less lesson positions), FEN/SAN/UCI/PGN (incl. variations), Game + draw rules, `MoveTree`, clock, Glicko-2, sessions (`IGameSession`), premove rules, openings (~150, embedded TSV), opening explorer (40k Lichess games, embedded move tree), puzzles (embedded CSV), lessons (embedded JSON) |
| Gambit.Engine | PVS + TT + QS/SEE + null-move + LMR + MultiPV; PeSTO eval; 13 bots with opening books, calibrated against real players (Chess.com rapid scale) and checked in games (docs/BOT-CALIBRATION.md); `GameReviewer` (parallel, move classes, accuracy) |
| Gambit.App | Inter typeface (bundled) and an accent color that follows the board theme (`Theming/AppAccent.cs`); icon-rail menu that opens on hover. Welcome screen (first run), Home (game in progress or next bot, daily puzzle, on small boards), Play (tabs: bots grouped by level, pass-and-play with names, time control, board turning and takebacks, online), Game (clocks, premoves, hints, takebacks, resume after restart, sounds, online rematch), Game Review (with plain-language explanations), Analysis (multi-core engine lines, position editor, variations, opening explorer, play from here), Puzzles (rated, Rush, Survival, daily, themes, openings), Learn (5 courses / 52 lessons, opening review), Online (lobby, friends, direct challenges, chat), Profile (stats, rating chart, openings, ~50 achievements), Settings (board themes and custom colors, highlight color, pieces, sound packs, premoves, chat, progress back up / restore / reset, report a problem, updates); one window per profile |
| Online | `Gambit.Server` (ASP.NET Core + SignalR, authoritative, rematches, spectators, rate limits; accounts with invite-only sign-up, SQLite `gambit.db` (schema 2), admin commands; friends with live status, direct challenges, chat, game history; protocol 3), `Gambit.Online.Client` (`RemoteGameSession`, `AccountClient`), Dockerfile, docs/ONLINE.md; `./build.ps1 server` for LAN play |
| Tools | `import_lichess_puzzles.py`, `Gambit.PuzzleGen` (experiments), `Gambit.BotArena` (ladder in games, with `id.Property=value` variants), `build_explorer.py` (explorer data), `Gambit.BotCalibration` + `extract_lichess_games.py` (bots vs real players), `Gambit.OnlineBot` (plays online, accepts rematches), `ui.ps1`, `ui-scroll.ps1`, `screenshot-quiet.ps1` |
| Content | 21,162 puzzles from the Lichess puzzle DB (CC0; `tools/import_lichess_puzzles.py`): 400–1,100 per 100-point band from 400 to 2999, 49 practice themes with ≥ 120 each, 25 practice openings with ≥ 50 each; 52 lessons; explorer: 14,826 moves from 40,452 Lichess rapid games; tactic/mate solutions audited by the engine in tests |
| Install | `./build.ps1 package -ServerUrl …` → Velopack Setup.exe + portable zip + update packages for win-arm64 and win-x64 in `artifacts/releases` (docs/RELEASING.md); the server offers them at `/download` and `/releases`. Not installed on this PC (installer tests are the user's). Developer install: `./build.ps1 install` |
| Tests | 239 passing (`./build.ps1 test`) incl. in-process server + real clients (games, challenges, rematches, spectating, rate limits) |

## Next steps

**The user's list of 2026-10-05 is done** (items 1–4 of "what's left"; no Chess960, at their
request): the bots finished and checked in games, play from any position, pass-and-play names and
board turning, the plain-http warning, bug report files, friends / direct challenges / chat / game
history (protocol 3), the opening explorer, three lessons, and board and sound customisation.

**UI rework done (2026-10-05, unreleased)**, from the options the user picked: Inter, accent from
the board theme, compact menu, Play with mode tabs, Home with continue + daily, tidier game page and
Settings in sections. **Next: package 0.3.1** when the user is happy with the look ("Before we
package it, let's do a UI rework").

**For the user to try** (can't be checked from here): a real online game with a friend, with their
PC as the server (LAN or Tailscale, RELEASING.md "Sharing without hosting"); the x64 installer on
an Intel/AMD PC; the sound packs (Settings > Sound pack plays a sample); the retuned Iris and Jade;
the menu opening on hover (needs a real mouse; the code was reviewed, not driven).

**Candidates, for the user to pick:**
* Chess960 (left out on 2026-10-05).
* The explorer with a rating filter or more games: its data is one 150 MB slice of one month
  (40k games); more needs a bigger Lichess download (ask first: size and source).
* More people positions: 2400 (Lumen's fit rests on 352) and 3,000 per band (half the noise).
* The full bishop-and-knight W manoeuvre lesson.
* From the 2026-10-03 audit: a one-command release upload, walkout counts that survive a server
  restart, an editable banned-words list (compiled into `NameRules.cs`).
* No deployment, signing or Fly.io for now (the user, 2026-10-03). Parked unless asked:
  localization, accessibility (the user excluded it twice).

**Priority (from the user, 2026-10-02): a functional, clean, correct app over bot tuning** (bot
work only on request).

## Known issues / notes

* Engine: ~1.5M nodes/s on one thread (Release, `tools/bench.cs`); the analysis board uses half the
  cores (max 8) and reaches a depth ~2.2x sooner with 6 threads. Ideas left: more diverse helper
  threads (better SMP scaling), incremental piece-square sums and a pawn hash (~5% each). Staged
  move generation would change move order (and the signature).
* Online: accounts are username + password, invite-only; no email, so forgotten passwords are reset
  by an admin (`reset-password`). Walkout counts are in memory (a restart forgives).
* Installers: unsigned (SmartScreen "unknown publisher"), ~100 MB each (self-contained .NET +
  Windows App SDK; updates are deltas, often < 1 MB). Setup.exe was never run on this PC (it would
  install and start on the user's real profile), so install/uninstall is unverified here; the
  update flow was verified with the portable package. Setup puts a "Gambit" shortcut on the
  desktop, replacing the user's current desktop shortcut to the development build.
* The x64 build was tested under emulation on the Snapdragon only, and Windows 10 never
  (`TargetPlatformMinVersion` 10.0.19041).
* Bots are limited by depth or nodes, not time: Kestrel (4M nodes) and Lumen (5M) have time caps
  of 3 s and 4 s a move, which bind first on a PC slower than ~2M nodes/s per core, so there they
  play a little below their calibration. Timed games use the clock as before.
* UI tests: `tools/ui.ps1` drives the app through UI Automation. Board squares are invokable
  elements (`sq-e4`), so moves can be played without the mouse:
  `./tools/ui.ps1 invoke -Name "Play"`, `./tools/ui.ps1 board -Moves "e2e4,g1f3"`.
  **The user plays Gambit while sessions run** — check it isn't in use before driving it, and
  test in a sandbox profile (`./build.ps1 run -Configuration Debug -DataDir artifacts/qa-profile`).
* The user's real profile was reset to a clean slate on 2026-10-03 at their request. That was done
  from inside the Claude app's container (see CLAUDE.md, AppData is virtualized), so the backup of
  the old profile is **not** at `%LOCALAPPDATA%\Gambit-backup-2026-10-03` as reported: it is in
  `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\Gambit-backup-2026-10-03`,
  invisible to the user, next to a stale private copy of the reset profile (`...\LocalCache\Local\Gambit`)
  that hides the real one from Claude's tools. The user (2026-10-03): the backup isn't needed (left
  where it is, harmless); the stale private copy was deleted, so the real profile shows through.
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
* Multi-core analysis board (Lazy SMP): `ParallelSearcher` runs helper searchers on their own
  threads, sharing one transposition table, while the main searcher reports the lines; odd helpers
  search a ply deeper so the threads diverge. The table became safe to share without locks (each
  entry stores its data and key XOR data, so a torn write reads as a miss) with identical
  single-thread behaviour: the bench signature is unchanged, so bots, hints and review play as
  before. Analysis uses half the cores, at most eight. `tools/bench.cs smp`: with 6 threads the
  analysis search (3 lines) reaches depth 13 2.1x and depth 15 2.2x sooner. 7 tests (mates found,
  time limits, cancellation joins every thread, 3 lines, table round trip). In the app (Debug):
  ~1,000-2,000 kN/s instead of ~300, depth 13 in about 3 s; restarts cleanly on every move, and the
  thread count stays flat over repeated position changes.
* Group 2, step 1 — accounts (the user chose username + password, SQLite, invite-only):
  * Server data in one SQLite file (`gambit.db`, `ServerDatabase` with schema versions): users,
    sessions, invites, ratings and finished games with PGN (replacing ratings.json and PGN files).
  * `AccountStore`: sign-up with an invite code, PBKDF2 password hashes (ASP.NET Core's hasher),
    session keys stored as SHA-256 hashes, 180-day sessions renewed by use, five wrong passwords
    pause a name for ten minutes, bans end sessions. Username and password rules in `NameRules`.
  * HTTP account API (`/api/register`, `signin`, `signout`, `me`, `password`, `invites`) with a
    session auth scheme; the game hub requires a signed-in account (protocol v2).
  * A new server logs a one-time invite for the first account (admin). Members make invites in the
    app (one use, 14 days, 5 open). Admin commands on the server binary: invite(s), users,
    reset-password, ban/unban, rename, make/remove-admin, delete-user, backup.
  * App: Online page sign-in / create-account form, Invite a friend, Sign out; the session key is
    kept in Windows Credential Manager (per data folder), not in settings.json.
  * Online bot plays as `<Name>Bot` (password from GAMBIT_BOT_PASSWORD, `--invite` once).
  * 25 new tests (store, rules, admin commands, API end to end, ratings and games across a restart);
    the online tests now sign up with invites. Verified in the app against a local server: first
    account with the logged code (admin), invite, sign out, wrong and right password, saved sign-in
    after a restart, and a rated game against the online bot stored in the database.
* Group 2, step 2 — walkouts: not moving first, resigning before both sides moved, or leaving a game
  is recorded per player (`Conduct`, in memory); three within 30 minutes pause quick pairing for 10
  minutes (friends' games still work). Hub errors now reach the player in the server's own words
  (`OnlineClient.ServerMessage`), and server notices show on the Online page and as game messages
  (they were dropped before). Tests: end to end (three aborts → paused, the opponent unaffected)
  and the timing with a test clock.
* Group 2, step 3 — name moderation: offensive usernames are refused at sign-up and rename (strong
  words anywhere, also with look-alike digits or split by - and _; short words only as a separate
  word, so Cassandra, Dickens, Peacock, Therapist pass). `AccountWatch` applies admin changes to
  connected players every 20 s: suspended or deleted accounts are told why and disconnected,
  renames take effect. Tests: refused and allowed names, a ban and a rename reaching connected players.
* Group 2, step 4 — backups: the server keeps daily copies (`backups/`, newest seven); admins
  download the whole database from the app (Online → Back up server…, `GET /api/admin/backup`);
  a `restore.db` in the data folder replaces the database at the next start, keeping the old one.
  Tests: admin download (and a member refused), the seven-day rotation, restore at startup. Verified
  in the app against a local server: the admin button saved a 61 KB copy through the real dialog.
* Group 3 — distribution:
  * Version 0.2.0 (`Directory.Build.props`), CHANGELOG.md; Settings → About shows the build's commit.
  * Velopack: `Program.cs` runs its hooks first (`DISABLE_XAML_GENERATED_MAIN`). `./build.ps1 package`
    publishes win-arm64 and win-x64 and packs each (package id `GambitChess`, not "Gambit":
    uninstalling deletes `%LOCALAPPDATA%\<id>`, and the data lives in `%LOCALAPPDATA%\Gambit`).
    Optional signing through `GAMBIT_SIGN_PARAMS` (signtool) or `GAMBIT_TRUSTED_SIGNING` (Azure).
  * Updates: installed copies check `<server>/releases` 8 s after start; Home shows "Update
    available" and Settings has an Updates row. `-ServerUrl` builds the server into the app
    (`AppInfo.HomeServer`): updates come from there, and new profiles connect to it. Updating
    waits while an online game is in progress (local games are saved after every move).
  * Server: `releases/` in the data folder is served at `/releases`; `/download` lists the
    installers (Arm vs Intel/AMD) with the SmartScreen note. Test: an empty page, then the
    installers, feed and package served with the right types.
  * **Found by the x64 test: every published copy crashed at startup** ("XAML parsing failed") —
    `dotnet publish` left out the compiled XAML (`*.xbf`, `Gambit.pri`), so `./build.ps1 publish`
    and `install` had been broken. `EnableMsixTooling` fixes it (still unpackaged). The x64 build then
    ran under emulation: analysis at depth 18, a bot game, 21,162 puzzles, a clean log.
  * Verified end to end with the portable package: 0.2.0 found 0.2.1 on a local server, showed the
    banner, downloaded, restarted on the same test profile as 0.2.1 and didn't offer it again.
    vpk made a 0.18 MB delta from the previous release (`artifacts/releases` must be kept).
  * **One window per data folder** (`SingleInstance`, Windows App SDK `AppInstance`): two copies on
    one profile each saved their own in-memory settings/profile over the other's (a game recorded in
    one vanished when the other saved). A second launch now brings the open window forward; a test
    profile runs alongside the real one; `App.Restart` hands over first. Verified: a second copy on
    the same test profile exits, one on another profile runs, reset-and-restart still works.
  * Fixed in passing: a raw NUL byte in `AccountTests.cs` (from an edit script) made git treat the
    file as binary; Settings showed "ready to install" instead of an update error.
  * `tools/ui.ps1 value -AutomationId X` reads a text box. Docs: RELEASING.md (new), README
    (Install), ONLINE.md (downloads and updates, 3 GB Fly volume), CLAUDE.md, ROADMAP, ARCHITECTURE.
* The user's installer check: install, the shortcuts and one window per profile worked. Uninstalling
  seemed to leave Gambit behind because the taskbar pin opened it, but that pin was the development
  build (`src/.../bin/ARM64/Release`), which shares the profile. The real log confirms it: the
  installed copy ran, the second launch handed over, and the later starts are the development build.
  Settings → About now says "installed" or "development copy (not installed)". Restored the
  desktop shortcut to the development build (Setup had replaced it and uninstalling removed it).
* Found while checking: everything Claude Code starts here sees a virtualized AppData (the Claude
  app is an MSIX package). My earlier "your progress survived" check read a stale private copy;
  the real profile was fine. `tools/run-outside.ps1` runs PowerShell outside the container (WMI) to
  read the real profile; CLAUDE.md explains it.

### Session 3 — 2026-10-04: bots calibrated against real players
* The user found the 1000 bot (Ember) blundering more than a 1000 player would and asked to retune
  every bot against real-world data. Plan: measure people per rating, then each bot on the same
  positions, with the same yardstick (Game Review's thresholds).
* Data (download approved by the user): the first 150 MB of Lichess's September 2026 database
  (range request; deleted afterwards) → 69,673 rated rapid games (`tools/extract_lichess_games.py`).
  Ratings mapped to Chess.com rapid with ChessGoals' survey (Chess.com 1000 ≈ Lichess 1430).
* New `tools/Gambit.BotCalibration`: `humans` samples 1,500 positions per band (players within ±60,
  opponents within ±150, move 5 on, ≥ 30 s on the clock) and scores the people's moves at a fixed
  node budget; `bots` / `grid` / `fit` score bots on the same positions; results cached per run.
* Findings: people find the best move ~50% of the time at every level; a 1000 player blunders on
  4.1% of moves. The four weakest bots blundered ~3x as often as people (random moves and high
  temperatures); Harbor and up were far stronger than their labels (Iris 94 accuracy vs 89).
* Revision 4: random moves replaced by oversights (the move that looks best right after it, ignoring
  the opponent's reply); Acorn to Jade fitted (temperature, noise, oversight share), checked on
  common positions (Dash and Flint nudged; Iris and Jade now weigh 5 candidates). Every fitted bot
  is within ~1 point of accuracy of people at its rating. Test: an oversight grabs a defended pawn.
* Kestrel and Lumen not fitted yet (expensive; see Next steps). The PC slept twice during long runs,
  which killed them; `request_keep_awake` helps against idle sleep only.

### Session 4 — 2026-10-05: the user's list (no Chess960)
* The user asked what was left to make Gambit the best, then picked items 1–4 except Chess960.
* Quick wins: "Play from here" on the analysis board (a bot or pass and play, side, time control;
  practice games are saved and listed but not counted); pass and play asks for both names and can
  turn the board after each move; a warning before signing in over plain http:// to a server on the
  internet (`ServerAddress.IsUnencryptedOverInternet`: private, loopback, .local/.lan/.home.arpa and
  Tailscale addresses don't count); Settings > Report a problem saves a zip of the last 14 days of
  logs and the settings without the online token.
* Online with friends (protocol 3, server schema 2): friend requests by username with live
  online/playing status, direct challenges only the friend can take (busy players decline
  automatically), chat in online games (200 characters, 5 per 10 s, offensive words masked; Settings
  can hide it), and `GET /api/games`, so games from other PCs appear in the game list (listed, not
  counted). Verified live with two accounts on a local server.
* Lessons 49 → 52: zwischenzug and x-ray (Lichess puzzle positions, each confirmed by the engine at
  depth 8), triangulation (info and quiz steps: every white king move wins there).
* Customisation: a Custom board theme (color pickers for both squares), a highlight color for the
  last move and selection, and sound packs (Classic, Soft, Retro; synthesized by `gen_sounds.py`,
  levels matched). Checked in the test window, dark and light.
* Opening explorer: an Explorer tab beside Moves on the analysis board. 40,452 Lichess rapid games
  (both players 1400+), first 15 moves, lines played ≥ 3 times: a 180 KB tree in Core, replayed at
  first use with transpositions merged; a click plays the move (`tools/build_explorer.py`).
* Bots: Kestrel and Lumen fitted (five candidates; Kestrel 92.4 accuracy vs people's 91.9 at 2200;
  Lumen extrapolated). The first revision-4 arena run (45 games a step) suggested flat steps that
  were mostly noise; playing the fast pairs on their own (thousands of games) and testing variants
  showed that in games temperature is what counts and depth barely does. Dash, Ember, Flint, Iris
  and Jade were retuned (Dash and Flint closer to people than before; Iris and Jade 1.3–1.7
  accuracy points sharper than people, with their blunder rates); Kestrel and Lumen got 3 s / 4 s
  time caps so their node budgets bind. Final ladder (bot-vs-bot): Acorn → Bramble +163, Bramble →
  Clover +152, Clover → Dash +90, Dash → Ember +126, Ember → Flint +120, Flint → Gale +117, Gale →
  Harbor +151, Harbor → Iris +187, Iris → Jade +228, Jade → Kestrel +133, Kestrel → Lumen +181.
  docs/BOT-CALIBRATION.md has the details.
* The PC went into standby twice during arena runs (10:50, 11:57), stalling them;
  `request_keep_awake` doesn't stop a closed lid or a manual sleep. Two arena runs can't share a
  results file (the writer locks it): use separate files and append them.
* Tools: the arena takes `id.Property=value` overrides and lets node/depth-limited bots search to
  their limits; `ui.ps1 invoke` opens combo boxes.
* Version 0.3.0 packaged. Online: a version check (`/api/info`, protocol and app version) before
  connecting, so the Online page says "Update required" where Connect was pressed.
* UI rework (the user: "quite clunky - especially on the play page"; options picked from a list):
  * Inter (variable font in Assets/Fonts). WinUI's text styles name the system font outright, so
    App.xaml redefines them with WinUI's sizes, adds an implicit TextBlock style and sets
    `ContentControlThemeFontFamily` in the theme dictionaries. Clocks keep Segoe UI Variable
    (equal-width digits).
  * Accent from the board theme: `AppAccent.Apply` writes `SystemAccentColor` and its six shades
    into the app resources; a theme change is applied by toggling the window theme once (250 ms
    after the last change). Built-in themes have a chosen accent; Custom derives one from the dark
    squares.
  * Menu: `LeftCompact`, opening after 400 ms of mouse rest on the rail (`PaneContentGrid`).
  * Play: SelectorBar tabs; bots in a grouped GridView; compact settings card; pass and play gained
    a time control and takebacks (`PassAndPlayTimeControl`, `PassAndPlayTakebacks`).
  * Home: next game / daily puzzle on small non-interactive boards. A bot game is saved only after
    its first move, so Home asks `GamePage.ActiveGameSnapshot()` for the open game.
  * Game and Analysis: `ToolbarButtonStyle` (based on WinUI's `SubtleButtonStyle`, so disabled
    buttons stay unfilled) in one card with the actions. Settings regrouped into six sections.
  * Checked in the test window, dark and light, on every page.
