<#
.SYNOPSIS
Installs (or updates) the signed dev MSIX produced by Build-SparseMsix.ps1.

.NOTES
Self-signed MSIX: trust the same public key as the PFX used for signtool.
- CurrentUser\TrustedPeople + CurrentUser\Root (always)
- LocalMachine\TrustedPeople + LocalMachine\Root when this script can open those stores (run **as Administrator** once if Add-AppxPackage still returns 0x800B0109 while signtool verify succeeds — AppX deployment often validates against LocalMachine).

Re-run after bumping Version in AppxManifest.xml or remove the old package first.
If you regenerate the PFX after signing the MSIX, rebuild the MSIX so the thumbprint matches the .cer.
#>
param(
    [string] $MsixPath = (Join-Path $PSScriptRoot "..\..\artifacts\FolderManagingSparseDev.msix"),
    [string] $CertificateCer = (Join-Path $PSScriptRoot "..\..\artifacts\certs\FolderManaging-Dev.cer"),
    [string] $CertificatePfx = (Join-Path $PSScriptRoot "..\..\artifacts\certs\FolderManaging-Dev.pfx"),
    [string] $PfxPassword = "devcert",
    [switch] $SkipCertificateTrust,
    [switch] $RestartExplorer = $true
)

$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path -LiteralPath $MsixPath).Path

function Import-CertificateIfMissing {
    param(
        [string] $CerPath,
        [string] $StoreLocation,
        [string] $StoreName
    )

    $sl = [System.Enum]::Parse([System.Security.Cryptography.X509Certificates.StoreLocation], $StoreLocation, $true)
    $sn = [System.Enum]::Parse([System.Security.Cryptography.X509Certificates.StoreName], $StoreName, $true)

    $cerObj = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CerPath)
    try {
        $thumb = $cerObj.Thumbprint
    }
    finally {
        $cerObj.Dispose()
    }

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($sn, $sl)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $found = $store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $thumb, $false)
        if ($found.Count -gt 0) {
            Write-Host "Certificate $thumb already in ${StoreLocation}\${StoreName}."
            return
        }

        $toAdd = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CerPath)
        try {
            $store.Add($toAdd)
            Write-Host "Imported $CerPath -> ${StoreLocation}\${StoreName}."
        }
        finally {
            $toAdd.Dispose()
        }
    }
    finally {
        $store.Close()
    }
}

function Ensure-DevSignerTrusted {
    param([string] $CerPath, [string] $PfxPath, [string] $PfxPwd)

    # MSIX is signed with the PFX; Windows must trust the SAME public key. Always re-export .cer from PFX when PFX exists
    # so a stale .cer (wrong thumbprint) cannot cause 0x800B0109 even if Root/TrustedPeople already contain an old cert.
    if (Test-Path -LiteralPath $PfxPath) {
        Write-Host "Syncing .cer from PFX -> $CerPath (matches signtool signing key)"
        $dir = Split-Path -Parent $CerPath
        if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPwd)
        try {
            if (Test-Path -LiteralPath $CerPath) { Remove-Item -LiteralPath $CerPath -Force }
            Export-Certificate -Cert $cert -FilePath $CerPath -Type CERT | Out-Null
            Write-Host "PFX public thumbprint: $($cert.Thumbprint)"
        }
        finally {
            $cert.Dispose()
        }
    }
    elseif (-not (Test-Path -LiteralPath $CerPath)) {
        Write-Warning "No PFX at $PfxPath and no CER at $CerPath. Run New-FolderManagingDevCertificate.ps1 first."
        return
    }

    if (-not (Test-Path -LiteralPath $CerPath)) {
        Write-Warning "No certificate at $CerPath. Run scripts\sparse\New-FolderManagingDevCertificate.ps1 first, then retry. Add-AppxPackage may fail with 0x800B0109."
        return
    }

    Write-Host "Trusting dev signer (CurrentUser): TrustedPeople + Root"
    Import-CertificateIfMissing -CerPath $CerPath -StoreLocation CurrentUser -StoreName TrustedPeople
    Import-CertificateIfMissing -CerPath $CerPath -StoreLocation CurrentUser -StoreName Root

    Write-Host "Trusting dev signer (LocalMachine): TrustedPeople + Root (needs elevation; skipped if access denied)"
    foreach ($sn in @("TrustedPeople", "Root")) {
        try {
            Import-CertificateIfMissing -CerPath $CerPath -StoreLocation LocalMachine -StoreName $sn
        }
        catch {
            Write-Host "Could not write LocalMachine\$sn : $($_.Exception.Message)"
        }
    }

    $isAdmin = ([Security.Principal.WindowsPrincipal]([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Warning "If Add-AppxPackage fails with 0x800B0109 while signtool verify is OK, re-run this script as Administrator (or import .cer to LocalMachine\Root once in elevated PowerShell)."
    }
}

if (-not $SkipCertificateTrust) {
    Ensure-DevSignerTrusted -CerPath $CertificateCer -PfxPath $CertificatePfx -PfxPwd $PfxPassword
}

function Resolve-SignTool {
    $cmd = Get-Command signtool.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }

    foreach ($base in @("${env:ProgramFiles(x86)}\Windows Kits\10", "$env:ProgramFiles\Windows Kits\10")) {
        if (-not (Test-Path -LiteralPath $base)) { continue }
        $binRoot = Join-Path $base "bin"
        $flat = Join-Path $binRoot "x64\signtool.exe"
        if (Test-Path -LiteralPath $flat) { return $flat }
        foreach ($verDir in Get-ChildItem -Path $binRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending) {
            $p = Join-Path $verDir.FullName "x64\signtool.exe"
            if (Test-Path -LiteralPath $p) { return $p }
        }
    }

    return $null
}

