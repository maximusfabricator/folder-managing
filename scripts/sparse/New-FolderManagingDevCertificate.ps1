<#
.SYNOPSIS
Creates a self-signed code-signing certificate for MSIX dev installs and trusts the public key for the current user.

.NOTES
- Publisher in sparse/AppxManifest.xml must match the certificate Subject exactly (default: CN=FolderManaging Dev).
- Requires Windows PowerShell 5+ or PowerShell 7 on Windows.
#>
param(
    [string] $Subject = "CN=FolderManaging Dev",
    [string] $OutDir = (Join-Path $PSScriptRoot "..\..\artifacts\certs"),
    [string] $PfxPassword = "devcert"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$pfxPath = Join-Path $OutDir "FolderManaging-Dev.pfx"
$cerPath = Join-Path $OutDir "FolderManaging-Dev.cer"

if (-not (Test-Path -LiteralPath $pfxPath)) {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Subject `
        -KeyUsage DigitalSignature `
        -FriendlyName "FolderManaging MSIX dev" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
        -NotAfter (Get-Date).AddYears(5)

    $secure = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $secure | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    Write-Host "Created PFX: $pfxPath"
    Write-Host "Exported CER: $cerPath"
}
else {
    Write-Host "PFX already exists: $pfxPath"
}

# Always export .cer from the PFX using the password so it matches the signing key (Get-PfxCertificate alone can be wrong for encrypted PFX).
$fromPfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxPath, $PfxPassword)
try {
    if (Test-Path -LiteralPath $cerPath) { Remove-Item -LiteralPath $cerPath -Force }
    Export-Certificate -Cert $fromPfx -FilePath $cerPath -Type CERT | Out-Null
    Write-Host "Synced CER from PFX (thumbprint $($fromPfx.Thumbprint)): $cerPath"
}
finally {
    $fromPfx.Dispose()
}

Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" -ErrorAction SilentlyContinue | Out-Null
Write-Host "Imported public certificate into Cert:\CurrentUser\TrustedPeople (run Install-SparseMsix.ps1 for Root + MSIX install)."
