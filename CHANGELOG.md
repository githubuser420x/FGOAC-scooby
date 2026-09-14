# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Version numbers cover the whole release: the launcher, the English game files it installs, and the
installer that puts them there.

## [Unreleased]

## [1.1.0] - 2026-09-14

### Added

- An updater. The launcher asks GitHub Releases whether a newer version exists and offers to fetch
  and apply it, so a player who installed once keeps getting the translation fixes.

### Changed

- The launcher is called **FGOAC scooby**. It shipped as "FGOA scooby" through 1.0.1; the installer
  still recognises the old executable name, so it refuses to run while an old build is open and
  replaces the firewall rules the old name created.
- The interface is redrawn. The Chinese front end's visual identity is gone - the skewed white
  header polygon, its purple palette, its flat buttons and tab strip, its icons and its footer
  notice. In its place is a graphite window with one ice-blue accent, Segoe UI throughout with the
  cabinet's own face kept for the wordmark and Play, the five sections in a sidebar with their
  sub-pages as a strip of tabs, and a Check for updates button in the header of every page.
  `docs/DESIGN.md` is the plan it was built from.
- The application icon is redrawn at every size from 16 to 256 rather than resampled from one
  bitmap.

## [1.0.1] - 2026-09-14

### Added

- Windows Firewall is set up on first run: one program-scoped inbound allow rule each for the local
  server, the game and the cabinet service, so Windows does not interrupt the first launch with a
  prompt that opens behind the game window. Missing rules are put back on any later start, and if
  the rules cannot be created the summary says Windows will ask instead.
- The player guide ships inside the release package as `GUIDE_EN.md` and `GUIDE_EN.pdf`.

### Fixed

- The Stop Game confirmation could be lost behind a fullscreen game and appeared in no window list.
  Dialogs gained a foreground mode - topmost, shown in the task bar, activated on load - and closing
  one without pressing a button now answers nothing rather than No. The first-run summary and the
  first-run error dialog use it too.

## [1.0.0] - 2026-09-14

First release, for Cloud23333's FGO Arcade local platform V1.01.

### Added

- **The launcher**, an English build of the platform's V1.01 front end, on five pages: Play,
  Account, Cards and Deck, Settings, Advanced. Published as a self-contained single file, so a
  player does not have to install a .NET runtime.
- **English card data**: 1,384 card names and 1,264 Craft Essence effects, searchable by their
  English names. Names follow the English release; effect text follows FGO NA phrasing with every
  number checked against the Japanese original, because the arcade's values differ from the mobile
  game's.
- **English game files**: 65,652 translated text rows covering story, quests, Servant and Craft
  Essence profiles, skills, items, missions and every menu, and 155 rebuilt sprite archives - title,
  card read, main menu, terminal, tutorial, formation, battle HUD, results, shops, synthesis,
  present box, master missions, matching, rankings, grail, help pages, title editor, on-screen
  keyboards and summon lines.
- **English replacements outside the assembly** for the launch scripts, the environment check, the
  account and server tools and the summon candidate notes, so every line that reaches the screen is
  English wherever it comes from.
- **Error codes and help** rewritten as one sentence and the fix, including the ones this project
  confirmed: 4102, 4104 on drives E: and Y:, 4105 without administrator rights, and the 0xC0000005
  crash caused by Windows Defender Controlled Folder Access.
- **A first run that sets everything up**: installs the English files, checks the game can write to
  its folders, runs the environment check, creates the account Master with a full Servant and Craft
  Essence roster, and fills in the launch settings that are missing - windowed 1280x720 on the
  primary monitor. Nothing at all on later starts.
- **The installer**, `Apply-EN-Patch.ps1`: finds the install, refuses drives E: and Y: and a running
  game, backs up every file it replaces, checks every file it copies against a SHA-256 manifest,
  never writes to accounts, decks, the server state folder or the database, and undoes itself with
  `-Rollback`.
- **The release packager**, `package.ps1`, which builds the zip and its manifest from the same
  bytes, so the manifest always describes what ships.

### Known limits

- No online play and no matchmaking; everything runs against the local server.
- The in-game summon does not draw cards. Servants come from the Account page and the card library.
- The co-op event banners, the co-op result screens and the event shops from 0029 on are still
  Japanese. They are artwork, not text.
