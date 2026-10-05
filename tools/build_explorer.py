"""Builds the opening explorer's data from real games, with the Python standard library only.

Run:  python tools/build_explorer.py artifacts/rapid-games.tsv src/Gambit.Core/Openings/explorer.tsv

The games file comes from tools/extract_lichess_games.py (a slice of the Lichess database, CC0;
docs/BOT-CALIBRATION.md shows how to get it). Keeps the games where both players are rated
MIN_RATING or more on Lichess, their first MAX_PLY half-moves, and every line played at least
MIN_GAMES times. The output is a tree of moves in pre-order, one line per move:
    depth <TAB> move (SAN) <TAB> white wins <TAB> draws <TAB> black wins
where a line's previous move is the nearest line above it with depth one less. The app replays it
and merges transpositions (OpeningExplorer in Gambit.Core).
"""
import sys
from collections import defaultdict

MIN_RATING = 1400
MAX_PLY = 30
MIN_GAMES = 3
MONTH = "September 2026"


def main(games_path, out_path):
    # node: tuple of SAN moves -> [white wins, draws, black wins]
    counts = defaultdict(lambda: [0, 0, 0])
    games = 0
    with open(games_path, encoding="utf-8") as f:
        for line in f:
            p = line.rstrip("\n").split("\t")
            if len(p) < 7 or min(int(p[1]), int(p[2])) < MIN_RATING:
                continue
            result = {"1-0": 0, "1/2-1/2": 1, "0-1": 2}.get(p[4])
            if result is None:
                continue
            games += 1
            sans = p[6].split()[:MAX_PLY]
            for i in range(len(sans)):
                counts[tuple(sans[:i + 1])][result] += 1

    children = defaultdict(list)
    for node, c in counts.items():
        if sum(c) >= MIN_GAMES:
            children[node[:-1]].append(node)

    lines = [
        f"# Opening explorer: {games:,} rated rapid games from the Lichess database ({MONTH}, CC0), "
        f"both players rated {MIN_RATING}+ on Lichess; the first {MAX_PLY // 2} moves, lines played at least {MIN_GAMES} times.",
        "# depth\tmove (SAN)\twhite wins\tdraws\tblack wins. Pre-order: a line's previous move is the nearest line above with depth - 1.",
        "# Built by tools/build_explorer.py; validated by OpeningExplorerTests.",
    ]

    def walk(node):
        for child in sorted(children[node], key=lambda n: (-sum(counts[n]), n[-1])):
            w, d, b = counts[child]
            lines.append(f"{len(child)}\t{child[-1]}\t{w}\t{d}\t{b}")
            walk(child)

    sys.setrecursionlimit(10000)
    walk(())
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"{games:,} games, {len(lines) - 3:,} moves -> {out_path}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
