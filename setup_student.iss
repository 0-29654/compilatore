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
WizardImageFile=Assets\wizard.bmp
WizardSmallImageFile=Assets\wizard_small.bmp
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
DisableWelcomePage=yes

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
  PreparationForm: TSetupForm;
  PreparationProgress: TNewProgressBar;
  PreparationText: TNewStaticText;
  InstallImage: TBitmapImage;

procedure ShowPreparationWindow;
begin
  if PreparationForm <> nil then
    exit;

  { La finestra viene costruita per prima e mostrata immediatamente,
    prima delle altre operazioni di inizializzazione del wizard. }
  PreparationForm := CreateCustomForm(ScaleX(430), ScaleY(92), False, False);
  PreparationForm.Position := poScreenCenter;
  PreparationForm.BorderStyle := bsNone;
  PreparationForm.Color := $00F2F2F2;

  PreparationText := TNewStaticText.Create(PreparationForm);
  PreparationText.Parent := PreparationForm;
  PreparationText.Left := ScaleX(18);
  PreparationText.Top := ScaleY(16);
  PreparationText.Width := ScaleX(394);
  PreparationText.Height := ScaleY(24);
  PreparationText.Caption := 'Attendere - preparazione dell''installazione...';
  PreparationText.Font.Name := 'Segoe UI';
  PreparationText.Font.Size := 10;
  PreparationText.Font.Style := [fsBold];
  PreparationText.Font.Color := $00505050;

  PreparationProgress := TNewProgressBar.Create(PreparationForm);
  PreparationProgress.Parent := PreparationForm;
  PreparationProgress.Left := ScaleX(18);
  PreparationProgress.Top := ScaleY(51);
  PreparationProgress.Width := ScaleX(394);
  PreparationProgress.Height := ScaleY(15);
  PreparationProgress.Style := npbstMarquee;

  PreparationForm.Show;
  PreparationForm.BringToFront;
  PreparationForm.Update;
end;

procedure HidePreparationWindow;
begin
  if PreparationForm <> nil then
  begin
    PreparationForm.Hide;
    PreparationForm.Free;
    PreparationForm := nil;
    PreparationProgress := nil;
    PreparationText := nil;
  end;
end;

procedure PositionInstallImage;
begin
  InstallImage.Left :=
    (WizardForm.InstallingPage.Width - InstallImage.Width) div 2;

  InstallImage.Top :=
    WizardForm.ProgressGauge.Top +
    WizardForm.ProgressGauge.Height +
    ScaleY(10);
end;

procedure InitializeWizard;
begin
  { Questa è la prima operazione eseguita dopo la conferma della lingua. }
  ShowPreparationWindow;

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
    'Leggi attentamente le condizioni d''uso, copyright e privacy.';
  WizardForm.LicenseLabel1.Font.Color := clNavy;
  WizardForm.LicenseLabel1.Font.Style := [fsBold];

  WizardForm.LicenseAcceptedRadio.Caption :=
    'Accetto integralmente le condizioni d''uso, copyright e privacy';
  WizardForm.LicenseAcceptedRadio.Font.Style := [fsBold];
  WizardForm.LicenseAcceptedRadio.Font.Color := clGreen;

  WizardForm.LicenseNotAcceptedRadio.Caption :=
    'Non accetto le condizioni d''uso';

  ExtractTemporaryFile('installing_a.bmp');

  InstallImage := TBitmapImage.Create(WizardForm);
  InstallImage.Parent := WizardForm.InstallingPage;
  InstallImage.Width := ScaleX(560);
  InstallImage.Height := ScaleY(270);
  InstallImage.Stretch := True;
  InstallImage.Center := True;
  InstallImage.Visible := False;
  InstallImage.Bitmap.LoadFromFile(
    ExpandConstant('{tmp}\\installing_a.bmp')
  );

  PositionInstallImage;
  { Non chiudere qui: resta visibile fino alla pagina della licenza. }
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpLicense then
  begin
    HidePreparationWindow;
    WizardForm.LicenseAcceptedRadio.Checked := True;
  end;

  if (CurPageID = wpSelectTasks) and
     (WizardForm.TasksList.Items.Count > 0) then
    WizardForm.TasksList.Checked[0] := True;

  InstallImage.Visible := CurPageID = wpInstalling;

  if InstallImage.Visible then
    PositionInstallImage;
end;

procedure DeinitializeSetup;
begin
  HidePreparationWindow;
end;
