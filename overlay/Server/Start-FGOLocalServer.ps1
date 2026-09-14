[CmdletBinding()]
param(
    [string]$ServerHost = ""
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$serverRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$artemisRoot = Join-Path $serverRoot "artemis"
$pythonPath = Join-Path $serverRoot "python\python.exe"
$mariaRoot = Join-Path $serverRoot "mariadb-10.11.16-winx64"
$mariaDaemon = Join-Path $mariaRoot "bin\mariadbd.exe"
$mariaIni = Join-Path $serverRoot "mariadb.ini"
$mariaData = Join-Path $serverRoot "data\mariadb"
$stateDir = Join-Path $serverRoot "state"
$logDir = Join-Path (Split-Path -Parent $serverRoot) "logs"
$coreConfig = Join-Path $artemisRoot "config\core.yaml"
. (Join-Path $serverRoot "ServerSettings.ps1")
$serverSettings = Get-FgoServerSettings

function Test-TcpPort {
    param([string]$HostName, [int]$Port, [int]$TimeoutMilliseconds = 500)
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $result = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $result.AsyncWaitHandle.WaitOne($TimeoutMilliseconds, $false)) {
            return $false
        }
        $client.EndConnect($result)
        return $true
    }
    catch { return $false }
    finally { $client.Dispose() }
}

function Get-FgoLocalHealth {
    $request = [Net.HttpWebRequest]::Create("http://127.0.0.1:$($serverSettings.http)/")
    $request.Proxy = $null
    $request.Timeout = 3000
    $response = $request.GetResponse()
    try {
        $reader = [IO.StreamReader]::new($response.GetResponseStream())
        try { [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Content = $reader.ReadToEnd() } }
        finally { $reader.Dispose() }
    } finally { $response.Dispose() }
}

function Assert-FgoPortOwner {
    param([int]$Port, [string]$Executable)
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
        if (-not $owner -or -not [string]::Equals($owner.ExecutablePath, $Executable, [StringComparison]::OrdinalIgnoreCase)) {
            throw "[FGO-SERVER:PORT] Port $Port is in use by another program (PID $($listener.OwningProcess)). Change the server port, or close that program."
        }
    }
}

function Wait-TcpPort {
    param([string]$HostName, [int]$Port, [int]$TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-TcpPort -HostName $HostName -Port $Port) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

foreach ($required in @($artemisRoot, $pythonPath, $mariaDaemon, $mariaIni, $mariaData, $coreConfig)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Local server component is missing: $required"
    }
}

New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

