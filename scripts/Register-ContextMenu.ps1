# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

<#!
Registers the static cascading "Folder Managing" context menu for folders (per-user, HKCU).
Run after publishing the CLI so -ExePath points to a stable location.

Example:
  .\Register-ContextMenu.ps1 -ExePath "D:\Important\FolderManaging\artifacts\FolderManaging.Cli.exe"
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $ExePath
)

$resolved = (Resolve-Path -LiteralPath $ExePath).Path
if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
    throw "Executable not found: $resolved"
}

& $resolved install-context-menu --exe $resolved
Write-Host "Done. If Explorer does not refresh, run: Stop-Process -Name explorer -Force"
