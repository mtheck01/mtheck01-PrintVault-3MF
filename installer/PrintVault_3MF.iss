#define MyAppName "PrintVault 3MF"
#define MyAppPublisher "Hecks Engraving and Design"
#define MyAppExeName "PrintVault.exe"

[Setup]
AppId={{D3F6B5C0-6E32-4A5B-9C31-8F4C7A2E1B00}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\PrintVault 3MF
DefaultGroupName=PrintVault 3MF
OutputDir=..\dist\installer
OutputBaseFilename=PrintVault_3MF_v{#MyAppVersion}_Setup
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\dist\PrintVault\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\PrintVault 3MF"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\PrintVault 3MF"; Filename: "{app}\{#MyAppExeName}"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
; User data remains in %APPDATA%\PrintVault and is not removed.
