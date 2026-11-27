# WAV Utils - Registry Uninstaller

param([switch]$AutoStart)

$ErrorActionPreference = 'Stop'

if ($AutoStart) {
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true)
    if ($null -ne $runKey) {
        try { $runKey.DeleteValue('WAVUtils', $false) }
        finally { $runKey.Dispose() }
    }
    Write-Host 'Tray autostart disabled for this Windows account.'
    return
}

Write-Host "Uninstalling WAV Utils context menu..."

# Merge context menu
$mergeKey = "HKCU:\Software\Classes\*\shell\WAVUtilsMerge"
if (Test-Path -LiteralPath $mergeKey) { Remove-Item -LiteralPath $mergeKey -Recurse -Force; Write-Host "  Removed: Merge menu" }

# Split context menu
$splitKey = "HKCU:\Software\Classes\*\shell\WAVUtilsSplit"
if (Test-Path -LiteralPath $splitKey) { Remove-Item -LiteralPath $splitKey -Recurse -Force; Write-Host "  Removed: Split menu" }

Write-Host ""
Write-Host "Uninstallation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "To install, run: Install.ps1"
