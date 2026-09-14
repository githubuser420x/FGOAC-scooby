# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Version numbers cover the whole release: the launcher, the English game files it installs, and the
installer that puts them there.

## [Unreleased]

## [1.1.0] - 2026-09-14

First public release, for Cloud23333's FGO Arcade local platform V1.01. Earlier version numbers were
internal builds and were never published.

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
- The co-op event banners, the co-op result screens, the event shops from 0029 on and the summon
  screen's title art are still Japanese. They are artwork, not text. A few lines in event stories
  are still Japanese as well.
