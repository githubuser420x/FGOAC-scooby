# Changelog

## {{VERSION}} - {{DATE}}

First release, for Cloud23333's FGO Arcade local platform V1.01.

### Game

- 65,652 translated text rows covering the story, quests, Servant and Craft Essence profiles,
  skills, items, missions and every menu. Names follow the English release (Atlas Academy and the
  FGO Arcade wiki).
- 155 rebuilt sprite archives: title, card read, main menu, terminal, tutorial, formation, battle
  HUD, results, shops, synthesis, present box, master missions, matching, rankings, grail, help
  pages, title editor, on-screen keyboards and summon lines.
- Still Japanese: the co-op event banners, the co-op result screens and the event shops from 0029
  on.

### Launcher

- FGOA scooby, an English build of the author's V1.01 front end, on five pages: Play, Account,
  Cards and Deck, Settings, Advanced.
- Card library in English: 1,384 card names and 1,264 Craft Essence effects, searchable by their
  English names.
- Error codes and help rewritten as one sentence and the fix, including the ones this project
  confirmed: 4102, 4104 on drive E:, 4105 without administrator rights, and the 0xC0000005 crash
  caused by Windows Defender Controlled Folder Access.
- First start sets everything up by itself: installs the patch, checks the folders, creates the
  account Master with a full roster, and picks windowed 1280x720 on the main monitor.

### Installer

- `Apply-EN-Patch.ps1` checks every file against `manifest.json` after copying, backs up whatever it
  replaces to `_en-patch-backup\<timestamp>\`, refuses to run while the game is open or from drives
  E: and Y:, leaves accounts, decks and the database alone, and can undo itself with `-Rollback`.
