; WAVUtils Installer
; Copyright (c) 2026 WTF Software
; https://github.com/wtfsoftware/WAVUtils

#define MyAppName "WAVUtils"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.1"
#endif
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
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=LICENSE
InfoBeforeFile=README-INSTALL.txt
AppMutex=WAVUtilsGlobalMutex
ShowLanguageDialog=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
english.CreateDesktopIcon=Create a &desktop icon
english.AdditionalIcons=Additional icons:
russian.CreateDesktopIcon=Создать ярлык на &рабочем столе
russian.AdditionalIcons=Дополнительные ярлыки:
german.CreateDesktopIcon=Desktop-&Symbol erstellen
german.AdditionalIcons=Zusätzliche Symbole:
french.CreateDesktopIcon=Créer une icône sur le &bureau
french.AdditionalIcons=Icônes supplémentaires :
spanish.CreateDesktopIcon=Crear un icono en el &escritorio
spanish.AdditionalIcons=Iconos adicionales:
japanese.CreateDesktopIcon=デスクトップにアイコンを作成(&D)
japanese.AdditionalIcons=追加アイコン:
chinesesimplified.CreateDesktopIcon=创建桌面图标(&D)
chinesesimplified.AdditionalIcons=其他图标:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\WAVUtils.Tray.exe"
Name: "{group}\{#MyAppName} Website"; Filename: "{#MyAppURL}"
Name: "{group}\{#MyAppName} Uninstall"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\WAVUtils.Tray.exe"; Tasks: desktopicon
