; WAVUtils Installer
; Copyright (c) 2025 WTF Software
; https://github.com/wtfsoftware/WAVUtils

#define MyAppName "WAVUtils"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "WTF Software"
#define MyAppURL "https://github.com/wtfsoftware/WAVUtils"
#define MyAppExeName "WAVUtils.exe"
#define MyAppDescription "Multi-channel WAV merge and split utility"

[Setup]
AppId={{A3B5C7D9-1E2F-4A6B-8C9D-0E1F2A3B4C5D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppComments={#MyAppDescription}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputBaseFilename=WAVUtils-Setup-{#MyAppVersion}
OutputDir=dist
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=LICENSE
InfoBeforeFile=README-INSTALL.txt
AppMutex=WAVUtilsGlobalMutex

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "contextmenu"; Description: "{cm:AddContextMenu}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} Website"; Filename: "{#MyAppURL}"
Name: "{group}\{#MyAppName} Uninstall"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Context menu - Merge
Root: HKCU; Subkey: "Software\Classes\*\shell\WAVUtilsMerge"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\WAVUtilsMerge"; ValueType: string; ValueName: ""; ValueData: "WAV Utils &// Merge WAV"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\WAVUtilsMerge\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" merge ""%1"" ""%2"" ""%3"" ""%4"" ""%5"" ""%6"" ""%7"" ""%8"" ""%9"""; Flags: uninsdeletekey

; Context menu - Split
Root: HKCU; Subkey: "Software\Classes\*\shell\WAVUtilsSplit"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\WAVUtilsSplit"; ValueType: string; ValueName: ""; ValueData: "WAV Utils &\\ Split WAV"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\WAVUtilsSplit\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" split ""%1"""; Flags: uninsdeletekey

; Auto-start
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WAVUtils"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\*\shell\WAVUtilsMerge');
  RegDeleteKeyIncludingSubKeys(HKCU, 'Software\Classes\*\shell\WAVUtilsSplit');
  Result := True;
end;

[UninstallDelete]
Type: files; Name: "{app}\WAVUtils.log"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
