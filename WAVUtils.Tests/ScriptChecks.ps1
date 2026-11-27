param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Scenario
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Read-Script([string]$Name) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $Root $Name), [ref]$tokens, [ref]$errors)
    Assert-Check ($errors.Count -eq 0) ($Name + ': ' + (($errors | ForEach-Object Message) -join ', '))
    return $ast
}

function Invoke-BuildFixture([bool]$FailNative, [switch]$MissingLock) {
    $sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ('wavutils-script-' + [guid]::NewGuid().ToString('N'))
    $calls = [System.Collections.Generic.List[object]]::new()
    New-Item -ItemType Directory -Path $sandbox | Out-Null
    try {
        [System.IO.File]::WriteAllText((Join-Path $sandbox 'build-release.ps1'), (Read-Script 'build-release.ps1').Extent.Text)
        foreach ($name in @('LICENSE', 'README.md', 'README-INSTALL.txt', 'Install.ps1', 'Uninstall.ps1', 'WAVUtils-Installer.iss')) {
            [System.IO.File]::WriteAllText((Join-Path $sandbox $name), 'fixture')
        }
        [void][System.IO.Directory]::CreateDirectory((Join-Path $sandbox 'build'))
        [System.IO.File]::WriteAllText((Join-Path $sandbox 'build/global.json'), '{}')
        foreach ($project in @('WAVUtils.App', 'WAVUtils.Core', 'WAVUtils.Tray')) {
            [void][System.IO.Directory]::CreateDirectory((Join-Path $sandbox $project))
            if ($MissingLock -and $project -eq 'WAVUtils.Core') { continue }
            [System.IO.File]::WriteAllText((Join-Path $sandbox "$project/packages.win-x64.lock.json"), '{}')
        }
        function dotnet {
            $Arguments = [string[]]$args
            $calls.Add($Arguments)
            $index = [array]::IndexOf($Arguments, '-o')
            Assert-Check ($index -ge 0) 'Publish must use an explicit output directory.'
            $directory = $Arguments[$index + 1]
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            $name = if (($Arguments -join ' ') -match 'WAVUtils.Tray[\\/]WAVUtils.Tray.csproj') { 'WAVUtils.Tray.exe' } else { 'WAVUtils.exe' }
            $bytes = [byte[]]::new(512)
            $bytes[0] = 77; $bytes[1] = 90; $bytes[60] = 128; $bytes[128] = 80; $bytes[129] = 69
            [System.IO.File]::WriteAllBytes((Join-Path $directory $name), $bytes)
            $global:LASTEXITCODE = if ($FailNative) { 9 } else { 0 }
        }
        function Test-Path {
            param([string]$Path, [string]$LiteralPath)
            $value = if ($LiteralPath) { $LiteralPath } else { $Path }
            if ($value -match '(?:ISCC\.exe|7z\.exe|7zS\.sfx)$') { return $false }
            return Microsoft.PowerShell.Management\Test-Path -LiteralPath $value
        }
        $failure = $null
        try { & (Join-Path $sandbox 'build-release.ps1') -Version '1.2.3' }
        catch { $failure = $_ }
        if ($MissingLock) {
            Assert-Check ($calls.Count -eq 0) 'Publishing must stop before invoking dotnet when a release lock is missing.'
            Assert-Check ($null -ne $failure -and $failure.Exception.Message -like '*WAVUtils.Core/packages.win-x64.lock.json*') 'The failure must identify the missing release lock.'
            return
        }
        if ($FailNative) {
            Assert-Check ($calls.Count -eq 1) 'The failure check must reach the mocked native publish tool.'
            Assert-Check ($null -ne $failure) 'A failed native tool must fail the build, even if it left an EXE.'
            return
        }
        if ($null -ne $failure) { throw $failure }
        Assert-Check ($calls.Count -eq 2) 'Publish must include both App and Tray.'
        foreach ($call in $calls) {
            Assert-Check ($call -contains '-p:Version=1.2.3') 'Both executables must carry the requested version.'
            Assert-Check ($call -contains '-p:RestoreLockedMode=true') 'Both publishes must reject changes to the dependency graph.'
            Assert-Check ($call -contains '-p:NuGetLockFilePath=packages.win-x64.lock.json') 'Publish must keep the development lock files separate.'
        }
        Assert-Check (-not (Microsoft.PowerShell.Management\Test-Path -LiteralPath (Join-Path $sandbox 'dist/WAVUtils-Setup-1.2.3.exe'))) 'Missing Inno Setup must not produce a counterfeit installer.'
        $zip = Join-Path $sandbox 'dist/WAVUtils-1.2.3-Portable.zip'
        Assert-Check (Microsoft.PowerShell.Management\Test-Path -LiteralPath $zip) 'The portable fallback is missing.'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($name in @('WAVUtils.exe', 'WAVUtils.Tray.exe', 'LICENSE', 'README.md', 'README-INSTALL.txt', 'Install.ps1', 'Uninstall.ps1')) {
                Assert-Check ($null -ne $archive.GetEntry($name)) ('Portable package omits ' + $name)
            }
        } finally { $archive.Dispose() }
    } finally { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}

