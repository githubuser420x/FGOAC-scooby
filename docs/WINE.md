# Running FGO Arcade under Wine (Linux install guide)

This is the Linux install guide for the English build. It covers putting the
game on disk, the prefix, the launcher, the compatibility patches and what to do
when something goes wrong. It was verified end to end with Wine 11.17 on 64-bit
Arch, an RTX 4090 and the Cloud23333 V1.02 platform.

Everything here is additive: nothing on this page changes the Windows
instructions in `README.md`.

## What you need

| | |
| --- | --- |
| OS | 64-bit Linux with a working Wine (11.x verified; older may work) |
| GPU | NVIDIA recommended. Mesa Zink is used to make OpenGL contexts work under Wine's EGL backend |
| Game | An FGO Arcade local platform install: Cloud23333's V1.00 base, updated to V1.01 then V1.02 |
| Patch | The `FGOAC scooby` release zip, unzipped into the install root (beside `App` and `Server`) |
| Tools | `wine`, `python3`, `curl`, `ip`/`sysctl` (via `sudo`), `mingw-w64` (to build the touch shim), `imagemagick` + Python Pillow (for the launcher's title check) |

The game folder is the one that holds `App` and `Server`. The examples below
assume it lives at `~/.cache/fgoac-game/install`; set `FGO_INSTALL=/path/to/install`
if yours is elsewhere.

## Install the game

1. Extract Cloud23333's V1.00 base into the game folder. The archives are split
   into parts; extract `part1` and the rest follow. The password is
   `bilibili Cloud23333`.
2. Extract V1.01 over the folder, then V1.02 over it. Both are front-end updates
   and ship their own `App` and `Server` trees. Only the front end changes;
   `App\ago.exe` is the same binary across all three.
3. Unzip the `FGOAC scooby` release into the same folder, so `FGOAC scooby.exe`
   sits beside `App` and `Server`.

Do not install onto a drive mapped as `E:` or `Y:`. The game's own file hook
redirects those to the cabinet data mount and it cannot open its own files
(ERROR 4104 on Windows; the same rule applies here).

## Prefix setup

The prefix needs fonts or every WPF window fails before it is shown. The
launcher is set in Segoe UI, which Wine does not ship:

```
winetricks -q corefonts tahoma
```

Wine's built-in PowerShell is a stub that exits 0 without running anything, and
a real PowerShell 7 also fails to execute under Wine 11.17. That is why this
guide does not use the Windows PowerShell flows at all: the English patch, the
server control and the launch sequence are all replicated natively by the
launcher and the scripts in `patch/wine`.

DXVK is not required (the game renders with OpenGL). Do not install it.

## The launcher

`patch/wine/play-fgo.sh` is the one entry point. A desktop shortcut named
**Play FGO Arcade** and a `play-fgo` symlink are installed for convenience.

```
play-fgo                 # start everything and launch the game
play-fgo --status        # what is running
play-fgo --stop          # close the game (server keeps running)
play-fgo --revert        # undo the patches in the install
FGO_DESKTOP=1 play-fgo   # run inside a Wine desktop window
FGO_INSTALL=/path play-fgo
```

One run does all of this, and is safe to repeat:

1. adds the cabinet virtual LAN addresses (`192.168.100.1` and
   `192.168.100.11` on `lo`) and lowers the privileged port floor so the title
   server can listen on 777
2. starts MariaDB and ARTEMiS under Wine if they are not already up
3. waits for the server's health check (`Service OK` on port 777)
4. builds the touch shim if it is missing and applies the compatibility patches
5. launches the game with Zink, the content hook and the touch shim, in a
   movable window
6. watches the game and, if it lands on AMDaemon's error screen, restarts the
   title server and the game, up to three attempts

The window takes about a minute to appear and roughly two more to reach the
title. Click inside it to play; left-click is your touch.

## What the patches change

All of this is applied by the launcher; the individual scripts are listed so the
changes can be reviewed or re-run by hand.

| File | Change |
| --- | --- |
| `apply-wine-fixes.py` | Two byte patches in `App\ago.exe`. See below. A pristine copy is kept as `ago.exe.wine-orig` |
| `merge-en-overlay.py` | Copies the English files under `App\zh\rom` over the matching files in `App\rom` (1683 files). Originals go to `App\rom.wine-orig` |
| `apply-executable-text.py` | Applies the 2774 in-`ago.exe` strings from `App\zh\executable-text.json` in place. The 550 with an empty translation are skipped |
| `touchshim.c` | Builds `touchshim.dll`, injected into the game so clicks arrive as touch |
| `play-fgo.sh` | Runs everything above, plus the server |

### The two `ago.exe` patches

1. **Clear the WGL robust-access flag.** At startup the game creates a second
   OpenGL context asking for robust access. Windows/NVIDIA grants it; Wine's EGL
   backend refuses with `EGL_BAD_MATCH`, so the game drops its context and then
   resolves its 280-entry OpenGL table with no context current. Wine's
   `wglGetProcAddress` returns NULL for every OpenGL 1.2+ symbol without a
   current context, so the load aborts at the first one and the whole table
   stays zero. The game then calls `glCreateBuffers` through it and dies on a
   NULL pointer. Clearing the flag lets the context exist and the table resolve.
2. **Stub `USER32!SetWindowFeedbackSetting`.** Wine implements it as a stub that
   aborts the process. The call only requests pen/touch feedback and its result
   is checked, so it is replaced with `mov eax, 1` (success).

### Why the English patch is applied to the files

The patch normally leaves `App\rom` alone and relies on the content hook
`App\zh\fgozh.dll` to redirect the game's reads to `App\zh\rom`. That hook will
not load into the game under Wine: `LoadLibrary` inside the process fails with
`ERROR_ALREADY_EXISTS`, by every injection method tried, with or without a
debugger, in either order, and from either directory. It loads fine into an
ordinary process, so it is specific to the game. Merging the overlay into
`App\rom` does the same substitution ahead of time and needs no hook.

### Why the touch shim exists

The game registers for touch (`RegisterTouchWindow`) and drives its title and
menus from `WM_TOUCH`. Wine stubs those APIs and there is no touch device, so no
touch ever arrives and the game waits on "Please touch the screen" forever. The
platform's own mouse-to-touch remap only engages once a session is running,
which is too late for the title. `touchshim.dll` is injected into the game and
turns left-button down/move/up into `WM_TOUCH` messages carrying a fake touch
handle, answered with screen coordinates in the format the game expects.

## What is verified working

- The game boots to its power-on self test (`SYSTEM STARTUP`, `ALL.Net: OK`,
  `Version 11.00.00`) and on to the title, the summoning animation and the main
  terminal menu.
- The local server runs: MariaDB on 8888 and ARTEMiS on 777 (title), 9999
  (billing) and 7777 (aimedb), reachable on the cabinet virtual LAN.
- The English patch is in effect: the UI, menus and story text are English.
- Input works: clicking the window drives the touch UI.

## Known limitations

- **AMDaemon can fail its platform probe.** Its log
  (`App\am\amdaemon.exe.log`) reports `amPlatformGetPlatformInformation()
  ErrCode 0` from `amw_platform_manager.cpp`. The probe names NVAPI
  (`amPlatformNvapiInit`), and this prefix has no `nvapi64.dll`, because
  NVIDIA's Linux driver does not ship one. When it fails, the game stops on
  `ERROR 4102`. The launcher retries this automatically; a fresh title server is
  what makes the handshake succeed.
- **`NET PARAM : WAIT (0/1)`** is shown for a minute or two during boot. It
  clears on its own.
- **Some event banner artwork is still Japanese.** It is baked into the images,
  not text, and is a documented limitation of the patch itself.
- **The mouse is the touch panel.** Clicks and drags inside the window act as
  touches. A controller is not wired through the shim.
- **Frame rates are below Windows.** Zink is a translation layer.

## Troubleshooting

| What you see | What to do |
| --- | --- |
| `ERROR 4102` | A known AMDaemon/Wine issue. Stop and run `play-fgo` again; the launcher retries by itself. Freshly restarting the title server is what fixes it |
| Clicks do nothing | The touch shim is not loaded. Check that `App\touchshim.dll` exists and that `play-fgo.log` has no `failed to load` line for it, then `play-fgo --revert` is unnecessary - just re-run `play-fgo` |
| The game window is behind everything | Run `FGO_DESKTOP=1 play-fgo`. Some compositors ignore raise requests for Wine windows; a Wine desktop window avoids that |
| The game exits right after launch | Read `logs\play-fgo.log` and `logs\wine-startup.log`. Re-run `play-fgo`; it reapplies patches and rebuilds the shim |
| The title server will not start | Port 777 is privileged on Linux. The launcher lowers the floor with `sysctl -w net.ipv4.ip_unprivileged_port_start=700`; if it cannot, run that yourself |
| Nothing at all starts | `play-fgo --status` shows which of the database, title, billing and aimedb ports are up and whether the game process is running |
| Want the original files back | `play-fgo --revert` restores `ago.exe` and the `rom` files from the backups |

Report problems with `logs\play-fgo.log`, `logs\wine-startup.log` and the
per-service logs listed in `README.md`.

## Building on Linux

The launcher project targets `net6.0-windows` with WPF, which the Linux .NET SDK
builds once Windows targeting is enabled. With the .NET 10 SDK:

```
dotnet build src/FGOLocalPlatform.csproj -c Release -p:EnableWindowsTargeting=true
dotnet publish src/FGOLocalPlatform.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableWindowsTargeting=true
```

Expect zero warnings and zero errors, the same bar as `build.cmd`.

The touch shim builds with mingw-w64:

```
x86_64-w64-mingw32-gcc -O2 -shared -o touchshim.dll touchshim.c -luser32
```
