# Developing

How the launcher is built, where its parts come from, and what must not be changed when the
translation is carried forward. `README.md` is the player-facing document; this is the one for
working on the code.

`src\` is a decompile of the platform's managed front end (`FGOLocalPlatform`, V1.01, by
Cloud23333) with the compile fixes listed below applied and the user-facing strings translated.
The deliverable is a self-contained single-file host published from `src\` and named
`FGOAC scooby.exe`.

## Layout

| Path | Contents |
| --- | --- |
| `src\` | buildable C#/XAML project (`FGOLocalPlatform.csproj`). `DeckReaderUI\` and `DeckReaderUI.Kancolle\` are the author's card-reader namespaces, compiled into the same assembly; the names are kept as decompiled so a new upstream version can be diffed against them |
| `compat\` | the OpenGL compatibility layer for AMD and Intel graphics, shipped as received (see its README) |
| `overlay\` | English replacements for files that live outside the assembly, laid out by their path relative to the install root |
| `patch\` | `Apply-EN-Patch.ps1` (the installer players run) and `Build-Manifest.ps1` (writes the `manifest.json` it checks against) |
| `dist\` | build output, `FGOAC scooby.exe` (not tracked) |
| `docs\` | player guide, release steps, and this file |
| `package\` | the README that ships inside the release zip |
| `build.cmd` | `dotnet build -c Release` (compile check only) |
| `publish.cmd` | publish + copy to `dist\FGOAC scooby.exe` |
| `deploy.cmd` | copy the published launcher into an install |

## Build

```
publish.cmd
```

Requirements: the .NET SDK (10.x; it builds the `net6.0-windows`
target and restores the 6.0 reference and runtime packs from nuget.org on the first run) and
Windows 10 or 11 x64. The one package dependency, `ZstdSharp.Port` 0.8.8, comes from nuget.org
too, so nothing has to be present on the machine beyond the SDK. The published host carries its
own .NET runtime, so a player does not have to install one.

Deploy into an install:

```
deploy.cmd <install root>
```

`AssemblyName` stays `FGOLocalPlatform`: pack URIs in code and XAML are built from it, and the
process name the diagnostics tooling looks for is still `FGOLocalPlatform`. Only the file is renamed.

## Re-deriving from a new author release

When the author ships a new `FGOLocalPlatform.dll`, the English build has to be re-derived rather
than patched - his `FGOLocalPlatform.exe` is a self-contained single-file bundle that carries its own
copy of the managed assembly and ignores the sibling DLL.

1. Decompile the new assembly with `ilspycmd` 10.1, installed as a dotnet global tool
   (`dotnet tool install -g ilspycmd`):

   ```
   ilspycmd -p --decompile-baml -o <newdir> <path>\FGOLocalPlatform.dll
   ```

2. Re-apply the fixes below, then diff `<newdir>` against `src\` to see what the author changed and
   port the translation forward.

### Compile fixes the decompile needs

| # | File | Fix |
| --- | --- | --- |
| 1 | `FGOLocalPlatform.csproj` | `TargetFramework` `net6.0` -> `net6.0-windows` (`UseWPF` requires the Windows TFM) |
| 2 | `FGOLocalPlatform.csproj` | add `ApplicationDefinition` for `app.xaml` and `Page` items for the other 12 XAML files; explicit `Compile` items with `EnableDefaultItems=false` |
| 3 | `FGOLocalPlatform.csproj` | `platform.ico` from `EmbeddedResource` to `Resource` (they are addressed by pack URI, which reads `.g.resources`). The two JSON tables stay `EmbeddedResource` - they are read with `GetManifestResourceStream` |
| 4 | XAML file names | `FGOLocalPlatform.MainWindow.xaml` -> `mainwindow.xaml`, and the same for the other 11: the resource key comes from the file path and the originals are `mainwindow.baml` etc. at the root |
| 5 | `app.xaml` | add `StartupUri="MainWindow.xaml"`; the original sets it in the generated `App.InitializeComponent`, which the BAML decompiler does not reproduce |
| 6 | `MainWindow.cs`, `ThemedMessageBox.cs` | 6 occurrences of `((Rect)(ref x)).Height` / `((Size)(ref x)).Width` reduced to `x.Height` / `x.Width` |
| 7 | `MainWindow.cs` | remove the generated `_CreateDelegate` helper (PresentationBuildTasks regenerates it from `mainwindow.xaml`) |
| 8 | `PhotoWindow.cs`, `MainWindow.cs` | `(val - 70 > 1 && val - 116 > 5) \|\| 1 == 0` -> `val != Key.LWin && val != Key.RWin && (val < Key.LeftShift || val > Key.RightAlt)`. Semantic fix: the original excludes `Key.LWin`/`Key.RWin` and the six modifier keys from key binding; the signed rendering rejects almost every key |
| 9 | `MainWindow.cs` | remove the dead `int num; _ = num - 1; _ = 1;` |
| 10 | `MainWindow.cs` | `new(string, double)[10]` -> `new (string Name, double Value)[10]`; the LINQ query below it addresses `item.Name` / `item.Value` |

Expect zero errors and zero warnings: the project builds with `Nullable` set to `annotations`, so the
decompile's `?` annotations compile without `CS8632`, and the only suppression left is a `#pragma` around
the XInput structures. A fresh decompile also brings back the leftovers the tree has been scrubbed of:
`//IL_` comments, numeric `Key` codes, `(DependencyObject)(object)` casts, `_ = 1;` statements and the
unwired handlers, so expect to remove them again.

### Things the translation must not change

- Every XAML `Tag` value, every `SelectedIndex` order, and the item order of any combo box whose
  index is persisted (cursor mode, input mode, movement and controller-number selectors, the two
  summon filters, photo-mode weapon mode and bone array).
