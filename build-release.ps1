# WAVUtils Release Builder
# Usage: .\build-release.ps1 [-Version 1.0.0] [-GitHubToken <token>]

param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version = "1.0.0",
    [string]$GitHubToken = "",
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repo = "wtfsoftware/WAVUtils"
)

$ErrorActionPreference = "Stop"
[void][version]::Parse($Version)
$Root = $PSScriptRoot
$PublishDir = Join-Path $Root "publish"
$DistDir = Join-Path $Root "dist"
$ExeName = "WAVUtils.exe"
$Projects = @('WAVUtils.App', 'WAVUtils.Tray')
$SdkDirectory = Join-Path $Root 'build'
$ReleaseLock = 'packages.win-x64.lock.json'

if (-not [IO.File]::Exists((Join-Path $SdkDirectory 'global.json'))) { throw 'Missing SDK pin: build/global.json' }
foreach ($project in ($Projects + 'WAVUtils.Core')) {
    # NuGet locked mode still creates a missing lock file.
    if (-not [IO.File]::Exists((Join-Path $Root "$project/$ReleaseLock"))) { throw "Missing release lock: $project/$ReleaseLock" }
}

function Invoke-CheckedTool([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}

function Assert-PeFile([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw 'Missing executable MZ header.' }
        $stream.Position = 60
        $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $stream.Length - 4) { throw 'Invalid PE header offset.' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw 'Missing executable PE signature.' }
    } finally { $reader.Dispose() }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WAVUtils Release Builder v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean
