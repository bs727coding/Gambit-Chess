# Gambit — Roadmap

Gambit is a native Windows 11 (Arm64-first) chess app: play bots of many strengths, rated puzzles,
interactive lessons, achievements, statistics and themes — architected so online multiplayer is an
additive feature, not a rewrite.

The work is split into **sessions** that each fit inside one ~5-hour Claude Code session. Every
session ends with a building app, passing tests, an updated `docs/PROGRESS.md`, and a git commit,
so the next session can start cold.

> **How to run a session:** open Claude Code in this folder and paste the kickoff prompt from
> `docs/PROGRESS.md` ("Next session kickoff"). Claude reads `CLAUDE.md`, this roadmap and the
> progress log, then continues with the first unchecked items below.

Legend: `[x]` done · `[~]` partially done (see PROGRESS.md) · `[ ]` not started

---

## Session 1 — Foundations: rules, engine, app shell, play vs bot

**Goal:** a real, playable native app. You can launch it, pick a bot, and play a full game.

- [x] Toolchain (.NET 10 SDK Arm64), repo, solution, `build.ps1`, docs (`CLAUDE.md`, roadmap, progress, architecture)
- [x] `Gambit.Core` — bitboards + magic sliders, legal move generation, make/unmake, Zobrist hashing
- [x] `Gambit.Core` — FEN, SAN (parse + format), UCI, PGN (read/write), `Game` with result detection
      (mate, stalemate, insufficient material, threefold, 50-move), Glicko-2 rating math
- [x] `Gambit.Tests` — perft suite (6 standard positions), FEN/SAN/PGN round-trips, draw rules
- [x] `Gambit.Engine` — PeSTO-style tapered eval, iterative deepening PVS, TT, quiescence, null-move,
      LMR, killers/history, time + node limits, MultiPV, cancellation
- [x] Bot roster v1 — ~10 named bots (≈250 → 2400+) using depth/node limits, eval noise, MultiPV
      softmax selection and blunder probability
- [x] `Gambit.App` (WinUI 3, unpackaged, self-contained, Arm64) — Mica, custom title bar, NavigationView
      shell with Home / Play / Puzzles / Learn / Analysis / Profile / Settings (future pages are placeholders)
- [x] Board control — click + drag moves, legal-move dots, last move / check / selection highlights,
      promotion picker, flip, coordinates, move animation, vector piece set
- [x] Play vs Bot — bot picker, color choice, SAN move list, resign, takeback, new game, game-over dialog
- [x] `IGameSession` abstraction (local now, remote later) — see `docs/ARCHITECTURE.md`
- [x] Settings — app theme (system/light/dark), board theme, piece style, show legal moves (sounds → Session 2)

## Session 2 — Play experience & analysis

- [x] Time controls (bullet/blitz/rapid/classical/unlimited) with clocks + flagging
- [x] Bot personalities: opening book per bot, avatars, chat lines, play styles (aggressive / solid / tricky)
- [x] Opening book (curated SAN lines → hash table), opening name detection (ECO subset)
- [x] Game review: engine pass over finished games; move classes (Brilliant, Great, Best, Excellent,
      Good, Book, Inaccuracy, Mistake, Miss, Blunder); accuracy %; eval graph; key moments
- [x] Analysis board: free play, MultiPV engine lines, eval bar, FEN/PGN import + export, board editor, variations (PGN import + export with nested variations)
- [x] Game archive: every finished game saved (PGN); list + reopen in analysis
- [x] Sounds (generated locally: move, capture, check, castle, promote, game end, low time)
- [x] Keyboard navigation, Play a Friend (hot-seat), hints, resume after restart, premoves

## Session 3 — Puzzles

- [x] Puzzle pipeline: **engine-generated** (`tools/Gambit.PuzzleGen`, bot self-play, verified unique solutions); optional Lichess CC0 import later (ask before downloading),
      pick a stratified subset (~15–25k puzzles, 400–3000 rating, all major themes), pack as an
      embedded compressed resource; validation test that every solution is legal
- [x] Rated puzzles with Glicko-2 puzzle rating; adaptive selection near rating; no repeats
- [x] Puzzle UX: opponent's setup move animates, wrong move feedback, hint (highlight piece → move),
      retry, show solution, "next", themes + rating shown after solve
