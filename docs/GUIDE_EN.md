# FGOAC scooby - Player Guide

FGOAC scooby is an English fan patch and launcher for the FGO Arcade local platform (Cloud23333's
V1.01 package). This guide covers install, first run, how to get Servants, controls,
and troubleshooting.

## What this is

FGOAC scooby puts the FGO Arcade game itself into English - menus, tutorial, story text, battle
screens, shops, help - and replaces the platform's front end with an English launcher of the same
name. It is not a game download: it needs an existing FGO Arcade local platform install (Cloud23333's
V1.01 package) and is applied on top of it.

## Requirements

| Requirement | Notes |
| --- | --- |
| OS | Windows 10 or 11, 64-bit |
| GPU | NVIDIA on a current driver. AMD and Intel run through the bundled compatibility layer (Settings > Display), turned on by itself when no NVIDIA card is found and greyed out on NVIDIA, where it does not work |
| .NET / Python | Not needed - both are bundled with the install |
| PowerShell | 5.1 (built into Windows) or 7 |
| Rights | Administrator (one UAC prompt) |
| Drive | Any drive except **E:** or **Y:** |

## Install

1. Unzip the FGOAC scooby package into your FGO Arcade folder - the one that holds `App` and `Server`.
2. Run **FGOAC scooby.exe**.
3. Click **Yes** on the Windows permission prompt.

The first start does the rest: it installs the English files, checks that the game can write to its
folders, creates the account **Master** with a full Servant and Craft Essence roster, and sets the
display to windowed 1280x720 on the primary monitor. Then press **Play**; the game takes about a
minute to reach the title screen. Later starts skip all of that and go straight to Play.

## First run and the launcher

The launcher has five pages:

- **Play** - the Play and Stop buttons, live server status, and a first-run hint that walks through
  the five setup steps. This is the page to leave open while the game runs.
- **Account** - create, select, or delete accounts, and grant a Servant, Craft Essence, or item pool
  to the selected one with one click (One-Click Inventory and Growth).
- **Cards and Deck** - the card library (searchable, with official English names and Craft Essence
  effects) and deck editor, plus the Draw Rates page.
- **Settings** - display (monitor, resolution, aspect ratio, frame rate), controls (keyboard, XInput,
  DualSense), and audio.
- **Advanced** - server start/stop and settings, Diagnostics and Help (environment check and error
  codes), mouse cursor, debug, photo mode, and About (credits, including Cloud23333's original
  FGOLocalPlatform).

## Getting Servants

The in-game summon works: it draws from the local server's card pool with the weights set on the
launcher's Draw Rates page, one credit per pull, and the platform gives you free credits. You do not
need it for a full roster, though - the launcher can grant everything at once:

- **Account page**: select an account and use One-Click Inventory and Growth to grant a full set of
  Servants, Craft Essences, levels, bond, costumes, and clear rewards at once.
- **Cards and Deck page**: build a deck from the full card library. Drag a card into the deck to add
  one copy, or double-click it to choose its art and how many copies; drag a deck card to reorder it,
  or back into the library to remove it. The deck is published to the game every time you press Play.
- **Draw Rates page**: sets the weight the local server gives each card when the in-game summon draws.
  It does not grant anything by itself.

## Playing

### Controls

| Action | Keyboard | XInput | DualSense (native) |
| --- | --- | --- | --- |
| Move | W / A / S / D | Left stick | Left stick |
| Dash | Left Shift | - | - |
| Switch target | F | - | - |
| Attack | Right mouse button | - | - |
| Noble Phantasm | Space | - | - |
| Recenter camera | C | - | - |
| Scan Aime card | Enter (hold) | - | - |
| Test menu | F1 | - | - |
| Service menu | F2 | - | - |
| Coin | F3 | - | - |

Full button mappings for XInput and DualSense are set on the launcher's Controls page, along with
dead zone, controller index, and rumble strength. For DualSense, choose **PS5 DualSense (native)**
under Settings > Controls, then **Detect Controller** and **Test Rumble** to confirm the right pad is
selected before starting the game. If you already use DS4Windows or Steam Input to present the pad as
an Xbox controller, keep XInput mode instead. Menus in the game are touch-driven, so they are worked
with a mouse (or the touch screen on real arcade hardware) - left click selects, regardless of which
control mode is active for play.

