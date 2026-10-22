<#
.SYNOPSIS
Builds the user edition and the single offline installer: artifacts\release\FolderManaging-Setup-<Version>.exe

.DESCRIPTION
1. Publishes Shell / CLI / Companion with -p:FmEdition=User (no dev diagnostics).
2. Packs sparse\AppxManifest.User.xml into FolderManaging.msix and signs it with the release certificate
   (created on first run under artifacts\certs, never committed).
3. Downloads the .NET 8 Desktop Runtime installer once (artifacts\cache) — the shell extension needs it.
4. Publishes FolderManaging.Setup twice: a small framework-dependent maintenance copy (uninstall), then the
   self-contained Setup.exe with everything embedded.

.NOTES
Requires the Windows SDK (makeappx.exe, signtool.exe) and the .NET 8 SDK. Run from any folder:
  powershell -ExecutionPolicy Bypass -File scripts\release\Build-UserRelease.ps1 -Version 1.1.0
#>
param(
    [string] $Version = "1.1.0",
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string] $CertificatePfx = (Join-Path $PSScriptRoot "..\..\artifacts\certs\FolderManaging-Release.pfx"),
    [string] $CertificatePassword = $env:FM_RELEASE_CERT_PASSWORD,
    [string] $RuntimeUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }
$packageVersion = "$Version.0"
$certSubject = "CN=Folder Managing"

$artifacts = Join-Path $RepoRoot "artifacts"
$releaseRoot = Join-Path $artifacts "release"
$layout = Join-Path $releaseRoot "layout"
$payloadDir = Join-Path $releaseRoot "payload"
$maintenanceDir = Join-Path $releaseRoot "maintenance"
$setupDir = Join-Path $releaseRoot "setup"
$cacheDir = Join-Path $artifacts "cache"
$msix = Join-Path $payloadDir "FolderManaging.msix"
$publishProps = @("-c", "Release", "-p:FmEdition=User", "-p:Version=$Version", "-nologo", "-v", "q")

function Resolve-WindowsSdkTool([string] $name) {
    $fromPath = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($fromPath) { return $fromPath.Source }
    foreach ($base in @("${env:ProgramFiles(x86)}\Windows Kits\10", "$env:ProgramFiles\Windows Kits\10")) {
        $bin = Join-Path $base "bin"
        if (-not (Test-Path -LiteralPath $bin)) { continue }
        $hit = Get-ChildItem -Path $bin -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\$name" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($hit) { return $hit }
        $flat = Join-Path $bin "x64\$name"
        if (Test-Path -LiteralPath $flat) { return $flat }
    }
    throw "Could not find $name. Install the Windows SDK (Visual Studio 'Desktop development with C++' or the standalone SDK)."
}

function Invoke-Checked([string] $what, [scriptblock] $block) {
    Write-Host "==> $what"
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." }
}

function Reset-Directory([string] $path) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

function Save-ScaledPng([System.Drawing.Image] $source, [int] $size, [string] $path) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($source, 0, 0, $size, $size)
    }
    finally { $g.Dispose() }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$makeAppx = Resolve-WindowsSdkTool "makeappx.exe"
$signTool = Resolve-WindowsSdkTool "signtool.exe"

