# Bot calibration

The 13 bots are labelled 250 → 2600 in steps of ~200, but the labels were estimates. This page
records what self-play measured and what to do about it.

## Method

`tools/Gambit.BotArena` plays each pair of neighbouring bots against each other: untimed games (each
bot uses its app settings and `ThinkTimeMs`), alternating colours, opening books on, 400-ply cap,
many games in parallel at below-normal priority. Every game is appended to `artifacts/arena.csv`
(local, git-ignored), so runs accumulate. Each row records a fingerprint of both bots' strength
settings, and only games played with the current settings count — after tuning a bot, its old
games drop out of the summary automatically. The summary turns each pairing's score into an Elo
gap, `400·log10(s / (1 − s))`, with a 95% margin.

```
dotnet run -c Release --project tools/Gambit.BotArena -- 45 artifacts/arena.csv acorn,bramble,clover,dash,ember,flint,gale,harbor,iris,jade,kestrel,lumen
dotnet run -c Release --project tools/Gambit.BotArena -- 0 artifacts/arena.csv    # summary of what's recorded
```

Monolith ("Max") was left out: at 6 s per move its games are very slow, and it has no number to check.

## Results — 2026-10-02, revision 1 (45 minutes, ~84 games per step)

| Step | Games | Stronger bot scores | Measured gap (±95%) | Labelled gap | Avg plies |
|---|---|---|---|---|---|
| Acorn → Bramble | 84 | 63% (+31 =44 −9) | +93 ± 77 | 150 | 217 |
| Bramble → Clover | 84 | 73% (+41 =41 −2) | +175 ± 84 | 200 | 234 |
| Clover → Dash | 84 | 68% (+35 =44 −5) | +130 ± 80 | 200 | 244 |
| Dash → Ember | 84 | 96% (+78 =5 −1) | +545 ± 186 | 200 | 113 |
| Ember → Flint | 84 | 92% (+77 =1 −6) | +431 ± 139 | 200 | 114 |
| Flint → Gale | 84 | 82% (+68 =2 −14) | +265 ± 97 | 200 | 119 |
| Gale → Harbor | 84 | 92% (+76 =3 −5) | +431 ± 139 | 200 | 114 |
| Harbor → Iris | 84 | 98% (+80 =4 −0) | +645 ± 244 | 200 | 120 |
| Iris → Jade | 83 | 89% (+70 =7 −6) | +355 ± 117 | 200 | 128 |
| Jade → Kestrel | 83 | 92% (+71 =11 −1) | +428 ± 139 | 200 | 126 |
| Kestrel → Lumen | 83 | 70% (+44 =28 −11) | +146 ± 81 | 200 | 141 |

Chained into a ladder anchored at Gale = 1400 (bot-vs-bot scale, not human Elo): Acorn −238,
Bramble −145, Clover 30, Dash 160, Ember 704, Flint 1135, Gale 1400, Harbor 1831, Iris 2476,
Jade 2831, Kestrel 3259, Lumen 3406.

## Revision 2: bots convert won endgames (2026-10-02)

`BotMoveProvider` now plays its best move, without random moves or noise, when it has found a mate
or is at least +500 in an endgame (no queens, or at most four pieces besides kings and pawns).
Mistakes stay in the opening and middlegame. Ten minutes on the six weakest bots, ~1,000 games per
step:

| Step | Games | Stronger bot scores | Measured gap (±95%) | Before | Avg plies (before) |
|---|---|---|---|---|---|
| Acorn → Bramble | 1018 | 78% (+780 =27 −211) | +219 ± 26 | +93 | 89 (217) |
| Bramble → Clover | 1018 | 88% (+891 =7 −120) | +344 ± 33 | +175 | 81 (234) |
| Clover → Dash | 1017 | 84% (+853 =0 −164) | +286 ± 29 | +130 | 82 (244) |
| Dash → Ember | 1017 | 95% (+970 =0 −47) | +526 ± 51 | +545 | 68 (113) |
| Ember → Flint | 1017 | 91% (+918 =6 −93) | +393 ± 37 | +431 | 81 (114) |

Draws at the low end fell from about half the games to almost none, games are a third as long,
and the low steps are now real steps.

The rest of the ladder under revision 2 (40 minutes; the strong bots are slow, so only ~48 games
per step and wide margins):

