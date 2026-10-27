# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

<#
.SYNOPSIS
Stages binaries + manifest, generates placeholder visual assets, packs an unsigned layout to MSIX, and signs it.

.PARAMETER SkipSign
If set, skips signtool (install will fail unless you sign manually or use another trust path).

.NOTES
Requires Windows SDK (makeappx.exe + signtool.exe) on PATH or under KitsRoot10.
#>
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string] $Configuration = "Release",
    [string] $CertificatePfx = (Join-Path $PSScriptRoot "..\..\artifacts\certs\FolderManaging-Dev.pfx"),
    [string] $CertificatePassword = "devcert",
    [switch] $SkipSign
)

$ErrorActionPreference = "Stop"

function Resolve-WindowsSdkTool([string] $name) {
    $candidates = [System.Collections.Generic.List[string]]::new()

    $regRoots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots"
    )
    foreach ($r in $regRoots) {
        if (-not (Test-Path -LiteralPath $r)) { continue }
        $kitsRoot = (Get-ItemProperty -LiteralPath $r -ErrorAction SilentlyContinue).KitsRoot10
        if (-not $kitsRoot) { continue }
        $binRoot = Join-Path $kitsRoot "bin"
        # Classic flat layout (some machines): bin\x64\makeappx.exe
        $flat = Join-Path $binRoot "x64\$name"
        if (Test-Path -LiteralPath $flat) { $candidates.Add($flat) }
        # Per-SDK-version layout: bin\10.0.xxxxx\x64\makeappx.exe
        foreach ($verDir in Get-ChildItem -Path $binRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending) {
            foreach ($arch in @("x64", "arm64", "x86")) {
                $p = Join-Path $verDir.FullName "$arch\$name"
                if (Test-Path -LiteralPath $p) { $candidates.Add($p) }
            }
        }
    }

    # VS / Build Tools sometimes register Kits under Program Files without the same registry on minimal installs
    foreach ($base in @(
            "${env:ProgramFiles(x86)}\Windows Kits\10",
            "$env:ProgramFiles\Windows Kits\10"
        )) {
        if (-not (Test-Path -LiteralPath $base)) { continue }
        $binRoot = Join-Path $base "bin"
        $flat = Join-Path $binRoot "x64\$name"
        if (Test-Path -LiteralPath $flat) { $candidates.Add($flat) }
        foreach ($verDir in Get-ChildItem -Path $binRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending) {
            foreach ($arch in @("x64", "arm64", "x86")) {
                $p = Join-Path $verDir.FullName "$arch\$name"
                if (Test-Path -LiteralPath $p) { $candidates.Add($p) }
            }
        }
    }

    # Already on PATH (e.g. "Developer PowerShell for VS")
    $fromPath = (Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
    if ($fromPath) { $candidates.Add($fromPath) }

    $unique = $candidates | Select-Object -Unique
    foreach ($p in $unique) {
        if (Test-Path -LiteralPath $p) { return $p }
    }

    throw @"
Could not locate $name.

Install one of:
  - Visual Studio workload ""Desktop development with C++"" (includes Windows SDK), or
  - standalone ""Windows SDK"" from https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/

Then confirm the file exists, for example:
  dir ""${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name""
"@
}

$makeAppx = Resolve-WindowsSdkTool "makeappx.exe"
$signTool = Resolve-WindowsSdkTool "signtool.exe"

$layout = Join-Path $RepoRoot "artifacts\SparseLayout"
$outMsix = Join-Path $RepoRoot "artifacts\FolderManagingSparseDev.msix"

# Always start from a clean layout so leftover bundled icons / stale binaries cannot ship.
if (Test-Path -LiteralPath $layout) {
    Remove-Item -LiteralPath $layout -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $layout | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $outMsix) | Out-Null

Write-Host "Publishing Shell -> $layout"
dotnet publish (Join-Path $RepoRoot "src\FolderManaging.Shell\FolderManaging.Shell.csproj") -c $Configuration -r win-x64 --self-contained false -p:Platform=x64 -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Shell failed with exit $LASTEXITCODE" }

Write-Host "Publishing CLI -> $layout"
dotnet publish (Join-Path $RepoRoot "src\FolderManaging.Cli\FolderManaging.Cli.csproj") -c $Configuration -r win-x64 --self-contained false -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish CLI failed with exit $LASTEXITCODE" }

Write-Host "Publishing Companion -> $layout"
dotnet publish (Join-Path $RepoRoot "src\FolderManaging.Companion\FolderManaging.Companion.csproj") -c $Configuration -r win-x64 --self-contained false -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Companion failed with exit $LASTEXITCODE" }
$companionExe = Join-Path $layout "FolderManaging.Companion.exe"
if (-not (Test-Path -LiteralPath $companionExe)) {
    throw "Companion publish succeeded but FolderManaging.Companion.exe is missing from $layout"
}
Write-Host "Copying manifest"
Copy-Item -Force (Join-Path $RepoRoot "sparse\AppxManifest.xml") (Join-Path $layout "AppxManifest.xml")

$propdescSrc = Join-Path $RepoRoot "sparse\FolderManaging.propdesc"
if (-not (Test-Path -LiteralPath $propdescSrc)) {
    throw "Missing property schema: $propdescSrc"
}
Copy-Item -Force $propdescSrc (Join-Path $layout "FolderManaging.propdesc")
Write-Host "Copied FolderManaging.propdesc (Color hue sort schema)"

Write-Host "Generating placeholder PNG assets"
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $layout "Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
foreach ($pair in @(
        @{ Name = "StoreLogo.png"; Size = 50 },
        @{ Name = "Square44x44Logo.png"; Size = 44 },
        @{ Name = "Square150x150Logo.png"; Size = 150 }
    )) {
    $bmp = New-Object System.Drawing.Bitmap $pair.Size, $pair.Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(30, 120, 180))
    $g.Dispose()
    $path = Join-Path $assets $pair.Name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Tinted folder icons are generated at runtime under %LocalAppData%\FolderManaging\Icons (no bundled Icons\ in MSIX).
# Defensive: drop any Icons\ that a previous publish step might have left behind so we ship 0 pre-tinted bitmaps.
$bundledIcons = Join-Path $layout "Icons"
if (Test-Path -LiteralPath $bundledIcons) {
    Remove-Item -LiteralPath $bundledIcons -Recurse -Force
}

if (Test-Path -LiteralPath $outMsix) {
    Remove-Item -LiteralPath $outMsix -Force
}

Write-Host "Packing with $makeAppx"
& $makeAppx pack /o /d $layout /p $outMsix /nv
if ($LASTEXITCODE -ne 0) {
    throw "makeappx failed with exit $LASTEXITCODE"
}

if (-not $SkipSign) {
    if (-not (Test-Path -LiteralPath $CertificatePfx)) {
        throw "Certificate PFX not found at $CertificatePfx. Run New-FolderManagingDevCertificate.ps1 first."
    }

    Write-Host "Signing with $signTool"
    & $signTool sign /fd SHA256 /f $CertificatePfx /p $CertificatePassword $outMsix
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed with exit $LASTEXITCODE"
    }

    Write-Host "Verifying signature (signtool verify /pa)"
    & $signTool verify /pa /v $outMsix
    if ($LASTEXITCODE -ne 0) {
        throw "signtool verify failed with exit $LASTEXITCODE"
    }
}
else {
    Write-Warning "SkipSign set: MSIX is unsigned. Add-AppxPackage will likely fail unless you sign manually."
}

Write-Host "MSIX output: $outMsix"
