# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Version numbers cover the whole release: the launcher, the English game files it installs, and the
installer that puts them there.

## [Unreleased]

### Added

- **First-class Linux support** (via Proton / Lutris / Wine):
  - Automated 1-Click setup script (`linux/setup-fgoa-linux.sh`).
  - Python PowerShell Shim (`linux/ps_shim.py`) to manage local MariaDB and Artemis server instances seamlessly under Wine.
  - Linux host network configuration script (`linux/setup-linux-network.sh`) to configure loopback virtual bridge `192.168.100.1/32` and privileged port binding.
  - Pre-configured Lutris game profile template (`linux/fgolocalplatform.lutris.yml`).
  - Comprehensive Linux player and troubleshooting guide (`docs/LINUX_GUIDE.md`).
- Automated 25-byte IAT patch for `App/ago.exe` (offset `0x1971978`) fixing `SetWindowFeedbackSetting` crash (`0x80000100` / `STATUS_WINE_STUB`) under Wine.
- Dual-GPU hybrid graphics support via automatic NVIDIA PRIME render offload.

## [1.1.1] - 2026-09-15

Works on Cloud23333's V1.01 and V1.02. V1.02 is the one to be on: it fixes ERROR 4102, the blank
Servant records and the sync error after enhancing a Servant. Unzip this release over your game
folder and run the launcher, or take it through Check for updates. Nothing to do by hand; your
accounts, decks and settings stay as they are.

### Fixed

- Updating through the launcher left the previous installer and file list in the game folder, so
  the next start put the previous version's files back. The launcher now unpacks the release into
  the game folder the way you would by hand.
- After a platform update (V1.01 or V1.02) the English scripts and text were gone until the patch
  marker was deleted by hand. The launcher now notices and puts them back on its own.
- A game folder that never had V1.01 is refused with a message that says so. Before, the launcher
  reported missing files and then crashed.
- `App\FGO_Runtime.dll` fresh out of a zip is unblocked on every start, so PowerShell no longer
  stops with 0x80131515 at Play.
- The Cards and Deck page says when the card images folder is missing, instead of listing zero
  Servants.
- The first-time hints on the Play page no longer clip at narrow window widths, and the card
  library toolbar no longer overlaps its own buttons with the log panel open.
- Diagnostics and Help, the guide and the README cover ERROR 8404, "Cannot use Aime card" at the
  title, 0x80131515, the missing-files dialog and the crash right after the game window opens.
- The bond talk scenes V1.02 restores for swimsuit Musashi, swimsuit Jeanne and Koyanskaya of Light
  are in English.

### Changed

- The AMD and Intel layer is now fluphus's open-source shim (`compat\amd-shim`, MIT). An install
  that already runs the older layer keeps it; Settings > Display shows which layer is on and
  switches to the newer one in one click, and back. A fresh install on a PC without an NVIDIA card
  gets the new layer on its first run. An update never adds, removes or replaces a layer on its own.
- The layer switch is no longer greyed out on NVIDIA PCs or when the shim's own installer put an
  `App\opengl32.dll` there; the line under it says what to expect, and the choice is yours.
- The launch log no longer prints a cabinet mode line; the game ignores that setting.

## [1.1.0] - 2026-09-14

First public release, for Cloud23333's FGO Arcade local platform V1.01. Earlier version numbers were
internal builds.

### What is in it

- **The game in English.** 65,266 translated text rows - story, quests, tutorial, Servant and Craft
  Essence profiles, skills, items, missions and every menu - and 240 rebuilt sprite archives:
  title, card read, main menu, terminal, formation, battle HUD, results, shops, synthesis, present
  box, master missions, matching, rankings, grail, help pages, title editor, on-screen keyboards and
  the summon screens. The launch scripts, the environment check, the account and server tools are
  English too, so every line that reaches the screen is English wherever it comes from.
- **The launcher**, an English front end for the platform on five pages: Play, Account, Cards and
  Deck, Settings, Advanced. One self-contained executable; no .NET runtime to install.
- **A first run that sets everything up**: installs the English files, checks the game can write to
  its folders, adds the Windows Firewall rules for the local server, the game and the cabinet
  service, creates the account Master with a full Servant and Craft Essence roster, and fills in
  the launch settings that are missing (windowed 1280x720 on the primary monitor). Nothing at all on
  later starts.
- **The deck editor**: 1,384 cards with their English names and 1,264 Craft Essence effects, searchable.
  Drag a card into the deck to add one copy, drag a deck card to reorder it, drag it back out to
  remove it; double-click picks the art and the number of copies. The deck reaches the game on Play.
- **Account tools**: create and select accounts, grant a full roster, all Craft Essences, max bond,
  materials and costumes in one click each.
- **Error codes and help** rewritten as one sentence and the fix, including 4102, 4104 on drives E:
  and Y:, 4105 without administrator rights, and the 0xC0000005 crash caused by Windows Defender
  Controlled Folder Access.
- **AMD and Intel graphics** through a bundled OpenGL compatibility layer (`compat\fgoglcompat.dll`)
  that translates the game's NVIDIA-only extensions. Turned on by itself on a PC without an NVIDIA
  card; greyed out on NVIDIA, where the layer only produces a white window.
- **An updater**: the launcher asks GitHub Releases whether a newer version exists and offers to
  fetch and apply it.
- **The installer** behind the first run, `Apply-EN-Patch.ps1`: finds the install, refuses drives
  E: and Y: and a running game, backs up every file it replaces, checks every file it copies
  against a SHA-256 manifest, never touches accounts, decks or the database, and undoes itself
  with `-Rollback`.
- **The player guide** inside the package as `GUIDE_EN.md` and `GUIDE_EN.pdf`: install, first run,
  controls, a sortie step by step, the shops, every error code.

### Known limits

- No online play and no matchmaking; everything runs against the local server.
- The co-op event banners, the co-op result screens, the later event shops and the summon
  screen's title art are still Japanese. They are artwork, not text. A few lines in event stories
  are still Japanese as well.
