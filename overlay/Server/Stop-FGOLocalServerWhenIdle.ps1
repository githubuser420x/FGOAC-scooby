[CmdletBinding()]
param([int]$FrontendProcessId = 0, [int]$LauncherProcessId = 0)
$ErrorActionPreference = 'Stop'
$serverRoot = $PSScriptRoot
$installRoot = Split-Path -Parent $serverRoot
$game = Join-Path $installRoot 'App\ago.exe'
$frontends = @((Join-Path $installRoot 'FGOLocalPlatform.exe'), (Join-Path $installRoot 'FGOLocalPlatform-独立版.exe'), (Join-Path $installRoot 'FGOA scooby.exe'))
$log = Join-Path $installRoot 'logs\server-control.log'
try {
    # Capture the original process handle; do not wait on a reused numeric PID.
    $ownerProcessId = if ($LauncherProcessId -gt 0) { $LauncherProcessId } else { $FrontendProcessId }
    $parent = if ($ownerProcessId -gt 0) { Get-Process -Id $ownerProcessId -ErrorAction SilentlyContinue } else { $null }
    if ($parent) { $parent.WaitForExit(); $parent.Dispose() }
    while ($true) {
        $processes = @(Get-CimInstance Win32_Process -Filter "Name='ago.exe' OR Name='FGOLocalPlatform.exe' OR Name='FGOLocalPlatform-独立版.exe' OR Name='FGOA scooby.exe'" -OperationTimeoutSec 5)
        # A newly opened frontend owns this server's lifetime now.
        if ($LauncherProcessId -eq 0 -and @($processes | Where-Object { $frontends -contains $_.ExecutablePath }).Count -gt 0) { exit 0 }
        if (@($processes | Where-Object { [string]::Equals($_.ExecutablePath, $game, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) { break }
        Start-Sleep -Seconds 1
    }
    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Session owner and game exited; stopping this installation's server (launcher=$LauncherProcessId)." | Out-File -LiteralPath $log -Append -Encoding utf8
    & (Join-Path $serverRoot 'Stop-FGOLocalServer.ps1') *>&1 | Out-File -LiteralPath $log -Append -Encoding utf8
} catch {
    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] Deferred server cleanup failed: $_" | Out-File -LiteralPath $log -Append -Encoding utf8
    exit 1
}
