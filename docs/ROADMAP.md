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

- [ ] Toolchain (.NET 10 SDK Arm64), repo, solution, `build.ps1`, docs (`CLAUDE.md`, roadmap, progress, architecture)
- [ ] `Gambit.Core` — bitboards + magic sliders, legal move generation, make/unmake, Zobrist hashing
- [ ] `Gambit.Core` — FEN, SAN (parse + format), UCI, PGN (read/write), `Game` with result detection
      (mate, stalemate, insufficient material, threefold, 50-move), Glicko-2 rating math
- [ ] `Gambit.Tests` — perft suite (6 standard positions), FEN/SAN/PGN round-trips, draw rules
- [ ] `Gambit.Engine` — PeSTO-style tapered eval, iterative deepening PVS, TT, quiescence, null-move,
      LMR, killers/history, time + node limits, MultiPV, cancellation
- [ ] Bot roster v1 — ~10 named bots (≈250 → 2400+) using depth/node limits, eval noise, MultiPV
      softmax selection and blunder probability
- [ ] `Gambit.App` (WinUI 3, unpackaged, self-contained, Arm64) — Mica, custom title bar, NavigationView
      shell with Home / Play / Puzzles / Learn / Analysis / Profile / Settings (future pages are placeholders)
- [ ] Board control — click + drag moves, legal-move dots, last move / check / selection highlights,
      promotion picker, flip, coordinates, move animation, vector piece set
- [ ] Play vs Bot — bot picker, color choice, SAN move list, resign, takeback, new game, game-over dialog
- [ ] `IGameSession` abstraction (local now, remote later) — see `docs/ARCHITECTURE.md`
- [ ] Settings — app theme (system/light/dark), board theme, piece style, show legal moves, sounds toggle

## Session 2 — Play experience & analysis

- [ ] Time controls (bullet/blitz/rapid/classical/unlimited) with clocks + flagging
- [ ] Bot personalities: opening book per bot, avatars, short "chat" lines, play styles
- [ ] Opening book (curated SAN lines → hash table), opening name detection (ECO subset)
- [ ] Game review: engine pass over finished games; move classes (Brilliant, Great, Best, Excellent,
      Good, Book, Inaccuracy, Mistake, Miss, Blunder); accuracy %; eval graph; key moments
- [ ] Analysis board: free play, MultiPV engine lines, eval bar, FEN/PGN import + export, board editor
- [ ] Game archive: every finished game saved; list + reopen in analysis/review
- [ ] Sounds (generated locally: move, capture, check, castle, promote, game end, low time)
- [ ] Hints (bot games), premoves, keyboard navigation of the move list, Play a Friend (hot-seat)

## Session 3 — Puzzles

- [ ] Puzzle pipeline (`tools/puzzles`): import the Lichess puzzle DB (CC0, ask before downloading),
      pick a stratified subset (~15–25k puzzles, 400–3000 rating, all major themes), pack as an
      embedded compressed resource; validation test that every solution is legal
- [ ] Rated puzzles with Glicko-2 puzzle rating; adaptive selection near rating; no repeats
- [ ] Puzzle UX: opponent's setup move animates, wrong move feedback, hint (highlight piece → move),
      retry, show solution, "next", themes + rating shown after solve
- [ ] Puzzle Rush: 3 min, 5 min, Survival (3 strikes); best scores
- [ ] Daily puzzle (deterministic by date) and streak tracking
- [ ] Progression: puzzle "ranks"/tiers by rating, per-theme practice and per-theme stats

## Session 4 — Lessons I: framework + fundamentals + tactics

- [ ] Lesson format (JSON): course → lesson → steps (explain, make-a-move, find-the-move,
      multiple choice, play-it-out vs bot from position), arrows + square highlights, hints
- [ ] Lesson player UI with progress, stars/score, retry; course map with unlock progression
- [ ] Content validator test (all FENs/moves legal, all steps reachable)
- [ ] Courses: **Basics** (pieces, check, mate, special moves), **Checkmate patterns**,
      **Tactics** (fork, pin, skewer, discovered attack, double check, deflection, decoy,
      removing the defender, overloading, zwischenzug, back rank, X-ray)

## Session 5 — Lessons II: openings + endgames

- [ ] **Opening principles** + opening courses with main lines, ideas and traps: Italian, Ruy Lopez,
      Scotch, Sicilian (Open/Alapin), French, Caro-Kann, Queen's Gambit (QGD/QGA/Slav), London,
      King's Indian, English — drilled with a "play the line" trainer
- [ ] **Endgames**: K+Q/K+R/2B/B+N mates, opposition, square rule, key squares, Lucena, Philidor,
      triangulation, rook-pawn draws, basic rook/queen endings
- [ ] Opening trainer (spaced repetition of repertoire lines)

## Session 6 — Profile: statistics, achievements, themes

- [ ] Persistent profile store (SQLite), schema versioning/migrations, import of earlier JSON data
- [ ] Statistics page: rating graphs (bots/puzzles), W/L/D by bot/color/time control, accuracy trend,
      openings played, puzzle themes heat-map, streaks, time played
- [ ] Achievements (~50): data-driven definitions, progress tracking, unlock toasts, achievement gallery
- [ ] Themes: 8+ board themes, 3+ piece sets, custom board colors, highlight styles, sound packs,
      accent options; live preview in Settings

## Session 7 — Online multiplayer (server + client)

- [ ] `Gambit.Server` (ASP.NET Core 10 + SignalR): server-authoritative games using `Gambit.Core`,
      server clocks, matchmaking pools (time control × rating), private challenges via invite code,
      draw/resign/abort/rematch, reconnection, Glicko-2 ratings, guest → registered accounts
- [ ] `Gambit.Online.Contracts`: shared DTOs + hub interfaces (strongly typed SignalR)
- [ ] Client: `RemoteGameSession : IGameSession`, Online page (sign in, quick pair, challenge a friend,
      lobby), connection status UX
- [ ] Integration tests: two clients play a full game against an in-process test server

## Session 8 — Hosting & distribution

- [ ] Dockerfile + config; deploy to a host with WebSockets (Azure Container Apps / App Service,
      Fly.io or similar — **you** create the account and sign in; Claude prepares scripts + docs)
- [ ] Production DB (PostgreSQL), TLS/domain, rate limiting, logging/health checks
- [ ] MSIX packaging + signing, app icon/assets, versioning, App Installer auto-update
- [ ] Online polish: friends list, game history sync, spectating

## Backlog / stretch

- Stronger engine (NNUE-style eval, multithreaded search), Elo calibration of bots via self-play
- Opening explorer, coach explanations ("why was this a blunder?"), endgame tablebase (syzygy) probing
- Accessibility pass (Narrator, keyboard-only play, high contrast), localization
- Cloud sync of profile, web/mobile client reusing `Gambit.Core`
