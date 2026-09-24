<#
.SYNOPSIS
Registers an extracted dev layout using Add-AppxPackage -Register (alternative to installing the .msix).

.PARAMETER LayoutDirectory
Folder that contains AppxManifest.xml plus all binaries (e.g. artifacts\SparseLayout after build, before packing).

.NOTES
The layout must be signed the same way as an MSIX (signtool on the manifest bundle) for non-Developer machines.
On many dev setups it is simpler to use Install-SparseMsix.ps1 after Build-SparseMsix.ps1.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $LayoutDirectory
)

$ErrorActionPreference = "Stop"
$layout = (Resolve-Path -LiteralPath $LayoutDirectory).Path
$manifest = Join-Path $layout "AppxManifest.xml"
if (-not (Test-Path -LiteralPath $manifest)) {
    throw "AppxManifest.xml not found under $layout"
}

Write-Host "Registering $manifest"
Add-AppxPackage -Register $manifest
Write-Host "Done. Restart explorer.exe before testing."
