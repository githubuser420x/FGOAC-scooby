[CmdletBinding()]
param(
    [ValidateSet("xinput", "keyboard")]
    [string]$InputMode,

    [ValidateSet("windowed", "borderless", "exclusive")]
    [string]$DisplayMode,

    [string]$MonitorDevice,

    [ValidateRange(480, 7680)]
    [int]$ResolutionWidth,

    [ValidateRange(480, 7680)]
    [int]$ResolutionHeight,

    [ValidateSet(60, 90, 120, 144)]
    [int]$TargetFps,

    [ValidateSet(100, 125, 150, 200)]
    [int]$RenderScale,

    [switch]$Windowed,
    [switch]$SkipServerCheck,
    [switch]$Chinese,
    [switch]$EnableExperimentalAudio,
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$script:transcriptActive = $false

$gameRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$installRoot = Split-Path -Parent $gameRoot
$amfsRoot = Join-Path $installRoot "AMFS"
$baseIni = Join-Path $gameRoot "segatools.ini"
$launcherConfigPath = Join-Path $gameRoot "fgo-launcher.json"
$injectPath = Join-Path $gameRoot "inject.exe"
$hookPath = Join-Path $gameRoot "fgohook.dll"
$pendingHookPath = Join-Path $gameRoot "fgohook.pending.dll"
$glCompatPath = Join-Path $gameRoot "fgoglcompat.dll"
$amdaemonPath = Join-Path $gameRoot "am\amdaemon.exe"
$localeHookPath = Join-Path $gameRoot "Tools\Locale_Remulator\LRHookx64.dll"
$audioHookPath = Join-Path $gameRoot "FGOAudio.dll"
$chineseHookPath = Join-Path $gameRoot "zh\fgozh.dll"
$pendingChineseHookPath = Join-Path $gameRoot "zh\fgozh.pending.dll"
$agoPath = Join-Path $gameRoot "ago.exe"
$deviceRoot = Join-Path $installRoot "DEVICE"
$runtimeDirectory = Join-Path $deviceRoot "runtime"
$logDirectory = Join-Path $installRoot "logs"
$runtimeIni = Join-Path $runtimeDirectory "segatools.runtime.ini"
$amdaemonMainConfig = Join-Path $runtimeDirectory "amdaemon_main.json"
$injectLiveLogPath = Join-Path $logDirectory "fgo-inject-live.log"

function Stop-WithMessage {
    param(
        [string]$Message,
        [int]$ExitCode = 1
    )

    Write-Host ""
    Write-Host "ERROR: $Message" -ForegroundColor Red
    if ($script:transcriptActive) {
        Stop-Transcript | Out-Null
        $script:transcriptActive = $false
    }
    Write-Host "[FGO-LAUNCHER:$ExitCode] $Message"
    exit $ExitCode
}

function Set-IniValue {
    param(
        [string]$Path,
        [string]$Section,
        [string]$Key,
        [string]$Value
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    $text = [System.IO.File]::ReadAllText($Path, $encoding)
    $headerPattern = "(?m)^\[" + [Regex]::Escape($Section) + "\]\s*$"
    $header = [Regex]::Match($text, $headerPattern)

    if (-not $header.Success) {
        $text = $text.TrimEnd() + "`r`n`r`n[$Section]`r`n$Key=$Value`r`n"
        [System.IO.File]::WriteAllText($Path, $text, $encoding)
        return
    }

    $sectionStart = $header.Index + $header.Length
    $nextHeaderRegex = New-Object System.Text.RegularExpressions.Regex("(?m)^\[[^\]]+\]\s*$")
    $nextHeader = $nextHeaderRegex.Match($text, $sectionStart)
    $sectionEnd = if ($nextHeader.Success) { $nextHeader.Index } else { $text.Length }
    $sectionBody = $text.Substring($sectionStart, $sectionEnd - $sectionStart)
    $keyPattern = "(?m)^(\s*" + [Regex]::Escape($Key) + "\s*=).*$"
    $keyMatch = [Regex]::Match($sectionBody, $keyPattern)

    if ($keyMatch.Success) {
        $replacement = $keyMatch.Groups[1].Value + $Value
        $sectionBody = $sectionBody.Substring(0, $keyMatch.Index) + $replacement + $sectionBody.Substring($keyMatch.Index + $keyMatch.Length)
    }
    else {
        $sectionBody = "`r`n$Key=$Value" + $sectionBody
    }

    $text = $text.Substring(0, $sectionStart) + $sectionBody + $text.Substring($sectionEnd)
    [System.IO.File]::WriteAllText($Path, $text, $encoding)
}

function ConvertTo-WindowsCommandLineArgument {
    param(
        [AllowEmptyString()]
        [string]$Argument
    )

    # ProcessStartInfo.ArgumentList is unavailable in Windows PowerShell 5.1.
    # Quote exactly as the Microsoft C runtime parses argv: backslashes are
    # doubled only when they precede a quote or the closing quote.
    if ($null -eq $Argument) {
        $Argument = ""
    }
    if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') {
        return $Argument
    }

    $quoted = New-Object System.Text.StringBuilder
    [void]$quoted.Append([char]34)
    $backslashes = 0

    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq [char]92) {
            $backslashes++
            continue
        }

        if ($character -eq [char]34) {
            if ($backslashes -gt 0) {
                [void]$quoted.Append([char]92, ($backslashes * 2))
            }
            [void]$quoted.Append([char]92)
            [void]$quoted.Append([char]34)
            $backslashes = 0
            continue
        }

        if ($backslashes -gt 0) {
            [void]$quoted.Append([char]92, $backslashes)
            $backslashes = 0
        }
        [void]$quoted.Append($character)
    }

    if ($backslashes -gt 0) {
        [void]$quoted.Append([char]92, ($backslashes * 2))
    }
    [void]$quoted.Append([char]34)

    return $quoted.ToString()
}

function Test-TcpPort {
    param(
        [string]$HostName,
        [int]$Port,
        [int]$TimeoutMilliseconds = 1200
    )

    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $result = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $result.AsyncWaitHandle.WaitOne($TimeoutMilliseconds, $false)) {
            return $false
        }
        $client.EndConnect($result)
        return $true
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Get-ActiveAudioRenderEndpoints {
    $renderRoot = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render"

    if (-not (Test-Path -LiteralPath $renderRoot)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $renderRoot -ErrorAction Stop |
            ForEach-Object {
                $device = Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction Stop

                # Windows DEVICE_STATE_ACTIVE is bit 0. Some USB endpoints also
                # carry an implementation-specific high bit, so compare the bit
                # instead of requiring the registry value to equal exactly 1.
                if (([int64]$device.DeviceState -band 1) -eq 0) {
                    return
                }

                $properties = Get-ItemProperty `
                    -LiteralPath (Join-Path $_.PSPath "Properties") `
                    -ErrorAction SilentlyContinue
                $friendlyName = if ($null -ne $properties) {
                    [string]$properties.'{a45c254e-df1c-4efd-8020-67d146a850e0},14'
                }
                else {
                    ""
                }

                if ([string]::IsNullOrWhiteSpace($friendlyName) -and
                        $null -ne $properties) {
                    $friendlyName = [string]$properties.'{b3f8fa53-0004-438e-9003-51a46e139bfc},6'
                }

                if ([string]::IsNullOrWhiteSpace($friendlyName)) {
                    $friendlyName = $_.PSChildName
                }

                [PSCustomObject]@{
                    Id = $_.PSChildName
                    Name = $friendlyName
                }
            }
    )
}

