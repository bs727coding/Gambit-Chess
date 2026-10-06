# Changelog

The version is set in `Directory.Build.props`; [docs/RELEASING.md](docs/RELEASING.md) explains how a
release goes out.

## Unreleased

**A cleaner look**
* Inter for all text (bundled, SIL Open Font License), and the accent color follows the board
  theme: buttons, selections and the clock turn green with Tournament green, purple with Amethyst,
  and pick up your own colors with Custom.
* The menu is an icon rail that leaves more room for the board; rest the mouse on it for the
  labels (or use the button at the top).
* Play: tabs for Computer, Pass and play and Online, so each comes first instead of below the
  bots. Bots are grouped by level (Beginner, Club player, Expert, Full strength) beside a compact
  card with the side, time control and takebacks. Pass and play has its own settings before the
  game starts: names, time control, turning the board, takebacks.
* Home: your game in progress (or the suggested next bot) and the daily puzzle, each on a small
  board, then shortcuts to puzzles, lessons and analysis, your progress and recent games.
* Game and analysis boards: the move buttons sit in a quiet toolbar in one card with the game's
  actions; a smaller opponent card; clock digits keep their width as the time runs.
* Settings in sections: Appearance, Board, Sound, Online, Your data and About, with a button to
  hear the sound pack, the online server and account, and credits.
* Your avatar on the Profile page wears the accent color.
* Fixed: Home called a bot game "Online game" until its first move.

**Online**
* When this copy and the server run different versions of Gambit, the Online page says so right
  under the server address ("Update required", or that the server needs an update) and offers the
  update; it also says so if the server is updated while you're connected. Connection and sign-in
  problems now show there too, instead of at the bottom of the page.
* The server's message to an older copy names the fix (Settings → Updates), and `/api/info` reports
  the server's version.

## 0.3.0 — 2026-10-05

Friends, chat and an opening explorer; bots that err like real players; your own board colors.
The online protocol changed (version 3): update the server and the apps together.

**Bots**
* Every bot from Acorn (250) to Lumen (2400) now makes mistakes like real players of its rating
  (Chess.com rapid scale): on the same positions, a bot blunders, makes mistakes and inaccuracies
  about as often as people rated like it (measured on 70,000 Lichess rapid games). The weakest bots
  used to blunder three times as often as people at their level; Harbor and up used to play far
  above their labels.
* Each bot is a real step up from the one below it in games (checked with thousands of bot games):
  Dash, Ember, Flint, Iris and Jade were retuned so no two neighbours play alike.
* Kestrel and Lumen think a little longer (up to 3 and 4 seconds a move), so they play at their
  calibrated strength on a typical PC.
* No more random moves: a bot's blunders are now oversights, a natural-looking move that misses
  what the opponent can do next, like a person's.

**Play**
* Opening explorer: the analysis board's Explorer tab lists the moves real players chose in the
  position (40,000 Lichess rapid games, players rated 1400+), how often, and how those games
  ended; click one to play it.
* Play from any position: "Play from here" on the analysis board, against a bot or pass and play
  (practice games: saved, not counted).
* Pass and play asks for the players' names and can turn the board after each move.

**Online** (server and app need updating together: protocol 3)
* Friends: add by username, see who's online or playing, challenge them directly.
* Chat with your opponent during and after online games (Settings can hide it).
* Your online games from other PCs appear in your game list.
* A warning before signing in to a server over plain http:// on the internet.

**Look and sound**
* A custom board theme: pick the light and dark square colors yourself.
* A choice of highlight color for the last move and the selected piece.
* Sound packs: Classic, Soft (felt taps, gentle bells) and Retro (8-bit blips).

**Other**
* Settings > Report a problem saves a zip of recent logs to send to whoever runs your server.

## 0.2.0 — 2026-10-03

The first version with installers.

**Install and update**
* Installers for Windows on Arm and for Intel/AMD PCs, and a download page on your server
  (`/download`).
* Installed copies update themselves from the server: a banner on Home, and Settings → Updates.
  Updates download only what changed.
* Settings → About shows the version and the build's commit.
* Gambit opens once per profile: starting it again brings the open window forward (two copies could
  overwrite each other's progress).

**Play and learn**
* Settings: back up, restore and reset your progress.
* Opening review: the opening lessons' lines come back on a spaced-repetition schedule.
* The analysis board searches on several cores (about twice as deep in the same time).
* Practice puzzles by opening; a faster engine (the bots play the same moves); a welcome screen for
  new players.
* Fixed: a "Left" tooltip that stuck to the board during games.

**Online**
* Accounts with a username and password; invite-only sign-ups; server data in SQLite.
* Repeated walkouts pause quick pairing; offensive usernames are refused; bans and renames apply
  at once.
* Daily server backups, a backup download for admins, and restoring at startup.

## 0.1.0 — 2026-10-02

The first builds: bots from beginner to strong, rated puzzles, lessons, game review, analysis,
achievements, statistics, themes, and online play through your own server.