$startLock = $null
try {
    $startLock = [IO.File]::Open((Join-Path $stateDir 'server-start.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
} catch { throw '[FGO-SERVER:BUSY] Another launcher is starting the server, or the state folder is not writable. Wait for the current startup to finish.' }
try {
if ([string]::IsNullOrWhiteSpace($ServerHost)) {
    $ServerHost = if ($serverSettings.host -and $serverSettings.host -ne "auto") { $serverSettings.host } else { "192.168.100.1" }
}
if ([string]::IsNullOrWhiteSpace($ServerHost)) {
    $ServerHost = "192.168.100.1"
}

if ($ServerHost -in @('auto','local','localhost','127.0.0.1')) { $ServerHost = '192.168.100.1' }
. (Join-Path (Split-Path -Parent $serverRoot) 'App\FGO_StartupChecks.ps1')
Test-FgoWritableLayout -InstallRoot (Split-Path -Parent $serverRoot)

$encoding = New-Object System.Text.UTF8Encoding($false)
$yaml = [System.IO.File]::ReadAllText($coreConfig, $encoding)
$updatedYaml = [Regex]::Replace(
    $yaml,
    '(?m)^(\s{2}hostname:\s*).+$',
    ('${1}"' + $ServerHost + '"'),
    1
)
if ($updatedYaml -eq $yaml -and $yaml -notmatch ('(?m)^\s{2}hostname:\s*"?' + [Regex]::Escape($ServerHost))) {
    throw "Could not update the ARTEMiS hostname in $coreConfig"
}
if ($updatedYaml -ne $yaml) {
    if (Test-TcpPort -HostName '127.0.0.1' -Port $serverSettings.http) {
        throw '[FGO-SERVER:HOST] The server is still using the old address. Stop the local server, then start it again to enable the offline virtual network.'
    }
    [System.IO.File]::WriteAllText($coreConfig, $updatedYaml, $encoding)
}

Assert-FgoPortOwner -Port $serverSettings.database -Executable $mariaDaemon
foreach ($port in @($serverSettings.http,$serverSettings.billing,$serverSettings.aime)) {
    Assert-FgoPortOwner -Port $port -Executable $pythonPath
}
if (-not (Test-TcpPort -HostName "127.0.0.1" -Port $serverSettings.database)) {
    $dbOut = Join-Path $logDir "mariadb-stdout.log"
    $dbErr = Join-Path $logDir "mariadb-stderr.log"
    $dbStart = @{
        FilePath = $mariaDaemon
        ArgumentList = @("--defaults-file=$mariaIni", "--basedir=$mariaRoot", "--datadir=$mariaData", "--pid-file=$(Join-Path $stateDir 'mariadb-engine.pid')", "--log-error=$(Join-Path $logDir 'mariadb.log')", "--console")
        WorkingDirectory = $serverRoot
        RedirectStandardOutput = $dbOut
        RedirectStandardError = $dbErr
    }
    $dbProcess = Start-FgoBackgroundProcess @dbStart
    [System.IO.File]::WriteAllText(
        (Join-Path $stateDir "mariadb.pid"),
        [string]$dbProcess.Id,
        $encoding
    )
    if (-not (Wait-TcpPort -HostName "127.0.0.1" -Port $serverSettings.database -TimeoutSeconds 20)) {
        throw "The local database did not start. See $dbErr and $logDir\mariadb.log"
    }
}

$serviceOk = $false
if (Test-TcpPort -HostName "127.0.0.1" -Port $serverSettings.http) {
    try {
        $health = Get-FgoLocalHealth
        $serviceOk = $health.StatusCode -eq 200 -and $health.Content -match "Service OK"
    }
    catch { $serviceOk = $false }
    if (-not $serviceOk) {
        throw "Port $($serverSettings.http) is already occupied by another program. Stop it before starting the FGO local server."
    }
}

if (-not $serviceOk) {
    $serverOut = Join-Path $logDir "artemis-stdout.log"
    $serverErr = Join-Path $logDir "artemis-stderr.log"
    $serverStart = @{
        FilePath = $pythonPath
        ArgumentList = @("index.py", "--config", "config")
        WorkingDirectory = $artemisRoot
        RedirectStandardOutput = $serverOut
        RedirectStandardError = $serverErr
    }
    $serverProcess = Start-FgoBackgroundProcess @serverStart
    [System.IO.File]::WriteAllText(
        (Join-Path $stateDir "artemis.pid"),
        [string]$serverProcess.Id,
        $encoding
    )
}

foreach ($port in @($serverSettings.http, $serverSettings.billing, $serverSettings.aime)) {
    if (-not (Wait-TcpPort -HostName "127.0.0.1" -Port $port -TimeoutSeconds 30)) {
        throw "ARTEMiS did not open required port $port. See $logDir\artemis-stderr.log"
    }
}

$health = Get-FgoLocalHealth
if ($health.StatusCode -ne 200 -or $health.Content -notmatch "Service OK") {
    throw "The local ALL.Net service did not pass its health check."
}

Write-Host "FGO local server is ready at $ServerHost ($($serverSettings.http)/$($serverSettings.billing)/$($serverSettings.aime))." -ForegroundColor Green

} finally { if ($startLock) { $startLock.Dispose() } }
