# WAV Utils - Registry Installer
# Configures the current user's context menu or Tray autostart.

param([switch]$AutoStart)

$ErrorActionPreference = 'Stop'

if ($AutoStart) {
    $trayPath = "$PSScriptRoot\WAVUtils.Tray.exe"
    if (-not (Test-Path -LiteralPath $trayPath -PathType Leaf)) { throw "WAVUtils.Tray.exe not found at: $trayPath" }
    [Microsoft.Win32.Registry]::SetValue('HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run', 'WAVUtils', "`"$trayPath`"")
    Write-Host 'Tray autostart enabled for this Windows account.'
    return
}

$exePath = "$PSScriptRoot\WAVUtils.exe"

Write-Host "Installing WAV Utils context menu..."
Write-Host "Executable: $exePath"

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    Write-Host "ERROR: WAVUtils.exe not found at: $exePath" -ForegroundColor Red
    exit 1
}

# Merge context menu (for all files)
# SetValue uses literal keys and preserves unrelated values on reinstall.
$mergeKey = 'HKEY_CURRENT_USER\Software\Classes\*\shell\WAVUtilsMerge'
[Microsoft.Win32.Registry]::SetValue($mergeKey, '', 'WAV Utils &// Объединить WAV')
[Microsoft.Win32.Registry]::SetValue($mergeKey, 'Icon', "`"$exePath`"")
[Microsoft.Win32.Registry]::SetValue("$mergeKey\command", '', "`"$exePath`" merge --explorer `"%1`"")

# Split context menu
$splitKey = 'HKEY_CURRENT_USER\Software\Classes\*\shell\WAVUtilsSplit'
[Microsoft.Win32.Registry]::SetValue($splitKey, '', 'WAV Utils &\\ Разъединить WAV')
[Microsoft.Win32.Registry]::SetValue($splitKey, 'Icon', "`"$exePath`"")
[Microsoft.Win32.Registry]::SetValue("$splitKey\command", '', "`"$exePath`" split `"%1`"")

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "Right-click any files in Explorer to see the new options."
Write-Host ""
Write-Host "To uninstall, run: Uninstall.ps1"
