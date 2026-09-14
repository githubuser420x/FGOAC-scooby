# FGOA scooby {{VERSION}} - FGO Arcade in English

An English patch for the FGO Arcade local platform. It puts the game itself into English - menus,
tutorial, story text, battle screens, shops, help - and replaces the Chinese front end with an
English one called **FGOA scooby**, which starts the server, manages your account and builds your
card deck.

This is not a game download. It does nothing on its own: you need an existing FGO Arcade local
platform install (Cloud23333's V1.01 package), and the patch is applied on top of it.

## Install

1. Unzip this package into your FGO Arcade folder - the one that holds `App` and `Server`.
2. Run **FGOA scooby**.
3. Click Yes on the Windows permission prompt.

The first start does the rest: it installs the English files, checks that the game can write to its
folders, creates the account **Master** with a full Servant and Craft Essence roster, and sets the
display to windowed 1280x720 on your main monitor. Then press **Play**. The game needs about a
minute to reach the title screen.

Later starts skip all of that and go straight to Play.

Two things to know before you start:

- **The game cannot run from drive E: or Y:.** Its own file hook sends every path on those drives to
  the cabinet data mount, and the game stops with ERROR 4104. Any other drive is fine.
- **The game has to run as administrator.** That is the one Windows prompt you cannot skip; without
  it the game shows ERROR 4105 about ninety seconds after boot.

If you would rather install the files without the launcher, run `Apply-EN-Patch.ps1` yourself - it
takes `-InstallRoot`, and `-Rollback` puts back everything it replaced. Once you are happy with the
install you can delete `payload\`, `Apply-EN-Patch.ps1` and `manifest.json` to get the space back,
but keep them if you may want to roll back later.

## What works

- The game text: 65,652 translated lines - story, quests, Servant profiles, skills, items, menus.
- The game artwork: 155 rebuilt sprite archives - title, tutorial, terminal, formation, battle HUD,
  results, shops, synthesis, present box, master missions, rankings, help pages, title editor.
- The launcher: Play, Account, Cards and Deck, Settings and Advanced, all in English, with the
  official English card names and Craft Essence effects in the card library.
- Offline single player: the tutorial, solo sorties, the terminal, the exchange shops, synthesis,
  My Room, rankings and the title editor.

## What does not work

- **No online play.** Everything runs against the local server; there is no matchmaking and no
  official service to connect to.
- **The in-game summon does not draw cards.** Servants come from the Account page (One-Click
  Inventory and Growth) and from the card library on the Cards and Deck page. The Draw Rates page
  only sets the weights the server would use - it does not pull anything by itself.
- **A few event screens are still Japanese**: the co-op event banners, the co-op result screens and
  the later event shops. They are artwork, not text, and the rest of the game is unaffected.
- The game is built for NVIDIA cards and wants a current driver; a black screen usually means the
  driver, not the patch.

## Reporting a problem

Say which screen you were on and what you expected, and attach the log files from the `logs` folder
next to `App`:

| File | What it shows |
| --- | --- |
| `fgo-last-launch.log` | the launch script, from start to exit code |
| `fgozh.log` | the English file hook: which files the game actually loaded |
| `server-control.log`, `artemis-stderr.log`, `mariadb.log` | the local server and its database |
| `environment-check.txt` | the environment check, written by the Diagnostics and Help page |

**Advanced > Diagnostics and Help** in the launcher lists the game's error codes with the fix for
each one; check it first, the answer is usually there.

## Credits

- **Cloud23333** wrote the FGO Arcade local platform - the server package, the launcher
  (`FGOLocalPlatform`) that FGOA scooby is built from, and the file hook this patch loads its English
  through. None of this exists without his work. His package is free; if anyone sold it to you, ask
  for your money back.
- The **FGO Arcade wiki** and **Atlas Academy** were used for the official English names of
  Servants, Craft Essences, skills and items, so the game and the launcher call everything what the
  English release calls it.
- FGO Arcade is Sega's and TYPE-MOON's. This is a fan translation, applied to files you already
  have, and it is not sold.
