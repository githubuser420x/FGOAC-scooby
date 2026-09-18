# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Version numbers cover the whole release: the launcher, the English game files it installs, and the
installer that puts them there.

## [Unreleased]

### Added

- A **Mods** page, with a Battle Tuner tab. It applies the game-side and server-side mods the
  launcher ships - the quest-timer mute, the deck cost, the dash cost and the rest - and
  takes them off again cleanly: the original of every file is stored first, so a mod comes off
  exactly as it went on. Verify reports a file an update put back and Repair puts the mod's edit
  there again, and a mod whose folder is removed is reverted by the launcher rather than left
  half-applied.
- **Battle Tuner**: one mod for the numbers behind a battle - the wave countdown (off, scaled per
  wave, the first wave only, or pinned to a fixed number of seconds), command-skill cooldown,
  Noble Phantasm gain from an Arts chain and from criticals, the penalty a botched Noble Phantasm
  costs, enemy drop quantity, quest bond gain, and the HP and attack of both Servants and enemies.
  Each is a slider on the Battle Tuner tab; set one back to 1 to leave it alone. The tab is off
  until the bundle is applied from the Mods tab, and a changed number is re-derived from the
  original file rather than stacked on the previous one.
- `docs\MODS.md`, a guide to writing a mod: the manifest field by field, how a rule rebuilds a line
  and how to pin one down, and a worked sample under `docs\mods\examples\` for each kind of file a
  mod can edit - a text field, a value that is not a number, a member inside a packed archive, a raw
  byte patch, a whole file, and one that puts numbers on the Battle Tuner tab.

## [1.1.2] - 2026-09-16

Works on Cloud23333's V1.01 and V1.02. Unzip this release over your game folder and run the
launcher, or take it through Check for updates. Your accounts, decks, settings and whichever
graphics layer you have stay as they are.

### Fixed

- The older AMD graphics layer is back in the package. A fresh install without an NVIDIA card
  gets it by itself, since the newer layer crashes at the first battle on RX 500, RX 6000 and
  RX 7600 cards. An install that already has a layer keeps it; if you turn the layer off and on
  again, the fresh choice is the older one, and the newer one is a click away.
- "Go back to the older layer" now also shows when App\opengl32.dll is a copy the launcher did
  not put there, and the Display page says when a fgoglcompat.dll sits where the game cannot use it.
- A game folder whose fgohook.dll is still the 11.00 build is refused with a message that says
  to apply V1.02 again, instead of crashing at start.
- Play works from a folder whose name has square brackets.
- The help page explains ERROR 6401, the 0xC000001D stop on CPUs without F16C, and the AMD
  first-battle crash.

### Added

- Deck loadouts: save the deck under a name, keep as many as you like, load any of them, and
  Export or Import to share with other players (Cards and Deck).
- Draw-rate presets: the same for the Draw Rates table.
- Sort the card list by name or card number.
- Max Master Level on the Account page.
- A short "what's new" window the first time the launcher starts after an update.
- A newer build of either graphics layer can be dropped into the compat folder and installed from
  Settings > Display, so a layer update no longer waits for a launcher release. The Display page
  now says up front that both layers are community work and not fully optimized yet.

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
