# Gambit — Architecture

## Goals that shape the design

1. **Native Windows 11 on Arm64.** WinUI 3 (Windows App SDK) + .NET 10, compiled for `win-arm64`
   (x64 also builds). Fluent controls, Mica backdrop, system accent color, light/dark theme.
2. **Online-ready from day one.** Everything about the *rules* of chess and the *flow* of a game lives
   in platform-neutral libraries (`net10.0`) that a Linux server can reuse unchanged. The UI only talks
   to an `IGameSession`, so "vs bot", "vs friend on this PC" and "vs someone online" are
   interchangeable implementations.
3. **Content is data.** Bots, puzzles, lessons, achievements and themes are data files validated by
   tests, so adding content never needs UI changes.

## Projects

```
src/
  Gambit.Core/              net10.0, no dependencies — the rules of chess + game flow
    Board/                  Bitboard helpers, Square, Piece, Color, Move, Position (make/unmake),
                            Attacks (magic bitboards), Zobrist, MoveGenerator (fully legal)
    Notation/               Fen, San, Uci, Pgn
    Games/                  Game (history, repetition, result), GameResult, TimeControl, Clock
    Rating/                 Glicko2 (shared by puzzle rating and online ratings)
    Sessions/               IGameSession, IMoveProvider, LocalGameSession, PlayerInfo, events
  Gambit.Engine/            net10.0 — search + evaluation + bots
    Evaluation/             Tapered PeSTO-style evaluation (+ pawn structure, mobility, king safety)
    Search/                 Iterative deepening PVS, TT, quiescence, pruning/reductions, MultiPV
    Bots/                   BotProfile (strength knobs), BotRoster (named bots), BotMoveProvider
  Gambit.App/               WinUI 3 desktop app (net10.0-windows10.0.26100.0, unpackaged, self-contained)
    Controls/               ChessBoardControl, MoveListControl, EvalBar, PlayerCard ...
    Pages/ + ViewModels/    One page per NavigationView item (MVVM via CommunityToolkit.Mvvm)
    Services/               Settings, Profile/Stats, Sound, Theme, Navigation
    Theming/                Board themes, piece sets (vector geometry), sounds
  (Session 7) Gambit.Online.Contracts/   DTOs + strongly typed SignalR hub interfaces
  (Session 7) Gambit.Online.Client/      RemoteGameSession : IGameSession (SignalR client)
  (Session 7) Gambit.Server/             ASP.NET Core + SignalR, authoritative game rooms
tests/
  Gambit.Tests/             xUnit: perft, notation round-trips, draw rules, engine tactics,
                            content validation (puzzles/lessons/achievements)
tools/                      Scripts: screenshots for visual checks, puzzle import, sound generation
```

Dependency direction: `App → Engine → Core`, `Server → Core (+ Contracts)`, `Online.Client → Core + Contracts`.
`Gambit.Core` must never reference UI, Windows APIs, or the engine.

## The game-session seam (why online is easy later)

```
                     ┌──────────────────────────┐
  GamePage/ViewModel │ IGameSession             │  State (Game), Players, Clocks,
  (board, clocks,  ──►  MoveMade / GameEnded /   │  SubmitMoveAsync, Resign, OfferDraw,
   move list)        │  ClockChanged events     │  RequestTakeback ...
                     └──────────┬───────────────┘
            ┌───────────────────┼──────────────────────────┐
   LocalGameSession       LocalGameSession            RemoteGameSession (Session 7)
   + BotMoveProvider      (hot-seat: both sides       SignalR ↔ Gambit.Server GameRoom
   (bot on one side)       are local humans)          (server validates with Gambit.Core)
```

* The page never asks "is this a bot game?". It asks the session which side(s) the local user may
  move (`CanMove(color)`), submits moves, and reacts to events.
* `IMoveProvider` is the only thing a bot needs to implement; the engine's `BotMoveProvider` is one.
* Moves cross the wire as **UCI strings + ply number** (idempotent, easy to validate and resync).
* The server is authoritative: it replays moves through `Gambit.Core.Game`; clients apply moves
  optimistically and resync from the move list if the server rejects one.
* Clocks are owned by the session (local) or the server (online); the UI just renders `ClockChanged`.
* Ratings use `Glicko2` from Core, so local puzzle ratings and online ratings behave the same.

## Engine notes

* Board: 12 piece bitboards + mailbox, magic bitboards generated at startup with a fixed seed
  (deterministic), incremental Zobrist keys, undo stack.
* Move generation is **fully legal** (pins + check evasion masks); perft-verified.
* Search: iterative deepening, aspiration windows, PVS, transposition table, quiescence with MVV-LVA,
  null-move pruning, late-move reductions, killer + history ordering, check extension, MultiPV.
* Bot strength knobs (`BotProfile`): max depth, node budget, think time, eval noise (cp),
  MultiPV width + softmax temperature, blunder probability, "misses captures" chance, book depth.

## Persistence (local, per Windows user)

`%LOCALAPPDATA%\Gambit\` — `settings.json`, `profile.json` (ratings, stats, achievements),
`games\*.pgn` (archive). Session 6 moves profile data to SQLite with migrations.

## UI conventions

* WinUI 3 Fluent controls + `CommunityToolkit.WinUI` Settings controls for a Windows 11 Settings feel.
* Theme resources only (no hard-coded colors outside board/piece themes) so light/dark/high-contrast work.
* Pages are thin; logic lives in view models and services; game logic lives in Core/Engine.
