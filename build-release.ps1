# WAVUtils Release Builder
# Usage: .\build-release.ps1 [-Version 1.0.0] [-GitHubToken <token>]

param(
    [string]$Version = "1.0.0",
    [string]$GitHubToken = "",
    [string]$Repo = "wtfsoftware/WAVUtils"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$PublishDir = Join-Path $Root "publish"
$DistDir = Join-Path $Root "dist"
$ExeName = "WAVUtils.exe"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WAVUtils Release Builder v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean
Write-Host "[1/6] Cleaning..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
if (Test-Path $DistDir) { Remove-Item -Recurse -Force $DistDir }
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Publish
Write-Host "[2/6] Publishing..." -ForegroundColor Yellow
dotnet publish WAVUtils.App/WAVUtils.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir `
    -v quiet

if (-not (Test-Path (Join-Path $PublishDir $ExeName))) {
    Write-Host "ERROR: Publish failed - $ExeName not found" -ForegroundColor Red
    exit 1
}
Write-Host "  Published to $PublishDir" -ForegroundColor Green

# Copy supporting files
Write-Host "[3/6] Copying supporting files..." -ForegroundColor Yellow
Copy-Item (Join-Path $Root "LICENSE") (Join-Path $PublishDir "LICENSE") -Force
Copy-Item (Join-Path $Root "README.md") (Join-Path $PublishDir "README.md") -Force 2>$null

# Build installer
Write-Host "[4/6] Building installer..." -ForegroundColor Yellow
$ISCC = "C:\Program Files\Inno Setup 7\ISCC.exe"

if (Test-Path $ISCC) {
    Write-Host "  Inno Setup found, building installer..." -ForegroundColor Green
    & $ISCC (Join-Path $Root "WAVUtils-Installer.iss") "/DMyAppVersion=$Version"
    
    $setupFile = Get-ChildItem $DistDir -Filter "WAVUtils-Setup-$Version.exe" -ErrorAction SilentlyContinue
    if ($setupFile) {
        Write-Host "  Installer: $($setupFile.Name)" -ForegroundColor Green
    }
} else {
    Write-Host "  Inno Setup not found, creating 7-Zip SFX..." -ForegroundColor Yellow
    $SFX = "C:\Program Files\7-Zip\7z.exe"
    
    if (Test-Path $SFX) {
        # Create archive
        $archive = Join-Path $DistDir "WAVUtils-$Version.zip"
        & $SFX a -tzip $archive (Join-Path $PublishDir "*") | Out-Null
        
        # Create SFX config
        $sfxConfig = @'
;!@Install@!UTF-8!
Title="WAVUtils Setup"
Path="%APPDATA%\WTF Software\WAVUtils"
Uninstall="1"
UninstallString="%APPDATA%\WTF Software\WAVUtils\WAVUtils.exe" --uninstall
RunProgram="WAVUtils.exe"
;!@InstallEnd@!
'@
        $sfxConfigPath = Join-Path $DistDir "sfx.cfg"
        Set-Content -Path $sfxConfigPath -Value $sfxConfig -Encoding ASCII
        
        # Build SFX
        $sfxOutput = Join-Path $DistDir "WAVUtils-Setup-$Version.exe"
        $sfxBin = "C:\Program Files\7-Zip\7zS.sfx"
        if (Test-Path $sfxBin) {
            Copy-Item -Path $sfxConfigPath -Destination $sfxOutput -Force
            Add-Content -Path $sfxOutput -Value (Get-Content $archive -Encoding Byte)
            Write-Host "  SFX Installer: WAVUtils-Setup-$Version.exe" -ForegroundColor Green
        } else {
            Write-Host "  Archive: WAVUtils-$Version.zip" -ForegroundColor Green
        }
    } else {
        # Fallback: just zip
        $archive = Join-Path $DistDir "WAVUtils-$Version.zip"
        Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $archive -Force
        Write-Host "  Archive: WAVUtils-$Version.zip" -ForegroundColor Yellow
    }
}

# Create portable zip
Write-Host "[5/6] Creating portable package..." -ForegroundColor Yellow
$portableZip = Join-Path $DistDir "WAVUtils-$Version-Portable.zip"
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $portableZip -Force
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
2. Installer adds context menu entries for WAV files
3. Supports auto-start in system tray

### Requirements
- Windows 10/11 (64-bit)
- .NET 10.0 (included, no installation needed)

### Links
- Repository: https://github.com/$Repo
- Issues: https://github.com/$Repo/issues
- License: MIT

### Files
- `WAVUtils-Setup-$Version.exe` — Installer (requires admin)
- `WAVUtils-$Version-Portable.zip` — Portable (no install needed)
"@

    # Create release
    $releaseUrl = "https://api.github.com/repos/$Repo/releases"
    $headers = @{
        "Authorization" = "token $GitHubToken"
        "Accept" = "application/vnd.github.v3+json"
    }
    
    $releaseData = @{
        tag_name = "v$Version"
        name = $releaseName
        body = $releaseBody
        draft = $false
        prerelease = $false
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri $releaseUrl -Method Post -Headers $headers -Body $releaseData -ContentType "application/json"
        Write-Host "  Release created: $($response.html_url)" -ForegroundColor Green
        
        # Upload assets
        Get-ChildItem $DistDir -Filter "*.exe" | ForEach-Object {
            $uploadUrl = "$($response.upload_url)?name=$($_.Name)"
            $content = [System.IO.File]::ReadAllBytes($_.FullName)
            Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers -ContentType "application/octet-stream" -Body $content | Out-Null
            Write-Host "  Uploaded: $($_.Name)" -ForegroundColor Green
        }
        
        Get-ChildItem $DistDir -Filter "*.zip" | ForEach-Object {
            $uploadUrl = "$($response.upload_url)?name=$($_.Name)"
            $content = [System.IO.File]::ReadAllBytes($_.FullName)
            Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers -ContentType "application/octet-stream" -Body $content | Out-Null
            Write-Host "  Uploaded: $($_.Name)" -ForegroundColor Green
        }
    } catch {
        Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "[6/6] Skipping GitHub release (no token provided)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Release ready: dist/" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Get-ChildItem $DistDir | Format-Table Name, @{Label="Size (MB)";Expression={[math]::Round($_.Length/1MB, 2)}}
