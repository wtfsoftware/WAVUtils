# WAV Utils - Registry Installer
# Run as Administrator

$exePath = "$PSScriptRoot\WAVUtils.exe"

Write-Host "Installing WAV Utils context menu..."
Write-Host "Executable: $exePath"

if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: WAVUtils.exe not found at: $exePath" -ForegroundColor Red
    exit 1
}

$escapedExe = $exePath -replace '\\', '\\\\'

# Merge context menu (for all files)
$mergeKey = "HKCU:\Software\Classes\*\shell\WAVUtilsMerge"
New-Item -Path $mergeKey -Force | Out-Null
New-ItemProperty -Path $mergeKey -Name "(Default)" -Value "WAV Utils `&// Объединить WAV" -Force | Out-Null
New-ItemProperty -Path $mergeKey -Name "Icon" -Value "`"$exePath`"" -Force | Out-Null

$mergeCmdKey = "$mergeKey\command"
New-Item -Path $mergeCmdKey -Force | Out-Null
New-ItemProperty -Path $mergeCmdKey -Name "(Default)" -Value "`"$exePath`" merge `%1`" `%2`" `%3`" `%4`" `%5`" `%6`" `%7`" `%8`" `%9`"" -Force | Out-Null

# Split context menu (for WAV files only)
$splitKey = "HKCU:\Software\Classes\*\shell\WAVUtilsSplit"
New-Item -Path $splitKey -Force | Out-Null
New-ItemProperty -Path $splitKey -Name "(Default)" -Value "WAV Utils `&\\ Разъединить WAV" -Force | Out-Null
New-ItemProperty -Path $splitKey -Name "Icon" -Value "`"$exePath`"" -Force | Out-Null

$splitCmdKey = "$splitKey\command"
New-Item -Path $splitCmdKey -Force | Out-Null
New-ItemProperty -Path $splitCmdKey -Name "(Default)" -Value "`"$exePath`" split `%1`" -Force | Out-Null

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host "Right-click any files in Explorer to see the new options."
Write-Host ""
Write-Host "To uninstall, run: Uninstall.ps1"
