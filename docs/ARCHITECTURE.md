# Gambit — Architecture

## Goals that shape the design

1. **Native Windows 11 on Arm64.** WinUI 3 (Windows App SDK) + .NET 10, compiled for `win-arm64`
   (x64 also builds). Fluent controls, Mica backdrop, system accent color, light/dark theme.
2. **Online-ready from day one.** Everything about the *rules* of chess and the *flow* of a game lives
   in platform-neutral libraries (`net10.0`) that the Linux/Docker server reuses unchanged. The game
   page only talks to an `IGameSession`, so "vs bot", "vs friend on this PC" and "vs someone online"
   are interchangeable implementations.
3. **Content is data.** Bots, puzzles, lessons, openings and achievements are data validated by
   tests, so adding content never needs UI changes.

## Projects

```
src/
  Gambit.Core/              net10.0, no dependencies — the rules of chess + game flow
    Board/                  Bitboard, Square, Piece, Color, Move (16 bit), Position (make/unmake),
                            Attacks (magic bitboards), Zobrist, MoveGenerator (fully legal), Premoves
    Notation/               Fen, San, Uci, Pgn (reader keeps raw movetext; ToTree reads variations)
    Games/                  Game (history, repetition, result), MoveTree (variations), TimeControl, ChessClock
    Rating/                 Glicko2 (puzzle rating and online ratings)
    Sessions/               IGameSession, IMoveProvider, LocalGameSession, PlayerInfo, event args
    Openings/ Puzzles/ Lessons/   embedded data (TSV / CSV / JSON) + catalogs
  Gambit.Engine/            net10.0 — search + evaluation + bots + review
    Evaluation/             Tapered PeSTO evaluation (+ mobility, pawn structure, king safety, mop-up)
    Search/                 Iterative deepening PVS, TT, quiescence + SEE, pruning/reductions, MultiPV
    Bots/                   BotProfile (strength knobs), BotRoster (13 bots), BotMoveProvider
    Review/                 GameReviewer (parallel analysis, move classes, accuracy)
  Gambit.App/               WinUI 3 desktop app (net10.0-windows10.0.26100.0, unpackaged, self-contained)
    Controls/               ChessBoardControl (+ UI Automation peers per square), MoveListView,
                            PlayerBar, EvalBar, EvalGraph, RatingChart
    Pages/                  One page per area (code-behind; CommunityToolkit.Mvvm for settings/models)
    Services/               Settings, Profile, Sound, Puzzles, Achievements, Online, ActiveGameStore
    Theming/                Board themes, piece sets (vector geometry)
  Gambit.Online.Contracts/  DTOs + strongly typed SignalR hub interfaces (protocol version)
  Gambit.Online.Client/     OnlineClient (SignalR) + RemoteGameSession : IGameSession
  Gambit.Server/            ASP.NET Core + SignalR: GameManager (rooms, matchmaking, challenges,
                            rematches, clocks), PlayerRegistry, RatingStore, rate limits
tests/
  Gambit.Tests/             xUnit: perft, notation (incl. PGN variations), rules, engine, premoves,
                            content validation (puzzles; lessons incl. an engine audit of tactic
                            solutions), online end-to-end against an in-process server
tools/                      PuzzleGen, BotArena, OnlineBot, ui.ps1 (UI Automation), screenshots,
                            icon/sound generators
```

Dependency direction: `App → Engine → Core`, `App → Online.Client → Core + Contracts`,
`Server → Core + Contracts`. `Gambit.Core` never references UI, Windows APIs, or the engine.

## The game-session seam

```
                     ┌───────────────────────────────┐
  GamePage           │ IGameSession                  │  Game, White/Black, Clock,
  (board, clocks,  ──►  MovePlayed / GameEnded /      │  TrySubmitMove, Resign, OfferDraw,
   move list)        │  StateReset / DrawOffer… events│  Takeback, RespondToDraw …
                     └──────────┬────────────────────┘
            ┌───────────────────┼───────────────────────────┐
   LocalGameSession       LocalGameSession            RemoteGameSession
   + BotMoveProvider      (pass-and-play: both        SignalR ↔ Gambit.Server GameRoom
   (bot on one side)       sides local)               (server validates with Gambit.Core)
```

* The page never asks "is this a bot game?". It asks the session which side the local user may move
  (`IsLocalSide`), submits moves, and reacts to events. Online-only extras (rating changes,
  rematches) are read from `RemoteGameSession` directly.
* `IMoveProvider` is all a bot needs to implement; the engine's `BotMoveProvider` is one.
* Moves cross the wire as **UCI strings + ply number** (idempotent, easy to validate and resync).
* The server is authoritative: it replays moves through `Gambit.Core.Game`; clients apply moves
  optimistically and resync from the move list if the server disagrees. Server clocks decide flags.
* Premoves are a board/page feature: the board remembers squares, and the page turns them into a
  legal move (`Premoves.Resolve`) when the opponent's move arrives — sessions are unaware of them.
* Ratings use `Glicko2` from Core, so local puzzle ratings and online ratings behave the same.

## Engine notes

* Board: piece bitboards + mailbox, magic bitboards generated at startup with a fixed seed
  (deterministic), incremental Zobrist keys, undo stack.
* Move generation is **fully legal** (pins + check evasion masks); perft-verified.
* Search: iterative deepening, aspiration windows, PVS, transposition table, quiescence with SEE,
  null-move pruning, reverse futility, LMR/LMP, killer + history ordering, check extension, MultiPV.
* Bot strength knobs (`BotProfile`): max depth, node budget, think time, candidate count + softmax
  temperature, eval noise (cp), random-move chance, book depth. `tools/Gambit.BotArena` measures
  the resulting ladder (see `docs/BOT-CALIBRATION.md`).

## Persistence (local, per Windows user)

`%LOCALAPPDATA%\Gambit\` — `settings.json`, `profile.json` (ratings, stats, achievements, lesson and
puzzle progress), `current-game.json` (resume after restart), `games\*.pgn` (archive), `logs\`.
JSON is fine at this size; SQLite with migrations is on the roadmap if it grows.

## UI conventions

* WinUI 3 Fluent controls; theme resources in XAML. Code-built UI uses `Ui.AccentBrush` /
  `Ui.NeutralFill` and leaves text colors to the theme, so light and dark both work.
* Game logic lives in Core/Engine/sessions; pages render state and forward user intent.
* Every interactive element has an automation name (UI tests and screen readers use them).