- Resolution captions: they are parsed back with `^\s*(\d{3,4})\s*[xX×]\s*(\d{3,4})\s*$`, so they
  must stay in `1920x1080` form.
- The JSON keys and field names of `FGOLocalPlatform.CardNames.json` (`SVT#####` / `CE#####`,
  `Japanese` / `Chinese`) and `FGOLocalPlatform.CraftEffects.json` (`CE#####`, `Normal`,
  `NormalJapanese`, `Maximum`, `MaximumJapanese`) - English goes into the existing `Chinese`,
  `Normal` and `Maximum` fields.
- Config keys and enum values shared with `FGO_Launcher.ps1` and the author's tooling
  (`windowed` / `borderless` / `exclusive`, `keyboard` / `xinput`, `16:9`, the `graphics` block).
- Interpolation holes in format strings, `StringFormat` placeholders, and the `|` in the
  `OpenFileDialog` filter.

## Publishing a release

`RELEASING.md` has the step-by-step version of this.

The launcher checks `https://api.github.com/repos/<owner>/<repo>/releases/latest` once per start and
installs from the release's own assets, so a release has to carry both of them:

1. Set the version in `src\FGOLocalPlatform.csproj` (`<Version>`). Nothing else holds a version
   number: the About page and the updater read it from the assembly, and `package.ps1` reads it back
   off the built launcher.
2. `publish.cmd`, then `package.ps1 -GameRoot <install root>`. That writes
   `release\FGOAC-scooby-v<ver>.zip` and `FGOAC-scooby-v<ver>.zip.sha256` beside it.
3. Tag the commit `v<ver>` (for example `v1.1.0`) and push the tag.
4. Create the GitHub release on that tag and upload **both** files as assets: the
   `FGOAC-scooby-v<ver>.zip` and its `.zip.sha256`. The updater looks for an asset whose name starts
   with `FGOAC-scooby-v` and ends in `.zip`, and for the `.zip.sha256` beside it; a release missing
   either one is logged in `logs\update.log` and skipped rather than half-installed.
5. `src\FGOLocalPlatform\UpdateSettings.cs` holds the owner and repository the launcher asks
   (`githubuser420x` / `FGOAC-scooby`).

Updating a running launcher: the patch script cannot overwrite the executable that is running it, so
it stages the new one as `FGOAC scooby.exe.new`. The launcher then writes `%TEMP%\update-swap.cmd`,
which waits for it to close, moves the staged file into place and starts it again.

## Assets that are not code

- `src\platform.ico` is the game's own icon, icon group 0 of `App\ago.exe`, pulled out by
  `src\assets\extract-icon.ps1`. It is the executable icon, the window icon and the mark in the top
  bar. The game ships one 32x32 frame, so that is what the file holds.
- `docs\GUIDE_EN.pdf` is printed from `docs\GUIDE_EN.md`: the markdown rendered to an HTML page with the
  guide's own stylesheet, then Chrome headless with `--print-to-pdf`. Reprint it whenever the markdown
  changes; the package carries both.
- There is no bundled typeface. Everything is set in Segoe UI, which every supported Windows has.

## Overlay

`overlay\` mirrors the install root. The release packager copies it over an existing V1.01 install
after backing up what it replaces; the `App` and `Server` folders of the install used for
development are left alone.

| Overlay file | Why |
| --- | --- |
| `App\FGO_EnvironmentCheck.ps1` | its whole stdout is the environment-check panel |
| `App\FGO_Launcher.ps1` | two lines reach the launcher log panel |
| `App\FGO_StartupChecks.ps1` | one line reaches the startup failure dialog |
| `Server\Start-FGOLocalServer.ps1` | three lines reach the server log panel |
| `Server\Stop-FGOLocalServerWhenIdle.ps1` | also recognises `FGOAC scooby.exe` as a running front end |
| `Server\tools\fgo_account.py` | every `message` it emits is shown verbatim by the account page |
| `Server\tools\fgo_server_config.py` | port and address validation errors on the server page |
| `Server\artemis\titles\fgo\data\summon_candidates.json` | the acquisition notes shown in the Draw Rates status line. The Japanese card names and quest titles in that file stay as they are - the column they feed is labelled Japanese Name |

## The English patch

`patch\Apply-EN-Patch.ps1` is what a player runs, and what the launcher runs for them on first
start. It works against a release package laid out like this, which is also what unzipping the
package into the game folder produces:

```
FGOAC scooby.exe
Apply-EN-Patch.ps1
manifest.json
payload\App\zh\...
payload\App\FGO_EnvironmentCheck.ps1
payload\Server\...
```

`manifest.json` maps each install-relative path to its SHA-256, and carries the patch version and a
`manifestHash` over the whole list. `patch\Build-Manifest.ps1 -PackageRoot <dir> -Version <v>` writes
it from a staged package; `package.ps1` calls it, so the manifest always describes the bytes that
ship.

The apply run finds the install (its own folder, then the parent, then a scan of the fixed drives,
then a folder picker), refuses drive E: and Y:, refuses to run while the game or a launcher is open
from that folder, backs up every file it replaces to `_en-patch-backup\<timestamp>\`, copies, checks
the copies against the manifest, sets `chineseEnabled` in `App\fgo-launcher.json` and writes
`App\zh\en-patch.json` with the version and the manifest hash. A second run with the same manifest
does nothing. `-Rollback` restores the newest backup, using the `en-patch-restore.json` written
beside it to also remove the files the patch added. Exit codes are listed at the top of the script.

Accounts, decks, `Server\state`, the database and the rest of `App\fgo-launcher.json` are never
written; `Apply-EN-Patch.ps1` refuses a manifest that lists a path under any of them.