switch ($Scenario) {
    'parse' {
        foreach ($path in Get-ChildItem -LiteralPath $Root -Filter '*.ps1') {
            $ast = Read-Script $path.Name
            $source = [System.IO.File]::ReadAllText($path.FullName)
            Assert-Check ($ast.Extent.Text.TrimEnd() -ceq $source.TrimEnd()) ($path.Name + ': Windows PowerShell must read the original script text.')
        }
    }
    'registry' {
        $merge = 'HKEY_CURRENT_USER\Software\Classes\*\shell\WAVUtilsMerge'
        $split = 'HKEY_CURRENT_USER\Software\Classes\*\shell\WAVUtilsSplit'
        $other = 'HKEY_CURRENT_USER\Software\Classes\OtherType\shell\WAVUtilsMerge'
        $run = 'HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run'
        $keys = @{}
        $keys[$merge] = @{ Extra = 'keep' }
        $keys[$other] = @{ '' = 'other verb' }
        $registry = [pscustomobject]@{ Keys = $keys }
        $registry | Add-Member ScriptMethod SetValue {
            param([string]$Path, [string]$Name, [string]$Value)
            if (-not $this.Keys.ContainsKey($Path)) { $this.Keys[$Path] = @{} }
            $this.Keys[$Path][$Name] = $Value
        }
        function New-Item {
            param([string]$Path, [switch]$Force)
            $key = $Path -replace '^HKCU:', 'HKEY_CURRENT_USER'
            if ($Force -or -not $keys.ContainsKey($key)) { $keys[$key] = @{} }
        }
        function New-ItemProperty {
            param([string]$Path, [string]$Name, [string]$Value, [switch]$Force)
            foreach ($key in @($keys.Keys -like ($Path -replace '^HKCU:', 'HKEY_CURRENT_USER'))) {
                $keys[$key][$Name] = $Value
            }
        }
        $sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ('wavutils [registry] ' + [guid]::NewGuid().ToString('N'))
        [void][System.IO.Directory]::CreateDirectory($sandbox)
        try {
            $exe = Join-Path $sandbox 'WAVUtils.exe'
            $tray = Join-Path $sandbox 'WAVUtils.Tray.exe'
            $script = Join-Path $sandbox 'Install.ps1'
            [System.IO.File]::WriteAllText($exe, '')
            # Redirect the literal registry API before running the packaged script.
            $source = (Read-Script 'Install.ps1').Extent.Text.Replace('[Microsoft.Win32.Registry]::SetValue(', '$registry.SetValue(')
            Assert-Check ($source -notmatch 'Microsoft\.Win32\.Registry') 'The fixture must redirect every registry write.'
            [System.IO.File]::WriteAllText($script, $source, [System.Text.UTF8Encoding]::new($true))
            & $script
            Assert-Check ($keys[$merge]['Extra'] -ceq 'keep') 'Reinstalling the menu must preserve unrelated values on the literal verb.'
            Assert-Check ($keys[$other][''] -ceq 'other verb') 'The literal star must not overwrite a verb owned by another file class.'
            Assert-Check ($keys.ContainsKey("$merge\command")) 'Install must find WAVUtils.exe in a literal directory containing brackets.'
            Assert-Check ($keys["$merge\command"][''] -ceq ('"{0}" merge --explorer "%1"' -f $exe)) 'Merge must quote its executable and input.'
            Assert-Check ($keys["$split\command"][''] -ceq ('"{0}" split "%1"' -f $exe)) 'Split must quote its executable and input.'
            Assert-Check (-not $keys.ContainsKey($run)) 'Context-menu setup must not enable Tray autostart.'
            [void][System.IO.Directory]::CreateDirectory($tray)
            $failure = $null
            try { & $script -AutoStart } catch { $failure = $_ }
            Assert-Check (-not $keys.ContainsKey($run) -and $null -ne $failure -and $failure.Exception.Message -like 'WAVUtils.Tray.exe not found at:*') 'Autostart must reject a directory in place of the Tray executable.'
            [System.IO.Directory]::Delete($tray)
            [System.IO.File]::WriteAllText($tray, '')
            $keys["$merge\command"][''] = 'unchanged'
            & $script -AutoStart
            Assert-Check ($keys["$merge\command"][''] -ceq 'unchanged') 'Enabling Tray autostart must not change the context menu.'
            Assert-Check ($keys.ContainsKey($run)) 'Tray autostart must be written only to the current profile.'
            Assert-Check ($keys[$run]['WAVUtils'] -ceq ('"{0}"' -f $tray)) 'Autostart must launch the packaged Tray executable in the current profile.'
        } finally { [System.IO.Directory]::Delete($sandbox, $true) }
    }
    'registry-uninstall' {
        $merge = 'HKCU:\Software\Classes\*\shell\WAVUtilsMerge'
        $split = 'HKCU:\Software\Classes\*\shell\WAVUtilsSplit'
        $other = 'HKCU:\Software\Classes\OtherType\shell\WAVUtilsMerge'
        $keys = @{}
        foreach ($key in @($merge, $split, $other)) { $keys[$key] = $true }
        $run = [pscustomobject]@{ Values = @{ WAVUtils = 'tray'; Other = 'unrelated' } }
        $run | Add-Member ScriptMethod DeleteValue {
            param([string]$Name, [bool]$ThrowOnMissing)
            if ($ThrowOnMissing -and -not $this.Values.ContainsKey($Name)) { throw 'The value does not exist.' }
            $this.Values.Remove($Name)
        }
        $run | Add-Member ScriptMethod Dispose {}
        function Test-Path {
            param([string]$Path, [string]$LiteralPath)
            if ($LiteralPath) { return $keys.ContainsKey($LiteralPath) }
            return @($keys.Keys -like $Path).Count -gt 0
        }
        function Remove-Item {
            param([string]$Path, [string]$LiteralPath, [switch]$Recurse, [switch]$Force)
            if ($LiteralPath) { $keys.Remove($LiteralPath); return }
            foreach ($key in @($keys.Keys -like $Path)) { $keys.Remove($key) }
        }
        $source = (Read-Script 'Uninstall.ps1').Extent.Text.Replace(
            "[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', `$true)", '$run')
        Assert-Check ($source -notmatch 'Microsoft\.Win32\.Registry') 'The fixture must redirect every registry write.'
        $script = [scriptblock]::Create($source)
        & $script -AutoStart
        Assert-Check ($keys.Count -eq 3) 'Removing Tray autostart must not remove any context-menu keys.'
        Assert-Check (-not $run.Values.ContainsKey('WAVUtils')) 'Uninstall must remove the current user''s WAVUtils Run value.'
        Assert-Check ($run.Values['Other'] -ceq 'unrelated') 'Uninstall must preserve other programs'' autostart settings.'
        & $script -AutoStart
        $run.Values['WAVUtils'] = 'tray'
        & $script
        Assert-Check ($keys.ContainsKey($other)) 'Uninstall must not delete a verb belonging to another file class.'
        Assert-Check ($keys.Count -eq 1) 'Uninstall must remove both literal verbs.'
        Assert-Check ($run.Values['WAVUtils'] -ceq 'tray') 'Removing context-menu keys must not change Tray autostart.'
        & $script
        Assert-Check ($keys.Count -eq 1) 'Uninstall must also be safe when the literal verbs are absent.'
        $run = $null
        & $script -AutoStart
        Assert-Check ($keys.Count -eq 1) 'An absent Run key is not an error and must not affect other keys.'
    }
    'installer-profile' {
        $source = [System.IO.File]::ReadAllText((Join-Path $Root 'WAVUtils-Installer.iss'))
        Assert-Check ($source -match '(?m)^PrivilegesRequired=admin\s*$') 'The installed build remains machine-wide.'
        Assert-Check ($source -notmatch '(?i)\b(?:HKCU|HKEY_CURRENT_USER)\b|(?m)^Name:\s*"(?:contextmenu|autostart)"') 'Administrative Setup must not apply current-user settings.'
        Assert-Check ($source -notmatch '(?i)CurrentVersion\\Run|WAVUtilsMerge|WAVUtilsSplit') 'Setup must not silently switch the user''s optional settings to all users.'
    }
    'tasks' {
        $source = [System.IO.File]::ReadAllText((Join-Path $Root 'WAVUtils-Installer.iss'))
        Assert-Check ($source -match '(?m)^Name: "desktopicon";.*Flags:\s*unchecked') 'The desktop shortcut must remain opt-in.'
        Assert-Check ($source -match '(?m)^Name: "\{group\}\\\{#MyAppName\}";\s*Filename: "\{app\}\\WAVUtils\.Tray\.exe"') 'The Start menu must launch Tray, not the CLI without arguments.'
        Assert-Check ($source -match '(?m)^Name: "\{commondesktop\}.*Filename: "\{app\}\\WAVUtils\.Tray\.exe";\s*Tasks:\s*desktopicon') 'The opt-in desktop shortcut must also launch Tray.'
    }
    'installer-launch' {
        $source = [System.IO.File]::ReadAllText((Join-Path $Root 'WAVUtils-Installer.iss'))
        Assert-Check ($source -notmatch '(?im)^\s*\[(?:Run|UninstallRun)\]\s*$') 'Setup must not start the application in the elevated context.'
    }
    'installer-log' {
        $source = [System.IO.File]::ReadAllText((Join-Path $Root 'WAVUtils-Installer.iss'))
        Assert-Check ($source -notmatch '(?im)^Type:.*WAVUtils\.log') 'Uninstall must not delete a log WAVUtils does not create.'
    }
    'installer-payload' {
        $source = [System.IO.File]::ReadAllText((Join-Path $Root 'WAVUtils-Installer.iss'))
        Assert-Check ($source -match '(?im)^Source:\s*"publish\\\*";\s*DestDir:\s*"\{app\}"') 'Setup must include the shared publish payload.'
        Assert-Check ($source -notmatch '(?im)^Source:\s*"LICENSE";\s*DestDir:\s*"\{app\}"') 'Setup must not install LICENSE a second time.'
    }
    'publish' { Invoke-BuildFixture -FailNative $false }
    'publish-missing-lock' { Invoke-BuildFixture -FailNative $false -MissingLock }
    'native-error' { Invoke-BuildFixture -FailNative $true }
    'fallback' {
        $source = (Read-Script 'build-release.ps1').Extent.Text
        Assert-Check ($source -notmatch '7zS\.sfx|sfx\.cfg') 'A ZIP/config fallback is not an Inno-compatible verified installer.'
    }
    'http' {
        $ast = Read-Script 'build-release.ps1'
        $requests = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-RestMethod' }, $true)
        foreach ($request in $requests) {
            Assert-Check ($request.Extent.Text -match '-TimeoutSec\s+\d+') 'Every release HTTP request needs a timeout.'
        }
        Assert-Check ($ast.Extent.Text -notmatch 'ReadAllBytes\(\$_\.FullName\)') 'Asset upload must stream the file, not allocate it entirely.'
    }
    default { throw 'Unknown script-check scenario.' }
}
