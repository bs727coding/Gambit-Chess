# Bot calibration

Each bot is labelled with a rating on the **Chess.com rapid** scale. Since revision 4 (2026-10-04)
a label means: *on the same positions, the bot blunders, makes mistakes and inaccuracies about as
often as real players of that rating* — and, checked in games since 2026-10-05, *each bot is a
real step up from the one below it.* This page explains how that is measured and records the
results. The earlier self-play ladder (revisions 1–3) is kept at the end.

## Revision 4: calibrated against real players (2026-10-04)

### Why

Playing Ember (1000), the user saw blunders a 1000 player wouldn't make. The labels were estimates,
and the weak bots played a *uniformly random* legal move now and then (Ember on 5% of its moves,
Acorn on 35%): moves no person would choose, which mostly hang material.

### People: how each rating actually plays

* **Games:** the first 150 MB of the Lichess database for September 2026 (database.lichess.org, CC0;
  an HTTP range request on the 28 GB file): 485,293 games, of which 69,673 rated rapid games that
  ended normally or on time (`tools/extract_lichess_games.py`).
* **Ratings:** Lichess rapid → Chess.com rapid with ChessGoals' survey of players on both sites
  (chessgoals.com/rating-comparison, July 2026 update), e.g. Chess.com 1000 ≈ Lichess 1430 and
  2000 ≈ 2170. Below Chess.com 815 and above 2260 the survey has no rows and the mapping is
  extrapolated (250 ≈ Lichess 850, 2400 ≈ 2540): the least certain bands.
* **Positions:** for each bot's rating, players within ±60 Lichess points of it, against opponents
  within ±150 of them; their moves from move 5 on, made with at least 30 seconds left (scrambles
  say little about a level), at most 8 per game, 1,500 per band.
* **Yardstick:** our engine at a fixed node budget scores the position before and after each move,
  and the move is classed as Game Review does it: a drop in winning chances of 5 points is an
  inaccuracy, 10 a mistake, 20 a blunder. The budget is 250k nodes for bands up to 1400, 800k for
  1600–1800, 2M for 2000 and 5M above, at least what that band's bot searches. (So the columns
  above 1400 aren't strictly comparable across bands; each bot is compared with its own band.)

| Chess.com | Lichess | Blunders | Mistakes | Inaccuracies | Best move | Accuracy | Avg loss (cp) |
|---|---|---|---|---|---|---|---|
| 250 | 851 | 7.6% | 9.2% | 11.6% | 49% | 83.6 | 128 |
| 400 | 967 | 7.0% | 9.3% | 12.1% | 49% | 84.0 | 118 |
| 600 | 1123 | 5.6% | 8.0% | 12.0% | 51% | 85.7 | 96 |
| 800 | 1278 | 4.8% | 8.1% | 11.5% | 48% | 85.8 | 81 |
| 1000 | 1429 | 4.1% | 7.1% | 11.6% | 48% | 86.8 | 77 |
| 1200 | 1575 | 2.9% | 5.5% | 11.2% | 53% | 88.3 | 60 |
| 1400 | 1725 | 3.3% | 7.3% | 11.9% | 50% | 87.1 | 66 |
| 1600 | 1865 | 2.8% | 5.0% | 10.7% | 53% | 88.8 | 58 |
| 1800 | 2015 | 1.7% | 6.1% | 10.7% | 52% | 88.8 | 52 |
| 2000 | 2169 | 1.9% | 3.9% | 10.3% | 56% | 90.0 | 42 |
| 2200 | 2341 | 0.8% | 2.9% | 8.0% | 58% | 91.9 | 38 |

Percentages are of moves (sampling error about ±0.4–0.7 points). A 1000-rated player blunders on
about one move in 25, 1–2 times a game. People find the engine's best move about half the time at
every level; what changes with rating is the size of the mistakes when they don't. The 2400 band
(only ~350 positions, the costliest to score) isn't finished yet.

### The bots, before

The same positions, each bot choosing its own move, scored the same way (Harbor to Kestrel on
smaller samples, 300–800 positions):

