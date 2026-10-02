# Bot calibration

The 13 bots are labelled 250 → 2600 in steps of ~200, but the labels were estimates. This page
records what self-play measured and what to do about it.

## Method

`tools/Gambit.BotArena` plays each pair of neighbouring bots against each other: untimed games (each
bot uses its app settings and `ThinkTimeMs`), alternating colours, opening books on, 400-ply cap,
many games in parallel at below-normal priority. Every game is appended to `artifacts/arena.csv`
(local, git-ignored), so runs accumulate. The summary turns each pairing's score into an Elo gap,
`400·log10(s / (1 − s))`, with a 95% margin.

```
dotnet run -c Release --project tools/Gambit.BotArena -- 45 artifacts/arena.csv acorn,bramble,clover,dash,ember,flint,gale,harbor,iris,jade,kestrel,lumen
dotnet run -c Release --project tools/Gambit.BotArena -- 0 artifacts/arena.csv    # summary of what's recorded
```

Monolith ("Max") was left out: at 6 s per move its games are very slow, and it has no number to check.

## Results — 2026-10-02 (first run, ~70 games per step)

| Step | Games | Stronger bot scores | Measured gap (±95%) | Labelled gap | Avg plies |
|---|---|---|---|---|---|
| Acorn → Bramble | 69 | 61% (+23 =38 −8) | +77 ± 84 | 150 | 225 |
| Bramble → Clover | 69 | 73% (+33 =35 −1) | +174 ± 93 | 200 | 243 |
| Clover → Dash | 68 | 68% (+28 =36 −4) | +128 ± 88 | 200 | 245 |
| Dash → Ember | 68 | 96% (+63 =4 −1) | +534 ± 201 | 200 | 103 |
| Ember → Flint | 68 | 92% (+62 =1 −5) | +422 ± 151 | 200 | 108 |
| Flint → Gale | 68 | 85% (+57 =2 −9) | +305 ± 117 | 200 | 124 |
| Gale → Harbor | 68 | 90% (+60 =3 −5) | +390 ± 140 | 200 | 116 |
| Harbor → Iris | 68 | 98% (+65 =3 −0) | +659 ± 281 | 200 | 120 |
| Iris → Jade | 67 | 90% (+57 =6 −4) | +373 ± 136 | 200 | 130 |
| Jade → Kestrel | 66 | 92% (+57 =8 −1) | +435 ± 158 | 200 | 127 |
| Kestrel → Lumen | 62 | 74% (+37 =18 −7) | +183 ± 99 | 200 | 142 |

## What it means

* **The middle of the ladder has cliffs.** From Dash (800) to Kestrel (2200) every step measures
  +300 to +650 bot-Elo instead of +200. A player who has just beaten Dash meets an Ember that wins
  96% of bot games against Dash. The worst steps are Harbor → Iris and Dash → Ember.
* **The low end is flat and drawish.** Acorn → Dash steps are +75 to +175, and half the games are
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
