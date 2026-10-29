# SPDX-License-Identifier: GPL-2.0-only
# Copyright (C) 2026 maximusfabricator

<#
.SYNOPSIS
Publishes a new version: build, update.json + signature, tag, push, GitHub release, live check.

.DESCRIPTION
Before you run it: set <Version> in Directory.Build.props and Version in sparse\AppxManifest.User.xml, add the
"## X.Y.Z - <date>" section at the top of CHANGELOG.md, commit, and stay on main. Then:

  powershell -ExecutionPolicy Bypass -File scripts\release\Publish-Release.ps1 -DryRun
  powershell -ExecutionPolicy Bypass -File scripts\release\Publish-Release.ps1

1. Checks: on main, nothing uncommitted, not behind origin, the same version in all three files, the commit has no
   foreign trailer, the commit-msg hook is installed, tag and release do not exist yet, version newer than the
   latest release.
2. Runs Build-UserRelease.ps1 (the build stamps the commit hash into the file version).
3. Writes artifacts\release\publish\update.json (version, setup name, size, SHA-256, notes from CHANGELOG) and signs it
   with the release certificate -> update.json.sig. Checks that the certificate key is the key built into the app.
4. Creates the annotated tag vX.Y.Z "Folder Managing X.Y.Z", pushes main and the tag (never forced).
5. Creates the GitHub release as a draft with the three files, checks the upload, then publishes it as latest.
   If anything fails before publishing, the draft is deleted again.
6. Downloads releases/latest/download/update.json anonymously and checks version and signature.

-DryRun does steps 1-3 and stops: no tag, no push, no release.
A run that stopped half way can simply be started again: an existing tag on the same commit is reused, a draft
release is replaced, and an already published release is only checked.

.NOTES
Needs git, the GitHub CLI (gh, signed in: gh auth login), the .NET 10 SDK and the Windows SDK (see
Build-UserRelease.ps1). The release certificate and its password file live in artifacts\certs (never committed).
#>
param(
    [switch] $DryRun,
    [switch] $SkipBuild,
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string] $Repo = "maximusfabricator/folder-managing",
    [string] $CertificatePfx = (Join-Path $PSScriptRoot "..\..\artifacts\certs\FolderManaging-Release.pfx"),
    [string] $CertificatePassword = $env:FM_RELEASE_CERT_PASSWORD
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$utf8 = New-Object System.Text.UTF8Encoding $false
$ignoredUntracked = @("docs/Samtalehistorikk-laerer-nb.md")

function Step([string] $text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }
function Info([string] $text) { Write-Host "    $text" }

# Windows PowerShell turns any stderr line of a native tool into a terminating error under "Stop"; git and gh
# write progress there, so they run with "Continue" and are judged by their exit code only.
function Invoke-Native([string] $exe, [object[]] $arguments) {
    $ErrorActionPreference = "Continue"
    $output = & $exe @arguments 2>&1 | ForEach-Object { "$_" }
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = @($output) }
}

