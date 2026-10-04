# Releasing Gambit

How a new version reaches people: an installer for those who don't have Gambit yet, and an
automatic update for those who do. Both come from your Gambit server.

## How it fits together

```
./build.ps1 package -ServerUrl https://<server>     (Velopack, per architecture: win-arm64, win-x64)
        │
        ▼
artifacts\releases\  ──copy──►  <server data>\releases\  ──►  https://<server>/download   (installer page)
                                                          ──►  https://<server>/releases   (update feed)
```

* `./build.ps1 package` publishes Gambit for Windows on Arm (`win-arm64`) and for Intel/AMD PCs
  (`win-x64`) and packs each with [Velopack](https://velopack.io) (`vpk`, a local dotnet tool:
  `dotnet-tools.json`). For each architecture, in `artifacts\releases`:

  | File | What |
  |---|---|
  | `GambitChess-win-arm64-Setup.exe` | The installer (what `/download` links to) |
  | `GambitChess-win-arm64-Portable.zip` | Runs from any folder, no install; also updates itself |
  | `GambitChess-<version>-win-arm64-full.nupkg` | The whole app: for new installs and big jumps |
  | `GambitChess-<version>-win-arm64-delta.nupkg` | Only what changed since the previous release (often under 1 MB) |
  | `releases.win-arm64.json`, `RELEASES-win-arm64` | The update feed: which versions exist |
  | `assets.win-arm64.json` | vpk's list of what it made (not needed on the server) |

* The server serves its data folder's `releases\` at `/releases` and lists the installers at
  `/download` (Arm and Intel/AMD, with a note about the Windows warning below).
* **Updates:** installed copies look at `<server>/releases` about 8 seconds after starting.
  When there is a newer version, Home shows "Update available" and **Update and restart**
  downloads it, closes Gambit, installs it and opens the new version. Settings → **Updates** checks
  on demand. Each architecture has its own feed, so an Arm PC only ever gets Arm packages.
* **Which server:** the one given to `-ServerUrl` when packaging. It is built into the app, and
  new players also find it already filled in on the Online page. A build made without
  `-ServerUrl` updates from whatever server is set on its Online page.
* Copies installed with Setup.exe or unzipped from the portable zip update themselves.
  Development builds (`./build.ps1 run`, `./build.ps1 install`) don't.

## Making a release

1. **Raise the version** in `Directory.Build.props` (`<Version>0.2.1</Version>`: the last number
   for fixes, the middle one for new features). It must go up: Velopack only offers newer
   versions. The app and the server share it.
2. **Note the changes** in `CHANGELOG.md`.
3. **Test and commit:** `./build.ps1 test`, then `git commit`. Settings → About shows the commit a
   build came from.
4. **Package:** `./build.ps1 package -ServerUrl https://<your-app>.fly.dev` (about 5 minutes).
   Use the same address every time. **Keep `artifacts\releases` between releases**: vpk builds
   the small delta package from the previous release it finds there. Without it, players
   download the full ~95 MB package (which still works).
5. **Try it** before uploading: unzip the portable zip into a scratch folder and start it with a
   throwaway profile, so your own progress isn't touched:
   ```powershell
   Expand-Archive artifacts\releases\GambitChess-win-arm64-Portable.zip artifacts\try-release
   $env:GAMBIT_DATA_DIR = "$PWD\artifacts\try-profile"; & artifacts\try-release\Gambit.exe
   ```
6. **Upload** the new files to the server's `releases` folder: the packages and installers
   first, then the feed files (`releases.*.json`, `RELEASES-*`) last, so no one is offered a
   version whose package isn't there yet.
   * **Fly.io:** `fly ssh sftp shell`, then one `put` per file, e.g.
     `put artifacts/releases/GambitChess-0.2.1-win-arm64-delta.nupkg /data/releases/GambitChess-0.2.1-win-arm64-delta.nupkg`.
   * **Your PC (`./build.ps1 server`):** copy the files to `%LOCALAPPDATA%\Gambit Server\releases`.
7. **Check:** `https://<server>/download` lists both installers, and
   `https://<server>/releases/releases.win-arm64.json` mentions the new version.

**Disk space:** a release is about 400 MB on the server (installers and full packages for both
architectures). The 1 GB Fly volume from ONLINE.md holds about two releases. Delete older
`*-full.nupkg` files from the server's `releases` folder, or grow the volume
(`fly volumes extend <id> --size 3`, about 15 cents a month per GB). Keep the newest full package and
the deltas.

## The "unknown publisher" warning (code signing)

The installers aren't signed, so Windows SmartScreen says "Windows protected your PC" the
first time someone runs one. They choose **More info → Run anyway**. The download page explains
this. That is fine for friends. Signing removes "unknown publisher". It costs money and needs an
identity check, so it's **your call**:

* **Azure Trusted Signing** (Microsoft's signing service): about $10 a month, with an identity
  check; who can sign up depends on the country, so check Microsoft's current terms. vpk supports
  it directly: put the service's `metadata.json` path in `GAMBIT_TRUSTED_SIGNING` before
  `./build.ps1 package` (needs the Azure CLI signed in; not tried here yet).
* **A code-signing certificate** from a certificate authority: roughly $200–500 a year, with the
  key on a hardware token or a cloud key vault. Put signtool arguments in `GAMBIT_SIGN_PARAMS`,
  e.g. `/a /fd sha256 /tr http://timestamp.digicert.com /td sha256`, and vpk signs every file.
* Even a signed installer can warn for its first few downloads, until the certificate builds a
  reputation with SmartScreen.

**Why not MSIX:** sideloading an MSIX needs a certificate the PC already trusts. A self-signed
one must be installed by an administrator on every PC. The Microsoft Store is the other MSIX
route (Microsoft signs the app; it needs a Partner Center account and passing certification).
Velopack installs per user without administrator rights and updates from your own server, with
no certificate needed.

## Good to know

* **Where it installs:** `%LOCALAPPDATA%\GambitChess` (just for that Windows user). Uninstalling
  (Settings → Apps → Gambit) removes that folder and the shortcuts. Player data lives in
  `%LOCALAPPDATA%\Gambit`, which survives uninstalling and reinstalling. **Never change the package
  id (`--packId GambitChess`) to "Gambit"**: uninstalling deletes `%LOCALAPPDATA%\<package id>`,
  which would then be the player's data.
* **Shortcuts:** Setup puts "Gambit" on the desktop and in the Start menu. It replaces a desktop
  shortcut that is already called Gambit (such as one to a development build).
* **One Gambit per profile:** opening Gambit while it's already open brings the open window
  forward. Two copies on the same data would overwrite each other's progress.
* **Online protocol changes:** when `OnlineProtocol.Version` goes up, older apps are told to update.
  Upload the release before (or together with) deploying the new server, so their update is
  waiting.
* **Rolling back:** Velopack won't install an older version over a newer one. Fix a bad release
  by releasing a newer version.
* **Testing an update locally:** package version A and unzip its portable copy. Raise the version,
  package again, and copy `artifacts\releases` to a test server's `releases` folder (`GAMBIT_DATA`
  pointing at a scratch folder). Then start the portable copy with a test profile
  (`GAMBIT_DATA_DIR`) whose `settings.json` has `"onlineServerUrl": "http://127.0.0.1:5080"`, or
  package with `-ServerUrl http://127.0.0.1:5080`.
