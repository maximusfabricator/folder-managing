# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

$ErrorActionPreference = "Stop"

# User asked artifacts\FolderManaging.Cli.exe - use SparseLayout publish output (same bits as MSIX layout).
$candidates = @(
    (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\FolderManaging.Cli.exe"),
    (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\SparseLayout\FolderManaging.Cli.exe"),
    (Join-Path (Split-Path $PSScriptRoot -Parent) "src\FolderManaging.Cli\bin\Release\net10.0-windows\FolderManaging.Cli.exe")
)

$exe = $null
foreach ($c in $candidates) {
    if (Test-Path -LiteralPath $c) {
        $exe = $c
        break
    }
}

if (-not $exe) {
    throw "FolderManaging.Cli.exe not found in expected locations."
}

$folder = "D:\CodingCodingCoding"
$ini = Join-Path $folder "desktop.ini"

$report = New-Object System.Collections.Generic.List[string]
$null = $report.Add("AUTO_DEBUG_REPORT generated: $(Get-Date -Format o)")
$null = $report.Add("Note: requested .\\artifacts\\FolderManaging.Cli.exe may not exist; first resolved candidate was used.")
$null = $report.Add("CLI used: $exe")
$null = $report.Add("Target folder: $folder")
$null = $report.Add("Folder exists: $(Test-Path -LiteralPath $folder)")
$null = $report.Add("desktop.ini BEFORE: $(Test-Path -LiteralPath $ini)")

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
# Windows PowerShell 5.1: use Arguments string (ArgumentList is not always available).
$psi.Arguments = '--action color --value mint --path "' + ($folder -replace '"', '\"') + '"'

$p = [System.Diagnostics.Process]::Start($psi)
$stdout = $p.StandardOutput.ReadToEnd()
$stderr = $p.StandardError.ReadToEnd()
$p.WaitForExit()
$code = $p.ExitCode

$null = $report.Add("Exit code: $code")
$null = $report.Add("STDOUT:")
$null = $report.Add($stdout.TrimEnd())
$null = $report.Add("STDERR:")
$null = $report.Add($stderr.TrimEnd())

$after = Test-Path -LiteralPath $ini
$null = $report.Add("desktop.ini AFTER: $after")

if ($after) {
    try {
        $attr = [System.IO.File]::GetAttributes($ini)
        $null = $report.Add("desktop.ini attributes: $attr")
        $head = Get-Content -LiteralPath $ini -TotalCount 12 -Encoding Unicode -ErrorAction Stop
        $null = $report.Add("desktop.ini first lines (Unicode read attempt):")
        $null = $report.Add(($head -join "`r`n"))
    }
    catch {
        $null = $report.Add("Could not read desktop.ini as Unicode: $($_.Exception.Message)")
        try {
            $raw = Get-Content -LiteralPath $ini -TotalCount 12 -Raw -ErrorAction Stop
            $null = $report.Add($raw)
        }
        catch {
            $null = $report.Add("Raw read failed: $($_.Exception.Message)")
        }
    }

    $marker = Select-String -LiteralPath $ini -Pattern "FolderManaging-managed" -Quiet -ErrorAction SilentlyContinue
    $null = $report.Add("Contains FolderManaging-managed marker: $marker")
}

# Analysis
$null = $report.Add("")
$null = $report.Add("=== ANALYSIS ===")
if ($code -eq 0 -and $after) {
    $null = $report.Add("RESULT: CLI SUCCEEDED - desktop.ini exists after run.")
    $null = $report.Add("INTERPRETATION: If Explorer context menu still does nothing while logs show COM success, bias diagnosis toward Explorer refresh / icon pipeline / a different code path from menu vs this CLI invocation.")
}
elseif ($code -eq 0 -and -not $after) {
    $null = $report.Add("RESULT: CLI reported success but desktop.ini missing - unexpected; check FolderIconManager paths or race.")
}
else {
    $null = $report.Add("RESULT: CLI FAILED (exit $code).")
    $null = $report.Add("INTERPRETATION: FolderIconManager / permissions / path / icon generation - fix here first before blaming COM sandbox.")
}

$dest = 'C:\Users\Public\AUTO_DEBUG_REPORT.txt'

$text = $report -join "`r`n"
[System.IO.File]::WriteAllText($dest, $text, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote: $dest"
Write-Host $text