| Step | Games | Stronger bot scores | Measured gap (±95%) | Revision 1 |
|---|---|---|---|---|
| Flint → Gale | 48 | 89% (+42 =1 −5) | +355 ± 154 | +265 |
| Gale → Harbor | 48 | 94% (+45 =0 −3) | +470 ± 203 | +431 |
| Harbor → Iris | 48 | 95% (+44 =3 −1) | +504 ± 221 | +645 |
| Iris → Jade | 48 | 89% (+38 =9 −1) | +355 ± 154 | +355 |
| Jade → Kestrel | 48 | 94% (+44 =2 −2) | +470 ± 203 | +428 |
| Kestrel → Lumen | 47 | 77% (+28 =16 −3) | +206 ± 117 | +146 |

As expected the endgame change leaves the middle cliffs in place (every step from Dash to Kestrel
is still +350…+530): that needs the systematic approach below.

Caveat: since revision 2 the whole ladder is fairly *uniform* in bot-vs-bot Elo (+220…+530 per
step), and bot-vs-bot gaps overstate gaps against people. Before a big retune it's worth looking at
real results: the profile already keeps per-bot win/loss records, so a player's record against
neighbouring bots shows whether a step feels like a cliff.

## Revision 3: play styles (2026-10-02)

Bots that sample among candidate moves now add a style bonus first (`BotStyles`): Aggressive
bots like checks, captures and king pressure; Solid bots castle, trade evenly and keep their pawn
shelter; Tricky bots create threats against loose or more valuable pieces. Ten minutes on the
styled bots, ~1,030 games per step, shows the ladder's shape unchanged within the margins:

| Step | Games | Stronger bot scores | Measured gap (±95%) | Revision 2 |
|---|---|---|---|---|
| Dash → Ember | 1029 | 95% (+978 =1 −50) | +515 ± 49 | +526 |
| Ember → Flint | 1029 | 89% (+916 =5 −108) | +368 ± 34 | +393 |
| Flint → Gale | 1028 | 90% (+913 =15 −100) | +373 ± 35 | +355 |
| Gale → Harbor | 1028 | 87% (+880 =30 −118) | +331 ± 32 | +470 |
| Harbor → Iris | 1028 | 95% (+959 =41 −28) | +522 ± 50 | +504 |

(Depth-limited bots move in tens of milliseconds, so these pairs get many games quickly; the
time-limited top bots — Jade and up — are what make full-ladder runs slow.)

## What it means

* **The middle of the ladder has cliffs.** From Dash (800) to Kestrel (2200) every step measures
  +265 to +645 bot-Elo instead of +200. A player who has just beaten Dash meets an Ember that wins
  96% of bot games against Dash. The worst steps are Harbor → Iris and Dash → Ember.
* **The low end is flat and drawish.** Acorn → Dash steps are +90 to +175, and half the games are
  draws that run 220+ plies: the weakest bots can't convert won endgames (random moves and depth 1–2
  never find the mate), so games end by the 50-move rule or repetition.
* Self-play gaps between bots are usually *larger* than the same bots' gaps against humans
  (a stronger bot punishes a weaker bot's systematic errors every time), so the absolute numbers
  shouldn't be read as human Elo. The *shape* is what matters: steps should be roughly even.
* Likely causes: random-move chance (one random move every ~100 moves is a lost piece against a
  bot that sees it), the jump from depth 2 to 3 (Dash → Ember), and node budgets doubling per step.

## Next steps

Nudging one bot only moves its cliff to the neighbouring step (the middle as a whole is too steep),
so a systematic approach will be faster than hand-tuning:

* Replace the uniform random move with a *plausible* mistake (a random pick among the candidate
  lines, or a move losing at most N centipawns). Uniformly random moves are what a stronger bot
  punishes every time; they make the steps cliff-like.
* Drive the knobs from one skill number per bot (interpolated depth, nodes, noise, temperature,
  mistake rate), measure skill → Elo against two or three fixed reference bots with the arena, and
  pick skills that give even steps.

Hand-tuning, if preferred:

1. Tune the steep steps first (Harbor → Iris, Dash → Ember, Jade → Kestrel, Ember → Flint): make
   neighbours closer, e.g. give the upper bot a little random-move chance or more eval noise, or
   lift the lower bot's depth/node budget. Re-run the arena on just the affected bots
   (`... -- 15 artifacts/arena-tuning.csv harbor,iris`) after each change; aim for even steps of
   ~200–250 bot-Elo with margins under ±100 (≈150 games per pair).
2. ~~Help the weakest bots convert won endgames~~ — done in revision 2 (see above).
3. Once the ladder is even, relabel ratings from the measured ladder anchored at a mid-ladder bot,
   and keep the arena CSV as the record.
