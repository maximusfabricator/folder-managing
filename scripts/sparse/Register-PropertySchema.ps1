<#
.SYNOPSIS
Registers FolderManaging.ColorHue so Explorer can sort colored folders by rainbow (hue).

.NOTES
Requires Administrator. Copies FolderManaging.propdesc to ProgramData and writes
DesktopIniPropertyMap under HKLM. Safe to re-run after rebuilding the package.
#>
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string] $PropdescPath = "",
    [string] $CliExe = ""
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    throw "Run Register-PropertySchema.ps1 from an elevated PowerShell (Administrator)."
}

if (-not $PropdescPath) {
    $candidates = @(
        (Join-Path $RepoRoot "artifacts\SparseLayout\FolderManaging.propdesc"),
        (Join-Path $RepoRoot "sparse\FolderManaging.propdesc")
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) {
            $PropdescPath = (Resolve-Path -LiteralPath $c).Path
            break
        }
    }
}

if (-not $PropdescPath -or -not (Test-Path -LiteralPath $PropdescPath)) {
    throw "FolderManaging.propdesc not found. Build the package first or pass -PropdescPath."
}

if (-not $CliExe) {
    $cliCandidates = @(
        (Join-Path $RepoRoot "artifacts\SparseLayout\FolderManaging.Cli.exe"),
        (Join-Path $RepoRoot "src\FolderManaging.Cli\bin\Release\net8.0-windows\FolderManaging.Cli.exe"),
        (Join-Path $RepoRoot "src\FolderManaging.Cli\bin\Debug\net8.0-windows\FolderManaging.Cli.exe")
    )
    foreach ($c in $cliCandidates) {
        if (Test-Path -LiteralPath $c) {
            $CliExe = (Resolve-Path -LiteralPath $c).Path
            break
        }
    }
}

if (-not $CliExe -or -not (Test-Path -LiteralPath $CliExe)) {
    throw "FolderManaging.Cli.exe not found. Build/publish first or pass -CliExe."
}

Write-Host "Registering Rating←ColorHue map via CLI:"
Write-Host "  $CliExe register-property-schema --propdesc `"$PropdescPath`""
& $CliExe register-property-schema --propdesc $PropdescPath
if ($LASTEXITCODE -ne 0) {
    throw "register-property-schema failed with exit $LASTEXITCODE"
}

Write-Host @"

Done. Rainbow sort in Explorer (re-color folders first):
  A) Sort → More… → Comments → Sort by Comments ascending
     (InfoTip = padded hue; works even without this script)
  B) Sort → More… → Rating → Sort by Rating ascending
     (needs this elevated register)

Custom column "Color hue" is unreliable for folders on Win11 — use Comments or Rating.
"@
