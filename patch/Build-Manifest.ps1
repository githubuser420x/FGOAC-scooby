<#
Builds the patch manifest for a staged FGOA scooby package.

The package root holds "FGOA scooby.exe", the payload folder and (after this script runs)
manifest.json. Every file under payload\ is listed by the path it takes inside the game
install, so payload\App\zh\rom\... becomes App\zh\rom\... . "FGOA scooby.exe" lists itself,
because the package is unzipped into the install folder and the exe is already in place.

  .\Build-Manifest.ps1 -PackageRoot D:\FGOA\release\FGOA-scooby-v1.0 -Version 1.0

Exit codes: 0 manifest written, 1 unexpected error, 2 package root is not a staged package.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [string]$Version = '1.0',
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

try {
    if (!(Test-Path -LiteralPath $PackageRoot -PathType Container)) {
        Write-Host "Package root not found: $PackageRoot"
        exit 2
    }
    $PackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd('\')
    $payload = [IO.Path]::Combine($PackageRoot, 'payload')
    $launcher = [IO.Path]::Combine($PackageRoot, 'FGOA scooby.exe')
    if (!(Test-Path -LiteralPath $payload -PathType Container)) {
        Write-Host "Payload folder not found: $payload"
        exit 2
    }
    if (!(Test-Path -LiteralPath $launcher -PathType Leaf)) {
        Write-Host "Launcher not found: $launcher"
        exit 2
    }
    if ([string]::IsNullOrWhiteSpace($Output)) { $Output = [IO.Path]::Combine($PackageRoot, 'manifest.json') }

    $entries = New-Object 'System.Collections.Generic.List[object]'
    $payloadPrefix = $payload + '\'
    foreach ($file in (Get-ChildItem -LiteralPath $payload -File -Recurse)) {
        $entries.Add([pscustomobject]@{
            Relative = $file.FullName.Substring($payloadPrefix.Length)
            FullName = $file.FullName
        })
    }
    $entries.Add([pscustomobject]@{ Relative = 'FGOA scooby.exe'; FullName = $launcher })

    $sorted = $entries | Sort-Object -Property Relative -CaseSensitive
    $files = [ordered]@{}
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $index = 0
    foreach ($entry in $sorted) {
        $hash = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $files[$entry.Relative] = $hash
        $lines.Add($entry.Relative + '|' + $hash)
        $index++
        if (($index % 200) -eq 0) { Write-Host "  hashed $index of $($sorted.Count) files" }
    }

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $manifestHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-','').ToLowerInvariant()
    } finally { $sha.Dispose() }

    $manifest = [ordered]@{
        version      = $Version
        created      = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        manifestHash = $manifestHash
        files        = $files
    }
    [IO.File]::WriteAllText($Output, ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    Write-Host "Manifest written: $Output"
    Write-Host "  version $Version, $($files.Count) files, hash $manifestHash"
    exit 0
} catch {
    Write-Host "Could not build the manifest: $($_.Exception.Message)"
    exit 1
}
