# Gambit — Progress log

## Current state (update at every milestone)

Sessions 1–3 and 7 of the roadmap are complete, most of 4–6 and 8. Version 0.2.0: the app builds
and runs natively on Arm64 (and x64), with Velopack installers and updates from the server.

| Area | Status |
|---|---|
| Toolchain | .NET SDK 10.0.401 Arm64 (machine-wide). Windows App SDK 2.5.1 via NuGet. No Visual Studio needed. |
| Gambit.Core | Board, legal movegen (perft-verified, also king-less lesson positions), FEN/SAN/UCI/PGN (incl. variations), Game + draw rules, `MoveTree`, clock, Glicko-2, sessions (`IGameSession`), premove rules, openings (~150, embedded TSV), puzzles (embedded CSV), lessons (embedded JSON) |
| Gambit.Engine | PVS + TT + QS/SEE + null-move + LMR + MultiPV; PeSTO eval; 13 bots with opening books; `GameReviewer` (parallel, move classes, accuracy) |
| Gambit.App | Welcome screen (first run), Home, Play (bots, pass-and-play, online), Game (clocks, premoves, hints, takebacks, resume after restart, sounds, online rematch), Game Review (with plain-language explanations), Analysis (multi-core engine lines, position editor, variations), Puzzles (rated, Rush, Survival, daily, themes, openings), Learn (5 courses / 49 lessons, opening review), Online lobby, Profile (stats, rating chart, openings, ~50 achievements), Settings (themes, pieces, sounds, premoves, progress back up / restore / reset, updates); one window per profile |
| Online | `Gambit.Server` (ASP.NET Core + SignalR, authoritative, rematches, spectators, rate limits; accounts with invite-only sign-up, SQLite `gambit.db`, admin commands), `Gambit.Online.Client` (`RemoteGameSession`, `AccountClient`), Dockerfile, docs/ONLINE.md; `./build.ps1 server` for LAN play |
| Tools | `import_lichess_puzzles.py`, `Gambit.PuzzleGen` (experiments), `Gambit.BotArena` (ladder measurement), `Gambit.BotCalibration` + `extract_lichess_games.py` (bots vs real players), `Gambit.OnlineBot` (plays online, accepts rematches), `ui.ps1`, `ui-scroll.ps1`, `screenshot-quiet.ps1` |
| Content | 21,162 puzzles from the Lichess puzzle DB (CC0; `tools/import_lichess_puzzles.py`): 400–1,100 per 100-point band from 400 to 2999, 49 practice themes with ≥ 120 each, 25 practice openings with ≥ 50 each; 49 lessons; tactic/mate solutions audited by the engine in tests |
| Install | `./build.ps1 package -ServerUrl …` → Velopack Setup.exe + portable zip + update packages for win-arm64 and win-x64 in `artifacts/releases` (docs/RELEASING.md); the server offers them at `/download` and `/releases`. Not installed on this PC (installer tests are the user's). Developer install: `./build.ps1 install` |
| Tests | 206 passing (`./build.ps1 test`) incl. in-process server + real clients (games, challenges, rematches, spectating, rate limits) |

## Next steps

**The user's list of 2026-10-05 (items 1-4 of "what's left", no Chess960), in progress:**
1. Finish the bots: Kestrel and Lumen being fitted (grids running), then an arena run. *Open.*
2. Quick wins: play from any position, pass-and-play names and turning board, plain-http
   warning, bug report file. *Done.*
3. Friends, direct challenges, chat, game history sync (protocol 3, schema 2). *Done, checked
   live with two accounts on a local server.*
4. Opening explorer, lessons (zwischenzug, X-ray, triangulation), custom board colors /
   highlight styles / sound packs. *Open.*

**Bot calibration against real players (2026-10-04, at the user's request): Acorn to Jade done.**
Each bot now blunders, errs and finds the best move about as often as people at its rating
(Chess.com rapid scale), measured on Lichess rapid games; random moves are gone (oversights
instead, revision 4). Still to do, in docs/BOT-CALIBRATION.md "Next steps": **fit Kestrel and
Lumen** (both far stronger than people at 2200/2400; give them 3–5 candidates first; finish the
2400 people band), and play the new ladder in the arena. Run long calibrations where the PC
won't sleep: this session's runs were killed twice by sleep. Data in `artifacts/calibration`
(git-ignored; `artifacts/rapid-games.tsv` holds the extracted games).

**Priority (from the user, 2026-10-02): a functional, clean, correct app over bot tuning.**
The user's list of 2026-10-03 is done (QA pass, GamePage view model, inline variations, puzzles
by opening, faster engine, onboarding) and the "Left" tooltip bug is fixed (confirmed by the user).

Proposed to the user on 2026-10-03, in this order (waiting for their pick):
1. **Single-player polish: done** (2026-10-03): progress tools in Settings, spaced repetition for the
   opening drills, a multi-core analysis board. The puzzle source link was dropped (the user's call).
2. **Server ready to go online: done** (2026-10-03, built and tested locally, not deployed):
   accounts with sign-in and invite-only sign-ups, SQLite, repeat-walkout pauses, name moderation,
   backups. The user chose username + password (no email), SQLite, invite-only. Going live is the
   Fly.io steps in docs/ONLINE.md, when the user wants (their account; they sign in themselves).
3. **Distribution: done** (2026-10-03): Velopack installers for Arm64 and x64, updates from the
   server (delta packages), a `/download` page, version 0.2.0 + CHANGELOG.md, the x64 build tested
   under emulation, one window per profile. Code signing is the user's call (RELEASING.md).
4. **With the user:** the Arm64 installer checked out (install, shortcuts, one window,
   uninstall). Still open: the x64 installer on an Intel/AMD PC, a real online game with a friend.
   **No deployment, signing or Fly.io for now (2026-10-03):** friends get the installer file, and the
   user's PC is the server (LAN or Tailscale), as in RELEASING.md "Sharing without hosting".
5. **Candidates found in the 2026-10-03 audit** (not started; the user picks): warn before signing
   in over plain http:// to a non-local server (passwords travel unencrypted with port forwarding),
   a way for friends to send a log/bug report (logs stay on their PC), a one-command release
   upload (instead of sftp, ~400 MB a release), walkout counts that survive a server restart,
   editable banned-words list (now compiled into `NameRules.cs`).
6. **Parked unless asked:** bot ladder tuning (BOT-CALIBRATION.md), lessons on zwischenzug /
   X-ray / triangulation, localization, accessibility (keyboard play, Narrator; the user said "no
   accessibility" when asking for next steps, 2026-10-03).

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
* Time-limited bots (Jade and up) get ~1.2x more nodes since the engine speed-up, so they may be a
  little stronger than BOT-CALIBRATION.md says (not re-measured; bot tuning is parked).
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
