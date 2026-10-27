# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

$ErrorActionPreference = "SilentlyContinue"
$g = "F4B3C2B1-0A1E-4D8F-9C7B-6E5D4C3B2A01"
$roots = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DesktopExtensions",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DesktopExtensions",
    "HKLM:\SOFTWARE\Classes\PackagedCom",
    "HKCU:\SOFTWARE\Classes\PackagedCom",
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\ShellExtensions",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ShellExtensions"
)

foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root)) {
        Write-Host "SKIP (missing): $root"
        continue
    }

    Write-Host ""
    Write-Host "SCAN: $root"
    Get-ChildItem -LiteralPath $root -Recurse -Depth 10 -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*$g*" -or $_.PSChildName -like "*$g*" } |
        Select-Object -First 25 -ExpandProperty FullName
}
