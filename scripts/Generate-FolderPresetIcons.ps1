# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

<#
.SYNOPSIS
  Regenerates coral/ocean/mint/plum .ico files (Windows shell folder shape + color tint).

.DESCRIPTION
  By default writes to %LocalAppData%\FolderManaging\Icons (unpackaged dev).
  Use -IconsDir to write elsewhere (e.g. MSIX layout before makeappx).

  Build-SparseMsix.ps1 calls the same logic via FOLDERMANAGING_ICONS_DIR + `dotnet run ... regenerate-folder-icons`.
#>
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string] $Configuration = "Release",
    [string] $IconsDir = ""
)

$ErrorActionPreference = "Stop"
$cliProj = Join-Path $RepoRoot "src\FolderManaging.Cli\FolderManaging.Cli.csproj"

$prev = $env:FOLDERMANAGING_ICONS_DIR
if ($IconsDir) {
    $env:FOLDERMANAGING_ICONS_DIR = (Resolve-Path -LiteralPath $IconsDir).Path
}

try {
    dotnet run -c $Configuration --project $cliProj -- regenerate-folder-icons
    if ($LASTEXITCODE -ne 0) { throw "regenerate-folder-icons failed (exit $LASTEXITCODE)" }
}
finally {
    if ($null -eq $prev) {
        Remove-Item Env:\FOLDERMANAGING_ICONS_DIR -ErrorAction SilentlyContinue
    }
    else {
        $env:FOLDERMANAGING_ICONS_DIR = $prev
    }
}