| Bot | Rating | Blunders (people → bot) | Mistakes | Inaccuracies | Accuracy |
|---|---|---|---|---|---|
| Acorn | 250 | 7.6% → **23.1%** | 9.2% → 15.7% | 11.6% → 16.1% | 83.6 → 67.4 |
| Bramble | 400 | 7.0% → **23.0%** | 9.3% → 16.5% | 12.1% → 15.5% | 84.0 → 67.2 |
| Clover | 600 | 5.6% → **15.5%** | 8.0% → 13.9% | 12.0% → 18.3% | 85.7 → 73.7 |
| Dash | 800 | 4.8% → **13.7%** | 8.1% → 15.1% | 11.5% → 18.1% | 85.8 → 74.5 |
| Ember | 1000 | 4.1% → 5.1% | 7.1% → 8.6% | 11.6% → 18.1% | 86.8 → 83.7 |
| Flint | 1200 | 2.9% → 3.0% | 5.5% → 6.5% | 11.2% → 14.8% | 88.3 → 86.6 |
| Gale | 1400 | 3.3% → 1.5% | 7.3% → 3.5% | 11.9% → 10.9% | 87.1 → 90.0 |
| Harbor | 1600 | 2.8% → 0.4% | 4.9% → 3.5% | 9.0% → 6.4% | 89.2 → **92.1** |
| Iris | 1800 | 1.2% → 0.2% | 6.4% → 1.6% | 10.9% → 4.8% | 88.9 → **94.0** |
| Jade | 2000 | 3.0% → 0.2% | 3.5% → 0.8% | 9.8% → 1.2% | 89.6 → **95.5** |
| Kestrel | 2200 | 0.3% → 0.0% | 3.3% → 1.0% | 10.0% → 2.0% | 91.6 → **95.9** |

The four weakest bots blundered about three times as often as people at their rating, mostly
through the random moves. Ember's blunder count was only a little high, but they were random moves
no person would play. From Harbor up the bots were far *stronger* than their labels: they rarely
erred at all.

### What changed

* **Oversights instead of random moves.** With probability `OversightChance` the bot picks its move
  by how the board looks right after it (static evaluation, plus its noise and style), without
  looking at the opponent's replies, as a person who doesn't check what the opponent can do next.
  It may leave a piece hanging or grab a defended pawn with its queen, but it never plays an
  aimless move. On real positions about a third of such moves are blunders, so a few percent of
  oversights supply the blunders people make.
* **Fitted, not guessed.** `tools/Gambit.BotCalibration fit` plays a bot's band with plain moves and
  with oversights at several temperatures (noise = temperature), works out the share of oversights
  that best matches the people at each temperature, and checks the best pair with a real run.
* **Checked on common positions.** People at lower ratings reach messier positions, so a bot fitted
  on its own band can look stronger on calmer ones. Every bot was therefore also played on one
  shared set of positions, where each must be stronger than the one below it. Two steps came out
  flat and were nudged within the people data's noise: Dash (more oversights, warmer) and Flint
  (warmer). Games later undid Dash's nudge and took Flint further (below). Iris and Jade consider five candidate moves instead of 3 and 2: with fewer, a
  temperature can't produce the small inaccuracies people make, and the fit used too many
  oversights instead.

The settings now (Dash, Ember, Flint, Iris and Jade as retuned in games on 2026-10-05, below; their
first fits were temperature 25 / 8%, 35 / 7%, 30 / 5.5%, 60 / 2.5% and 40, noise 60 / 4%):

| Bot | Rating | Depth | Nodes | Candidates | Temperature | Noise | Oversights |
|---|---|---|---|---|---|---|---|
| Acorn | 250 | 1 | – | all | 35 | 35 | 15.75% |
| Bramble | 400 | 1 | – | all | 20 | 20 | 10.75% |
| Clover | 600 | 2 | – | all | 35 | 35 | 7.5% |
| Dash | 800 | 2 | – | all | 20 | 20 | 7.5% |
| Ember | 1000 | 3 | 40k | 12 | 25 | 25 | 8.25% |
| Flint | 1200 | 4 | 80k | 8 | 40 | 40 | 5.5% |
| Gale | 1400 | 5 | 150k | 6 | 55 | 55 | 3.75% |
| Harbor | 1600 | 6 | 300k | 4 | 35 | 35 | 5.75% |
| Iris | 1800 | 8 | 700k | 5 | 35 | 35 | 2.5% |
| Jade | 2000 | 10 | 1.5M | 5 | 20 | 25 | 2.5% |
| Kestrel | 2200 | 14 | 4M | 5 | 30 | 45 | 1% |
| Lumen | 2400 | – | 5M | 5 | 30 | 30 | – |