- [x] Puzzle Rush: 3 min, 5 min, Survival (3 strikes); best scores
- [x] Daily puzzle (deterministic by date) and streak tracking
- [x] Progression: puzzle "ranks"/tiers by rating, per-theme practice and per-theme stats

## Session 4 — Lessons I: framework + fundamentals + tactics

- [x] Lesson format (JSON): course → lesson → steps (explain, make-a-move, find-the-move,
      multiple choice, play-it-out vs bot from position), arrows + square highlights, hints
- [x] Lesson player UI with progress, stars/score, retry; course map with unlock progression
- [x] Content validator test (all FENs/moves legal, all steps reachable)
- [~] Courses: **Basics** (pieces, check, mate, special moves) ✔, **Checkmate patterns** ✔,
      **Tactics**: fork, pin, skewer, discovered attack, double check, removing the defender,
      overloading, trapped pieces, decoy, deflection ✔ (back rank is in Checkmate patterns) —
      zwischenzug and X-ray still to write

## Session 5 — Lessons II: openings + endgames

- [x] **Opening principles** + opening courses with main lines, ideas and traps: Italian, Ruy Lopez,
      Scotch, Sicilian (incl. Alapin), French, Caro-Kann, Queen's Gambit (QGD, QGA, Slav), London,
      King's Indian, English ✔ — drilled with a "play the line" trainer
- [~] **Endgames**: K+Q, K+R, two-bishop and bishop+knight mates, opposition, key squares, square rule, Lucena,
      Philidor, wrong bishop (rook-pawn draws), rooks behind passed pawns ✔ — triangulation
      still to write; the B+N lesson teaches the corner rule, not the full W manoeuvre
- [~] Opening trainer: "play the line" drills in each opening lesson ✔ — spaced repetition still to do

## Session 6 — Profile: statistics, achievements, themes

- [ ] Persistent profile store (SQLite), schema versioning/migrations (currently JSON — works fine so far)
- [x] Statistics page: rating graphs (bots/puzzles), W/L/D by bot/color/time control, accuracy trend,
      openings played, puzzle themes heat-map, streaks, time played
- [x] Achievements (~50): data-driven definitions, progress tracking, unlock toasts, achievement gallery
- [~] Themes: 8 board themes ✔, 4 piece sets ✔, live preview ✔ — custom colors, highlight styles, sound packs,
      accent options; live preview in Settings

## Session 7 — Online multiplayer (server + client)

- [~] `Gambit.Server` (ASP.NET Core 10 + SignalR): server-authoritative games using `Gambit.Core`,
      server clocks, matchmaking pools (time control × rating), private challenges via invite code,
      draw/resign/abort/rematch, reconnection, Glicko-2 ratings ✔ — guest tokens only; registered
      accounts → Session 8
- [x] `Gambit.Online.Contracts`: shared DTOs + hub interfaces (strongly typed SignalR)
- [x] Client: `RemoteGameSession : IGameSession`, Online page (connect, quick pair, challenge a friend,
      lobby), connection status UX
- [x] Integration tests: two clients play a full game against an in-process test server

## Session 8 — Hosting & distribution

- [x] Dockerfile + `fly.toml` + hosting guide (one instance, `/data` volume, forwarded headers);
      deploy to Fly.io / Azure Container Apps / any Docker host — **you** create the account and
      sign in; Claude prepares config + docs
- [~] Rate limiting ✔, health check ✔, console logging ✔ — production DB (PostgreSQL), TLS/domain still to do
- [~] Per-user install with Start menu shortcut (`./build.ps1 install`) ✔, app icon ✔ — MSIX packaging + signing, versioning, auto-update still to do
- [~] Online polish: spectating ✔ — friends list, game history sync still to do

## Backlog / stretch

- Stronger engine (NNUE-style eval, multithreaded search); even out the bot ladder with
  `tools/Gambit.BotArena` (first measurement in docs/BOT-CALIBRATION.md)
- Opening explorer, endgame tablebase (syzygy) probing; richer coach explanations (Game Review
  already explains hung pieces, allowed/missed mates and forks)
- Accessibility pass (Narrator, keyboard-only play, high contrast), localization
- Cloud sync of profile, web/mobile client reusing `Gambit.Core`
