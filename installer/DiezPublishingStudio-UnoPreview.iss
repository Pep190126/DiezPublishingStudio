#define MyAppName "Diez Publishing Studio Preview"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "Diez Publishing Studio"
#define MyAppExeName "Diez.Uno.exe"

[Setup]
AppId={{B8E5B72B-C040-4B3B-846C-CC2F40F3C34E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion=0.2.0.0
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Diez Publishing Studio Uno Preview
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installers
OutputBaseFilename=DiezPublishingStudio-UnoPreview-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\artifacts\uno-preview-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent
