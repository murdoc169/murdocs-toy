; Inno Setup script for Murdoc's Toy. Build: publish to dist/app first (see README), then
;   ISCC.exe installer\MurdocsToy.iss   ->  dist\MurdocsToy-Setup-0.4.0.exe
#define AppName "Murdoc's Toy"
#define AppVersion "0.4.0"

[Setup]
AppId={{6F1B7C52-3A0D-4E7B-9C54-5D2E8B1A7F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=murdoc169
DefaultDirName={autopf}\Murdocs Toy
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=MurdocsToy-Setup-{#AppVersion}
SetupIconFile=..\src\MurdocsToy.Desktop\app.ico
UninstallDisplayIcon={app}\MurdocsToy.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\dist\app\MurdocsToy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\MurdocsToy.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\MurdocsToy.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MurdocsToy.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
