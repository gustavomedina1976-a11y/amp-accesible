#define MyAppName "Amp Accessible"
#define MyAppVersion "2.41.62"
#define MyAppPublisher "GDM"
#define MyAppExeName "AmpAccessible.exe"

[Setup]
AppId={{6A5E6F57-5134-4A63-9B37-7A29D4DAD240}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\GDM Amp Accessible
DefaultGroupName=GDM Amp Accessible
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir=Salida
OutputBaseFilename=Amp_Accessible_Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
ChangesAssociations=yes
UninstallDisplayName=Amp Accessible
UninstallDisplayIcon={app}\AmpAccessible.exe
UsePreviousAppDir=yes
UsePreviousGroup=yes
CreateUninstallRegKey=yes
Uninstallable=yes

[Files]
Source: "..\PUBLICACION_AUTOCONTENIDA\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\Amp Accessible"; Filename: "{app}\AmpAccessible.exe"; WorkingDir: "{app}"
Name: "{group}\Amp Accessible"; Filename: "{app}\AmpAccessible.exe"; WorkingDir: "{app}"
Name: "{group}\Reparar o actualizar Amp Accessible"; Filename: "{app}\Maintenance\AmpAccessible_Setup.exe"
Name: "{group}\Desinstalar Amp Accessible"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Classes\.nam"; ValueType: string; ValueName: ""; ValueData: "GDMAmpAccessible.NAM"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.nam\OpenWithProgids"; ValueType: none; ValueName: "GDMAmpAccessible.NAM"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\GDMAmpAccessible.NAM"; ValueType: string; ValueName: ""; ValueData: "Modelo Neural Amp Modeler"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\GDMAmpAccessible.NAM\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\AmpAccessible.exe,0"
Root: HKCU; Subkey: "Software\Classes\GDMAmpAccessible.NAM\shell\open"; ValueType: string; ValueName: ""; ValueData: "Abrir con Amp Accessible"
Root: HKCU; Subkey: "Software\Classes\GDMAmpAccessible.NAM\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\AmpAccessible.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.nam\shell\GDMAmpAccessible.Import"; ValueType: string; ValueName: ""; ValueData: "Agregar al Banco NAM de Amp Accessible"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.nam\shell\GDMAmpAccessible.Import\command"; ValueType: string; ValueName: ""; ValueData: """{app}\AmpAccessible.exe"" --import-nam ""%1"""

[Run]
Filename: "{app}\AmpAccessible.exe"; Description: "Iniciar Amp Accessible"; Flags: nowait postinstall skipifsilent

[Code]
var
  MaintenancePage: TInputOptionWizardPage;
  AlreadyInstalled: Boolean;

function InstalledUninstaller(): String;
begin
  Result := ExpandConstant('{localappdata}\Programs\GDM Amp Accessible\unins000.exe');
end;

function InitializeSetup(): Boolean;
begin
  AlreadyInstalled := FileExists(InstalledUninstaller());
  Result := True;
end;

procedure InitializeWizard();
begin
  MaintenancePage := CreateInputOptionPage(wpWelcome,
    'Mantenimiento de Amp Accessible',
    'Amp Accessible ya está instalado.',
    'Puede reparar o actualizar la instalación con este mismo instalador, o abrir el desinstalador. Sus NAM, presets, escenas y copias de seguridad no se borran.',
    True, False);
  MaintenancePage.Add('Reparar o actualizar instalación');
  MaintenancePage.Add('Desinstalar Amp Accessible');
  MaintenancePage.SelectedValueIndex := 0;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = MaintenancePage.ID) and (not AlreadyInstalled);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if AlreadyInstalled and (CurPageID = MaintenancePage.ID) and (MaintenancePage.SelectedValueIndex = 1) then
  begin
    if FileExists(InstalledUninstaller()) then
      Exec(InstalledUninstaller(), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode)
    else
      MsgBox('No se encontró el desinstalador de la instalación actual.', mbError, MB_OK);
    Result := False;
    WizardForm.Close;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  MaintenanceDir, CachedSetup: String;
begin
  if CurStep = ssPostInstall then
  begin
    MaintenanceDir := ExpandConstant('{app}\Maintenance');
    ForceDirectories(MaintenanceDir);
    CachedSetup := MaintenanceDir + '\AmpAccessible_Setup.exe';
    FileCopy(ExpandConstant('{srcexe}'), CachedSetup, False);
  end;
end;
