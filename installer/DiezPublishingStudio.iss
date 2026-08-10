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
  DiezSettingsKey = 'Software\Diez Publishing Studio';

var
  CleanInstallPage: TInputOptionWizardPage;
  UserDataPage: TInputOptionWizardPage;
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

function ResetDiezUserData(): Boolean;
var
  LocalDataDir: String;
  RoamingDataDir: String;
begin
  Result := True;
  LocalDataDir := ExpandConstant('{localappdata}\Diez Publishing Studio');
  RoamingDataDir := ExpandConstant('{userappdata}\Diez Publishing Studio');

  if DirExists(LocalDataDir) then
    if not DelTree(LocalDataDir, True, True, True) then Result := False;

  if DirExists(RoamingDataDir) then
    if not DelTree(RoamingDataDir, True, True, True) then Result := False;

  RegDeleteKeyIncludingSubkeys(HKCU, DiezSettingsKey);
end;

procedure InitializeWizard;
var
  UserDataAfterPage: Integer;
begin
  PreviousInstallDetected := HasPreviousInstall();
  UserDataAfterPage := wpWelcome;

  if PreviousInstallDetected then
  begin
    CleanInstallPage := CreateInputOptionPage(
      wpWelcome,
      'Aggiornamento di Diez',
      'È già presente una versione di Diez Publishing Studio.',
      'Scegli se aggiornare normalmente oppure rimuovere prima i soli file del programma. La scelta sui dati utente viene fatta nella schermata successiva.',
      True,
      True);
    CleanInstallPage.Add('Aggiornamento normale (consigliato)');
    CleanInstallPage.Add('Installazione pulita del programma: rimuovi prima la versione precedente');
    CleanInstallPage.Values[0] := True;
    UserDataAfterPage := CleanInstallPage.ID;
  end;

  UserDataPage := CreateInputOptionPage(
    UserDataAfterPage,
    'Dati utente di Diez',
    'Vuoi mantenere i dati locali creati da Diez?',
    'Questa scelta riguarda impostazioni, cache, log e stato locale dell’app. I progetti .diez salvati nelle cartelle scelte da te non vengono cercati né eliminati.',
    True,
    True);
  UserDataPage.Add('Mantieni i dati utente di Diez (consigliato)');
  UserDataPage.Add('Elimina i dati utente di Diez e riparti da uno stato locale vuoto');
  UserDataPage.Values[0] := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ForceCleanInstall: Boolean;
  CleanSelected: Boolean;
  ResetRequested: Boolean;
begin
  Result := '';
  ForceCleanInstall := CompareText(ExpandConstant('{param:CLEANOLD|0}'), '1') = 0;
  CleanSelected := False;
  if PreviousInstallDetected then
    CleanSelected := CleanInstallPage.Values[1];

  if PreviousInstallDetected and (ForceCleanInstall or CleanSelected) then
  begin
    if not RemovePreviousInstall() then
    begin
      Result := 'Non è stato possibile rimuovere la versione precedente. Chiudi Diez Publishing Studio e riprova.';
      Exit;
    end;
  end;

  ResetRequested := CompareText(ExpandConstant('{param:RESETUSERDATA|0}'), '1') = 0;
  if not ResetRequested then
    ResetRequested := UserDataPage.Values[1];

  if ResetRequested then
  begin
    if not ResetDiezUserData() then
      Result := 'Non è stato possibile eliminare completamente i dati locali di Diez. Chiudi eventuali programmi che li stanno usando e riprova.';
  end;
end;
