# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

# Lists .dll under installed MSIX and under local SparseLayout (if present), with FileVersion.
$ErrorActionPreference = "SilentlyContinue"

function Show-DllVersions([string] $root, [string] $label) {
    Write-Host ""
    Write-Host "======== $label ========"
    Write-Host "Root: $root"
    if (-not (Test-Path -LiteralPath $root)) {
        Write-Host "(path not found)"
        return
    }

    Get-ChildItem -LiteralPath $root -Filter "*.dll" -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        ForEach-Object {
            $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName)
            [PSCustomObject]@{
                Relative = $_.FullName.Substring($root.Length).TrimStart('\')
                FileVersion = $vi.FileVersion
                ProductVersion = $vi.ProductVersion
                FileSize = $_.Length
                LastWrite = $_.LastWriteTimeUtc.ToString("o")
            }
        } |
        Format-Table -AutoSize
}

$pkg = Get-AppxPackage -Name "FolderManagingSparseDev*" | Select-Object -First 1
if ($pkg) {
    Show-DllVersions $pkg.InstallLocation "INSTALLED MSIX (Get-AppxPackage)"
}
else {
    Write-Host "No installed package matching FolderManagingSparseDev*"
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$layout = Join-Path $repo "artifacts\SparseLayout"
Show-DllVersions $layout "LOCAL STAGED LAYOUT (artifacts\SparseLayout)"

$msix = Join-Path $repo "artifacts\FolderManagingSparseDev.msix"
if (Test-Path -LiteralPath $msix) {
    Write-Host ""
    Write-Host "======== MSIX FILE (on disk) ========"
    $fi = Get-Item -LiteralPath $msix
    Write-Host "Path: $($fi.FullName)"
    Write-Host "Length: $($fi.Length)  LastWriteUtc: $($fi.LastWriteTimeUtc.ToString('o'))"
}