### The bots, after

On their own bands (Acorn to Gale and Kestrel 1,500 positions each; Harbor 800, Iris 800, Jade 600,
Lumen 352), with the settings above:

| Bot | Rating | Blunders (people → bot) | Mistakes | Inaccuracies | Accuracy | Same move as the person |
|---|---|---|---|---|---|---|
| Acorn | 250 | 7.6% → 8.3% | 9.2% → 9.7% | 11.6% → 13.0% | 83.6 → 82.5 | 23% (was 9%) |
| Bramble | 400 | 7.0% → 7.0% | 9.3% → 7.7% | 12.1% → 12.3% | 84.0 → 84.1 | 30% (was 12%) |
| Clover | 600 | 5.6% → 4.7% | 8.0% → 8.0% | 12.0% → 15.2% | 85.7 → 85.1 | 27% (was 15%) |
| Dash | 800 | 4.8% → 4.8% | 8.1% → 6.7% | 11.5% → 14.2% | 85.8 → 85.6 | 29% (was 18%) |
| Ember | 1000 | 4.1% → 4.6% | 7.1% → 4.8% | 11.6% → 12.0% | 86.8 → 86.8 | 28% (was 22%) |
| Flint | 1200 | 2.9% → 3.0% | 5.5% → 5.5% | 11.2% → 13.1% | 88.3 → 87.6 | 29% (was 27%) |
| Gale | 1400 | 3.3% → 3.4% | 7.3% → 5.4% | 11.9% → 13.9% | 87.1 → 87.0 | 26% (was 31%) |
| Harbor | 1600 | 2.8% → 3.5% | 4.9% → 4.8% | 9.0% → 10.1% | 89.2 → 88.6 | |
| Iris | 1800 | 1.2% → 1.2% | 6.4% → 4.5% | 10.9% → 8.5% | 88.9 → **90.2** | 29% |
| Jade | 2000 | 2.3% → 2.0% | 3.7% → 1.7% | 9.8% → 8.2% | 89.8 → **91.5** | 38% |
| Kestrel | 2200 | 0.8% → 0.4% | 2.9% → 1.6% | 8.0% → 7.5% | 91.9 → 92.4 | 34% |
| Lumen | 2400 | 0.9% → 0.0% | 1.7% → 0.6% | 6.0% → 4.8% | 92.5 → **94.2** | 38% |

Acorn to Harbor and Kestrel are within about one point of accuracy of real players at their rating,
and the weak bots now play the move a person chose far more often. Iris, Jade and Lumen are 1.3–1.7
points more accurate than people at theirs, but blunder no more often: that is the price of a
ladder whose steps are real in games (below). On shared positions accuracy rises with the label
(first fits, before the retune): on the 1000 band's positions Acorn 79.3, Bramble 83.6, Clover
83.7, Dash 85.0, Ember 86.0, Flint 88.1, Gale 88.0 (average loss 115, 92, 82, 76, 74, 61, 57 cp);
on the 2000 band's Gale 85.5, Harbor 87.1, Iris 88.0, Jade 89.5. Neighbours whose numbers are this
close (Bramble and Clover, Flint and Gale) are within the measurement noise of about ±0.5.

### Kestrel and Lumen (2026-10-05)

With one candidate move a temperature does nothing, so both now weigh five. Kestrel was fitted on
the 2200 band: temperature 30, noise 45, 1% oversights, and plays 92.4 accuracy there against the
people's 91.9 (it was 95.9). The 2400 band has only 352 positions (the costliest to score), too few
to fit on, so Lumen follows the trend from 2000 to 2200: temperature 30, no oversights, capped at
5M nodes so its strength doesn't depend on the PC. Both now search to their node budgets rather
than the clock: their time caps went from 1.8 s and 3 s a move (which a typical PC reached first,
at ~3M nodes/s) to 3 s and 4 s.

### In games: the arena (2026-10-05)

`tools/Gambit.BotArena` plays neighbouring bots against each other (method at the end). The first
revision-4 run gave each step only 45 games, because the slow top pairs set the pace for all, and
its ±100 margins misled: Dash → Ember (+23) and Harbor → Iris (−8) looked flat. Playing the fast
pairs on their own (thousands of games in minutes) and trying variants of a bot with
`id.Property=value` overrides showed the real picture:

