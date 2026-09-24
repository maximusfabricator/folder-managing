$ErrorActionPreference = "SilentlyContinue"
$guid = "F4B3C2B1-0A1E-4D8F-9C7B-6E5D4C3B2A01"
$pkg = Get-AppxPackage -Name "FolderManagingSparseDev*" | Select-Object -First 1
if (-not $pkg) {
    Write-Host "Package not installed."
    exit 1
}

$pkg | Format-List Name, PackageFamilyName, PackageFullName, InstallLocation

$pf = $pkg.PackageFamilyName
Write-Host ""
Write-Host "Searching PackagedCom under PackageFamilyName: $pf"

$roots = @(
    "HKCU:\Software\Classes\PackagedCom\Package\$pf",
    "HKLM:\SOFTWARE\Classes\PackagedCom\Package\$pf"
)

foreach ($r in $roots) {
    if (-not (Test-Path -LiteralPath $r)) {
        Write-Host "MISSING: $r"
        continue
    }

    Write-Host "EXISTS: $r"
    Get-ChildItem -LiteralPath $r -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $guid -or $_.PSChildName -match $guid } |
        Select-Object -First 30 FullName
}

Write-Host ""
Write-Host "Also search ActivatableClass (short):"
foreach ($hive in @("HKLM:\SOFTWARE\Microsoft\WindowsRuntime\ActivatableClassId", "HKCU:\SOFTWARE\Microsoft\WindowsRuntime\ActivatableClassId")) {
    if (Test-Path -LiteralPath $hive) {
        $hit = Get-ChildItem -LiteralPath $hive -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match $guid }
        if ($hit) { $hit | Format-Table FullName -AutoSize } else { Write-Host "No hit under $hive" }
    }
}
