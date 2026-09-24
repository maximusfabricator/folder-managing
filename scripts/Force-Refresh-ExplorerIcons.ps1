<#
.SYNOPSIS
  Clears Explorer icon caches and restarts explorer.exe (same steps as FolderManaging.Cli force-refresh-icons).

.DESCRIPTION
  Use once after desktop.ini / folder icon changes if Windows keeps showing old icons.
  Closes all Explorer windows briefly.
#>
$ErrorActionPreference = 'Continue'
Write-Host 'Stopping explorer.exe...'
Get-Process -Name explorer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$iconDb = Join-Path $env:LOCALAPPDATA 'IconCache.db'
if (Test-Path -LiteralPath $iconDb) {
  Remove-Item -LiteralPath $iconDb -Force -ErrorAction SilentlyContinue
  Write-Host "Removed: $iconDb"
}

$explorerDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'
Get-ChildItem -LiteralPath $explorerDir -Filter 'iconcache*' -ErrorAction SilentlyContinue |
  Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host 'Starting explorer.exe...'
Start-Process explorer.exe
