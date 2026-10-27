# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

# Senior diagnostics: Event Log, ACL on D:\CodingCodingCoding, COM CLSID registry.
$ErrorActionPreference = "Continue"

Write-Host "======== 1) Application log - Level Error (2), last 2h - dllhost / FolderManaging / .NET Runtime ========"
$since = (Get-Date).AddHours(-2)
try {
    $events = Get-WinEvent -FilterHashtable @{
        LogName   = 'Application'
        Level     = 2
        StartTime = $since
    } -MaxEvents 500 -ErrorAction Stop |
        Where-Object {
            $m = $_.Message
            $prov = $_.ProviderName
            $m -match 'dllhost|FolderManaging|Folder Managing' -or
            $prov -match '\.NET Runtime' -or
            $m -match '\.NET Runtime'
        } |
        Select-Object -First 40 TimeCreated, Id, ProviderName, Message

    if (-not $events) {
        Write-Host "(no matching Error events in Application for last 2 hours)"
    }
    else {
        $events | ForEach-Object {
            Write-Host "---"
            Write-Host $_.TimeCreated $_.ProviderName "Id=$($_.Id)"
            Write-Host ($_.Message -replace "`r`n", " | ")
        }
    }
}
catch {
    Write-Host "Get-WinEvent failed: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "======== 2) ACL: D:\CodingCodingCoding ========"
$target = 'D:\CodingCodingCoding'
if (-not (Test-Path -LiteralPath $target)) {
    Write-Host "PATH_NOT_FOUND: $target (skip ACL)"
}
else {
    icacls $target
    Write-Host "---"
    $who = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    Write-Host "Current user: $who"
    $acl = Get-Acl -LiteralPath $target
    $short = ($who -split '\\')[-1]
    $acl.Access |
        Where-Object {
            $id = $_.IdentityReference.Value
            $id -eq $who -or $id -like "*\$short" -or $id -eq 'BUILTIN\Users' -or $id -eq 'Everyone'
        } |
        Sort-Object IdentityReference |
        Format-Table IdentityReference, FileSystemRights, AccessControlType, InheritanceFlags -AutoSize

    $modifyLike = [System.Security.AccessControl.FileSystemRights]::Modify
    $full = [System.Security.AccessControl.FileSystemRights]::FullControl
    $mine = $acl.Access | Where-Object { $_.IdentityReference.Value -eq $who }
    $authUsers = $acl.Access | Where-Object {
        $_.IdentityReference.Value -ieq 'NT AUTHORITY\Authenticated Users' -or
        $_.IdentityReference.Value -ieq 'NT AUTHORITY\AUTHENTICATED USERS'
    }
    $authModify = $false
    foreach ($a in $authUsers) {
        if ($a.AccessControlType -ne 'Allow') { continue }
        $s = $a.FileSystemRights.ToString()
        if ($s -match 'Modify|FullControl') { $authModify = $true }
    }

    $ok = $false
    foreach ($a in $mine) {
        if ($a.FileSystemRights -match 'FullControl|Modify|Write|Delete') { $ok = $true }
    }

    if ($mine) {
        Write-Host "Direct ACE(s) for current user on folder: $($mine.Count)"
        if ($ok) { Write-Host "SUMMARY: Direct ACE for $who includes write-capable rights." }
        elseif ($authModify) { Write-Host "SUMMARY: No direct Modify on $who, but NT AUTHORITY\Authenticated Users has inherited Modify (M) - interactive users normally inherit this = effective Modify for desktop.ini." }
        else { Write-Host "SUMMARY: No direct write ACE for $who; Authenticated Users Modify not detected - review icacls." }
    }
    elseif ($authModify) {
        Write-Host "SUMMARY: No direct ACE for $who; NT AUTHORITY\Authenticated Users has inherited Modify (M) - effective Modify for a normal interactive logon."
    }
    else {
        Write-Host "SUMMARY: No direct ACE for $who and no Authenticated Users Modify - check icacls / elevation."
    }
}

Write-Host ""
Write-Host "======== 3) COM CLSID F4B3C2B1-0A1E-4D8F-9C7B-6E5D4C3B2A01 (classic vs packaged) ========"
Write-Host "NOTE: MSIX packaged context-menu COM is NOT required to appear under HKCU/HKLM Software\Classes\CLSID."
Write-Host "      Registration comes from AppxManifest (com:ComServer / FileExplorerContextMenus) + system merge."
$guid = '{F4B3C2B1-0A1E-4D8F-9C7B-6E5D4C3B2A01}'
$paths = @(
    "HKCU:\Software\Classes\CLSID\$guid",
    "HKLM:\SOFTWARE\Classes\CLSID\$guid",
    "HKCU:\Software\Classes\WOW6432Node\CLSID\$guid",
    "HKLM:\SOFTWARE\Classes\WOW6432Node\CLSID\$guid"
)
foreach ($p in $paths) {
    if (Test-Path -LiteralPath $p) {
        Write-Host "EXISTS (classic): $p"
        Get-Item -LiteralPath $p | Format-List *
    }
    else {
        Write-Host "MISSING (expected for sparse MSIX): $p"
    }
}

Write-Host ""
Write-Host "======== Installed MSIX (context) ========"
Get-AppxPackage -Name 'FolderManagingSparseDev*' | Format-List Name, PackageFullName, InstallLocation