* The low end was fine: Dash → Ember +132 ± 9 over 6,883 games, Ember → Flint +227 ± 10.
* The middle was squeezed: Harbor to Jade spanned only ~+155 in all, whatever Iris's settings
  (baseline Harbor → Iris +86 ± 44, Iris → Jade +67 ± 44 over 252 games each).
* **In games, temperature is what counts.** A deeper search did nothing measurable (Iris at depth 10
  instead of 8, Jade at 12 instead of 10), while a cooler bot gained 100–250. Per move the same
  change barely shows, because people at 1600, 1800 and 2000 differ little per move (88.8, 88.8,
  90.0 accuracy) and a lot in games.

| Change | Games per step | Effect |
|---|---|---|
| Ember: temperature 35 → 25, oversights 7% → 8.25% | 7,293 | Dash → Ember +132 → +155, Ember → Flint +227 → +199 |
| Dash: temperature 25 → 20, oversights 8% → 7.5% (its best fit, before the nudge) | 872 | Clover → Dash +42 → +101, Dash → Ember +162 → +116 |
| Flint: temperature 30 → 40 | 563 | Ember → Flint +177 → +119, Flint → Gale +56 → +108 |
| Gale: temperature 55 → 35 with 7% oversights (to keep its blunder rate) | 872 | no gain: the extra oversights cancel the cooler choice |
| Iris: temperature and noise 60 → 35 | 186 | Harbor → Iris +86 → +201 ± 59 |
| Jade: temperature 40 → 20, noise 60 → 25, oversights 4% → 2.5% | 339 | Iris (new) → Jade −40 → +169 ± 41 |
| Iris at depth 10 / Jade at depth 12 (same temperatures) | 78 / 165 | no change within the margins |

Per move, Dash, Ember and Flint match their people as well as or better than before (85.6, 86.8
and 87.6 accuracy against 85.8, 86.8 and 88.3); Iris and Jade now find the best move a little more
often than people at their rating, but blunder about as often (table above). The arena now lets bots with a node or depth limit search to it, without their time cap
(ten games run at once, so the clock would cut Kestrel and Lumen short).

The whole ladder with these settings (each segment played on its own: the fast low end, the middle,
then the slow top; bot-vs-bot gaps, not human rating gaps):

| Step | Games | Stronger bot scores | Measured gap (±95%) | Labelled gap | Avg plies |
|---|---|---|---|---|---|
| Acorn → Bramble | 2061 | 72% (+1409 =144 −508) | +163 ± 17 | 150 | 80 |
| Bramble → Clover | 2061 | 71% (+1388 =135 −538) | +152 ± 16 | 200 | 86 |
| Clover → Dash | 546 | 63% (+335 =15 −196) | +90 ± 30 | 200 | 90 |
| Dash → Ember | 546 | 67% (+364 =8 −174) | +126 ± 31 | 200 | 87 |
| Ember → Flint | 546 | 67% (+359 =9 −178) | +120 ± 31 | 200 | 90 |
| Flint → Gale | 546 | 66% (+356 =11 −179) | +117 ± 31 | 200 | 99 |
| Gale → Harbor | 2061 | 70% (+1438 =28 −595) | +151 ± 16 | 200 | 93 |
| Harbor → Iris | 267 | 75% (+192 =14 −61) | +187 ± 48 | 200 | 101 |
| Iris → Jade | 267 | 79% (+207 =7 −53) | +228 ± 51 | 200 | 107 |
| Jade → Kestrel | 115 | 68% (+73 =11 −31) | +133 ± 68 | 200 | 121 |
| Kestrel → Lumen | 115 | 74% (+76 =18 −21) | +181 ± 72 | 200 | 132 |

Every step is a real one: the stronger bot scores 63–79% and no margin reaches zero. Bot-vs-bot
gaps don't convert directly into human rating points, so they aren't expected to equal the labels'
200. Chained and anchored at Gale = 1400: Acorn 632, Bramble 794, Clover 947, Dash 1037, Ember 1163, Flint 1283, Gale 1400, Harbor 1551, Iris 1737, Jade 1966, Kestrel 2099, Lumen 2280.

### Caveats

* The rating conversion is the biggest uncertainty, most of all at the extrapolated ends.
* The yardstick is our own engine at a limited budget. For the strongest bots it is barely
  stronger than the bot, so it sees the errors the bot's noise and oversights add, not its search's.
* The labels come from positions; the arena only checks that each step is real. Bot-vs-bot gaps
  aren't human rating gaps (a bot punishes another bot's errors more consistently than people do).
  Bots still convert won endgames perfectly (revision 2), unlike real beginners.
* Moves made with less than 30 seconds left were left out, so time-scramble blunders aren't in the
  targets.

### Next steps

* More positions per band (3,000) would halve the noise that blurs neighbouring bots, and the
  2400 band (352 positions) needs more before Lumen can be fitted rather than extrapolated.
* Kestrel's and Lumen's time caps bind on PCs slower than ~2M nodes/s per core (they then play a
  little weaker); a quick speed check at startup could scale them.

### How to repeat it

```
curl -r 0-157286399 -o artifacts/lichess-2026-09-slice.pgn.zst https://database.lichess.org/standard/lichess_db_standard_rated_2026-09.pgn.zst
python tools/extract_lichess_games.py artifacts/lichess-2026-09-slice.pgn.zst artifacts/rapid-games.tsv
dotnet run -c Release --project tools/Gambit.BotCalibration -- humans artifacts/rapid-games.tsv artifacts/calibration 1500
dotnet run -c Release --project tools/Gambit.BotCalibration -- bots artifacts/calibration all
dotnet run -c Release --project tools/Gambit.BotCalibration -- bots artifacts/calibration all band=1000
dotnet run -c Release --project tools/Gambit.BotCalibration -- fit artifacts/calibration ember temps=10,20,35,55
```

Scores are cached in `artifacts/calibration/analysis-*.tsv`, so repeated runs only analyse new
moves. Long runs take hours at the top bands. Each run writes its own cache file, so several can
run side by side.

## Earlier: the self-play ladder (revisions 1–3)

Until revision 4 the labels were estimates, checked only by playing neighbouring bots against
each other. That shows whether each step is a real step, not what rating it corresponds to.

### Method

`tools/Gambit.BotArena` plays each pair of neighbouring bots against each other: untimed games
(each bot uses its app settings; since 2026-10-05 a bot with a node or depth limit searches to it
without its `ThinkTimeMs`), alternating colours, opening books on, 400-ply cap,
many games in parallel at below-normal priority. Every game is appended to `artifacts/arena.csv`
(local, git-ignored), so runs accumulate. Each row records a fingerprint of both bots' strength
settings, and only games played with the current settings count — after tuning a bot, its old
games drop out of the summary automatically. The summary turns each pairing's score into an Elo
gap, `400·log10(s / (1 − s))`, with a 95% margin.

```
dotnet run -c Release --project tools/Gambit.BotArena -- 45 artifacts/arena.csv acorn,bramble,clover,dash,ember,flint,gale,harbor,iris,jade,kestrel,lumen
dotnet run -c Release --project tools/Gambit.BotArena -- 0 artifacts/arena.csv    # summary of what's recorded
dotnet run -c Release --project tools/Gambit.BotArena -- 5 artifacts/variants.csv harbor,iris,jade iris.Temperature=35
```

Games are spread evenly over the pairs, so the slowest pair sets the pace: play the fast low end,
the middle and the slow top (Jade, Kestrel, Lumen) as separate runs. Overrides try a variant
without editing the roster; its games are recorded under the variant's fingerprint.

Monolith ("Max") was left out: at 6 s per move its games are very slow, and it has no number to check.

### Results — 2026-10-02, revision 1 (45 minutes, ~84 games per step)

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

### Revision 2: bots convert won endgames (2026-10-02)

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

### Revision 3: play styles (2026-10-02)

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

### Engine speed-up (2026-10-03, revision unchanged)

The search got ~1.2x faster with an identical search tree (the bench signature in
`BenchmarkTests` is unchanged). Bots bound by depth or nodes (Acorn to Iris) play exactly as before.
Jade and up are mostly bound by time, so they now search ~1.2x more nodes per move: very roughly
+15 Elo each, inside the margins above. Not re-measured.

### What it means

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

### Next steps (2026-10-02, superseded by revision 4)

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
