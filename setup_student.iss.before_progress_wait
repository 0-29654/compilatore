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
WizardImageFile=Assets\wizard_wave.bmp
WizardSmallImageFile=Assets\wizard_wave_small.bmp
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
Source: "Assets\startup_wave_00.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_01.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_02.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_03.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_04.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_05.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_06.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_07.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_08.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_09.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_10.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_11.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_12.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_13.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_14.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_15.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_16.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_17.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_18.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_19.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_20.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_21.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_22.bmp"; Flags: dontcopy
Source: "Assets\startup_wave_23.bmp"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  StartupForm: TSetupForm;
  StartupImage: TBitmapImage;
  StartupFrame: Integer;
  InstallImage: TBitmapImage;

procedure CreateStartupForm;
var
  I: Integer;
begin
  { Viene eseguito da InitializeWizard, quindi soltanto DOPO
    che l'utente ha scelto la lingua dell'installazione. }
  for I := 0 to 23 do
    ExtractTemporaryFile(Format('startup_wave_%.2d.bmp', [I]));

  StartupForm := CreateCustomForm(ScaleX(760), ScaleY(440), False, False);
  StartupForm.Caption := 'CV+ Compilatore Alunno';
  StartupForm.Position := poScreenCenter;
  StartupForm.BorderStyle := bsNone;
  StartupForm.Color := clWhite;

  StartupImage := TBitmapImage.Create(StartupForm);
  StartupImage.Parent := StartupForm;
  StartupImage.Left := ScaleX(10);
  StartupImage.Top := ScaleY(10);
  StartupImage.Width := ScaleX(740);
  StartupImage.Height := ScaleY(420);
  StartupImage.Stretch := True;
  StartupImage.Center := True;
  StartupImage.Bitmap.LoadFromFile(
    ExpandConstant('{tmp}\startup_wave_00.bmp'));

  StartupFrame := 0;
  StartupForm.Show;
  StartupForm.BringToFront;
  StartupForm.Update;
end;

procedure AnimateStartupUntilLicenseIsReady;
var
  I: Integer;
  FrameFile: String;
begin
  { Mantiene l'animazione visibile per circa 5,8 secondi.
    In questo intervallo il setup ha già acquisito la lingua,
    mentre la finestra delle condizioni non è ancora mostrata. }
  for I := 0 to 47 do
  begin
    StartupFrame := I mod 24;
    FrameFile := ExpandConstant(
      Format('{tmp}\startup_wave_%.2d.bmp', [StartupFrame]));

    if FileExists(FrameFile) then
      StartupImage.Bitmap.LoadFromFile(FrameFile);

    StartupForm.Update;
    Sleep(120);
  end;
end;

procedure CloseStartupForm;
begin
  if StartupForm <> nil then
  begin
    StartupForm.Hide;
    StartupForm.Free;
    StartupForm := nil;
    StartupImage := nil;
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

  { Le onde partono adesso: la scelta della lingua è già terminata. }
  CreateStartupForm;
  AnimateStartupUntilLicenseIsReady;

  { Non chiudiamo qui la schermata: resta visibile fino a quando
    Inno Setup apre davvero la pagina delle condizioni d'uso.
    CurPageChanged(wpLicense) la chiude in quel preciso momento. }
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpLicense then
  begin
    CloseStartupForm;
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
  CloseStartupForm;
end;
