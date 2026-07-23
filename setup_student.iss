#define MyAppName "CV+ Compilatore Alunno"
#define MyAppVersion "1.9.5"
#define MyAppPublisher "Alessandro Barazzuol"
#define MyAppExeName "CppStudentClient.exe"

[Setup]
LicenseFile=CONDIZIONI_USO_PRIVACY.rtf
AppId={{A6C18F0D-6CA6-4D34-9A45-4D3DA754D8C1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Editor e compilatore C++17 per l'invio degli esercizi al docente
VersionInfoCopyright=Copyright (C) Alessandro Barazzuol
DefaultDirName={localappdata}\Programs\CVPlusCompilatoreAlunno
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=CppStudentClient_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
SetupIconFile=Assets\app.ico
WizardImageFile=Assets\wizard_dog.bmp
WizardSmallImageFile=Assets\wizard_dog_small.bmp
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti:"; Flags: checkedonce

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Assets\A.png"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "Assets\installing_a.bmp"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  StartupForm: TSetupForm;
  StartupLabel: TNewStaticText;
  StartupSubLabel: TNewStaticText;
  StartupProgress: TNewProgressBar;

var
  InstallImage: TBitmapImage;

procedure PositionInstallImage;
begin
  InstallImage.Left :=
    (WizardForm.InstallingPage.Width - InstallImage.Width) div 2;

  InstallImage.Top :=
    WizardForm.ProgressGauge.Top +
    WizardForm.ProgressGauge.Height +
    ScaleY(10);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  StartupForm :=
    CreateCustomForm(
      ScaleX(440),
      ScaleY(160),
      False,
      False
    );

  StartupForm.Caption := 'CV+ Compilatore Alunno';
  StartupForm.Position := poScreenCenter;
  StartupForm.BorderStyle := bsDialog;
  StartupForm.Color := clWhite;

  StartupLabel := TNewStaticText.Create(StartupForm);
  StartupLabel.Parent := StartupForm;
  StartupLabel.Caption := 'Preparazione installazione...';
  StartupLabel.Left := ScaleX(26);
  StartupLabel.Top := ScaleY(24);
  StartupLabel.Width := ScaleX(390);
  StartupLabel.Height := ScaleY(32);
  StartupLabel.Font.Size := 14;
  StartupLabel.Font.Style := [fsBold];
  StartupLabel.Font.Color := clNavy;

  StartupSubLabel := TNewStaticText.Create(StartupForm);
  StartupSubLabel.Parent := StartupForm;
  StartupSubLabel.Caption :=
    'Caricamento dei componenti. Attendi qualche secondo.';
  StartupSubLabel.Left := ScaleX(26);
  StartupSubLabel.Top := ScaleY(66);
  StartupSubLabel.Width := ScaleX(390);
  StartupSubLabel.Height := ScaleY(24);
  StartupSubLabel.Font.Size := 9;
  StartupSubLabel.Font.Color := clGray;

  StartupProgress := TNewProgressBar.Create(StartupForm);
  StartupProgress.Parent := StartupForm;
  StartupProgress.Left := ScaleX(26);
  StartupProgress.Top := ScaleY(108);
  StartupProgress.Width := ScaleX(388);
  StartupProgress.Height := ScaleY(18);
  StartupProgress.Min := 0;
  StartupProgress.Max := 100;
  StartupProgress.Position := 45;

  StartupForm.Show;
  StartupForm.Update;
end;

procedure CloseStartupForm;
begin
  if StartupForm <> nil then
  begin
    StartupProgress.Position := 100;
    StartupForm.Update;
    Sleep(100);
    StartupForm.Close;
    StartupForm.Free;
    StartupForm := nil;
  end;
end;

procedure InitializeWizard;
begin
  CloseStartupForm;
  WizardForm.WelcomeLabel1.Caption :=
    'Benvenuto in CV+ Compilatore Alunno';

  WizardForm.WelcomeLabel2.Caption :=
    'Scrivi, compila ed esegui codice C++17 e invia gli esercizi al docente.' +
    Chr(13) + Chr(10) + Chr(13) + Chr(10) +
    'Il compilatore GCC è incluso e verificato automaticamente.' +
    Chr(13) + Chr(10) + Chr(13) + Chr(10) +
    '© Alessandro Barazzuol';

  WizardForm.WelcomeLabel1.Font.Color := clNavy;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];

  WizardForm.LicenseLabel1.Caption :=
    'Leggi le condizioni d''uso, copyright e privacy.';
  WizardForm.LicenseLabel1.Font.Color := clNavy;
  WizardForm.LicenseLabel1.Font.Style := [fsBold];

  WizardForm.LicenseAcceptedRadio.Caption :=
    'Accetto integralmente le condizioni d''uso e privacy';
  WizardForm.LicenseAcceptedRadio.Font.Style := [fsBold];
  WizardForm.LicenseAcceptedRadio.Font.Color := clGreen;

  WizardForm.LicenseNotAcceptedRadio.Caption :=
    'Non accetto le condizioni';

  ExtractTemporaryFile('installing_a.bmp');

  InstallImage := TBitmapImage.Create(WizardForm);
  InstallImage.Parent := WizardForm.InstallingPage;
  InstallImage.Width := ScaleX(560);
  InstallImage.Height := ScaleY(270);
  InstallImage.Stretch := True;
  InstallImage.Center := True;
  InstallImage.Visible := False;
  InstallImage.Bitmap.LoadFromFile(
    ExpandConstant('{tmp}\installing_a.bmp')
  );

  PositionInstallImage;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpLicense then
    WizardForm.LicenseAcceptedRadio.Checked := True;

  if (CurPageID = wpSelectTasks) and
     (WizardForm.TasksList.Items.Count > 0) then
    WizardForm.TasksList.Checked[0] := True;

  InstallImage.Visible := CurPageID = wpInstalling;

  if InstallImage.Visible then
    PositionInstallImage;
end;


procedure DeinitializeSetup;
begin
  CloseStartupForm;
end;
