# Full tree backup excluding bin, obj, .git (and common junk).
param(
    [string] $SourceDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string] $BackupRoot = (Split-Path (Resolve-Path (Join-Path $PSScriptRoot "..")).Path -Parent)
)

$ErrorActionPreference = "Stop"
$date = Get-Date -Format "yyyy-MM-dd"
$dest = Join-Path $BackupRoot "Project_Backup_$date"

if (Test-Path -LiteralPath $dest) {
    Remove-Item -LiteralPath $dest -Recurse -Force
}
New-Item -ItemType Directory -Path $dest -Force | Out-Null

# /MIR mirrors; /XD excludes directories at every level.
# Exit codes: 0-7 success; >=8 failure.
$args = @(
    $SourceDir, $dest,
    "/MIR",
    "/XD", "bin", "obj", ".git", ".vs", "node_modules",
    "/NFL", "/NDL", "/NJH", "/NP", "/NS", "/NC"
)
& robocopy.exe @args
$rc = $LASTEXITCODE
if ($rc -ge 8) {
    throw "robocopy failed with exit code $rc"
}

Write-Host "Backup complete: $dest"
Write-Host "Source was: $SourceDir"
