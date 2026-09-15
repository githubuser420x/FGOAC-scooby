<#
Applies the FGOAC scooby English patch to an FGO Arcade local-platform install.

The script is shipped at the root of the release package, next to "FGOAC scooby.exe",
manifest.json and the payload folder. Unzipping the package into the game folder already puts
every file beside the install, so a normal run copies the payload into place, checks every file
against manifest.json and writes the marker App\zh\en-patch.json. Re-running the same version
does nothing, unless a platform update has since put its own scripts and text over the patched
ones; then the files that differ are copied back.

Usage
  .\Apply-EN-Patch.ps1
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA -NonInteractive
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA -Rollback

The script asks Windows for administrator rights only when the game folder cannot be written to
as it stands, so a normal install on a data drive applies the patch without a prompt.

What it never touches: accounts, decks, Server\state, the database, and App\fgo-launcher.json
apart from setting chineseEnabled to true, which is the switch that makes the game load the
App\zh folder the patch fills with English.

Exit codes
  0  the patch was applied, was already up to date, or the rollback finished
  1  an unexpected error; the message says what failed
  2  no FGO Arcade install was found, or the folder given is not one
  3  the install is on drive E: or Y:, where the game cannot run
  4  the game, the server or a launcher is still running from that folder
  5  the patch package is incomplete, or lists a file the patch is not allowed to write
  6  a file did not match its checksum after it was copied
  7  -Rollback found no backup to restore
  8  cancelled: no folder was chosen
  9  the install has not had Cloud23333's V1.01 update, which the English scripts need
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = '',
    [string]$PackageRoot = '',
    [switch]$Rollback,
    [switch]$Force,
    [switch]$NonInteractive,
    [int]$IgnoreProcessId = 0
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptHost = [IO.Path]::Combine($PSHOME, 'pwsh.exe')
if (!(Test-Path -LiteralPath $scriptHost -PathType Leaf)) { $scriptHost = [IO.Path]::Combine($PSHOME, 'powershell.exe') }
if ([string]::IsNullOrWhiteSpace($PackageRoot)) { $PackageRoot = $PSScriptRoot }