function Invoke-Git {
    $r = Invoke-Native "git" (@("-C", $RepoRoot) + $args)
    if ($r.Code -ne 0) { throw "git $($args -join ' ') failed:`n$($r.Output -join "`n")" }
    $r.Output
}

function Invoke-Gh {
    $r = Invoke-Native "gh" $args
    if ($r.Code -ne 0) { throw "gh $($args -join ' ') failed:`n$($r.Output -join "`n")" }
    $r.Output
}

function Read-Text([string] $path) { [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8) }

function Get-ReleaseState([string] $tag) {
    $r = Invoke-Native "gh" @("release", "view", $tag, "--repo", $Repo, "--json", "isDraft,tagName,url,assets")
    if ($r.Code -ne 0) { return $null }
    ($r.Output -join "`n") | ConvertFrom-Json
}

function Test-CommitMessage([string] $what, [string] $message) {
    if ($message -match '(?im)^\s*Co-authored-by:') { throw "$what has a Co-authored-by trailer. Amend it before publishing." }
}

# CHANGELOG markdown -> the plain text the update window shows.
function ConvertTo-PlainNotes([string] $markdown) {
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($raw in ($markdown -split "`n")) {
        $line = $raw.TrimEnd("`r")
        if ($line -match '^\s{2,}\S' -and $out.Count -gt 0 -and $out[$out.Count - 1] -ne "") {
            $out[$out.Count - 1] = $out[$out.Count - 1] + " " + $line.Trim()
            continue
        }
        if ($line -match '^#{2,}\s+(.*)$') { $line = $Matches[1] }
        elseif ($line -match '^- (.*)$') { $line = [string][char]0x2022 + " " + $Matches[1] }
        $out.Add($line)
    }
    $text = $out -join "`n"
    $text = $text -replace '\*\*([^*]+)\*\*', '$1'
    $text = $text -replace '(?<![\w*])\*([^*\n]+)\*(?![\w*])', '$1'
    $text = $text -replace '`([^`]+)`', '$1'
    ($text -replace "`n{3,}", "`n`n").Trim()
}

function Get-AppPublicKeys {
    $source = Read-Text (Join-Path $RepoRoot "src\FolderManaging.Core\Updates\UpdateSignature.cs")
    $block = [regex]::Match($source, '(?s)TrustedKeys\s*\{\s*get;\s*\}\s*=\s*\[(.*?)\];').Groups[1].Value
    @($block -split "," | ForEach-Object { ($_ -replace '"\s*\+\s*"', '') -replace '["\s]', '' } |
        Where-Object { $_ -match '^MII[A-Za-z0-9+/=]{200,}$' })
}

function ConvertTo-Hex([byte[]] $bytes) { ($bytes | ForEach-Object { $_.ToString("x2") }) -join "" }

# The app accepts update.json only with a signature from one of its built-in keys; make sure ours is one of them.
function Assert-KeyIsTrusted($publicRsa) {
    $modulus = ConvertTo-Hex $publicRsa.ExportParameters($false).Modulus
    $trusted = Get-AppPublicKeys | Where-Object { (ConvertTo-Hex ([Convert]::FromBase64String($_))).Contains($modulus) }
    if (-not $trusted) { throw "The release certificate key is not among the keys in UpdateSignature.cs; the app would refuse this update." }
}

function Test-Signature($publicRsa, [byte[]] $manifest, [byte[]] $signatureFile) {
    foreach ($line in ([System.Text.Encoding]::ASCII.GetString($signatureFile) -split "`n")) {
        $text = $line.Trim()
        if (-not $text) { continue }
        try { $sig = [Convert]::FromBase64String($text) } catch { continue }
        if ($publicRsa.VerifyData($manifest, $sig, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)) { return $true }
    }
    $false
}

function Get-Anonymous([string] $url) {
    $response = Invoke-WebRequest -Uri $url -UseBasicParsing -MaximumRedirection 10 -UserAgent "FolderManaging-release-check"
    $response.RawContentStream.ToArray()
}

function Confirm-LiveRelease($publicRsa, [string] $version, [long] $size, [string] $sha) {
    $base = "https://github.com/$Repo/releases/latest/download"
    for ($attempt = 1; $attempt -le 12; $attempt++) {
        try {
            $manifest = Get-Anonymous "$base/update.json"
            $signature = Get-Anonymous "$base/update.json.sig"
            $live = ($utf8.GetString($manifest)) | ConvertFrom-Json
            if ($live.version -eq $version) {
                if (-not (Test-Signature $publicRsa $manifest $signature)) { throw "The published update.json.sig does not match update.json." }
                if ($live.setupSize -ne $size -or $live.setupSha256 -ne $sha) { throw "The published update.json describes another setup." }
                Info "latest/download/update.json: version $version, signature valid"
                return
            }
            Info "latest still shows $($live.version), waiting ($attempt/12)"
        }
        catch [System.Net.WebException] { Info "not reachable yet ($($_.Exception.Message)), waiting ($attempt/12)" }
        Start-Sleep -Seconds 10
    }
    throw "releases/latest/download/update.json did not switch to $version. Check the release page; it may only need a minute more."
}

Set-Location -LiteralPath $RepoRoot
$publishDir = Join-Path $RepoRoot "artifacts\release\publish"

# --- 1. Checks ---
Step "Checks"
foreach ($tool in "git", "gh") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not installed or not on PATH." }
}
if ((Invoke-Native "gh" @("auth", "status", "--hostname", "github.com")).Code -ne 0) { throw "The GitHub CLI is not signed in. Run: gh auth login" }