### A sortie, step by step

1. **Load Deck** - press the Load Deck button at the bottom-left of the main menu; the game reads the
   deck you built in the launcher. The Aime login itself happens automatically at the title screen.
2. **Formation** - set up your party from the Servants on the card.
3. **Terminal** - pick the chapter or event.
4. **Quest** - pick a quest from the list.
5. **Start** - confirm the sortie and fight; controls are as in the table above.

After the tutorial finishes, restart the game once before your first real sortie - this is a known
quirk of the tutorial-to-main-menu handoff, and a restart clears it.

## Exchange shops

The local server restores two in-game shops:

- **Costume Shop** - trade costume fragments, 175 items.
- **Item Exchange** - trade material tickets, 80 items; resets monthly on the 1st at 07:00 JST.

## Known limits

- No online play or matchmaking - everything runs against the local server.
- No co-op events - the co-op banners and co-op result screens stay Japanese; this is artwork, not
  text, and the rest of the game is unaffected.

## Troubleshooting

| Error | Fix |
| --- | --- |
| 4102 | Server unreachable - start it from the Play page and wait for it to report ready. If it keeps happening on V1.01, update to Cloud23333's V1.02, which fixes it. |
| 8404 at boot | The game's own Startup Mode was saved as Satellite (Sub Unit). On the error screen press F1 for the Game Test Menu (F2 moves the arrow, F1 confirms), open Game Settings, set Startup Mode to Main Unit, then Exit. |
| Cannot use Aime card, at the title | The first message to the local server timed out on that boot - close the game, check the server shows ready, press Play again. |
| 0x80131515 at Play | Windows marked `App\FGO_Runtime.dll` as downloaded. The launcher clears the mark itself; if it comes back, right-click the file, Properties, tick Unblock. |
| "Some of the files the game needs are missing" | The zip was not unzipped into the game folder itself, or the folder never had Cloud23333's V1.01 update - unzip beside `App` and `Server`, and apply V1.01 or V1.02 first. |
| The game window opens and closes again (exit code 22) | Try windowed 1280x720 on the primary monitor; when reporting it, attach `logs\ago-crash-*.dmp` with your graphics card and driver version. |
| "Update failed" at the end of Cloud23333's V1.02 updater | His files are in place; only the last step (recovering Servants enhanced before V1.02) stopped, because his bundled Python points at a folder that exists only on his PC. Run FGOAC scooby as usual. To run that recovery anyway: `Server\python\python.exe Server\tools\repair_fgo_grail.py --logs logs --report logs\grail-recovery.json --apply` from the game folder. |
| 4104 | The install is on drive E: or Y: - move the whole folder to any other drive. |
| 4105 | The game was not run as administrator - click Yes on the UAC prompt. |
| 0xC0000005, a few seconds after launch | Windows Defender Controlled Folder Access is blocking the game folder - add the game folder (or the exe) to the allowed list. |
| Black screen at launch | Almost always the NVIDIA driver, not the patch - update it and try again. |

Other notes:

- **Windows Firewall** may prompt once for the server's Python process and once for the game - allow
  both. The launcher creates these rules itself on first run; the prompt is only a fallback.
- **Advanced > Diagnostics and Help** lists every error code with its fix - check it first.
- To report a problem, say which screen you were on and what you expected, and attach these logs from
  the `logs` folder next to `App`: `fgo-last-launch.log`, `fgozh.log`, `server-control.log`,
  `artemis-stderr.log`, `mariadb.log`, `environment-check.txt`.

## Credits

- **Cloud23333** wrote the FGO Arcade local platform - the server package, the launcher
  (FGOLocalPlatform) that FGOAC scooby is built from, and the file hook this patch loads through. His
  package is free; if anyone sold it to you, ask for your money back.
- **fluphus** wrote the AMD and Intel compatibility layer (fgo-arcade-amd-shim, MIT), shipped under
  `compat\amd-shim`.
- The **FGO Arcade wiki** and **Atlas Academy** gave the official English names for Servants, Craft
  Essences, skills, and items.
- FGO Arcade belongs to **Sega** and **TYPE-MOON**. This is a fan translation applied to files you
  already have, and it is not sold.
