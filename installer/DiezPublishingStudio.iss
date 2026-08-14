#define MyAppName "Diez Publishing Studio"
#define MyAppVersion "1.0.0-rc8"
#define MyAppPublisher "Diez Publishing Studio"
#define MyAppExeName "DiezPublishingStudio.exe"

[Setup]
AppId={{E6BE35BE-1F4B-4A3D-8CEB-27D970D85911}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion=1.0.0.8
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Diez Publishing Studio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=DiezPublishingStudio-Setup
Compression=lzma2/fast
SolidCompression=no
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
  DiezSettingsKey = 'Software\Diez Publishing Studio';

var
  ProgramActionPage: TInputOptionWizardPage;
  WorkDataPage: TInputOptionWizardPage;
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

function DeleteDirectoryIfPresent(const Path: String): Boolean;
begin
  Result := True;
  if DirExists(Path) then
    Result := DelTree(Path, True, True, True);
end;

function ResetDiezConfiguration(): Boolean;
var
  LocalRoot: String;
  RoamingRoot: String;
begin
  Result := True;
  LocalRoot := ExpandConstant('{localappdata}\Diez Publishing Studio');
  RoamingRoot := ExpandConstant('{userappdata}\Diez Publishing Studio');

  { Configurazione e dati non editoriali: vengono sempre eliminati quando
    l'utente sceglie "Rimuovi e reinstalla". }
  if not DeleteDirectoryIfPresent(LocalRoot + '\config') then Result := False;
  if not DeleteDirectoryIfPresent(LocalRoot + '\cache') then Result := False;
  if not DeleteDirectoryIfPresent(LocalRoot + '\logs') then Result := False;
  if not DeleteDirectoryIfPresent(LocalRoot + '\temp') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\config') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\cache') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\logs') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\temp') then Result := False;
  RegDeleteKeyIncludingSubkeys(HKCU, DiezSettingsKey);
end;

function ResetDiezWorkData(): Boolean;
var
  LocalRoot: String;
  RoamingRoot: String;
begin
  Result := True;
  LocalRoot := ExpandConstant('{localappdata}\Diez Publishing Studio');
  RoamingRoot := ExpandConstant('{userappdata}\Diez Publishing Studio');

  { Solo lavoro gestito internamente da Diez. Comprende eventuali copie di
    lavoro, versioni, history/snapshot e asset interni. NON cerca mai .diez,
    foto o documenti salvati dall'utente in cartelle esterne. }
  if not DeleteDirectoryIfPresent(LocalRoot + '\work') then Result := False;
  if not DeleteDirectoryIfPresent(LocalRoot + '\projects') then Result := False;
  if not DeleteDirectoryIfPresent(LocalRoot + '\versions') then Result := False;
  if not DeleteDirectoryIfPresent(LocalRoot + '\history') then Result := False;
  if not DeleteDirectoryIfPresent(LocalRoot + '\snapshots') then Result := False;
  if not DeleteDirectoryIfPresent(LocalRoot + '\assets') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\work') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\projects') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\versions') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\history') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\snapshots') then Result := False;
  if not DeleteDirectoryIfPresent(RoamingRoot + '\assets') then Result := False;
end;

procedure InitializeWizard;
begin
  PreviousInstallDetected := HasPreviousInstall();

  ProgramActionPage := CreateInputOptionPage(
    wpWelcome,
    'Programma Diez',
    'Come vuoi procedere con il programma?',
    'La rimozione e reinstallazione elimina sempre la configurazione di Diez prima di installare la nuova versione.',
    True,
    True);
  ProgramActionPage.Add('Aggiorna Diez');
  ProgramActionPage.Add('Rimuovi la versione precedente e reinstalla Diez');
  ProgramActionPage.Values[0] := True;

  WorkDataPage := CreateInputOptionPage(
    ProgramActionPage.ID,
    'File di lavoro',
    'Vuoi mantenere i file di lavoro gestiti da Diez?',
    'Comprende copie di lavoro, asset interni, storico e versioning. I progetti .diez, le foto e gli altri file che hai salvato personalmente in cartelle esterne non vengono mai cancellati automaticamente dall''installer.',
    False,
    True);
  WorkDataPage.Add('Mantieni i file di lavoro di Diez, compreso versioning e storico');
  WorkDataPage.Values[0] := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RemoveAndReinstall: Boolean;
  KeepWorkData: Boolean;
begin
  Result := '';

  RemoveAndReinstall := ProgramActionPage.Values[1] or
    (CompareText(ExpandConstant('{param:CLEANOLD|0}'), '1') = 0);
  KeepWorkData := WorkDataPage.Values[0];
  if CompareText(ExpandConstant('{param:KEEPWORKDATA|1}'), '0') = 0 then
    KeepWorkData := False;

  if RemoveAndReinstall then
  begin
    if PreviousInstallDetected and (not RemovePreviousInstall()) then
    begin
      Result := 'Non è stato possibile rimuovere la versione precedente. Chiudi Diez Publishing Studio e riprova.';
      Exit;
    end;

    { Una rimozione/reinstallazione non mantiene mai la configurazione Diez. }
    if not ResetDiezConfiguration() then
    begin
      Result := 'Non è stato possibile eliminare completamente la configurazione di Diez. Chiudi eventuali programmi che la stanno usando e riprova.';
      Exit;
    end;
  end;

  { Il checkbox file di lavoro è indipendente dall'azione sul programma. }
  if not KeepWorkData then
  begin
    if not ResetDiezWorkData() then
      Result := 'Non è stato possibile eliminare completamente i file di lavoro gestiti da Diez. Chiudi eventuali programmi che li stanno usando e riprova.';
  end;
end;
