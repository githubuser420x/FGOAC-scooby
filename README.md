<div align="center">

# FGOAC scooby

**Fate/Grand Order Arcade, in English, on your own PC.**

[![Latest release](https://img.shields.io/github/v/release/githubuser420x/FGOAC-scooby?label=latest%20release)](https://github.com/githubuser420x/FGOAC-scooby/releases/latest)
[![Licence](https://img.shields.io/badge/licence-MIT-blue)](LICENSE)

![The Play page](docs/screenshots/play.png)

</div>

FGOAC scooby is a fan-made English patch and launcher for the FGO Arcade local platform. It puts the
game itself into English - menus, tutorial, story, battle screens, shops, help - and replaces the
platform's Chinese front end with an English one that starts the local server, manages your Master
account, and builds your thirty-card deck from the real card art. It is not a game download and it
carries no game files: it is applied on top of an FGO Arcade local platform install you already have.

## What you need

| | |
| --- | --- |
| The game | An existing **FGO Arcade local platform V1.01** install (Cloud23333's package) - the folder that holds `App` and `Server` |
| OS | Windows 10 or 11, 64-bit |
| GPU | NVIDIA on a current driver. AMD and Intel run through the bundled compatibility layer, which the launcher turns on by itself on a PC without an NVIDIA card (and keeps off on NVIDIA, where it does not work) |
| Drive | Any drive **except E: or Y:** - see the table further down |
| Rights | Administrator: one Windows prompt when the launcher starts |

.NET and Python are not needed. The launcher carries its own runtime, and the platform brings its own
Python.

## Install

1. Download the latest release zip and unzip it into your FGO Arcade folder, beside `App` and `Server`.
2. Run **FGOAC scooby.exe** and click **Yes** on the Windows permission prompt.
3. Press **Play**. The game takes about a minute to reach the title screen.

The first start does the rest on its own: it installs the English files, checks the game can write to
its own folders, allows the game and the local server through Windows Firewall, creates the account
**Master** with a full Servant and Craft Essence roster, and sets the display to windowed 1280x720 on
your main monitor. Later starts go straight to Play.

[`docs/GUIDE_EN.pdf`](docs/GUIDE_EN.pdf) is the full player guide: getting Servants without the
summon, controls, a sortie step by step, the exchange shops and troubleshooting.

## What works

- **The game text** - 65,652 translated rows: story, quests, Servant and Craft Essence profiles,
  skills, items, missions and every menu. Names follow the English release.
- **The game artwork** - 155 rebuilt sprite archives: title, tutorial, terminal, formation, battle
  HUD, results, shops, synthesis, present box, master missions, rankings, help, title editor.
- **Offline single player, end to end** - the tutorial, solo sorties, the terminal, the exchange
  shops, synthesis, My Room, rankings and the title editor.
- **The launcher**, all five pages, with the official English card names and Craft Essence effects.

## What does not work

- **No online play.** Everything runs against the local server; there is no matchmaking, and no
  official service left to connect to.
- **The in-game summon works** - it draws from the local server's pool with the weights from the Draw
  Rates page - but you do not need it: the Account page grants a full roster in one click, and the
  card library holds all 1,384 cards.
- **A few event screens are still Japanese** - the co-op event banners, the co-op result screens and
  the event shops from 0029 on. They are artwork rather than text, and nothing else is affected.

## The launcher

| Page | What it does |
| --- | --- |
| **Play** | Play and Stop Game, start and stop the local server, open the logs, and live readouts for the server, the selected Master and the deck. The page to leave open while the game runs. |
| **Account** | Create, select and delete Master accounts, and grant one of them a full Servant, Craft Essence and item roster in a single click, with levels, bond, costumes and clear rewards. |
| **Cards and Deck** | The card library and the deck editor, searchable by official English name, with Craft Essence effects in FGO NA phrasing. The deck is sent to the game every time you press Play. |
| **Settings** | Display - monitor, resolution, aspect ratio, frame rate, display mode. Controls - keyboard, XInput or native DualSense, with dead zone, rumble and a controller test. Audio. |
| **Advanced** | The local server and its settings, Diagnostics and Help with every game error code and its fix, mouse cursor, debug, photo mode, and About. |

It keeps itself up to date: the launcher asks GitHub Releases whether there is a newer version and
offers to fetch and apply it, so a translation fix reaches you without a reinstall.

<p align="center">
  <img src="docs/screenshots/cards.png" width="32%" alt="Cards and Deck" />
  <img src="docs/screenshots/controls.png" width="32%" alt="Settings, Controls" />
  <img src="docs/screenshots/diagnostics.png" width="32%" alt="Diagnostics and Help" />
</p>

## If something goes wrong

Open **Advanced > Diagnostics and Help** first. It lists every game error code with the fix, and the
answer is usually there.

| What you see | What it means | What to do |
| --- | --- | --- |
| **ERROR 4102** | The game cannot reach the local server. | Start the server from the Play page, wait for it to report ready, then press Play again. |
| **ERROR 4104** | The install is on drive **E:** or **Y:**. The game's own file hook sends every path on those drives to the cabinet data mount, so it cannot open its own files. | Move the whole game folder to any other drive. |
| **ERROR 4105**, about ninety seconds after launch | The game was not started as administrator. | Click **Yes** on the Windows permission prompt when the launcher starts. |
| **0xC0000005**, a few seconds after launch | Windows Defender **Controlled Folder Access** is blocking the game from writing its own files. | Allow the game folder, or `App\ago.exe`, under Windows Security, Virus and threat protection, Ransomware protection. |
| **A black screen at launch** | Almost always the NVIDIA driver rather than the patch. | Update the driver and try again. |
| **The game hangs at a black screen on the very first launch** | A Windows Firewall prompt is waiting behind the game window. The launcher normally creates those rules itself, but a company policy or a security suite can stop it. | Look in the task bar for the prompt and allow both `Server\python\python.exe` and `App\ago.exe`. |
| **The main menu misbehaves right after the tutorial** | A known quirk of the tutorial-to-main-menu handoff. | Restart the game once. |

To report a problem, open an issue and say which screen you were on and what you expected. Attach
what you have from the `logs` folder next to `App`: `fgo-last-launch.log`, `fgozh.log`,
`server-control.log`, `artemis-stderr.log`, `mariadb.log`, and `environment-check.txt`, which
Diagnostics and Help writes for you.

## Building from source

You need the .NET SDK (10.x is what this is developed on) and Windows 10 or 11 x64. Everything else -
the .NET 6 reference and runtime packs, and the one package dependency - is restored from nuget.org
on the first build.

```
build.cmd                     compile check only
publish.cmd                   self-contained single file, into dist\
deploy.cmd <install root>     copy the published launcher into an install
```

`publish.cmd` writes `dist\FGOAC scooby.exe`. Expect around 165 `CS8632` warnings and zero errors;
they come from the decompiled source and are not worth silencing one file at a time.

[`docs/DEVELOPING.md`](docs/DEVELOPING.md) explains where `src\` comes from, the compile fixes the
decompile needs, what the translation must never change, and how to re-derive the build when the
author ships a new version. [`docs/DESIGN.md`](docs/DESIGN.md) is the plan the interface is built
from, [`docs/HISTORY.md`](docs/HISTORY.md) is the log of what was done, and
[`CONTRIBUTING.md`](CONTRIBUTING.md) has the house style.

## Releases

A release is one zip built from this repository and the English game files in an install, with a
SHA-256 manifest generated from the same bytes that ship. The updater in the launcher reads
`releases/latest`, so a release only reaches players once it is published and not marked
pre-release.

```
publish.cmd
package.ps1 -GameRoot <install root>
```

That writes `release\FGOAC-scooby-vX.Y.Z.zip` and `FGOAC-scooby-vX.Y.Z.zip.sha256`. Tag the commit
`vX.Y.Z`, publish a GitHub release on that tag, and upload **both** files as assets: the updater
looks for an asset whose name starts with `FGOAC-scooby-v` and ends in `.zip`, and for the
`.zip.sha256` beside it, and skips a release that is missing either rather than half-installing it.
[`docs/RELEASING.md`](docs/RELEASING.md) has the exact steps and the checks.

## Credits

**Cloud23333** wrote the FGO Arcade local platform: the server package, the front end
(`FGOLocalPlatform`) that FGOAC scooby is built from, and the file hook this patch loads its English
through. None of this exists without that work, and his package is free - if anyone sold it to you,
ask for your money back. The **FGO Arcade wiki** and **Atlas Academy** are where the official English
names of Servants, Craft Essences, skills and items come from, so the game and the launcher call
everything what the English release calls it. **Fate/Grand Order Arcade is Sega's and TYPE-MOON's**;
they own the game. This is a fan translation applied to files you already have, it is not sold, and
it carries no game files of its own.

Released under the [MIT licence](LICENSE).
