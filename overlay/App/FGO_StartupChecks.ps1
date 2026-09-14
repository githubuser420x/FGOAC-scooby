function Test-FgoWritableLayout {
    param([Parameter(Mandatory=$true)][string]$InstallRoot)
    $root = [IO.Path]::GetFullPath($InstallRoot)
    # These are application data folders, never a drive root or Windows folder.
    $folders = @('App','AMFS','GameData','DEVICE','DEVICE\runtime','DEVICE\print',
        'logs','Server\state','Server\artemis\config','Server\data\mariadb')
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach ($relative in $folders) {
        $path = Join-Path $root $relative
        try {
            [IO.Directory]::CreateDirectory($path) | Out-Null
            $probe = Join-Path $path ('.fgo-write-' + [Guid]::NewGuid().ToString('N'))
            try { [IO.File]::WriteAllText($probe, 'write check') }
            catch [UnauthorizedAccessException] {
                # Repair only this application's directory for this user.
                $aclOutput = & "$env:SystemRoot\System32\icacls.exe" $path /grant "*$($identity):(OI)(CI)M" 2>&1
                if ($LASTEXITCODE -ne 0) { throw "ACL repair failed: $aclOutput" }
                [IO.File]::WriteAllText($probe, 'write check')
            }
            [IO.File]::Delete($probe)
        } catch {
            throw "[FGO-LAUNCHER:4] Cannot write to $path. Run the launcher as administrator and make sure the drive is not full, write-protected, or blocked by your security software. Original error: $($_.Exception.Message)"
        }
    }
    $files = @('App\fgo-launcher.json','App\deck.json','App\segatools.ini',
        'Server\mariadb.ini','Server\artemis\config\core.yaml')
    foreach ($relative in $files) {
        $path = Join-Path $root $relative
        if ([IO.File]::Exists($path)) {
            $attrs = [IO.File]::GetAttributes($path)
            if ($attrs -band [IO.FileAttributes]::ReadOnly) {
                [IO.File]::SetAttributes($path, $attrs -band (-bnot [IO.FileAttributes]::ReadOnly))
            }
            $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
            $stream.Dispose()
        }
    }
    # Extracted archives sometimes mark existing runtime/save files read-only.
    foreach ($relative in @('AMFS','GameData','DEVICE','Server\state','Server\data\mariadb')) {
        Get-ChildItem -LiteralPath (Join-Path $root $relative) -File -Recurse -ErrorAction Stop |
            Where-Object { $_.IsReadOnly } | ForEach-Object { $_.IsReadOnly = $false }
    }
}

function Protect-FgoChildProcessStreams {
    # Background processes must not retain the launcher's output pipes: the
    # frontend waits for EOF after the startup script finishes.
    if (!('FgoChildStreams' -as [type])) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'FGO_Runtime.dll'))
    }
    [FgoChildStreams]::Protect()
}

function Start-FgoBackgroundProcess {
    param([string]$FilePath,[string[]]$ArgumentList,[string]$WorkingDirectory,
          [string]$RedirectStandardOutput,[string]$RedirectStandardError)
    if (!('FgoBackgroundProcess' -as [type])) {
        [void][Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'FGO_Runtime.dll'))
    }
    [pscustomobject]@{ Id=[FgoBackgroundProcess]::Start($FilePath,$ArgumentList,$WorkingDirectory,$RedirectStandardOutput,$RedirectStandardError) }
}