[xml] $props = Read-Text (Join-Path $RepoRoot "Directory.Build.props")
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Directory.Build.props: <Version> must look like 1.2.3 (got '$version')." }
[xml] $appx = Read-Text (Join-Path $RepoRoot "sparse\AppxManifest.User.xml")
if ($appx.Package.Identity.Version -ne "$version.0") {
    throw "sparse\AppxManifest.User.xml has Version $($appx.Package.Identity.Version), expected $version.0."
}
$changelog = (Read-Text (Join-Path $RepoRoot "CHANGELOG.md")) -replace "`r`n", "`n"
$headers = [regex]::Matches($changelog, '(?m)^## .*$')
if ($headers.Count -eq 0) { throw "CHANGELOG.md has no '## ' section." }
$top = $headers[0]
if ($top.Value -notmatch ('^## ' + [regex]::Escape($version) + ' \u2014 (\d{4}-\d{2}-\d{2})\s*$')) {
    throw "The top CHANGELOG.md section is '$($top.Value)'; expected '## $version <em dash> YYYY-MM-DD'."
}
$sectionEnd = if ($headers.Count -gt 1) { $headers[1].Index } else { $changelog.Length }
$section = $changelog.Substring($top.Index, $sectionEnd - $top.Index).Trim()
Info "version $version (Directory.Build.props, AppxManifest.User.xml, CHANGELOG.md)"

$branch = (Invoke-Git rev-parse --abbrev-ref HEAD) -join ""
if ($branch -ne "main") { throw "You are on '$branch'. Switch to main first." }
$dirty = @(Invoke-Git status --porcelain --untracked-files=all | Where-Object {
        $_ -and -not ($_.StartsWith("?? ") -and $ignoredUntracked -contains $_.Substring(3).Trim('"'))
    })
