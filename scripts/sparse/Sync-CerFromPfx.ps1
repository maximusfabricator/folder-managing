# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

<#
.SYNOPSIS
Exports the public certificate from a password-protected PFX to a .cer file (overwrites).
Use this so TrustedPeople/Root trust the SAME key that signtool used to sign the MSIX.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $PfxPath,
    [Parameter(Mandatory = $true)]
    [string] $CerOutPath,
    [string] $PfxPassword = "devcert"
)

$ErrorActionPreference = "Stop"
$pfxPath = (Resolve-Path -LiteralPath $PfxPath).Path
$dir = Split-Path -Parent $CerOutPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxPath, $PfxPassword)
try {
    if (Test-Path -LiteralPath $CerOutPath) { Remove-Item -LiteralPath $CerOutPath -Force }
    Export-Certificate -Cert $cert -FilePath $CerOutPath -Type CERT | Out-Null
    Write-Host "Exported CER thumbprint $($cert.Thumbprint) -> $CerOutPath"
}
finally {
    $cert.Dispose()
}
