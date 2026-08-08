#define MyAppName "Diez Publishing Studio"
#define MyAppVersion "0.15.0-preview"
#define MyAppPublisher "Diez Publishing Studio"
#define MyAppExeName "DiezPublishingStudio.exe"

[Setup]
AppId={{E6BE35BE-1F4B-4A3D-8CEB-27D970D85911}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion=0.15.0.0
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
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter={#MyAppExeName}
AppMutex=DiezPublishingStudio.App
SetupLogging=yes
ChangesAssociations=yes

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
Root: HKCU; Subkey: "Software\Classes\DiezPublishingStudio.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKCU; Subkey: "Software\Classes\DiezPublishingStudio.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  PreviousUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{E6BE35BE-1F4B-4A3D-8CEB-27D970D85911}_is1';

var
  CleanInstallPage: TInputOptionWizardPage;
  PreviousInstallDetected: Boolean;

function HasPreviousInstall(): Boolean;
begin
  Result := RegKeyExists(HKCU, PreviousUninstallKey);
end;

function ExtractExecutable(const CommandLine: String): String;
var
  S: String;
  P: Integer;
begin
  S := Trim(CommandLine);
  if (Length(S) > 0) and (S[1] = '"') then
  begin
    Delete(S, 1, 1);
    P := Pos('"', S);
    if P > 0 then Result := Copy(S, 1, P - 1) else Result := S;
  end
  else
  begin
    P := Pos(' ', S);
    if P > 0 then Result := Copy(S, 1, P - 1) else Result := S;
  end;
end;

function RemovePreviousInstall(): Boolean;
var
  UninstallString: String;
  UninstallExe: String;
  ResultCode: Integer;
begin
  Result := True;
  if not RegQueryStringValue(HKCU, PreviousUninstallKey, 'UninstallString', UninstallString) then Exit;

  UninstallExe := ExtractExecutable(UninstallString);
  if (UninstallExe = '') or (not FileExists(UninstallExe)) then
  begin
    Result := False;
    Exit;
  end;

  Result := Exec(UninstallExe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if Result and (ResultCode <> 0) then Result := False;
end;

procedure InitializeWizard;
begin
  PreviousInstallDetected := HasPreviousInstall();
  if PreviousInstallDetected then
  begin
    CleanInstallPage := CreateInputOptionPage(
      wpWelcome,
      'Aggiornamento di Diez',
      'È già presente una versione di Diez Publishing Studio.',
      'Lascia disattivata questa opzione per il normale aggiornamento. Attivala solo se una nuova versione non riesce a installarsi correttamente.',
      False,
      False);
    CleanInstallPage.Add('Installazione pulita: rimuovi prima la versione precedente (i file .diez personali non vengono eliminati)');
    CleanInstallPage.Values[0] := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ForceCleanInstall: Boolean;
begin
  Result := '';
  ForceCleanInstall := CompareText(ExpandConstant('{param:CLEANOLD|0}'), '1') = 0;

  if PreviousInstallDetected and (ForceCleanInstall or CleanInstallPage.Values[0]) then
  begin
    if not RemovePreviousInstall() then
      Result := 'Non è stato possibile rimuovere la versione precedente. Chiudi Diez Publishing Studio e riprova. I tuoi file .diez non vengono toccati.';
  end;
end;