$ProtectedPrefixes = @('Server\state\', 'Server\data\', 'DEVICE\', 'AMFS\', 'GameData\', '_en-patch-backup\', '_update-backup\')
$ProtectedFiles = @('App\fgo-launcher.json', 'App\deck.json', 'App\deck.json.bak')
# "FGOA scooby.exe" is the name this launcher shipped under before 1.1.0. It stays in the list so an
# upgrade still refuses to run while the old build is open.
$RunningNames = @('ago.exe', 'amdaemon.exe', 'inject.exe', 'FGOLocalPlatform.exe', 'FGOAC scooby.exe', 'FGOA scooby.exe')
$RetiredLauncherName = 'FGOA scooby.exe'

function Test-FgoInstallRoot {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ([IO.File]::Exists([IO.Path]::Combine($Path, 'App\ago.exe')) -and
        [IO.File]::Exists([IO.Path]::Combine($Path, 'App\fgo-launcher.json')) -and
        [IO.Directory]::Exists([IO.Path]::Combine($Path, 'Server')))
}

function Find-FgoInstallRoot {
    foreach ($candidate in @($PackageRoot, (Split-Path -Parent $PackageRoot))) {
        if (Test-FgoInstallRoot -Path $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    $skip = @('Windows', 'Program Files', 'Program Files (x86)', 'ProgramData', '$Recycle.Bin', 'System Volume Information')
    foreach ($drive in (Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object -ExpandProperty DeviceID)) {
        $root = $drive + '\'
        if (Test-FgoInstallRoot -Path $root) { return $root }
        $first = @(Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $skip -notcontains $_.Name })
        foreach ($level1 in $first) {
            if (Test-FgoInstallRoot -Path $level1.FullName) { return $level1.FullName }
        }
        foreach ($level1 in $first) {
            foreach ($level2 in @(Get-ChildItem -LiteralPath $level1.FullName -Directory -Force -ErrorAction SilentlyContinue)) {
                if (Test-FgoInstallRoot -Path $level2.FullName) { return $level2.FullName }
            }
        }
    }
    return ''
}

function Select-FgoInstallRoot {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.Application]::EnableVisualStyles()
    $picker = New-Object Windows.Forms.FolderBrowserDialog
    $picker.Description = 'Select your FGO Arcade folder - the one that holds the App and Server folders.'
    $picker.ShowNewFolderButton = $false
    try {
        while ($picker.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) {
            if (Test-FgoInstallRoot -Path $picker.SelectedPath) { return $picker.SelectedPath }
            [void][Windows.Forms.MessageBox]::Show(
                "That folder is not an FGO Arcade install. Choose the folder that holds App\ago.exe and the Server folder." + [Environment]::NewLine + [Environment]::NewLine + "You chose: " + $picker.SelectedPath,
                'FGOAC scooby - English patch', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Warning)
        }
        return ''
    } finally { $picker.Dispose() }
}

function Get-FgoDestinationPath {
    param([string]$Root, [string]$Relative)
    if ($Relative -match '^[A-Za-z]:' -or $Relative.StartsWith('\') -or $Relative.Split('\') -contains '..') {
        throw "The patch package lists an invalid path: $Relative"
    }
    foreach ($prefix in $ProtectedPrefixes) {
        if ($Relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The patch package lists a file the patch is not allowed to write: $Relative"
        }
    }
    foreach ($protected in $ProtectedFiles) {
        if ($Relative.Equals($protected, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The patch package lists a file the patch is not allowed to write: $Relative"
        }
    }
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, $Relative))
    if (!$full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "The patch package lists a path outside the game folder: $Relative"
    }
    return $full
}

function Get-FgoFileHash {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Clear-FgoReadOnly {
    param([string]$Path)
    if ([IO.File]::Exists($Path)) {
        $attributes = [IO.File]::GetAttributes($Path)
        if ($attributes -band [IO.FileAttributes]::ReadOnly) {
            [IO.File]::SetAttributes($Path, $attributes -band (-bnot [IO.FileAttributes]::ReadOnly))
        }
    }
}

function Copy-FgoFile {
    param([string]$Source, [string]$Destination)
    $parent = Split-Path -Parent $Destination
    if (!([IO.Directory]::Exists($parent))) { [void][IO.Directory]::CreateDirectory($parent) }
    Clear-FgoReadOnly -Path $Destination
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    Clear-FgoReadOnly -Path $Destination
}

function Write-FgoJson {
    param([string]$Path, $Value)
    $parent = Split-Path -Parent $Path
    if (!([IO.Directory]::Exists($parent))) { [void][IO.Directory]::CreateDirectory($parent) }
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
}

function Test-FgoWritable {
    param([string]$Path)
    if (!([IO.Directory]::Exists($Path))) { return $false }
    $probe = [IO.Path]::Combine($Path, '.fgoa-scooby-' + [guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllText($probe, 'write check')
        [IO.File]::Delete($probe)
        return $true
    } catch { return $false }
}

function Stop-WithMessage {
    param([string]$Message, [int]$Code)
    Write-Host $Message
    exit $Code
}

# The folder picker needs an STA thread, the same way the author's updater gets one.
if ([string]::IsNullOrWhiteSpace($InstallRoot) -and !$NonInteractive -and
    [Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    $arguments = @('-NoLogo', '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($PackageRoot -ne $PSScriptRoot) { $arguments += @('-PackageRoot', $PackageRoot) }
    if ($Rollback) { $arguments += '-Rollback' }
    if ($Force) { $arguments += '-Force' }
    & $scriptHost @arguments
    exit $LASTEXITCODE
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    Write-Host 'Looking for your FGO Arcade folder...'
    $InstallRoot = Find-FgoInstallRoot
    if ([string]::IsNullOrWhiteSpace($InstallRoot) -and !$NonInteractive) { $InstallRoot = Select-FgoInstallRoot }
    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        Stop-WithMessage 'No FGO Arcade folder was chosen, so nothing was changed.' 8
    }
}
if (!(Test-FgoInstallRoot -Path $InstallRoot)) {
    Stop-WithMessage "That folder is not an FGO Arcade install: $InstallRoot. Choose the folder that holds App\ago.exe, App\fgo-launcher.json and the Server folder." 2
}
$InstallRoot = (Resolve-Path -LiteralPath $InstallRoot).Path.TrimEnd('\')
$PackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd('\')
$rootPrefix = $InstallRoot + '\'
$driveLetter = $InstallRoot.Substring(0, 1).ToUpperInvariant()
if ($driveLetter -eq 'E' -or $driveLetter -eq 'Y') {
    Stop-WithMessage "The game cannot run from drive ${driveLetter}: - its own file hook sends every ${driveLetter}: path to the cabinet data mount, so the game stops with ERROR 4104. Move the whole $InstallRoot folder to another drive, such as D:, and run the patch again." 3
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevationNeeded = !(Test-FgoWritable -Path $InstallRoot) -or !(Test-FgoWritable -Path ([IO.Path]::Combine($InstallRoot, 'App')))
if ($elevationNeeded -and ![Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $command = "& '" + $PSCommandPath.Replace("'", "''") + "' -InstallRoot '" + $InstallRoot.Replace("'", "''") + "' -PackageRoot '" + $PackageRoot.Replace("'", "''") + "' -NonInteractive"
    if ($Rollback) { $command += ' -Rollback' }
    if ($Force) { $command += ' -Force' }
    $transcript = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'fgoa-scooby-patch-' + [guid]::NewGuid().ToString('N') + '.log')
    $quoted = $transcript.Replace("'", "''")
    $command = "try { $command *> '$quoted' } catch { " + '$_' + " | Out-String | Out-File -LiteralPath '$quoted' -Append; exit 1 }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $elevated = Start-Process -FilePath $scriptHost -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    if (Test-Path -LiteralPath $transcript) {
        Get-Content -LiteralPath $transcript
        Remove-Item -LiteralPath $transcript -Force
    }
    exit $elevated.ExitCode
}

$running = @(Get-CimInstance Win32_Process | Where-Object {
    $RunningNames -contains $_.Name -and $_.ProcessId -ne $IgnoreProcessId -and
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
})
if ($running) {
    Stop-WithMessage ("Close the game and the launcher in $InstallRoot first, then run the patch again. Still running: " + (($running | ForEach-Object { $_.Name }) -join ', ')) 4
}

$backupRoot = [IO.Path]::Combine($InstallRoot, '_en-patch-backup')
$markerPath = [IO.Path]::Combine($InstallRoot, 'App\zh\en-patch.json')

if ($Rollback) {
    if (!([IO.Directory]::Exists($backupRoot))) { Stop-WithMessage "There is no patch backup to restore in $backupRoot." 7 }
    $backup = Get-ChildItem -LiteralPath $backupRoot -Directory | Sort-Object -Property Name | Select-Object -Last 1
    if (!$backup) { Stop-WithMessage "There is no patch backup to restore in $backupRoot." 7 }
    $recordPath = [IO.Path]::Combine($backup.FullName, 'en-patch-restore.json')
    if (!([IO.File]::Exists($recordPath))) {
        Stop-WithMessage "The backup in $($backup.FullName) has no en-patch-restore.json, so it cannot be rolled back automatically." 7
    }
    $record = Get-Content -LiteralPath $recordPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "Restoring the files saved in $($backup.FullName)..."
    $restored = 0
    foreach ($relative in @($record.replaced)) {
        $saved = [IO.Path]::Combine($backup.FullName, $relative)
        if ([IO.File]::Exists($saved)) {
            Copy-FgoFile -Source $saved -Destination ([IO.Path]::Combine($InstallRoot, $relative))
            $restored++
        }
    }
    $removed = 0
    foreach ($relative in @($record.added)) {
        $added = [IO.Path]::Combine($InstallRoot, $relative)
        if ([IO.File]::Exists($added)) {
            Clear-FgoReadOnly -Path $added
            [IO.File]::Delete($added)
            $removed++
        }
    }
    $savedMarker = [IO.Path]::Combine($backup.FullName, 'App\zh\en-patch.json')
    if ([IO.File]::Exists($savedMarker)) { Copy-FgoFile -Source $savedMarker -Destination $markerPath }
    elseif ([IO.File]::Exists($markerPath)) { [IO.File]::Delete($markerPath) }
    Write-Host "Rollback finished: $restored files restored, $removed files removed."
    Write-Host 'The launcher settings were left as they are; turn English Text off in the launcher if you want the Chinese set back.'
    exit 0
}

# App\FGO_Runtime.dll came with Cloud23333's V1.01 update, and the English launch and server
# scripts in this package load it. On the original 11.00 package they would leave the server
# unable to start, so the patch stops here instead.
if (!([IO.File]::Exists([IO.Path]::Combine($InstallRoot, 'App\FGO_Runtime.dll')))) {
    Stop-WithMessage "This game folder has not had Cloud23333's V1.01 update yet: App\FGO_Runtime.dll is missing, and the English patch needs it. Apply his V1.01 or V1.02 update to $InstallRoot first, then run FGOAC scooby again." 9
}

$manifestPath = [IO.Path]::Combine($PackageRoot, 'manifest.json')
if (!([IO.File]::Exists($manifestPath))) {
    Stop-WithMessage "The patch package is incomplete: manifest.json is missing from $PackageRoot. Unzip the whole package again." 5
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$manifest.version
$manifestHash = [string]$manifest.manifestHash
$entries = @($manifest.files.PSObject.Properties)
if ($entries.Count -eq 0) { Stop-WithMessage 'The patch package is incomplete: manifest.json lists no files.' 5 }

if (!$Force -and [IO.File]::Exists($markerPath)) {
    try {
        $marker = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]$marker.version -eq $version -and [string]$marker.manifestHash -eq $manifestHash) {
            # A platform update copies its own launch scripts, server tools and Chinese text over
            # the patched ones and leaves the marker alone, so the marker on its own is not proof.
            # The files outside App\zh\rom are few and small, and an update overwrites those too,
            # so checking them on every run is cheap and enough.
            $replaced = ''
            foreach ($entry in $entries) {
                if ($entry.Name.StartsWith('App\zh\rom\', [StringComparison]::OrdinalIgnoreCase) -or $entry.Name -eq 'FGOAC scooby.exe') { continue }
                $installed = [IO.Path]::Combine($InstallRoot, $entry.Name)
                if (!([IO.File]::Exists($installed)) -or (Get-FgoFileHash -Path $installed) -ne [string]$entry.Value) { $replaced = $entry.Name; break }
            }
            if ($replaced -eq '') {
                Write-Host "The English patch version $version is already installed in $InstallRoot. Nothing was changed."
                exit 0
            }
            Write-Host "The English patch version $version is recorded as installed, but $replaced has been replaced since, usually by a platform update, so the patch is applied again."
        }
    } catch {
        Write-Host 'The patch marker already there could not be read, so the patch will be applied again.'
    }
}

Write-Host "Applying the English patch version $version to $InstallRoot"
$payloadRoot = [IO.Path]::Combine($PackageRoot, 'payload')
$plan = New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in $entries) {
    $relative = $entry.Name
    try { $destination = Get-FgoDestinationPath -Root $InstallRoot -Relative $relative }
    catch { Stop-WithMessage $_.Exception.Message 5 }
    $source = [IO.Path]::Combine($payloadRoot, $relative)
    if (!([IO.File]::Exists($source))) { $source = [IO.Path]::Combine($PackageRoot, $relative) }
    if (!([IO.File]::Exists($source))) {
        Stop-WithMessage "The patch package is incomplete: $relative is missing. Unzip the whole package again." 5
    }
    $plan.Add([pscustomobject]@{
        Relative    = $relative
        Source      = $source
        Destination = $destination
        Expected    = [string]$entry.Value
        InPlace     = $source.Equals($destination, [StringComparison]::OrdinalIgnoreCase)
        IsLauncher  = [IO.Path]::GetFileName($destination) -eq 'FGOAC scooby.exe'
    })
}

# When the package sits somewhere else, as it does when a launcher's updater unpacks it into the
# temporary folder, the game folder gets its own copy of the package first - installer, manifest,
# payload, the rest - the way an unzip by hand leaves it, so the next start finds the version it is
# on and a later re-apply or -Rollback has its files. The launcher is left to its manifest entry,
# which stages it as .new while it runs; manifest.json goes last, so a copy that stops half way
# still describes the previous package.
if (!$PackageRoot.Equals($InstallRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host 'Copying the package into the game folder...'
    $packagePrefix = $PackageRoot + '\'
    foreach ($file in (Get-ChildItem -LiteralPath $PackageRoot -File -Recurse)) {
        if ($file.Name -eq 'FGOAC scooby.exe' -or $file.FullName.Equals($manifestPath, [StringComparison]::OrdinalIgnoreCase)) { continue }
        Copy-FgoFile -Source $file.FullName -Destination ([IO.Path]::Combine($InstallRoot, $file.FullName.Substring($packagePrefix.Length)))
    }
    Copy-FgoFile -Source $manifestPath -Destination ([IO.Path]::Combine($InstallRoot, 'manifest.json'))
}

Write-Host "Checking $($plan.Count) files against the package..."
$toCopy = New-Object 'System.Collections.Generic.List[object]'
$checked = 0
foreach ($item in $plan) {
    $checked++
    if (($checked % 400) -eq 0) { Write-Host "  checked $checked of $($plan.Count) files" }
    if ($item.InPlace) { continue }
    if ([IO.File]::Exists($item.Destination) -and (Get-FgoFileHash -Path $item.Destination) -eq $item.Expected) { continue }
    $toCopy.Add($item)
}

if ($toCopy.Count -eq 0) {
    Write-Host 'Every file was already in place, so only the marker needed writing.'
} else {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backup = [IO.Path]::Combine($backupRoot, $stamp)
    [void][IO.Directory]::CreateDirectory($backup)
    $replaced = New-Object 'System.Collections.Generic.List[string]'
    $added = New-Object 'System.Collections.Generic.List[string]'
    Write-Host "Backing up the files that will be replaced to $backup"
    foreach ($item in $toCopy) {
        if ([IO.File]::Exists($item.Destination)) {
            Copy-FgoFile -Source $item.Destination -Destination ([IO.Path]::Combine($backup, $item.Relative))
            $replaced.Add($item.Relative)
        } else {
            $added.Add($item.Relative)
        }
    }
    if ([IO.File]::Exists($markerPath)) {
        Copy-FgoFile -Source $markerPath -Destination ([IO.Path]::Combine($backup, 'App\zh\en-patch.json'))
    }
    Write-FgoJson -Path ([IO.Path]::Combine($backup, 'en-patch-restore.json')) -Value ([ordered]@{
        version  = $version
        savedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        replaced = @($replaced)
        added    = @($added)
    })

    Write-Host "Copying $($toCopy.Count) files..."
    $copied = 0
    foreach ($item in $toCopy) {
        try {
            Copy-FgoFile -Source $item.Source -Destination $item.Destination
        } catch {
            if (!$item.IsLauncher) { throw }
            # A running program cannot overwrite its own file, so the new launcher is left beside the
            # old one. The launcher swaps it in and restarts itself when it closes.
            $item.Destination = $item.Destination + '.new'
            Copy-FgoFile -Source $item.Source -Destination $item.Destination
            Write-Host 'The launcher is open, so the new one was staged next to it and is swapped in when it closes.'
        }
        $copied++
        if (($copied % 200) -eq 0) { Write-Host "  copied $copied of $($toCopy.Count) files" }
    }

    Write-Host 'Checking the copied files...'
    $bad = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in $toCopy) {
        if ((Get-FgoFileHash -Path $item.Destination) -ne $item.Expected) { $bad.Add($item.Relative) }
    }
    if ($bad.Count -gt 0) {
        Write-Host 'These files did not match their checksum after copying, so the patch is not complete:'
        foreach ($relative in $bad) { Write-Host "  $relative" }
        Write-Host "Run the patch again; if it keeps failing, check your antivirus and the free space on drive ${driveLetter}:. The replaced files are in $backup, and -Rollback puts them back."
        exit 6
    }
    Write-Host "Backup of the replaced files: $backup"
}

# Before 1.1.0 the launcher was called "FGOA scooby.exe". Once the new one is in place the old file is
# only a second icon that starts an out-of-date build, so it goes.
$retiredLauncher = [IO.Path]::Combine($InstallRoot, $RetiredLauncherName)
if ([IO.File]::Exists($retiredLauncher) -and [IO.File]::Exists([IO.Path]::Combine($InstallRoot, 'FGOAC scooby.exe'))) {
    try {
        Clear-FgoReadOnly -Path $retiredLauncher
        Remove-Item -LiteralPath $retiredLauncher -Force
        Write-Host "Removed $RetiredLauncherName, which this version replaces."
    } catch {
        Write-Host "$RetiredLauncherName is still there and could not be removed: $($_.Exception.Message). Delete it yourself so you do not start the old build by mistake."
    }
}

# chineseEnabled is the switch that makes the game read App\zh, which now holds the English set.
$configurationPath = [IO.Path]::Combine($InstallRoot, 'App\fgo-launcher.json')
try {
    $configuration = Get-Content -LiteralPath $configurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($configuration.chineseEnabled -ne $true) {
        $configuration | Add-Member -NotePropertyName 'chineseEnabled' -NotePropertyValue $true -Force
        Write-FgoJson -Path $configurationPath -Value $configuration
        Write-Host 'Turned the English text on in the launcher settings.'
    }
} catch {
    Write-Host "The English text switch could not be set in App\fgo-launcher.json: $($_.Exception.Message)"
    Write-Host 'Open the launcher and turn English Text on yourself.'
}

# Windows marks every file that came out of a downloaded zip, and Windows PowerShell then refuses
# to load App\FGO_Runtime.dll (0x80131515). A platform update puts the mark back, so this runs on
# every pass, not only when files were copied.
$marked = @(Get-ChildItem -LiteralPath ([IO.Path]::Combine($InstallRoot, 'App')) -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.dll', '.exe', '.ps1' }) +
    @(Get-ChildItem -LiteralPath ([IO.Path]::Combine($InstallRoot, 'Server')) -Filter '*.ps1' -File -ErrorAction SilentlyContinue)
foreach ($file in $marked) { Unblock-File -LiteralPath $file.FullName -ErrorAction SilentlyContinue }

Write-FgoJson -Path $markerPath -Value ([ordered]@{
    version      = $version
    appliedUtc   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    manifestHash = $manifestHash
})
Write-Host "The English patch version $version is installed in $InstallRoot."
exit 0
