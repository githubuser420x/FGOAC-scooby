[CmdletBinding()]
param([switch]$AsJson)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$root=Split-Path -Parent $PSScriptRoot
$checks=[Collections.Generic.List[object]]::new()
function Add-Check([string]$Name,[bool]$Passed,[string]$Detail) {
    $checks.Add([pscustomobject]@{ Name=$Name; Passed=$Passed; Detail=$Detail })
}
Add-Check 'Operating System' ([Environment]::Is64BitOperatingSystem -and [Environment]::OSVersion.Version.Major -ge 10) ([Environment]::OSVersion.VersionString+'; this update targets Windows 10/11 x64.')
Add-Check 'PowerShell' ([IntPtr]::Size -eq 8 -and $PSVersionTable.PSVersion -ge [version]'5.1' -and $PSVersionTable.PSVersion.Major -ne 6) ("Found $($PSVersionTable.PSVersion), $([IntPtr]::Size*8)-bit. Windows PowerShell 5.1 and PowerShell 7 are both supported.")
Add-Check 'Launcher .NET' $true 'Both front-end EXEs bundle the .NET desktop runtime, so you never have to install .NET separately.'
$criticalPaths=@('App\ago.exe','App\fgohook.dll','App\am\amdaemon.exe',
    'App\Tools\Locale_Remulator\LRHookx64.dll','DEVICE\runtime\segatools.runtime.ini',
    'DEVICE\runtime\amdaemon_main.json','Server\mariadb-10.11.16-winx64\bin\mariadbd.exe') |
    ForEach-Object { [IO.Path]::GetFullPath((Join-Path $root $_)) }