# --- Release signing certificate (self-signed; Setup.exe trusts it in LocalMachine\TrustedPeople) ---
$passwordFile = [System.IO.Path]::ChangeExtension($CertificatePfx, ".password.txt")
if (-not $CertificatePassword -and (Test-Path -LiteralPath $passwordFile)) {
    $CertificatePassword = (Get-Content -LiteralPath $passwordFile -Raw).Trim()
}
if (-not (Test-Path -LiteralPath $CertificatePfx)) {
    Write-Host "==> Creating release certificate $certSubject"
    New-Item -ItemType Directory -Force -Path (Split-Path $CertificatePfx) | Out-Null
    if (-not $CertificatePassword) {
        $CertificatePassword = [Convert]::ToBase64String((1..24 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
        Set-Content -LiteralPath $passwordFile -Value $CertificatePassword -NoNewline
        Write-Host "    password saved to $passwordFile (keep it private, it is git-ignored)"
    }
    $cert = New-SelfSignedCertificate -Type Custom -Subject $certSubject -KeyUsage DigitalSignature `
        -FriendlyName "Folder Managing release signing" -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(10) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    $secure = ConvertTo-SecureString -String $CertificatePassword -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $CertificatePfx -Password $secure | Out-Null
    Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -Force
}
if (-not $CertificatePassword) { throw "Set -CertificatePassword or FM_RELEASE_CERT_PASSWORD for $CertificatePfx." }

$pfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertificatePfx, $CertificatePassword)
if ($pfx.Subject -ne $certSubject) { throw "Certificate subject '$($pfx.Subject)' must be '$certSubject' (AppxManifest.User.xml Publisher)." }

Reset-Directory $releaseRoot
New-Item -ItemType Directory -Force -Path $layout, $payloadDir, $cacheDir | Out-Null

# --- App binaries (user edition) ---
Invoke-Checked "Publish Shell" { dotnet publish (Join-Path $RepoRoot "src\FolderManaging.Shell\FolderManaging.Shell.csproj") @publishProps -r win-x64 --self-contained false -p:Platform=x64 -o $layout }
Invoke-Checked "Publish CLI" { dotnet publish (Join-Path $RepoRoot "src\FolderManaging.Cli\FolderManaging.Cli.csproj") @publishProps -r win-x64 --self-contained false -o $layout }
Invoke-Checked "Publish Companion" { dotnet publish (Join-Path $RepoRoot "src\FolderManaging.Companion\FolderManaging.Companion.csproj") @publishProps -r win-x64 --self-contained false -o $layout }
Get-ChildItem -LiteralPath $layout -Filter *.pdb | Remove-Item -Force

# --- Package ---
Write-Host "==> Manifest $packageVersion"
[xml] $manifest = Get-Content -LiteralPath (Join-Path $RepoRoot "sparse\AppxManifest.User.xml") -Raw
$manifest.Package.Identity.Version = $packageVersion
$manifest.Save((Join-Path $layout "AppxManifest.xml"))

Copy-Item -LiteralPath (Join-Path $RepoRoot "sparse\FolderManaging.propdesc") -Destination $layout

Add-Type -AssemblyName System.Drawing
$assets = Join-Path $layout "Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$logo = [System.Drawing.Image]::FromFile((Join-Path $RepoRoot "logo.png"))
try {
    Save-ScaledPng $logo 50 (Join-Path $assets "StoreLogo.png")
    Save-ScaledPng $logo 44 (Join-Path $assets "Square44x44Logo.png")
    Save-ScaledPng $logo 150 (Join-Path $assets "Square150x150Logo.png")
}
finally { $logo.Dispose() }

Invoke-Checked "makeappx pack" { & $makeAppx pack /o /d $layout /p $msix /nv | Out-Null }
Invoke-Checked "signtool sign" { & $signTool sign /q /fd SHA256 /f $CertificatePfx /p $CertificatePassword $msix }

# --- Payload ---
$cer = Join-Path $payloadDir "FolderManaging.cer"
[System.IO.File]::WriteAllBytes($cer, $pfx.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
$pfx.Dispose()
Copy-Item -LiteralPath (Join-Path $RepoRoot "sparse\FolderManaging.propdesc") -Destination $payloadDir
Copy-Item -LiteralPath (Join-Path $RepoRoot "assets\FolderManaging.ico") -Destination $payloadDir

$runtime = Join-Path $cacheDir "windowsdesktop-runtime-8-win-x64.exe"
if (-not (Test-Path -LiteralPath $runtime)) {
    Write-Host "==> Downloading .NET 8 Desktop Runtime"
    Invoke-WebRequest -Uri $RuntimeUrl -OutFile $runtime -UseBasicParsing
}
$sig = Get-AuthenticodeSignature -LiteralPath $runtime
if ($sig.Status -ne "Valid" -or $sig.SignerCertificate.Subject -notmatch "Microsoft Corporation") {
    Remove-Item -LiteralPath $runtime -Force
    throw "Downloaded runtime installer is not a valid Microsoft-signed file; deleted, re-run to download again."
}
Copy-Item -LiteralPath $runtime -Destination (Join-Path $payloadDir "windowsdesktop-runtime-win-x64.exe")

$setupProject = Join-Path $RepoRoot "src\FolderManaging.Setup\FolderManaging.Setup.csproj"
Invoke-Checked "Publish maintenance tool (uninstaller)" {
    dotnet publish $setupProject @publishProps -r win-x64 --self-contained false -p:PublishSingleFile=true -p:FmPayloadZip= -o $maintenanceDir
}
Copy-Item -LiteralPath (Join-Path $maintenanceDir "FolderManaging.Setup.exe") -Destination $payloadDir

$payloadZip = Join-Path $releaseRoot "payload.zip"
Write-Host "==> payload.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($payloadDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

# --- Setup.exe ---
Invoke-Checked "Publish Setup.exe" {
    dotnet publish $setupProject @publishProps -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        "-p:FmPayloadZip=$payloadZip" -o $setupDir
}
$final = Join-Path $releaseRoot "FolderManaging-Setup-$Version.exe"
Move-Item -LiteralPath (Join-Path $setupDir "FolderManaging.Setup.exe") -Destination $final -Force

$sizeMb = [Math]::Round((Get-Item -LiteralPath $final).Length / 1MB, 1)
Write-Host ""
Write-Host "Installer ready: $final ($sizeMb MB)"
