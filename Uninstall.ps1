# WAV Utils - Registry Uninstaller

Write-Host "Uninstalling WAV Utils context menu..."

# Merge context menu
$mergeKey = "HKCU:\Software\Classes\*\shell\WAVUtilsMerge"
if (Test-Path $mergeKey) { Remove-Item -Path $mergeKey -Recurse -Force; Write-Host "  Removed: Merge menu" }

# Split context menu
$splitKey = "HKCU:\Software\Classes\*\shell\WAVUtilsSplit"
if (Test-Path $splitKey) { Remove-Item -Path $splitKey -Recurse -Force; Write-Host "  Removed: Split menu" }

Write-Host ""
Write-Host "Uninstallation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "To install, run: Install.ps1 (as Administrator)"