if ($dirty.Count -gt 0) { throw "Uncommitted changes:`n$($dirty -join "`n")`nCommit them (or undo them) first." }

$hookPath = Join-Path $RepoRoot ".git\hooks\commit-msg"
if (-not (Test-Path -LiteralPath $hookPath) -or (Read-Text $hookPath) -notmatch 'Co-authored-by') {
    throw "The commit-msg hook (.git\hooks\commit-msg) that removes the co-author trailer is missing."
}
$userName = (Invoke-Git config user.name) -join ""
$userEmail = (Invoke-Git config user.email) -join ""
$head = (Invoke-Git rev-parse HEAD) -join ""
$headAuthor = (Invoke-Git log -1 "--format=%an <%ae>|%cn <%ce>") -join ""
if ($headAuthor -ne "$userName <$userEmail>|$userName <$userEmail>") { throw "The last commit is by '$headAuthor', not '$userName <$userEmail>'." }
Test-CommitMessage "The last commit" ((Invoke-Git log -1 --format=%B) -join "`n")

Invoke-Git fetch --quiet origin main | Out-Null
$behind = [int]((Invoke-Git rev-list --count HEAD..origin/main) -join "")
if ($behind -gt 0) { throw "main is $behind commit(s) behind origin/main. Pull first." }
Info "main at $($head.Substring(0, 7)), clean, $userName <$userEmail>"

$tag = "v$version"
$tagMessage = "Folder Managing $version"
$localTag = ((Invoke-Native "git" @("-C", $RepoRoot, "rev-parse", "-q", "--verify", "refs/tags/$tag^{commit}")).Output | Where-Object { $_ -match '^[0-9a-f]{40}$' }) -join ""
$remoteTag = ((Invoke-Git ls-remote --tags origin "refs/tags/$tag^{}") -join "").Split("`t")[0]
if (-not $remoteTag) { $remoteTag = ((Invoke-Git ls-remote --tags origin "refs/tags/$tag") -join "").Split("`t")[0] }
foreach ($existing in @($localTag, $remoteTag) | Where-Object { $_ }) {
    if ($existing -ne $head) { throw "Tag $tag already exists on another commit ($existing). Choose a new version." }
}
$release = Get-ReleaseState $tag
$alreadyPublished = $release -and -not $release.isDraft
if ($alreadyPublished) {
    Info "release $tag is already published; only checking it"
}
elseif ($localTag -or $remoteTag -or $release) {
    Info "continuing an earlier run (tag: $([bool]($localTag -or $remoteTag)), draft release: $([bool]$release))"
}
else {
    $latest = Invoke-Native "gh" @("release", "view", "--repo", $Repo, "--json", "tagName")
    if ($latest.Code -eq 0) {
        $latestTag = (($latest.Output -join "`n") | ConvertFrom-Json).tagName
        if ([version]($latestTag.TrimStart("v")) -ge [version]$version) { throw "The latest release is $latestTag; $version must be newer." }
        Info "latest release now: $latestTag"
    }
}

# --- Certificate (the password is never printed) ---
$passwordFile = [System.IO.Path]::ChangeExtension($CertificatePfx, ".password.txt")
if (-not $CertificatePassword -and (Test-Path -LiteralPath $passwordFile)) { $CertificatePassword = (Get-Content -LiteralPath $passwordFile -Raw).Trim() }
if (-not (Test-Path -LiteralPath $CertificatePfx) -or -not $CertificatePassword) {
    throw "Release certificate or its password not found ($CertificatePfx). Restore artifacts\certs from your backup."
}
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertificatePfx, $CertificatePassword,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
$publicRsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($cert)
Assert-KeyIsTrusted $publicRsa

$setupName = "FolderManaging-Setup-$version.exe"
$setup = Join-Path $RepoRoot "artifacts\release\$setupName"
$manifestPath = Join-Path $publishDir "update.json"
$signaturePath = Join-Path $publishDir "update.json.sig"
$notesPath = Join-Path $publishDir "release-notes.md"

if ($alreadyPublished) {
    $live = (Get-Anonymous "https://github.com/$Repo/releases/download/$tag/update.json")
    $liveManifest = $utf8.GetString($live) | ConvertFrom-Json
    Step "Live check"
    Confirm-LiveRelease $publicRsa $version $liveManifest.setupSize $liveManifest.setupSha256
    Write-Host ""; Write-Host "Release ${tag}: $($release.url)" -ForegroundColor Green
    return
}

# --- 2. Build ---
if ($SkipBuild -and (Test-Path -LiteralPath $setup)) {
    Step "Build skipped, using $setup"
}
else {
    Step "Build (Build-UserRelease.ps1)"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Build-UserRelease.ps1") -Version $version
    if ($LASTEXITCODE -ne 0) { throw "Build-UserRelease.ps1 failed (exit $LASTEXITCODE)." }
}
if (-not (Test-Path -LiteralPath $setup)) { throw "The build did not produce $setup." }
$productVersion = (Get-Item -LiteralPath $setup).VersionInfo.ProductVersion
if ($productVersion -notlike "$version*") { throw "$setupName has product version '$productVersion', expected $version." }
if ($productVersion -notmatch [regex]::Escape($head.Substring(0, 7))) { Write-Warning "$setupName product version '$productVersion' does not name commit $($head.Substring(0, 7))." }
$size = (Get-Item -LiteralPath $setup).Length
$sha = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Info "$setupName, $size bytes, SHA-256 $sha"

# --- 3. update.json and signature ---
Step "update.json"
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
$manifestObject = [ordered]@{
    schema      = 1
    product     = "FolderManaging"
    version     = $version
    tag         = $tag
    setupFile   = $setupName
    setupSize   = $size
    setupSha256 = $sha
    releaseUrl  = "https://github.com/$Repo/releases/tag/$tag"
    published   = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    notes       = ConvertTo-PlainNotes $section
}
$manifestBytes = $utf8.GetBytes((($manifestObject | ConvertTo-Json -Depth 3) -replace "`r`n", "`n") + "`n")
[System.IO.File]::WriteAllBytes($manifestPath, $manifestBytes)

$privateRsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
try {
    $signature = $privateRsa.SignData($manifestBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
}
finally { $privateRsa.Dispose() }
[System.IO.File]::WriteAllBytes($signaturePath, [System.Text.Encoding]::ASCII.GetBytes([Convert]::ToBase64String($signature) + "`n"))
if (-not (Test-Signature $publicRsa ([System.IO.File]::ReadAllBytes($manifestPath)) ([System.IO.File]::ReadAllBytes($signaturePath)))) {
    throw "The new signature does not verify."
}
Info "$manifestPath (signed, the key is the one built into the app)"

$install = (Read-Text (Join-Path $PSScriptRoot "release-install-notes.md")) -replace "`r`n", "`n"
$install = $install.Replace("{VERSION}", $version).Replace("{SHA256}", $sha.ToUpperInvariant())
[System.IO.File]::WriteAllText($notesPath, $section + "`n`n" + $install.Trim() + "`n", $utf8)
Info "$notesPath"

if ($DryRun) {
    Write-Host ""
    Write-Host "Dry run finished. Nothing was tagged, pushed or released. Files: $publishDir" -ForegroundColor Green
    return
}

# --- 4. Tag and push ---
if (-not $localTag -and -not $remoteTag) {
    Step "Tag $tag"
    Invoke-Git tag -a $tag -m $tagMessage | Out-Null
}
$tagObject = (Invoke-Git cat-file -p $tag) -join "`n"
if ($tagObject -notmatch "(?m)^tagger $([regex]::Escape("$userName <$userEmail>")) ") { throw "Tag $tag has another tagger. Delete the local tag (git tag -d $tag) and run again." }
if (($tagObject -split "`n`n", 2)[1].Trim() -ne $tagMessage) { throw "Tag $tag message is not '$tagMessage'. Delete the local tag (git tag -d $tag) and run again." }
Test-CommitMessage "Tag $tag" $tagObject

Step "Push main and $tag"
Invoke-Git push origin main | Out-Null
if (-not $remoteTag) { Invoke-Git push origin "refs/tags/$tag" | Out-Null }
Info "pushed"

# --- 5. Release (draft first, published only when complete) ---
Step "GitHub release"
if ($release -and $release.isDraft) {
    Info "removing the draft of an earlier run"
    Invoke-Gh release delete $tag --repo $Repo --yes | Out-Null
}
try {
    Invoke-Gh release create $tag --repo $Repo --draft --verify-tag --title $tagMessage --notes-file $notesPath `
        $setup $manifestPath $signaturePath | Out-Null
    $draft = Get-ReleaseState $tag
    $expected = @{ $setupName = $size; "update.json" = (Get-Item $manifestPath).Length; "update.json.sig" = (Get-Item $signaturePath).Length }
    foreach ($name in $expected.Keys) {
        $asset = $draft.assets | Where-Object { $_.name -eq $name }
        if (-not $asset -or [long]$asset.size -ne [long]$expected[$name]) { throw "Upload of $name is missing or incomplete." }
    }
    Invoke-Gh release edit $tag --repo $Repo --draft=false --latest | Out-Null
}
catch {
    $state = Get-ReleaseState $tag
    if ($state -and $state.isDraft) {
        Invoke-Native "gh" @("release", "delete", $tag, "--repo", $Repo, "--yes") | Out-Null
        Write-Warning "The draft release was deleted again. The tag $tag stays; run the script again to retry."
    }
    throw
}
$release = Get-ReleaseState $tag
Info "published: $($release.url)"

# --- 6. Live check ---
Step "Live check (anonymous)"
Confirm-LiveRelease $publicRsa $version $size $sha
$cert.Dispose()

Write-Host ""
Write-Host "Folder Managing $version is released: $($release.url)" -ForegroundColor Green
Write-Host "Setup: $setup"
Write-Host "SHA-256: $($sha.ToUpperInvariant())"