Write-Host "[1/6] Cleaning..." -ForegroundColor Yellow
if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
# The directory itself can be undeletable (open handle on a network share) - drop its
# contents instead so stale artifacts cannot ship; a locked leftover fails the build later.
if (Test-Path -LiteralPath $DistDir) { Get-ChildItem -LiteralPath $DistDir -Force | Remove-Item -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Publish
Write-Host "[2/6] Publishing..." -ForegroundColor Yellow
Push-Location -LiteralPath $SdkDirectory
try {
    foreach ($project in $Projects) {
        Invoke-CheckedTool -File 'dotnet' -Arguments @('publish', (Join-Path $Root "$project/$project.csproj"),
            '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:PublishReadyToRun=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true', "-p:Version=$Version",
            '-p:RestoreLockedMode=true', "-p:NuGetLockFilePath=$ReleaseLock",
            '-p:DebugType=None', '-p:DebugSymbols=false', '-warnaserror', '-o', $PublishDir, '-v', 'quiet')
    }
} finally { Pop-Location }
foreach ($name in @($ExeName, 'WAVUtils.Tray.exe')) { Assert-PeFile -Path (Join-Path $PublishDir $name) }
Write-Host "  Published to $PublishDir" -ForegroundColor Green

# Copy supporting files
Write-Host "[3/6] Copying supporting files..." -ForegroundColor Yellow
$packageNames = @($ExeName, 'WAVUtils.Tray.exe', 'LICENSE', 'README.md', 'README-INSTALL.txt', 'Install.ps1', 'Uninstall.ps1')
foreach ($name in $packageNames[2..($packageNames.Length - 1)]) {
    Copy-Item -LiteralPath (Join-Path $Root $name) -Destination (Join-Path $PublishDir $name) -Force
}

# Build installer
Write-Host "[4/6] Building installer..." -ForegroundColor Yellow
$ISCC = 'C:\Program Files\Inno Setup 7\ISCC.exe'
$setupFile = $null

if (Test-Path $ISCC) {
    Write-Host "  Inno Setup found, building installer..." -ForegroundColor Green
    Invoke-CheckedTool -File $ISCC -Arguments @((Join-Path $Root 'WAVUtils-Installer.iss'), "/DMyAppVersion=$Version")
    $setupFile = Get-Item -LiteralPath (Join-Path $DistDir "WAVUtils-Setup-$Version.exe")
    Assert-PeFile -Path $setupFile.FullName
    Write-Host "  Installer: $($setupFile.Name)" -ForegroundColor Green
} else {
    Write-Host '  Inno Setup not found; only a portable ZIP will be produced.' -ForegroundColor Yellow
}

# Create portable zip
Write-Host "[5/6] Creating portable package..." -ForegroundColor Yellow
$portableZip = Join-Path $DistDir "WAVUtils-$Version-Portable.zip"
$packageFiles = $packageNames | ForEach-Object { Join-Path $PublishDir $_ }
Compress-Archive -LiteralPath $packageFiles -DestinationPath $portableZip -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($portableZip)
try {
    foreach ($name in $packageNames) {
        if ($null -eq $archive.GetEntry($name)) { throw "Portable package omits $name." }
    }
    if ($archive.Entries.Count -ne $packageNames.Count) { throw 'Portable package contains unexpected files.' }
} finally { $archive.Dispose() }
Write-Host "  Portable: WAVUtils-$Version-Portable.zip" -ForegroundColor Green

# GitHub Release
if ($GitHubToken) {
    Write-Host "[6/6] Publishing GitHub release..." -ForegroundColor Yellow
    
    $releaseName = "WAVUtils v$Version"
    $releaseBody = @"
## WAVUtils v$Version

Multi-channel WAV merge and split utility.

### Features
- **Merge**: Combine multiple WAV files into a single multi-channel file
- **Split**: Extract tracks from multi-channel WAV files using PMAL metadata
- **Typed Metadata**: Preserves bEXT, iXML, INFO, cue, smpl, adtl chunks
- **7 Languages**: English, Russian, German, French, Spanish, Japanese, Chinese
- **System Tray**: Background notification support
- **Context Menu**: Right-click merge/split from Windows Explorer

### Installation
1. Run `WAVUtils-Setup-$Version.exe` (installer) or extract `WAVUtils-$Version-Portable.zip` (portable)
2. Use the PowerShell scripts without administrator rights to opt into the Explorer menu or Tray autostart for your account. See the bundled README.

### Requirements
- Windows 10/11 (64-bit)
- .NET 10.0 (included, no installation needed)

### Links
- Repository: https://github.com/$Repo
- Issues: https://github.com/$Repo/issues
- License: MIT

### Files
- `WAVUtils-Setup-$Version.exe` - Installer (requires admin)
- `WAVUtils-$Version-Portable.zip` - Portable (no install needed)
"@

    # Create release
    $releaseUrl = "https://api.github.com/repos/$Repo/releases"
    $headers = @{
        'Authorization' = "Bearer $GitHubToken"
        'Accept' = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2026-03-10'
    }
    
    $releaseData = @{
        tag_name = "v$Version"
        name = $releaseName
        body = $releaseBody
        draft = $true
        prerelease = $false
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri $releaseUrl -Method Post -Headers $headers -Body $releaseData -ContentType 'application/json' -TimeoutSec 30
        Write-Host "  Release created: $($response.html_url)" -ForegroundColor Green
        
        $uploadBase = $response.upload_url -replace '\{.*\}$', ''
        $uploadUri = [uri]$uploadBase
        if ($uploadUri.Scheme -ne 'https' -or $uploadUri.Host -ne 'uploads.github.com' -or $uploadUri.UserInfo) { throw 'Unexpected release upload endpoint.' }
        $assets = @(Get-Item -LiteralPath $portableZip)
        if ($setupFile) { $assets += $setupFile }
        foreach ($asset in $assets) {
            if ($asset.Length -gt 512MB) { throw 'Release asset exceeds the supported upload budget.' }
            $uploadUrl = $uploadBase + '?name=' + [uri]::EscapeDataString($asset.Name)
            $uploaded = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $asset.FullName -TimeoutSec 300
            $digest = 'sha256:' + (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($uploaded.size -ne $asset.Length -or $uploaded.digest -cne $digest) { throw 'Uploaded release asset failed length or SHA-256 verification.' }
            Write-Host "  Uploaded and verified: $($asset.Name)" -ForegroundColor Green
        }
        # An incomplete upload remains a draft and cannot become the automatic-update target.
        Invoke-RestMethod -Uri $response.url -Method Patch -Headers $headers -ContentType 'application/json' -Body '{"draft":false}' -TimeoutSec 30 | Out-Null
    } catch {
        throw 'GitHub release failed; inspect the draft before retrying. No automatic retry is performed.'
    }
} else {
    Write-Host "[6/6] Skipping GitHub release (no token provided)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Release ready: dist/" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Get-ChildItem $DistDir | Format-Table Name, @{Label="Size (MB)";Expression={[math]::Round($_.Length/1MB, 2)}}