$tooLong=@($criticalPaths | Where-Object { $_.Length -ge 260 })
Add-Check 'Startup Path Length' ($tooLong.Count -eq 0) $(if($tooLong.Count){
    'These paths are too long for the native modules: '+($tooLong -join '; ')+'. Move the installation into fewer nested folders; the drive letter can stay as it is.'
}else{'The critical startup paths are short enough for the native modules. Very deep resource subfolders can still hit the native path length limit.'})
try {
    if (!('FgoEnvironmentNative' -as [type])) {
        # A DLL still marked as downloaded cannot be loaded by Windows PowerShell (0x80131515).
        Unblock-File -LiteralPath (Join-Path $PSScriptRoot 'FGO_Runtime.dll') -ErrorAction SilentlyContinue
        [void][Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'FGO_Runtime.dll'))
    }
    function Test-Library([string]$Folder,[string]$Name) {
        $path=Join-Path $Folder $Name
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
        $module=[FgoEnvironmentNative]::LoadLibraryEx($path,[IntPtr]::Zero,0x900)
        if ($module -eq [IntPtr]::Zero) { return $false }
        [void][FgoEnvironmentNative]::FreeLibrary($module)
        return $true
    }
    foreach ($runtime in @(
        @{Name='Visual C++ 2010 x64'; Files=@('msvcr100.dll','msvcp100.dll')},
        @{Name='Visual C++ 2012 x64'; Files=@('msvcr110.dll','msvcp110.dll')},
        @{Name='Visual C++ v14 x64'; Files=@('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')}
    )) {
        $missing=@()
        foreach ($folder in @($PSScriptRoot,(Join-Path $PSScriptRoot 'am'))) {
            foreach ($name in $runtime.Files) {
                $searchFolder=if (Test-Path -LiteralPath (Join-Path $folder $name)) {$folder} else {[Environment]::SystemDirectory}
                if (!(Test-Library $searchFolder $name)) { $missing+="$searchFolder\$name" }
            }
        }
        $detail=if ($missing.Count -eq 0) {'The required DLLs load; this package ships the base runtime libraries.'} else {"Cannot load: $($missing -join '; '). Reapply the full package, or install the matching Microsoft x64 redistributable."}
        Add-Check $runtime.Name ($missing.Count -eq 0) $detail
    }
    $mediaMissing=@(@('mfplat.dll','mfreadwrite.dll') | Where-Object { !(Test-Library ([Environment]::SystemDirectory) $_) })
    Add-Check 'Windows Media Components' ($mediaMissing.Count -eq 0) $(if($mediaMissing.Count){"Cannot load $($mediaMissing -join ', '). Install the Media Feature Pack on Windows N/KN, or restore the media components on a trimmed-down Windows."}else{'The Media Foundation libraries load.'})
} catch { Add-Check 'Runtime Library Check' $false $_.Exception.Message }
try {
    $service=Get-Service -Name Audiosrv
    Add-Check 'Windows Audio Service' ($service.Status -eq 'Running') ("Current state: $($service.Status). Start the Windows Audio service if it is not running.")
    $registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
    $render=$registry.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render')
    $active=0
    try {
        if ($render) { foreach ($name in $render.GetSubKeyNames()) {
            $endpoint=$render.OpenSubKey($name)
            try { if (([long]$endpoint.GetValue('DeviceState',0) -band 1) -ne 0) { $active++ } } finally { $endpoint.Dispose() }
        } }
    } finally { if ($render) {$render.Dispose()}; $registry.Dispose() }
    Add-Check 'Audio Output Device' ($active -gt 0) ("Found $active enabled output endpoints; the game uses shared mode. If there are none, connect or enable speakers or headphones and set a default output device in Windows.")
} catch { Add-Check 'Audio Check' $false $_.Exception.Message }
$required=@('App\ago.exe','App\am\amdaemon.exe','App\fgohook.dll','App\FGO_Runtime.dll','App\inject.exe','App\Tools\Locale_Remulator\LRHookx64.dll','App\config.json','App\segatools.ini','AMFS\ICF1','AMFS\ICF2','Server\mariadb-10.11.16-winx64\bin\mariadbd.exe')
$missing=@($required | Where-Object {!(Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)})
Add-Check 'Game and Server Files' ($missing.Count -eq 0) $(if($missing.Count){'Missing: '+($missing -join ', ')+'. Restore the full installation package; an incremental update does not contain the game itself.'}else{'The core program files are present; this check does not verify every ROM resource.'})
try {
    $python=Join-Path $root 'Server\python\python.exe'
    $result=& $python -I -c "import sys,yaml,sqlalchemy,aiomysql,uvicorn,starlette,Crypto; print(sys.version.split()[0])" 2>&1
    if ($LASTEXITCODE -ne 0) {throw ($result -join "`n")}
    Add-Check 'Bundled Python and Server Libraries' $true ("Python $result and the main server libraries load; no system Python or online dependency install is needed.")
} catch { Add-Check 'Bundled Python and Server Libraries' $false ("The bundled environment is incomplete: $($_.Exception.Message). Restore Server/python and Server/venv, and do not overwrite them with another project's venv.") }
foreach ($folder in @('App','AMFS','GameData','DEVICE','Server\state','Server\data\mariadb','logs')) {
    $probe=Join-Path (Join-Path $root $folder) ('.fgo-env-'+[guid]::NewGuid().ToString('N'))
    try { [IO.File]::WriteAllText($probe,'check'); [IO.File]::Delete($probe); Add-Check ("Folder permissions: $folder") $true 'Temporary files can be created and deleted.' }
    catch { Add-Check ("Folder permissions: $folder") $false ("Cannot write: $($_.Exception.Message). Run the launcher as administrator and make sure the drive is not full, write-protected, or blocked by your security software.") }
}
Add-Check 'Network Mode' $true 'In auto mode the game reaches the local services through an in-process virtual adapter and 127.0.0.1, so no physical adapter, gateway or internet connection is needed. This check does not start the server.'
if ($AsJson) { ConvertTo-Json -InputObject @($checks.ToArray()) -Depth 4 }
else { foreach ($check in $checks) { $status=if($check.Passed){'PASS'}else{'ACTION NEEDED'}; Write-Output "[$status] $($check.Name): $($check.Detail)`n" } }
