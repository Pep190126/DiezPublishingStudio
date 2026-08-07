#define MyAppName "Diez Publishing Studio"
#define MyAppVersion "0.0.1-preview"
#define MyAppPublisher "Diez Publishing Studio"
#define MyAppExeName "DiezPublishingStudio.exe"

[Setup]
AppId={{E6BE35BE-1F4B-4A3D-8CEB-27D970D85911}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Diez Publishing Studio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=DiezPublishingStudio-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Classes\.diez"; ValueType: string; ValueName: ""; ValueData: "DiezPublishingStudio.Project"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\DiezPublishingStudio.Project"; ValueType: string; ValueName: ""; ValueData: "Progetto Diez Publishing Studio"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DiezPublishingStudio.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent
