"""Builds Gambit's puzzle collection from the Lichess puzzle database (CC0).

    python tools/import_lichess_puzzles.py artifacts/lichess_db_puzzle.csv.zst src/Gambit.Core/Puzzles/puzzles.csv

Download the database from https://database.lichess.org/#puzzles (lichess_db_puzzle.csv.zst,
~300 MB; Python 3.14+ reads zstd natively). The selection is deterministic for a given input:

* stratified by rating: a target per 100-point band from 400 to 2999, most puzzles where most
  players are (800-2199);
* quality first: well-established ratings, popular and much-played puzzles; looser tiers only
  fill bands the strict tier can't (the very easy and very hard ends);
* variety: at most one puzzle per source game and no two puzzles with the same start position;
* every theme on the Puzzles page gets at least MIN_PER_THEME puzzles (topped up if needed);
* the most common openings (Lichess OpeningTags, family only) get at least MIN_PER_OPENING each,
  for practice by opening.

The output uses Gambit's CSV format (the Lichess one minus a few columns):
id,fen,moves,rating,themes,opening (the opening family tag, e.g. Sicilian_Defense, or empty).
"""
import csv
import random
import re
import sys
from collections import Counter
from compression import zstd

SEED = 2026
MIN_PER_THEME = 120
MIN_PER_OPENING = 80

# The most common opening families among good Lichess puzzles (OpeningTags' first tag).
PRACTICE_OPENINGS = [
    "Sicilian_Defense", "French_Defense", "Queens_Pawn_Game", "Caro-Kann_Defense", "Italian_Game",
    "Scandinavian_Defense", "Queens_Gambit_Declined", "English_Opening", "Ruy_Lopez", "Indian_Defense",
    "Scotch_Game", "Philidor_Defense", "Zukertort_Opening", "Kings_Gambit_Accepted", "Four_Knights_Game",
    "Pirc_Defense", "Modern_Defense", "Vienna_Game", "Russian_Game", "Kings_Pawn_Game", "Bishops_Opening",
    "Slav_Defense", "Kings_Indian_Defense", "Queens_Gambit_Accepted",
]

# Themes offered as practice on the Puzzles page (keep in sync with PuzzlesPage.ThemeGroups).
PRACTICE_THEMES = [
    "mateIn1", "mateIn2", "mateIn3", "mateIn4", "mateIn5", "backRankMate", "smotheredMate",
    "anastasiaMate", "arabianMate", "hookMate", "bodenMate", "doubleBishopMate", "dovetailMate",
    "fork", "pin", "skewer", "discoveredAttack", "doubleCheck", "hangingPiece", "trappedPiece",
    "sacrifice", "deflection", "attraction", "clearance", "interference", "intermezzo",
    "xRayAttack", "capturingDefender", "quietMove", "defensiveMove", "zugzwang", "exposedKing",
    "kingsideAttack", "queensideAttack", "attackingF2F7", "advancedPawn", "promotion",
    "underPromotion", "enPassant", "castling",
    "opening", "middlegame", "endgame", "rookEndgame", "pawnEndgame", "bishopEndgame",
    "knightEndgame", "queenEndgame", "queenRookEndgame",
]


def band_target(band: int) -> int:
    """How many puzzles to take from the 100-point band starting at `band`."""
    if band < 400 or band >= 3000:
        return 0
    if band < 800:
        return {400: 400, 500: 500, 600: 600, 700: 700}[band]
    if band < 2200:
        return 1000
    return {2200: 800, 2300: 700, 2400: 600, 2500: 500, 2600: 400, 2700: 300, 2800: 200, 2900: 100}[band]


def tier(rd: int, popularity: int, plays: int) -> int:
    """1 = best; 0 = rejected."""
    if rd <= 80 and popularity >= 80 and plays >= 500:
        return 1
    if rd <= 90 and popularity >= 70 and plays >= 200:
        return 2
    if rd <= 110 and popularity >= 50 and plays >= 50:
        return 3
    return 0


GAME_ID = re.compile(r"lichess\.org/([A-Za-z0-9]{8})")


def start_key(fen: str, uci: str) -> str:
    """The position after the setup move (pieces + side to move), for duplicate detection."""
    board, side = fen.split()[:2]
    grid = {}
    for r, row in enumerate(board.split("/")):
        f = 0
        for ch in row:
            if ch.isdigit():
                f += int(ch)
            else:
                grid[(f, 8 - r)] = ch
                f += 1
    f1, r1, f2, r2 = ord(uci[0]) - 97, int(uci[1]), ord(uci[2]) - 97, int(uci[3])
    piece = grid.pop((f1, r1))
    if piece in "Pp" and f1 != f2 and (f2, r2) not in grid:  # en passant
        grid.pop((f2, r1), None)
    if piece in "Kk" and abs(f2 - f1) == 2:  # castling moves the rook too
        grid[((f1 + f2) // 2, r1)] = grid.pop((7 if f2 > f1 else 0, r1))
    if len(uci) == 5:
        piece = uci[4].upper() if piece.isupper() else uci[4]
    grid[(f2, r2)] = piece
    return "".join(f"{k[0]}{k[1]}{v}" for k, v in sorted(grid.items())) + ("b" if side == "w" else "w")


class Reservoir:
    """Uniform random sample of a stream (Algorithm R)."""

    def __init__(self, size: int, rng: random.Random):
        self.size, self.rng, self.seen, self.items = size, rng, 0, []

    def add(self, item):
        self.seen += 1
        if len(self.items) < self.size:
            self.items.append(item)
        else:
            j = self.rng.randrange(self.seen)
            if j < self.size:
                self.items[j] = item


def main(source: str, target: str) -> None:
    rng = random.Random(SEED)
    by_band = {}   # (band, tier) -> Reservoir
    by_theme = {}  # (theme, tier) -> Reservoir
    by_opening = {}  # (opening family, tier) -> Reservoir
    practice = set(PRACTICE_THEMES)
    openings = set(PRACTICE_OPENINGS)
    opener = zstd.open if source.endswith(".zst") else open
    total = 0
    with opener(source, "rt", encoding="utf-8", newline="") as f:
        reader = csv.reader(f)
        header = next(reader)
        assert header[:8] == ["PuzzleId", "FEN", "Moves", "Rating", "RatingDeviation", "Popularity", "NbPlays", "Themes"], header
        for row in reader:
            total += 1
            rating = int(row[3])
            band = rating // 100 * 100
            want = band_target(band)
            if want == 0:
                continue
            t = tier(int(row[4]), int(row[5]), int(row[6]))
            if t == 0:
                continue
            m = GAME_ID.search(row[8])
            family = row[9].split()[0] if len(row) > 9 and row[9] else ""
            item = (row[0], row[1], row[2], rating, row[7], m.group(1) if m else row[0], family)
            key = (band, t)
            if key not in by_band:
                by_band[key] = Reservoir(4 * want, rng)
            by_band[key].add(item)
            for theme in row[7].split():
                if theme in practice:
                    tkey = (theme, t)
                    if tkey not in by_theme:
                        by_theme[tkey] = Reservoir(3 * MIN_PER_THEME, rng)
                    by_theme[tkey].add(item)
            if family in openings:
                okey = (family, t)
                if okey not in by_opening:
                    by_opening[okey] = Reservoir(3 * MIN_PER_OPENING, rng)
                by_opening[okey].add(item)

    chosen, games, starts = {}, set(), set()

    def take(item) -> bool:
        pid, fen, moves = item[0], item[1], item[2]
        if pid in chosen or item[5] in games:
            return False
        key = start_key(fen, moves.split()[0])
        if key in starts:
            return False
        chosen[pid] = item
        games.add(item[5])
        starts.add(key)
        return True

    for band in range(400, 3000, 100):
        need = band_target(band)
        for t in (1, 2, 3):
            pool = list(by_band[(band, t)].items) if (band, t) in by_band else []
            rng.shuffle(pool)
            for item in pool:
                if need == 0:
                    break
                if take(item):
                    need -= 1
        if need:
            print(f"band {band}: {need} short")

    theme_counts = Counter(th for item in chosen.values() for th in item[4].split())
    for theme in PRACTICE_THEMES:
        have = theme_counts[theme]
        for t in (1, 2, 3):
            pool = list(by_theme[(theme, t)].items) if (theme, t) in by_theme else []
            rng.shuffle(pool)
            for item in pool:
                if have >= MIN_PER_THEME:
                    break
                if take(item):
                    have += 1
                    theme_counts.update(item[4].split())

    opening_counts = Counter(item[6] for item in chosen.values() if item[6])
    for family in PRACTICE_OPENINGS:
        have = opening_counts[family]
        for t in (1, 2, 3):
            pool = list(by_opening[(family, t)].items) if (family, t) in by_opening else []
            rng.shuffle(pool)
            for item in pool:
                if have >= MIN_PER_OPENING:
                    break
                if take(item):
                    have += 1
                    theme_counts.update(item[4].split())
        opening_counts[family] = have

    puzzles = sorted(chosen.values(), key=lambda p: (p[3], p[0]))
    with open(target, "w", encoding="utf-8", newline="\n") as out:
        out.write("# Puzzles from the Lichess puzzle database (https://database.lichess.org, CC0),\n")
        out.write("# selected by tools/import_lichess_puzzles.py.\n")
        out.write("id,fen,moves,rating,themes,opening\n")
        for pid, fen, moves, rating, themes, _, family in puzzles:
            out.write(f"{pid},{fen},{moves},{rating},{themes},{family}\n")

    print(f"read {total:,} puzzles, wrote {len(puzzles):,} to {target}")
    per_band = Counter(p[3] // 100 * 100 for p in puzzles)
    print("per band:", " ".join(f"{b}:{per_band[b]}" for b in sorted(per_band)))
    print("practice themes:", " ".join(f"{t}={theme_counts[t]}" for t in PRACTICE_THEMES))
    print("practice openings:", " ".join(f"{o}={opening_counts[o]}" for o in PRACTICE_OPENINGS))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
