<#
Builds the release package for FGOAC scooby.

  .\package.ps1                       # version read from the built launcher
  .\package.ps1 -Version 1.1.1        # override it
  .\package.ps1 -Publish              # run publish.cmd first
  .\package.ps1 -SkipZip              # leave the folder, do not zip it

The run is repeatable: the payload is mirrored, so a second run only copies what changed and
removes what is no longer part of the package.

Exit codes: 0 packaged, 1 unexpected error, 2 a source the package needs is missing.
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$GameRoot = 'D:\FGOA',
    [string]$OutputRoot = 'D:\FGOA\release',
    [switch]$Publish,
    [switch]$SkipZip
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function Stop-WithMessage {
    param([string]$Message, [int]$Code)
    Write-Host $Message
    exit $Code
}

try {
    $repository = $PSScriptRoot
    $launcher = [IO.Path]::Combine($repository, 'dist\FGOAC scooby.exe')
    if ($Publish -or !(Test-Path -LiteralPath $launcher -PathType Leaf)) {
        Write-Host 'Publishing the launcher...'
        & ([IO.Path]::Combine($repository, 'publish.cmd'))
        if ($LASTEXITCODE -ne 0) { Stop-WithMessage 'publish.cmd failed, so nothing was packaged.' 2 }
    }
    if (!(Test-Path -LiteralPath $launcher -PathType Leaf)) {
        Stop-WithMessage "The launcher is missing: $launcher. Run publish.cmd, or pass -Publish." 2
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        # The csproj is the one place the version is written; read it back off the built launcher.
        $Version = [Diagnostics.FileVersionInfo]::GetVersionInfo($launcher).ProductVersion
        if ($Version -match '^(\d+\.\d+\.\d+)') { $Version = $Matches[1] }
        Write-Host "Version $Version, from the launcher"
    }
    $englishSet = [IO.Path]::Combine($GameRoot, 'App\zh')
    if (!(Test-Path -LiteralPath $englishSet -PathType Container)) {
        Stop-WithMessage "The English game files are missing: $englishSet" 2
    }
    $overlay = [IO.Path]::Combine($repository, 'overlay')
    if (!(Test-Path -LiteralPath $overlay -PathType Container)) {
        Stop-WithMessage "The overlay folder is missing: $overlay" 2
    }

    $packageName = "FGOAC-scooby-v$Version"
    $packageRoot = [IO.Path]::Combine($OutputRoot, $packageName)
    $payloadRoot = [IO.Path]::Combine($packageRoot, 'payload')
    [void][IO.Directory]::CreateDirectory($payloadRoot)

    # The English game files. file-trace.enabled turns on a per-file log meant for development,
    # and the .v101 / .bak copies are the author's originals kept beside ours.
    Write-Host "Mirroring the English game files into $payloadRoot\App\zh"
    $robocopy = & "$env:SystemRoot\System32\robocopy.exe" $englishSet ([IO.Path]::Combine($payloadRoot, 'App\zh')) /MIR /NJH /NJS /NP /NDL /NFL /R:2 /W:2 /XF 'file-trace.enabled' '*.v101' '*.bak' 'en-patch.json'
    if ($LASTEXITCODE -ge 8) { Stop-WithMessage "Copying the English game files failed: $robocopy" 1 }

    Write-Host 'Copying the English replacements for the scripts and data outside the launcher'
    $overlayPrefix = $overlay.TrimEnd('\') + '\'
    $overlayFiles = @(Get-ChildItem -LiteralPath $overlay -File -Recurse | ForEach-Object { $_.FullName.Substring($overlayPrefix.Length) })
    foreach ($relative in $overlayFiles) {
        $destination = [IO.Path]::Combine($payloadRoot, $relative)
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
        Copy-Item -LiteralPath ([IO.Path]::Combine($overlay, $relative)) -Destination $destination -Force
    }

    # Anything left over from an earlier run with a different overlay.
    $keptPrefix = [IO.Path]::Combine($payloadRoot, 'App\zh').TrimEnd('\') + '\'
    $payloadPrefix = $payloadRoot.TrimEnd('\') + '\'
    foreach ($file in (Get-ChildItem -LiteralPath $payloadRoot -File -Recurse)) {
        if ($file.FullName.StartsWith($keptPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($overlayFiles -contains $file.FullName.Substring($payloadPrefix.Length)) { continue }
        Write-Host "  removing a file that is no longer part of the package: $($file.FullName.Substring($payloadPrefix.Length))"
        Remove-Item -LiteralPath $file.FullName -Force
    }

    Write-Host 'Copying the launcher, the installer and the release notes'
    Copy-Item -LiteralPath $launcher -Destination ([IO.Path]::Combine($packageRoot, 'FGOAC scooby.exe')) -Force
    Copy-Item -LiteralPath ([IO.Path]::Combine($repository, 'patch\Apply-EN-Patch.ps1')) -Destination ([IO.Path]::Combine($packageRoot, 'Apply-EN-Patch.ps1')) -Force
    foreach ($guide in @('GUIDE_EN.md', 'GUIDE_EN.pdf')) {
        $source = [IO.Path]::Combine($GameRoot, 'docs', $guide)
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { Stop-WithMessage "The user guide is missing: $source" 2 }
        Copy-Item -LiteralPath $source -Destination ([IO.Path]::Combine($packageRoot, $guide)) -Force
    }
    $today = (Get-Date).ToString('yyyy-MM-dd')
    foreach ($document in @('README.md', 'CHANGELOG.md')) {
        $text = [IO.File]::ReadAllText([IO.Path]::Combine($repository, 'package', $document))
        $text = $text.Replace('{{VERSION}}', $Version).Replace('{{DATE}}', $today)
        [IO.File]::WriteAllText([IO.Path]::Combine($packageRoot, $document), $text, [Text.UTF8Encoding]::new($false))
    }

    Write-Host 'Building the manifest'
    & ([IO.Path]::Combine($repository, 'patch\Build-Manifest.ps1')) -PackageRoot $packageRoot -Version $Version
    if ($LASTEXITCODE -ne 0) { Stop-WithMessage 'The manifest could not be built, so the package is not complete.' 1 }

    $sums = New-Object 'System.Collections.Generic.List[string]'
    foreach ($name in @('FGOAC scooby.exe', 'Apply-EN-Patch.ps1', 'manifest.json', 'README.md', 'CHANGELOG.md', 'GUIDE_EN.md', 'GUIDE_EN.pdf')) {
        $hash = (Get-FileHash -LiteralPath ([IO.Path]::Combine($packageRoot, $name)) -Algorithm SHA256).Hash.ToLowerInvariant()
        $sums.Add("$hash *$name")
    }
    $sums.Add('')
    $sums.Add('The payload files are listed with their SHA-256 in manifest.json, and Apply-EN-Patch.ps1 checks every one of them after it copies.')
    [IO.File]::WriteAllLines([IO.Path]::Combine($packageRoot, 'SHA256SUMS.txt'), $sums.ToArray(), [Text.UTF8Encoding]::new($false))

    $packagedFiles = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse)
    $packagedSize = [math]::Round((($packagedFiles | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
    Write-Host "Package: $packageRoot"
    Write-Host "  $($packagedFiles.Count) files, $packagedSize MB"

    if ($SkipZip) { exit 0 }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = [IO.Path]::Combine($OutputRoot, "$packageName.zip")
    if ([IO.File]::Exists($zipPath)) { [IO.File]::Delete($zipPath) }
    Write-Host "Zipping to $zipPath - this takes a few minutes"
    # No base directory in the zip: the package is meant to be extracted straight into the game
    # folder, so its files have to land beside App and Server rather than in a subfolder.
    [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zipPath.sha256", "$zipHash *$packageName.zip" + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Host "Zip: $zipPath"
    Write-Host "  $([math]::Round(([IO.FileInfo]$zipPath).Length / 1MB, 1)) MB, SHA-256 $zipHash"
    exit 0
} catch {
    Write-Host "The package could not be built: $($_.Exception.Message)"
    exit 1
}
