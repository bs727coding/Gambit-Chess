"""Extracts rated rapid games from a Lichess game database file (or a slice of one) for bot calibration.

    python tools/extract_lichess_games.py artifacts/lichess-2026-09-slice.pgn.zst artifacts/rapid-games.tsv

The monthly files at https://database.lichess.org/ (CC0) are ~30 GB; the first 150 MB (an HTTP range
request) hold about a million games, plenty for calibration. A truncated file is fine: reading
stops at the last complete game. Python 3.14+ reads zstd natively.

Output: one game per line, tab-separated:
id, white rating, black rating, time control, result, termination, moves (SAN, space-separated),
clocks (seconds left after each move, space-separated; empty when the game has none).
Kept: rated rapid games that ended normally or on time, with both ratings and at least 20 plies.
"""
import re
import sys
from compression import zstd

CLOCK = re.compile(r"\[%clk (\d+):(\d+):(\d+)\]")
COMMENT = re.compile(r"\{[^}]*\}")
MOVE_NUMBER = re.compile(r"^\d+\.+$")
RESULTS = {"1-0", "0-1", "1/2-1/2", "*"}


def games(path):
    """Yields (headers, movetext) for each complete game in the file."""
    headers, moves = {}, []
    with zstd.open(path, "rt", encoding="utf-8", errors="replace") as f:
        try:
            for line in f:
                line = line.rstrip("\n")
                if line.startswith("["):
                    if moves:  # a new game starts
                        yield headers, " ".join(moves)
                        headers, moves = {}, []
                    key, _, value = line[1:-1].partition(" ")
                    headers[key] = value.strip('"')
                elif line:
                    moves.append(line)
        except EOFError:
            return  # a slice of the file ends mid-game: drop that game


def main(source, target):
    kept = seen = 0
    with open(target, "w", encoding="utf-8", newline="\n") as out:
        for headers, text in games(source):
            seen += 1
            if headers.get("Event", "") != "Rated Rapid game":
                continue
            if headers.get("Termination") not in ("Normal", "Time forfeit"):
                continue
            try:
                white, black = int(headers["WhiteElo"]), int(headers["BlackElo"])
            except (KeyError, ValueError):
                continue
            clocks = [int(h) * 3600 + int(m) * 60 + int(s) for h, m, s in CLOCK.findall(text)]
            sans = [t.rstrip("?!") for t in COMMENT.sub(" ", text).split()
                    if not MOVE_NUMBER.match(t) and t not in RESULTS]
            if len(sans) < 20:
                continue
            if len(clocks) != len(sans):
                clocks = []
            game_id = headers.get("Site", "").rsplit("/", 1)[-1]
            out.write("\t".join([game_id, str(white), str(black), headers.get("TimeControl", ""),
                                 headers.get("Result", ""), headers["Termination"], " ".join(sans),
                                 " ".join(map(str, clocks))]) + "\n")
            kept += 1
            if kept % 20000 == 0:
                print(f"{kept} rapid games kept of {seen} read", file=sys.stderr)
    print(f"done: {kept} rapid games kept of {seen} read -> {target}", file=sys.stderr)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
