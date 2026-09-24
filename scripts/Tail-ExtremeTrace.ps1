$ErrorActionPreference = "SilentlyContinue"
$targets = @(
    (Join-Path $env:USERPROFILE ".FolderManaging\FolderManager_ExtremeTrace.log"),
    (Join-Path $env:LOCALAPPDATA "FolderManaging\FolderManager_ExtremeTrace.log"),
    (Join-Path $env:TEMP "FolderManager_ExtremeTrace.log"),
    (Join-Path $(if ($env:PUBLIC) { $env:PUBLIC } else { 'C:\Users\Public' }) "FolderManager_ExtremeTrace.log"),
    (Join-Path $(if ($env:PUBLIC) { $env:PUBLIC } else { 'C:\Users\Public' }) "Documents\FolderManager_ExtremeTrace.log"),
    (Join-Path $env:ProgramData "FolderManaging\FolderManager_ExtremeTrace.log")
)
foreach ($p in $targets) {
    if (Test-Path -LiteralPath $p) {
        Write-Host "--- $p (size=$((Get-Item -LiteralPath $p).Length) bytes) ---"
        Get-Content -LiteralPath $p -Tail 40
        Write-Host ""
    }
}
$pointer = Join-Path $env:USERPROFILE ".FolderManaging\FOLDERMANAGING_EXTREME_LOG.txt"
if (Test-Path -LiteralPath $pointer) {
    Write-Host "--- pointer file: $pointer ---"
    Get-Content -LiteralPath $pointer
    Write-Host ""
}

$alt = Join-Path $env:LOCALAPPDATA 'FolderManaging\shell-menu.log'
if (Test-Path -LiteralPath $alt) {
    Write-Host ""
    Write-Host "--- $alt (size=$((Get-Item -LiteralPath $alt).Length) bytes) ---"
    Get-Content -LiteralPath $alt -Tail 40
}
else {
    Write-Host "(no managed shell-menu.log at $alt)"
}