Write-Host "FGO Arcade safe launcher" -ForegroundColor Cyan
Write-Host "Windows locale will not be changed. Segatools will provide JST and UTF conversion for the game process."

$requiredFiles = @(
    (Join-Path $gameRoot "FGO_LocalNetwork.ps1"),
    (Join-Path $gameRoot "FGO_StartupChecks.ps1"),
    (Join-Path $gameRoot "FGO_EnvironmentCheck.ps1"),
    $baseIni,
    $launcherConfigPath,
    $injectPath,
    $hookPath,
    (Join-Path $gameRoot "config.json"),
    $localeHookPath,
    $agoPath,
    $amdaemonPath,
    (Join-Path $amfsRoot "ICF1"),
    (Join-Path $amfsRoot "ICF2")
)

$missingFiles = $requiredFiles | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missingFiles) {
    Stop-WithMessage ("Required files are missing:`r`n" + ($missingFiles -join "`r`n")) 2
}

$audioEndpointInspectionSucceeded = $false
$activeAudioEndpoints = @()
try {
    $activeAudioEndpoints = @(Get-ActiveAudioRenderEndpoints)
    $audioEndpointInspectionSucceeded = $true
}
catch {
    Write-Warning "Could not inspect Windows audio render endpoints: $($_.Exception.Message)"
}

if ($audioEndpointInspectionSucceeded -and $activeAudioEndpoints.Count -eq 0) {
    Stop-WithMessage (
        "No active Windows audio output endpoint was found. " +
        "Connect or enable the intended headphones/speakers before launching FGO; " +
        "revision 11.00 binds its WASAPI endpoint only during startup."
    ) 5
}

try {
    $launcherConfig = Get-Content -LiteralPath $launcherConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $mainConfig = Get-Content -LiteralPath (Join-Path $gameRoot 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
} catch { Stop-WithMessage "Configuration JSON is invalid: $($_.Exception.Message)" 13 }
try {
    . (Join-Path $gameRoot 'FGO_StartupChecks.ps1')
    Test-FgoWritableLayout -InstallRoot $installRoot
} catch { Stop-WithMessage $_.Exception.Message 4 }

try {
    $environmentChecks = & (Join-Path $gameRoot 'FGO_EnvironmentCheck.ps1') -AsJson | ConvertFrom-Json
    $environmentFailures = @($environmentChecks | Where-Object { -not $_.Passed })
    if ($environmentFailures.Count -gt 0) {
        Stop-WithMessage (($environmentFailures | ForEach-Object { "$($_.Name): $($_.Detail)" }) -join "`r`n") 15
    }
} catch { Stop-WithMessage ("Environment check failed: "+$_.Exception.Message) 15 }
. (Join-Path $gameRoot 'FGO_LocalNetwork.ps1')
$network = Get-FgoNetworkPlan -ServerHost ([string]$launcherConfig.serverHost)
$localIp = $network.Cabinet
$subnet = $network.Subnet
$addrSuffix = $network.AddressSuffix
$routerSuffix = $network.RouterSuffix
$broadcast = $network.Broadcast
$serverHost = $network.Server
$env:FGO_LOCAL_NETWORK = if ($network.Local) { '1' } else { '0' }
$env:FGO_INSTALL_ROOT = $installRoot
$env:FGO_LOCAL_HTTP_PORT = [string]$launcherConfig.serverPorts.http
$env:FGO_LOCAL_BILLING_PORT = [string]$launcherConfig.serverPorts.billing
$env:FGO_LOCAL_AIME_PORT = [string]$launcherConfig.serverPorts.aime
$shouldStartLocalServer = [bool]$launcherConfig.autoStartLocalServer -and $network.Local

if ($shouldStartLocalServer -and -not $SkipServerCheck) {
    $localServerSetting = [string]$launcherConfig.localServerLauncher
    if ([string]::IsNullOrWhiteSpace($localServerSetting) -or
            [IO.Path]::GetFileName($localServerSetting) -eq "Start-FGOLocalServer.ps1") {
        $localServerSetting = "..\Server\Start-FGOLocalServer.ps1"
    }
    $localServerLauncher = [System.IO.Path]::GetFullPath((Join-Path $gameRoot $localServerSetting))
    if (-not (Test-Path -LiteralPath $localServerLauncher)) {
        Stop-WithMessage "The local FGO server launcher is missing: $localServerLauncher" 10
    }

    Write-Host "Starting/checking the local ALL.Net, billing, AimeDB, and SDEJ capture services..."
    try {
        & $localServerLauncher -ServerHost $serverHost
    }
    catch {
        Stop-WithMessage "The local FGO server could not start: $($_.Exception.Message)" 10
    }
    # Independent watcher survives a launcher cancellation and does not wait for
    # the frontend to close. It only stops services from this installation.
    $serverCleanup = Join-Path $installRoot 'Server\Stop-FGOLocalServerWhenIdle.ps1'
    $cleanupHost = (Get-Process -Id $PID).Path
    Start-Process -FilePath $cleanupHost -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $serverCleanup + '"'),
        '-LauncherProcessId', [string]$PID) | Out-Null
}

if (-not $SkipServerCheck) {
    $closedPorts = New-Object System.Collections.Generic.List[int]
    foreach ($port in @($launcherConfig.requiredPorts)) {
        if (-not (Test-TcpPort -HostName $network.ProbeHost -Port ([int]$port))) {
            $closedPorts.Add([int]$port)
        }
    }

    if ($closedPorts.Count -gt 0) {
        Stop-WithMessage "The configured server $serverHost is not reachable on required port(s): $($closedPorts -join ', ')." 11
    }
}

