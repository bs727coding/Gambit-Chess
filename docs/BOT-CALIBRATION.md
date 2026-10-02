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

## Results — 2026-10-02 (first run, 45 minutes, ~84 games per step)

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

1. Tune the steep steps first (Harbor → Iris, Dash → Ember, Jade → Kestrel, Ember → Flint): make
   neighbours closer, e.g. give the upper bot a little random-move chance or more eval noise, or
   lift the lower bot's depth/node budget. Re-run the arena on just the affected bots
   (`... -- 15 artifacts/arena-tuning.csv harbor,iris`) after each change; aim for even steps of
   ~200–250 bot-Elo with margins under ±100 (≈150 games per pair).
2. Help the weakest bots convert: e.g. switch off random moves and noise once the bot is a rook
   or more ahead in an endgame, or always play a found mate. Fewer 200-ply draws.
3. Once the ladder is even, relabel ratings from the measured ladder anchored at a mid-ladder bot,
   and keep the arena CSV as the record.
