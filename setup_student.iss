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
  WaitForm: TSetupForm;
  WaitProgress: TNewProgressBar;
  WaitTitle: TNewStaticText;
  WaitMessage: TNewStaticText;
  InstallImage: TBitmapImage;

procedure ShowPreparationProgress;
var
  Cycle: Integer;
  Value: Integer;
  Direction: Integer;
begin
  { Questa procedura viene chiamata da InitializeWizard: a questo punto
    la lingua è già stata scelta, mentre la pagina della licenza non è
    ancora comparsa. }
  WaitForm := CreateCustomForm(ScaleX(500), ScaleY(170), False, False);
  WaitForm.Caption := 'CV+ Compilatore Alunno';
  WaitForm.Position := poScreenCenter;
  WaitForm.BorderStyle := bsDialog;
  WaitForm.Color := clWhite;

  WaitTitle := TNewStaticText.Create(WaitForm);
  WaitTitle.Parent := WaitForm;
  WaitTitle.Left := ScaleX(28);
  WaitTitle.Top := ScaleY(24);
  WaitTitle.Width := ScaleX(440);
  WaitTitle.Height := ScaleY(28);
  WaitTitle.Caption := 'Preparazione dell''installazione';
  WaitTitle.Font.Size := 13;
  WaitTitle.Font.Style := [fsBold];
  WaitTitle.Font.Color := clNavy;

  WaitMessage := TNewStaticText.Create(WaitForm);
  WaitMessage.Parent := WaitForm;
  WaitMessage.Left := ScaleX(28);
  WaitMessage.Top := ScaleY(59);
  WaitMessage.Width := ScaleX(440);
  WaitMessage.Height := ScaleY(22);
  WaitMessage.Caption := 'Attendere l''apertura delle condizioni d''uso e privacy...';
  WaitMessage.Font.Size := 9;

  WaitProgress := TNewProgressBar.Create(WaitForm);
  WaitProgress.Parent := WaitForm;
  WaitProgress.Left := ScaleX(28);
  WaitProgress.Top := ScaleY(98);
  WaitProgress.Width := ScaleX(440);
  WaitProgress.Height := ScaleY(18);
  WaitProgress.Min := 0;
  WaitProgress.Max := 100;
  WaitProgress.Position := 0;

  WaitForm.Show;
  WaitForm.BringToFront;
  WaitForm.Update;

  { Barra blu che continua ad andare avanti e indietro. La durata di
    circa 5,5 secondi copre l'attesa iniziale; appena termina, la form
    viene chiusa e Inno Setup mostra immediatamente la pagina licenza. }
  Value := 0;
  Direction := 1;
  for Cycle := 0 to 219 do
  begin
    Value := Value + (Direction * 2);
    if Value >= 100 then
    begin
      Value := 100;
      Direction := -1;
    end
    else if Value <= 0 then
    begin
      Value := 0;
      Direction := 1;
    end;

    WaitProgress.Position := Value;
    WaitForm.Update;
    Sleep(25);
  end;

  WaitForm.Hide;
  WaitForm.Free;
  WaitForm := nil;
  WaitProgress := nil;
  WaitTitle := nil;
  WaitMessage := nil;
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

  { Dopo la scelta della lingua, mostra soltanto la barra di attesa.
    Quando la procedura termina, InitializeWizard restituisce il controllo
    a Inno Setup e si apre subito la pagina delle condizioni d'uso. }
  ShowPreparationProgress;
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
  if WaitForm <> nil then
  begin
    WaitForm.Hide;
    WaitForm.Free;
    WaitForm := nil;
  end;
end;