# The user edition (FolderManaging-Setup.exe) registers the same COM class / verb: never keep both.
$userEdition = Get-AppxPackage -Name "FolderManaging" -ErrorAction SilentlyContinue
if ($userEdition) {
    Write-Host "Removing user edition package $($userEdition.PackageFullName)"
    Remove-AppxPackage -Package $userEdition.PackageFullName
}

$existing = Get-AppxPackage -Name "FolderManagingSparseDev" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing existing package $($existing.PackageFullName)"
    Remove-AppxPackage -Package $existing.PackageFullName
}

$explorerWasRunning = $false
if ($RestartExplorer) {
    $procs = Get-Process explorer -ErrorAction SilentlyContinue
    if ($procs) {
        $explorerWasRunning = $true
        Write-Host "Stopping explorer.exe to avoid COM DLL caching..."
        try {
            Stop-Process -Name explorer -Force
            Start-Sleep -Milliseconds 750
        }
        catch {
            Write-Warning "Failed to stop explorer.exe: $($_.Exception.Message)"
        }
    }
}

$signExe = Resolve-SignTool
if ($signExe) {
    Write-Host "Verifying MSIX signature with: $signExe verify /pa"
    $verifyOut = & $signExe verify /pa /v $resolved 2>&1
    Write-Host ($verifyOut | Out-String)
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "signtool verify failed (exit $LASTEXITCODE). Re-run Build-SparseMsix.ps1 or fix signing; Add-AppxPackage may still fail."
    }
}
else {
    Write-Warning "signtool.exe not found; skipping signature verify."
}

Write-Host "Installing $resolved"
try {
    Add-AppxPackage -Path $resolved
}
catch {
    if ($_.Exception.Message -match "800B0109|0x800B0109") {
        Write-Host @"

Still untrusted (0x800B0109) even though signtool verify passed — AppX often requires **LocalMachine** trust.

Run **elevated** PowerShell once:

  Import-Certificate -FilePath `"$CertificateCer`" -CertStoreLocation Cert:\LocalMachine\Root
  Import-Certificate -FilePath `"$CertificateCer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

Then: .\scripts\sparse\Install-SparseMsix.ps1

Also enable Windows **Developer Mode** if sideloading is restricted.
"@
    }

    throw
}

if ($RestartExplorer -and $explorerWasRunning) {
    Write-Host "Starting explorer.exe..."
    try {
        Start-Process explorer.exe | Out-Null
    }
    catch {
        Write-Warning "Failed to start explorer.exe: $($_.Exception.Message)"
    }
}

# Color-hue sort property (optional; needs Administrator for HKLM + PSRegisterPropertySchema).
try {
    $regScript = Join-Path $PSScriptRoot "Register-PropertySchema.ps1"
    if (Test-Path -LiteralPath $regScript) {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($id)
        if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            Write-Host "Registering FolderManaging.ColorHue property schema..."
            & $regScript -RepoRoot (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
        }
        else {
            Write-Host @"
Skipped Color hue property registration (not elevated).
To enable Explorer Sort → Color hue (rainbow), run elevated:

  .\scripts\sparse\Register-PropertySchema.ps1
"@
        }
    }
}
catch {
    Write-Warning "Property schema registration failed: $($_.Exception.Message)"
}

Write-Host "Done. If actions still do nothing, open the log at: %LocalAppData%\\FolderManaging\\shell-menu.log"