$effectiveInputMode = if ($InputMode) { $InputMode } else { [string]$launcherConfig.inputMode }
if ($effectiveInputMode -notin @("xinput", "keyboard")) {
    $effectiveInputMode = "xinput"
}
$effectiveCabinetMode = [string]$launcherConfig.cabinetMode
if ($effectiveCabinetMode -notin @("saved", "server", "satellite")) {
    $effectiveCabinetMode = "saved"
}
$effectiveGameVersion = [string]$launcherConfig.gameVersion
if ([string]::IsNullOrWhiteSpace($effectiveGameVersion)) {
    $effectiveGameVersion = "11.00"
}
if ($effectiveGameVersion -notmatch '^\d{1,2}\.\d{2}$') {
    Stop-WithMessage "Invalid gameVersion '$effectiveGameVersion'. Expected a value such as 11.00." 13
}
$configuredDisplayMode = [string]$launcherConfig.displayMode
if ($configuredDisplayMode -notin @("windowed", "borderless", "exclusive")) {
    $configuredDisplayMode = if ([bool]$launcherConfig.windowed) { "windowed" } else { "exclusive" }
}
$effectiveDisplayMode = if ($DisplayMode) { $DisplayMode } elseif ($Windowed) { "windowed" } else { $configuredDisplayMode }
$effectiveWindowed = $effectiveDisplayMode -ne "exclusive"
$effectiveFramed = $effectiveDisplayMode -eq "windowed"
$effectiveMonitorDevice = if ($PSBoundParameters.ContainsKey('MonitorDevice')) { $MonitorDevice } else { [string]$launcherConfig.monitorDevice }
if ($effectiveMonitorDevice -and $effectiveMonitorDevice -notmatch '^\\\\\.\\DISPLAY\d+$') {
    Stop-WithMessage "Invalid monitor device '$effectiveMonitorDevice'. Expected a Windows display device such as \\.\DISPLAY1." 14
}
$effectiveResolutionWidth = if ($ResolutionWidth -gt 0) { $ResolutionWidth } else { [int]$launcherConfig.resolutionWidth }
$effectiveResolutionHeight = if ($ResolutionHeight -gt 0) { $ResolutionHeight } else { [int]$launcherConfig.resolutionHeight }
if ($effectiveResolutionWidth -lt 480 -or $effectiveResolutionWidth -gt 7680 -or
        $effectiveResolutionHeight -lt 480 -or $effectiveResolutionHeight -gt 7680) {
    Stop-WithMessage "Invalid render resolution $($effectiveResolutionWidth)x$($effectiveResolutionHeight)." 14
}

# ago.exe exposes a full render surface plus a separate centered 16:9 UI safe
# area. fgohook patches the selected native mode to the exact requested size;
# this extends the 3D world on 16:10/ultrawide displays without stretching UI.
$nativeRenderArgument = "-hdtv1080"
$nativeRenderWidth = $effectiveResolutionWidth
$nativeRenderHeight = $effectiveResolutionHeight
$logicalRenderWidth = $effectiveResolutionWidth
$logicalRenderHeight = $effectiveResolutionHeight
$requestedAspectLeft = [int64]$effectiveResolutionWidth * 9
$requestedAspectRight = [int64]$effectiveResolutionHeight * 16
if ($requestedAspectLeft -gt $requestedAspectRight) {
    $nativeRenderArgument = "-wqhd"
}
elseif ($requestedAspectLeft -eq $requestedAspectRight) {
    if ($effectiveResolutionWidth -le 1280 -and $effectiveResolutionHeight -le 720) {
        $nativeRenderArgument = "-hdtv720"
    }
    elseif ($effectiveResolutionWidth -lt 2560 -and $effectiveResolutionHeight -lt 1440) {
        $nativeRenderArgument = "-hdtv1080"
    }
    else {
        $nativeRenderArgument = "-wqhd"
    }
}
elseif ($effectiveResolutionWidth -ge 2560) {
    $nativeRenderArgument = "-wqxga"
}
else {
    $nativeRenderArgument = "-wuxga"
}
$configuredTargetFps = if ($null -ne $launcherConfig.targetFps) { [int]$launcherConfig.targetFps } else { 60 }
if ($configuredTargetFps -notin @(60, 90, 120, 144)) {
    $configuredTargetFps = 60
}
$requestedTargetFps = if ($TargetFps -gt 0) { $TargetFps } else { $configuredTargetFps }
$effectiveTargetFps = $requestedTargetFps
if ($effectiveTargetFps -gt 60) {
    Write-Warning "High-FPS engine scheduling is suspended after UI/touch regressions; using native 60 FPS."
    $effectiveTargetFps = 60
}
$useAudioHook = [bool]$EnableExperimentalAudio
if ($launcherConfig.audioHook -and -not $EnableExperimentalAudio) { Write-Warning "Ignored legacy audioHook setting; using built-in shared audio for 11.00." }
$useProcessJapaneseLocale = [bool]$launcherConfig.processJapaneseLocale
$useWasapiShared = [bool]$launcherConfig.wasapiShared
$preferHighPerformanceGpu = $null -eq $launcherConfig.preferHighPerformanceGpu -or [bool]$launcherConfig.preferHighPerformanceGpu
$enableDiagnostics = [bool]$launcherConfig.diagnostics
$enableCryptoDiagnostics = [bool]$launcherConfig.cryptoDiagnostics
$enableProtocolDiagnostics = if ($null -ne $launcherConfig.protocolDiagnostics) {
    [bool]$launcherConfig.protocolDiagnostics
}
else {
    $enableCryptoDiagnostics
}

if ($useAudioHook -and -not (Test-Path -LiteralPath $audioHookPath)) {
    Stop-WithMessage "FGOAudio.dll is missing." 12
}

if ($useAudioHook) {
    Write-Warning "FGOAudio.dll documents support for FGO 10.70/10.80. This $effectiveGameVersion installation is unsupported by that optional hook, so it remains experimental."
}

New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $deviceRoot "print") -Force | Out-Null
$launchLogPath = Join-Path $logDirectory "fgo-last-launch.log"
[System.IO.File]::WriteAllText(
    $injectLiveLogPath,
    "Waiting for the injector to start and stream live debug output...`r`n",
    (New-Object System.Text.UTF8Encoding($false))
)
Start-Transcript -LiteralPath $launchLogPath -Force | Out-Null
$script:transcriptActive = $true
Copy-Item -LiteralPath $baseIni -Destination $runtimeIni -Force

# The public setup package currently provides 10.80 ICF files, while this
# ago.exe was built from the 11.00 source snapshot.  AMDaemon supports an
# explicit development-version override; apply it before authentication so
# ALL.Net and billing advertise the same version as the game executable.
$amdaemonRuntimeConfig = [ordered]@{
    credit = [ordered]@{ max_credit = 99 }
    allnet_auth = [ordered]@{
        develop_version = $effectiveGameVersion
    }
}
$amdaemonRuntimeJson = $amdaemonRuntimeConfig | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText(
    $amdaemonMainConfig,
    $amdaemonRuntimeJson + "`r`n",
    (New-Object System.Text.UTF8Encoding($false))
)

Set-IniValue -Path $runtimeIni -Section "vfs" -Key "amfs" -Value $amfsRoot
Set-IniValue -Path $runtimeIni -Section "vfs" -Key "option" -Value (Join-Path $gameRoot "option")
Set-IniValue -Path $runtimeIni -Section "vfs" -Key "appdata" -Value (Join-Path $installRoot "GameData")
Set-IniValue -Path $runtimeIni -Section "aime" -Key "aimePath" -Value (Join-Path $deviceRoot "aime.txt")
Set-IniValue -Path $runtimeIni -Section "printer" -Key "mainFwPath" -Value (Join-Path $deviceRoot "printer_main_fw.bin")
Set-IniValue -Path $runtimeIni -Section "printer" -Key "paramFwPath" -Value (Join-Path $deviceRoot "printer_param_fw.bin")
Set-IniValue -Path $runtimeIni -Section "printer" -Key "dspFwPath" -Value (Join-Path $deviceRoot "printer_dsp_fw.bin")
Set-IniValue -Path $runtimeIni -Section "keychip" -Key "billingCa" -Value (Join-Path $deviceRoot "ca.crt")
Set-IniValue -Path $runtimeIni -Section "keychip" -Key "billingPub" -Value (Join-Path $deviceRoot "billing.pub")
Set-IniValue -Path $runtimeIni -Section "misc" -Key "nextProcessFilePath" -Value (Join-Path $deviceRoot "NextProcess.txt")
$printAccount = 'unassigned'
$printName = ''
$aimeCode = [IO.File]::ReadAllText((Join-Path $deviceRoot 'aime.txt')).Trim()
$playerFile = Join-Path $installRoot 'Server\state\fgo-players.json'
if (Test-Path -LiteralPath $playerFile) {
    $players = Get-Content -LiteralPath $playerFile -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($player in $players.PSObject.Properties) {
        if ($player.Name -match '^aime:(\d+)$' -and ([string]$player.Value.auth_access_code -eq $aimeCode -or [string]$player.Value.access_code -eq $aimeCode)) {
            $printAccount = 'aime-' + $Matches[1]
            $printName = [string]$player.Value.master_name
            break
        }
    }
}
$printDirectory = Join-Path $deviceRoot ('print\players\' + $printAccount)
New-Item -ItemType Directory -Path $printDirectory -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $printDirectory 'player.json'), (@{ account = $printAccount; name = $printName } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Set-IniValue -Path $runtimeIni -Section "printer" -Key "printerOutPath" -Value $printDirectory
$env:FGO_PRINT_METADATA_ONLY = '1'
Set-IniValue -Path $runtimeIni -Section "dns" -Key "default" -Value $serverHost
if ($launcherConfig.serverPorts) {
    Set-IniValue -Path $runtimeIni -Section "dns" -Key "startupPort" -Value ([string]$launcherConfig.serverPorts.http)
    Set-IniValue -Path $runtimeIni -Section "dns" -Key "billingPort" -Value ([string]$launcherConfig.serverPorts.billing)
    Set-IniValue -Path $runtimeIni -Section "dns" -Key "aimedbPort" -Value ([string]$launcherConfig.serverPorts.aime)
}
Set-IniValue -Path $runtimeIni -Section "netenv" -Key "enable" -Value "1"
Set-IniValue -Path $runtimeIni -Section "netenv" -Key "routerSuffix" -Value ([string]$routerSuffix)
Set-IniValue -Path $runtimeIni -Section "netenv" -Key "addrSuffix" -Value ([string]$addrSuffix)
Set-IniValue -Path $runtimeIni -Section "netenv" -Key "broadcast" -Value $broadcast
Set-IniValue -Path $runtimeIni -Section "keychip" -Key "subnet" -Value $subnet
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "windowed" -Value $(if ($effectiveWindowed) { "1" } else { "0" })
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "framed" -Value $(if ($effectiveFramed) { "1" } else { "0" })
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "width" -Value ([string]$effectiveResolutionWidth)
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "height" -Value ([string]$effectiveResolutionHeight)
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "logicalWidth" -Value ([string]$logicalRenderWidth)
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "logicalHeight" -Value ([string]$logicalRenderHeight)
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "preserveAspect" -Value "1"
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "monitor" -Value "0"
Set-IniValue -Path $runtimeIni -Section "gfx" -Key "monitorDevice" -Value $effectiveMonitorDevice
Set-IniValue -Path $runtimeIni -Section "amvideo" -Key "resolutionWidth" -Value ([string]$nativeRenderWidth)
Set-IniValue -Path $runtimeIni -Section "amvideo" -Key "resolutionHeight" -Value ([string]$nativeRenderHeight)
Set-IniValue -Path $runtimeIni -Section "io4" -Key "mode" -Value $effectiveInputMode
Set-IniValue -Path $runtimeIni -Section "touch" -Key "remap" -Value "1"
# Lock both mouse emulation and real WM_TOUCH to the one 16:9 UI rectangle.
# Accepted pixels use the exact inverse of r47808's fixed 1920x1080-to-native-
# surface projection. The Win32 top edge comes from the live client height, so
# monitor clipping cannot shift the portrait hit region. Legacy branches are
# disabled.
Set-IniValue -Path $runtimeIni -Section "touch" -Key "inputWidth" -Value "1920"
Set-IniValue -Path $runtimeIni -Section "touch" -Key "inputHeight" -Value "1080"
Set-IniValue -Path $runtimeIni -Section "touch" -Key "nativeCoordinates" -Value "0"
Set-IniValue -Path $runtimeIni -Section "system" -Key "freeplay" -Value "0"
Set-IniValue -Path $runtimeIni -Section "clock" -Key "timezone" -Value "0"
Set-IniValue -Path $runtimeIni -Section "clock" -Key "daystart" -Value "0"
Set-IniValue -Path $runtimeIni -Section "clock" -Key "startHour" -Value "0"
Set-IniValue -Path $runtimeIni -Section "clock" -Key "startMinute" -Value "0"
Set-IniValue -Path $runtimeIni -Section "clock" -Key "timewarp" -Value "0"
Set-IniValue -Path $runtimeIni -Section "clock" -Key "writeable" -Value "0"

# GetPrivateProfileStringW accepts UTF-16 INI files; UTF-8 absolute paths
# otherwise turn Chinese install directories into mojibake in native hooks.
[IO.File]::WriteAllText($runtimeIni, [IO.File]::ReadAllText($runtimeIni), [Text.Encoding]::Unicode)
$env:SEGATOOLS_CONFIG_PATH = $runtimeIni
$env:FGO_TARGET_FPS = [string]$effectiveTargetFps
Remove-Item Env:FGO_DISABLE_FRAME_PACING -ErrorAction SilentlyContinue
Remove-Item Env:FGO_REUSE_AMDAEMON -ErrorAction SilentlyContinue

if ($enableDiagnostics) {
    $env:FGO_EXIT_DIAGNOSTICS = "1"
}
else {
    Remove-Item Env:FGO_EXIT_DIAGNOSTICS -ErrorAction SilentlyContinue
}

if ($enableProtocolDiagnostics) {
    $env:FGO_PROTOCOL_DIAGNOSTICS = "1"
}
else {
    Remove-Item Env:FGO_PROTOCOL_DIAGNOSTICS -ErrorAction SilentlyContinue
}

if ($enableCryptoDiagnostics) {
    $env:BCRYPT_DUMP_ENABLED = "1"
}
else {
    Remove-Item Env:BCRYPT_DUMP_ENABLED -ErrorAction SilentlyContinue
}

if ($preferHighPerformanceGpu) {
    # FGO 11.00 uses NVIDIA bindless-buffer OpenGL extensions.  On hybrid-GPU
    # laptops Windows can otherwise select the Intel adapter, leaving required
    # function pointers null.  This is the same per-app preference exposed by
    # Windows Settings > System > Display > Graphics.
    $gpuPreferenceKey = "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences"
    New-Item -Path $gpuPreferenceKey -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $gpuPreferenceKey `
        -Name $agoPath `
        -Value "GpuPreference=2;" `
        -PropertyType String `
        -Force | Out-Null
    New-ItemProperty `
        -LiteralPath $gpuPreferenceKey `
        -Name $injectPath `
        -Value "GpuPreference=2;" `
        -PropertyType String `
        -Force | Out-Null
}

if ($useProcessJapaneseLocale) {
    # Locale Remulator reads these values as UTF-16 code units.  The hook is
    # injected only into the game process; Windows language settings remain
    # untouched.
    $env:LRCodePage = [string][char]932
    $env:LRLCID = [string][char]1041
    $env:LRBIAS = [string][char]540
    $env:LRHookLCID = [string][char]1
    Remove-Item Env:LRHookIME -ErrorAction SilentlyContinue
}

$launchArguments = New-Object System.Collections.Generic.List[string]
$launchArguments.Add("-d")
if ($useProcessJapaneseLocale) {
    $launchArguments.Add("-k")
    $launchArguments.Add($localeHookPath)
}
if (Test-Path -LiteralPath $glCompatPath -PathType Leaf) {
    # Must load before fgohook: MinHook on opengl32 exports, IAT left for fgohook.
    $launchArguments.Add("-k")
    $launchArguments.Add($glCompatPath)
}
$launchArguments.Add("-k")
$launchArguments.Add($hookPath)
$useChinese = $Chinese -or [bool]$launcherConfig.chineseEnabled
$easterSettings = Join-Path $gameRoot 'BGM\settings.ini'
$useMasterEaster = (Test-Path -LiteralPath $easterSettings) -and [bool](Select-String -LiteralPath $easterSettings -Pattern '^\s*enabled\s*=\s*1\s*$' -Quiet)
if ($useChinese -or $useMasterEaster) {
    if (-not (Test-Path -LiteralPath $chineseHookPath -PathType Leaf)) {
        Stop-WithMessage "Chinese resource hook is missing: $chineseHookPath"
    }
    $launchArguments.Add("-k")
    $launchArguments.Add($chineseHookPath)
    if ($useChinese) { Write-Output "[zh] Chinese resources enabled: $gameRoot\zh (missing resources use original files)." }
    if ($useMasterEaster) { Write-Output "[easter] Master portraits enabled: $gameRoot\EasterEgg" }
}
if ($useAudioHook) {
    $launchArguments.Add("-k")
    $launchArguments.Add($audioHookPath)
}
$launchArguments.Add($agoPath)
$launchArguments.Add($nativeRenderArgument)
if ($effectiveCabinetMode -ne "saved") {
    $launchArguments.Add("-sm")
    $launchArguments.Add($effectiveCabinetMode)
}
if ($effectiveWindowed) {
    $launchArguments.Add("-w")
}
if ($useWasapiShared) {
    $launchArguments.Add("--wasapi-shared")
}

Write-Host "Virtual LAN: $localIp (local bridge=$($network.Local))"
Write-Host "Server     : $serverHost"
Write-Host "Version    : $effectiveGameVersion"
Write-Host "Input      : $effectiveInputMode"
Write-Host "Cabinet    : $($effectiveCabinetMode.ToUpperInvariant()) (test menu is persisted as SATELLITE:MAIN)"
Write-Host "Display    : $effectiveDisplayMode $($effectiveResolutionWidth)x$($effectiveResolutionHeight)"
Write-Host "Monitor    : $(if ($effectiveMonitorDevice) { $effectiveMonitorDevice } else { 'primary' }) (disconnected device falls back to primary)"
if ($effectiveDisplayMode -eq "borderless") {
    Write-Host "Borderless : desktop-composed guard enabled (no display-mode change)"
}
Write-Host "Renderer   : $nativeRenderArgument $($nativeRenderWidth)x$($nativeRenderHeight) native; UI/touch safe area 1920x1080"
Write-Host "Frame rate : $effectiveTargetFps FPS (engine-normalized)"
Write-Host "Audio hook : $useAudioHook"
Write-Host "Process JP : $useProcessJapaneseLocale"
Write-Host "WASAPI     : $useWasapiShared"
if ($audioEndpointInspectionSucceeded) {
    Write-Host "Audio out  : $($activeAudioEndpoints.Name -join ', ')"
}
Write-Host "High GPU   : $preferHighPerformanceGpu"
Write-Host "GL compat  : $(if (Test-Path -LiteralPath $glCompatPath -PathType Leaf) { 'fgoglcompat.dll (before fgohook)' } else { 'not installed' })"
Write-Host "GP lock    : 2333 (consumption disabled)"
Write-Host "Diagnostics: $enableDiagnostics"
Write-Host "Protocol diag: $enableProtocolDiagnostics"
Write-Host "Crypto diag: $enableCryptoDiagnostics"
Write-Host "Launch log  : $launchLogPath"

# Let ago.exe create and own AMDaemon.  FGO's generated amdaemon_aux.json
# contains the cabinet role selected by -sm; pre-starting or proxying the
# daemon breaks the game's process-state handshake.
if ($CheckOnly) {
    Write-Host 'FGO startup checks passed; runtime configuration prepared.' -ForegroundColor Green
    if ($script:transcriptActive) { Stop-Transcript | Out-Null; $script:transcriptActive = $false }
    exit 0
}
Get-Process -Name "ago", "amdaemon", "inject" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$gameRoot*" } |
    Stop-Process -Force -ErrorAction SilentlyContinue

# A rebuilt hook can be staged while the currently loaded DLL is locked.  The
# next launcher run installs it only after all previous FGO processes are gone.
if (Test-Path -LiteralPath $pendingHookPath) {
    $remainingProcesses = Get-Process -Name "ago", "amdaemon", "inject" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$gameRoot*" }
    if ($remainingProcesses) {
        $remainingProcesses | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }

    $backupDirectory = Join-Path $gameRoot "backup"
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $backupStamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $hookBackup = Join-Path $backupDirectory "fgohook-before-update-$backupStamp.dll"
    Copy-Item -LiteralPath $hookPath -Destination $hookBackup -Force
    Copy-Item -LiteralPath $pendingHookPath -Destination $hookPath -Force
    Remove-Item -LiteralPath $pendingHookPath -Force
    Write-Host "Installed staged FGO compatibility hook; backup: $hookBackup" -ForegroundColor Green
}

# Install audio files that were held open by the previous game session.
$pendingBgmDirectory = Join-Path $gameRoot "BGM\pending"
if (Test-Path -LiteralPath $pendingBgmDirectory -PathType Container) {
    $bgmBackupDirectory = Join-Path $gameRoot ("backup\bgm-" + (Get-Date -Format "yyyyMMdd_HHmmss"))
    New-Item -ItemType Directory -Path $bgmBackupDirectory -Force | Out-Null
    foreach ($bgmFile in Get-ChildItem -LiteralPath $pendingBgmDirectory -Filter '*.ogg' -File) {
        $bgmDestination = Join-Path $gameRoot ("BGM\" + $bgmFile.Name)
        if (Test-Path -LiteralPath $bgmDestination) {
            Copy-Item -LiteralPath $bgmDestination -Destination (Join-Path $bgmBackupDirectory $bgmFile.Name) -Force
        }
        Copy-Item -LiteralPath $bgmFile.FullName -Destination $bgmDestination -Force
        Remove-Item -LiteralPath $bgmFile.FullName -Force
    }
}

if (Test-Path -LiteralPath $pendingChineseHookPath -PathType Leaf) {
    $remainingChineseProcesses = Get-Process -Name "ago" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$gameRoot*" }
    if ($remainingChineseProcesses) {
        $remainingChineseProcesses | Wait-Process -Timeout 10 -ErrorAction Stop
    }
    $chineseBackupDirectory = Join-Path $gameRoot "backup"
    New-Item -ItemType Directory -Path $chineseBackupDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $chineseHookPath -PathType Leaf) {
        Copy-Item -LiteralPath $chineseHookPath -Destination (Join-Path $chineseBackupDirectory ("fgozh-before-update-" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".dll")) -Force
    }
    Copy-Item -LiteralPath $pendingChineseHookPath -Destination $chineseHookPath -Force
    Remove-Item -LiteralPath $pendingChineseHookPath -Force
    Write-Output "[zh] Installed staged Chinese hook update."
}

Protect-FgoChildProcessStreams
$launchStart = Get-Date

Push-Location $gameRoot
try {
    # PowerShell transcripts buffer native debugger output until inject.exe
    # exits. Drain both pipes and mirror complete lines to a dedicated file so
    # the platform can display the debugger stream while the game is running.
    # WPF platform starts this PowerShell host with CREATE_NO_WINDOW and
    # redirected handles. Start inject.exe with its own explicit no-window flag
    # as well; CREATE_NO_WINDOW is a creation flag, not a transitive property.
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $gameExitCode = -1
    $injectProcess = $null
    $injectProcessStarted = $false
    $injectLogStream = $null
    $injectLogWriter = $null
    try {
        Write-Output "[inject] starting; stdout and stderr are live."

        $quotedLaunchArguments = @(
            $launchArguments |
                ForEach-Object {
                    ConvertTo-WindowsCommandLineArgument -Argument ([string]$_)
                }
        )
        $injectStartInfo = New-Object System.Diagnostics.ProcessStartInfo
        $deckHasher = [System.Security.Cryptography.SHA256]::Create()
        try {
            $deckRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\').ToUpperInvariant()
            $deckHash = $deckHasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($deckRoot))
            $injectStartInfo.EnvironmentVariables['FGO_DECK_CHANNEL'] = 'FGODeck_' + ([BitConverter]::ToString($deckHash).Replace('-', ''))
        } finally { $deckHasher.Dispose() }
        $injectStartInfo.FileName = $injectPath
        $injectStartInfo.Arguments = [string]::Join(" ", [string[]]$quotedLaunchArguments)
        $injectStartInfo.WorkingDirectory = $gameRoot
        $injectStartInfo.UseShellExecute = $false
        $injectStartInfo.EnvironmentVariables["FGO_ZH_ENABLED"] = $(if ($useChinese) { "1" } else { "0" })
        $injectStartInfo.CreateNoWindow = $true
        $injectStartInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
        $injectStartInfo.ErrorDialog = $false
        $injectStartInfo.RedirectStandardOutput = $true
        $injectStartInfo.RedirectStandardError = $true
        $injectStartInfo.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $injectStartInfo.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
        if ($effectiveDisplayMode -eq "borderless") {
            # A monitor-sized WS_POPUP can be promoted to Windows' independent
            # flip path and make an external display renegotiate its signal as
            # though the title entered exclusive fullscreen.  Keep borderless
            # launches on the desktop-composed path.  This compatibility flag
            # is inherited only by this launch and does not change resolution,
            # aspect ratio, refresh rate, or the user's persistent registry.
            $compatLayer = [string]$injectStartInfo.EnvironmentVariables["__COMPAT_LAYER"]
            $compatTokens = @(
                $compatLayer -split '\s+' |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            )
            if ($compatTokens -notcontains "DISABLEDXMAXIMIZEDWINDOWEDMODE") {
                $compatTokens += "DISABLEDXMAXIMIZEDWINDOWEDMODE"
            }
            $injectStartInfo.EnvironmentVariables["__COMPAT_LAYER"] =
                [string]::Join(" ", [string[]]$compatTokens)
            # The external DisplayPort panel is directly attached to the
            # NVIDIA GPU. Keep the visible result borderless, but expose an
            # ordinary desktop-window style to the OpenGL driver so Alt-Tab
            # cannot promote it to the direct fullscreen presentation path.
            $injectStartInfo.EnvironmentVariables["FGO_BORDERLESS_COMPOSED"] = "1"
        }
        # Production renderer path: keep every cabinet/server/input hook and
        # the requested native resolution. Texture payloads, FBOs, samplers
        # and all render-pass depth state remain native.
        $injectStartInfo.EnvironmentVariables.Remove("FGO_GL_PASSTHROUGH")
        # Opt-in compatibility capture must also work when launched from the platform.
        if (Test-Path -LiteralPath (Join-Path $gameRoot 'fgo-gl-diagnostics.enabled')) {
            $injectStartInfo.EnvironmentVariables['FGO_GL_DIAGNOSTICS'] = '1'
        }
        # Texture payload capture has completed. Do not keep writing multi-MB
        # diagnostic copies during normal play.
        $injectStartInfo.EnvironmentVariables.Remove("FGO_SKY_UPLOAD_CAPTURE")
        # World cameras use the requested display aspect. Their scene and
        # post-process buffers must have the same dimensions; using the 16:9
        # UI safe rectangle here stretches the world during final composition.
        # UI layout continues to use the separate safe-mode getter.
        $injectStartInfo.EnvironmentVariables["FGO_FULL_SURFACE_FBO"] = "1"
        $graphics = $launcherConfig.graphics
        $smaa = if ($null -ne $graphics.smaa -and [int]$graphics.smaa -in @(0, 1, 2)) { [int]$graphics.smaa } else { 0 }
        $anisotropy = if ($null -ne $graphics.anisotropy -and [int]$graphics.anisotropy -in @(1, 2, 4, 8, 16)) { [int]$graphics.anisotropy } else { 16 }
        $hideUiKey = if ($null -ne $graphics.hideUiKey -and [int]$graphics.hideUiKey -ge 8 -and [int]$graphics.hideUiKey -le 254) { [int]$graphics.hideUiKey } else { 121 }
        $injectStartInfo.EnvironmentVariables["FGO_SMAA"] = [string]$smaa
        $renderScalePercent = if ($PSBoundParameters.ContainsKey('RenderScale')) { $RenderScale } elseif ($null -ne $graphics.renderScale -and [int]$graphics.renderScale -in @(100, 125, 150, 200)) { [int]$graphics.renderScale } else { 100 }
        $injectStartInfo.EnvironmentVariables["FGO_RENDER_SCALE"] = [string]$renderScalePercent
        $shadowResolution = if ($null -ne $graphics.shadowResolution -and [int]$graphics.shadowResolution -in @(1024, 2048, 4096)) { [int]$graphics.shadowResolution } else { 0 }
        $injectStartInfo.EnvironmentVariables["FGO_SHADOW_RESOLUTION"] = [string]$shadowResolution
        $injectStartInfo.EnvironmentVariables["FGO_HIDE_TARGET_LINES"] = if ($graphics.hideTargetLines -eq $true) { "1" } else { "0" }
        foreach ($damageSetting in @(
            @('damageNumberScale', 'FGO_DAMAGE_NUMBER_SCALE', 0, 200),
            @('damageTextureScale', 'FGO_DAMAGE_TEXTURE_SCALE', 0, 200),
            @('damageNumberOpacity', 'FGO_DAMAGE_NUMBER_OPACITY', 0, 100),
            @('damageTextureOpacity', 'FGO_DAMAGE_TEXTURE_OPACITY', 0, 100))) {
            $damageValue = 100
            [double]$parsedDamageValue = 0
            if ([double]::TryParse([string]$graphics.($damageSetting[0]), [ref]$parsedDamageValue) -and
                $parsedDamageValue -ge $damageSetting[2] -and $parsedDamageValue -le $damageSetting[3]) {
                $damageValue = $parsedDamageValue
            }
            $injectStartInfo.EnvironmentVariables[$damageSetting[1]] = ([double]$damageValue).ToString([Globalization.CultureInfo]::InvariantCulture)
        }
        $photoKeys = @(120,87,83,65,68,81,69,37,39,38,40,90,67,82)
        if ($graphics.photo.keys -and @($graphics.photo.keys).Count -eq 14) {
            for ($photoIndex=0; $photoIndex -lt 14; $photoIndex++) {
                $photoKey = 0
                if ([int]::TryParse([string]$graphics.photo.keys[$photoIndex], [ref]$photoKey) -and $photoKey -ge 8 -and $photoKey -le 254) { $photoKeys[$photoIndex]=$photoKey }
            }
        }
        $injectStartInfo.EnvironmentVariables["FGO_PHOTO_KEY"] = [string]$photoKeys[0]
        $injectStartInfo.EnvironmentVariables["FGO_PHOTO_KEYS"] = ($photoKeys[1..13] -join ',')
        $injectStartInfo.EnvironmentVariables["FGO_ANISOTROPY"] = [string]$anisotropy
        $injectStartInfo.EnvironmentVariables["FGO_MOTION_BLUR"] = if ($graphics.motionBlur -eq $true) { "1" } else { "0" }
        $injectStartInfo.EnvironmentVariables["FGO_DEPTH_OF_FIELD"] = if ($graphics.depthOfField -eq $false) { "0" } else { "1" }
        $injectStartInfo.EnvironmentVariables["FGO_BLOOM"] = if ($graphics.bloom -eq $false) { "0" } else { "1" }
        $injectStartInfo.EnvironmentVariables["FGO_HIDE_UI"] = if ($graphics.hideUi -eq $true) { "1" } else { "0" }
        $injectStartInfo.EnvironmentVariables["FGO_DISABLE_CAMERA_SHAKE"] = if ($graphics.disableCameraShake -eq $true) { "1" } else { "0" }
        $injectStartInfo.EnvironmentVariables["FGO_HIDE_CABINET_HUD"] = if ($graphics.hideCabinetHud -eq $true) { "1" } else { "0" }
        $injectStartInfo.EnvironmentVariables["FGO_HIDE_UI_KEY"] = [string]$hideUiKey
        # Keep the explicit frame limiter active even on the native renderer.
        # The title advances simulation from its presentation loop; disabling
        # pacing here makes gameplay and tutorial animation run at unrestricted
        # render speed instead of the configured target FPS.
        $injectStartInfo.EnvironmentVariables.Remove("FGO_DISABLE_FRAME_PACING")
        # Preserve the title's sampler state. The compatibility hook's former
        # global LOD/anisotropy override remains disabled.
        $injectStartInfo.EnvironmentVariables["FGO_TEXTURE_QUALITY"] = "0"
        # Remove every disproved diagnostic/repair flag. Some historical hook
        # builds treated even a value of "0" as enabled because they checked
        # only for environment-variable presence.
        $injectStartInfo.EnvironmentVariables.Remove("FGO_SKY_DIAGNOSTICS")
        $injectStartInfo.EnvironmentVariables.Remove("FGO_SKY_RESTART_FIX")
        $injectStartInfo.EnvironmentVariables.Remove("FGO_SKY_DEPTH_EQUAL_FIX")
        $injectStartInfo.EnvironmentVariables.Remove("FGO_SKY_DOME_EXTEND_PERCENT")
        # The title already performs a reverse-Z depth prepass followed by an
        # equal-depth colour pass. Historical sky fixes changed both passes
        # and caused the large block, distant-vegetation occlusion and seams.
        $injectStartInfo.EnvironmentVariables.Remove("FGO_SKY_CULL_FIX")
        $injectStartInfo.EnvironmentVariables.Remove("FGO_SKY_BACKGROUND_FIX")
        $injectLogStream = [System.IO.File]::Open(
            $injectLiveLogPath,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
        )
        $injectLogWriter = New-Object System.IO.StreamWriter(
            $injectLogStream,
            (New-Object System.Text.UTF8Encoding($false))
        )
        $injectLogWriter.AutoFlush = $true

        $injectProcess = New-Object System.Diagnostics.Process
        $injectProcess.StartInfo = $injectStartInfo
        if (-not $injectProcess.Start()) {
            throw "Process.Start returned false for $injectPath"
        }
        $injectProcessStarted = $true

        # ReadLineAsync on both pipes before waiting for either one. This keeps
        # a noisy stderr from blocking stdout (or vice versa), while emitting
        # complete lines to the log and WPF as soon as either task completes.
        $stdoutOpen = $true
        $stderrOpen = $true
        $stdoutRead = $injectProcess.StandardOutput.ReadLineAsync()
        $stderrRead = $injectProcess.StandardError.ReadLineAsync()

        while ($stdoutOpen -or $stderrOpen) {
            $waitTasks = @()
            if ($stdoutOpen) {
                $waitTasks += [System.Threading.Tasks.Task]$stdoutRead
            }
            if ($stderrOpen) {
                $waitTasks += [System.Threading.Tasks.Task]$stderrRead
            }

            $lineReady =
                ($stdoutOpen -and $stdoutRead.IsCompleted) -or
                ($stderrOpen -and $stderrRead.IsCompleted)
            if (-not $lineReady -and $waitTasks.Count -gt 0) {
                [void][System.Threading.Tasks.Task]::WaitAny(
                    [System.Threading.Tasks.Task[]]$waitTasks,
                    500
                )
            }

            if ($stdoutOpen -and $stdoutRead.IsCompleted) {
                $stdoutLine = $stdoutRead.GetAwaiter().GetResult()
                if ($null -eq $stdoutLine) {
                    $stdoutOpen = $false
                }
                else {
                    $injectLogWriter.WriteLine($stdoutLine)
                    Write-Output $stdoutLine
                    $stdoutRead = $injectProcess.StandardOutput.ReadLineAsync()
                }
            }

            if ($stderrOpen -and $stderrRead.IsCompleted) {
                $stderrLine = $stderrRead.GetAwaiter().GetResult()
                if ($null -eq $stderrLine) {
                    $stderrOpen = $false
                }
                else {
                    $stderrDisplayLine = "[inject:stderr] $stderrLine"
                    $injectLogWriter.WriteLine($stderrDisplayLine)
                    Write-Output $stderrDisplayLine
                    $stderrRead = $injectProcess.StandardError.ReadLineAsync()
                }
            }
        }

        $injectProcess.WaitForExit()
        $gameExitCode = $injectProcess.ExitCode
        Write-Output "[inject] exited with code $gameExitCode."
    }
    catch {
        $captureFailure = "[inject:stderr] inject process/capture failed: $($_.Exception.Message)"
        if ($null -ne $injectLogWriter) {
            $injectLogWriter.WriteLine($captureFailure)
        }
        Write-Output $captureFailure

        if ($injectProcessStarted) {
            try {
                if (-not $injectProcess.HasExited) {
                    $injectProcess.Kill()
                    $injectProcess.WaitForExit()
                }
                $gameExitCode = $injectProcess.ExitCode
            }
            catch {
                $gameExitCode = 1
            }
        }
        else {
            $gameExitCode = 1
        }
    }
    finally {
        if ($null -ne $injectLogWriter) {
            $injectLogWriter.Dispose()
            $injectLogWriter = $null
            $injectLogStream = $null
        }
        elseif ($null -ne $injectLogStream) {
            $injectLogStream.Dispose()
            $injectLogStream = $null
        }
        if ($null -ne $injectProcess) {
            $injectProcess.Dispose()
            $injectProcess = $null
        }
        $ErrorActionPreference = $previousErrorActionPreference
    }
}
finally {
    Pop-Location
}

$sessionSeconds = ((Get-Date) - $launchStart).TotalSeconds
Get-Process -Name "amdaemon", "inject" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$gameRoot*" -and $_.StartTime -ge $launchStart.AddSeconds(-1) } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item Env:FGO_REUSE_AMDAEMON -ErrorAction SilentlyContinue
Remove-Item Env:FGO_EXIT_DIAGNOSTICS -ErrorAction SilentlyContinue
Remove-Item Env:BCRYPT_DUMP_ENABLED -ErrorAction SilentlyContinue
Remove-Item Env:FGO_PROTOCOL_DIAGNOSTICS -ErrorAction SilentlyContinue
Remove-Item Env:FGO_TARGET_FPS -ErrorAction SilentlyContinue
Remove-Item Env:FGO_PRINT_METADATA_ONLY -ErrorAction SilentlyContinue

# AMDaemon writes these diagnostics in its data directory; collect them after it exits.
foreach ($diagnosticName in @('amdaemon.exe.log', 'tmp.dmp')) {
    $diagnosticPath = Join-Path $installRoot "GameData\SDEJ\$diagnosticName"
    if (Test-Path -LiteralPath $diagnosticPath -PathType Leaf) {
        $destination = Join-Path $logDirectory ("amdaemon-{0}-{1}" -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'), $diagnosticName)
        Move-Item -LiteralPath $diagnosticPath -Destination $destination -ErrorAction Continue
    }
}

$roundedSeconds = [Math]::Round($sessionSeconds, 1)
if ($gameExitCode -ne 0) {
    if ($gameExitCode -eq -1073741819) {
        Stop-WithMessage "ago.exe crashed after $roundedSeconds seconds with access violation 0xC0000005. Check the logs folder for crash diagnostics." 22
    }
    Stop-WithMessage "ago.exe crashed after $roundedSeconds seconds (exit $gameExitCode). Check the logs folder." 22
}

if ($sessionSeconds -lt 60) {
    Stop-WithMessage "ago.exe closed after $roundedSeconds seconds without a crash code. Check the logs folder." 22
}

Write-Host "FGO session ended normally." -ForegroundColor Green
if ($script:transcriptActive) {
    Stop-Transcript | Out-Null
    $script:transcriptActive = $false
}
exit 0
