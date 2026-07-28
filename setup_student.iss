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
Source: "Assets\cpp_anim_00.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_01.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_02.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_03.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_04.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_05.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_06.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_07.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_08.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_09.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_10.bmp"; Flags: dontcopy
Source: "Assets\cpp_anim_11.bmp"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  SplashForm: TSetupForm;
  SplashImage: TBitmapImage;
  SplashProgress: TNewProgressBar;
  SplashText: TNewStaticText;
  InstallImage: TBitmapImage;

procedure LoadSplashFrame(FrameIndex: Integer);
var
  FrameName: String;
begin
  FrameName := Format('cpp_anim_%.2d.bmp', [FrameIndex]);
  SplashImage.Bitmap.LoadFromFile(ExpandConstant('{tmp}\\') + FrameName);
end;

procedure ShowPreparationSplash;
var
  I: Integer;
  FrameIndex: Integer;
  ProgressValue: Integer;
begin
  { Tutti i fotogrammi vengono estratti subito dopo la scelta della lingua. }
  for I := 0 to 11 do
    ExtractTemporaryFile(Format('cpp_anim_%.2d.bmp', [I]));

  SplashForm := CreateCustomForm(ScaleX(570), ScaleY(318), False, False);
  SplashForm.Position := poScreenCenter;
  SplashForm.BorderStyle := bsNone;
  SplashForm.Color := $00130F0B;

  SplashImage := TBitmapImage.Create(SplashForm);
  SplashImage.Parent := SplashForm;
  SplashImage.Left := ScaleX(10);
  SplashImage.Top := ScaleY(10);
  SplashImage.Width := SplashForm.ClientWidth - ScaleX(20);
  SplashImage.Height := ScaleY(233);
  SplashImage.Stretch := True;
  SplashImage.Center := True;
  LoadSplashFrame(0);

  SplashText := TNewStaticText.Create(SplashForm);
  SplashText.Parent := SplashForm;
  SplashText.Left := ScaleX(16);
  SplashText.Top := ScaleY(254);
  SplashText.Width := ScaleX(355);
  SplashText.Height := ScaleY(24);
  SplashText.Caption := 'Attendere - preparazione dell''installazione...';
  SplashText.Font.Name := 'Segoe UI';
  SplashText.Font.Size := 10;
  SplashText.Font.Style := [fsBold];
  SplashText.Font.Color := clWhite;

  SplashProgress := TNewProgressBar.Create(SplashForm);
  SplashProgress.Parent := SplashForm;
  SplashProgress.Left := ScaleX(382);
  SplashProgress.Top := ScaleY(256);
  SplashProgress.Width := ScaleX(168);
  SplashProgress.Height := ScaleY(17);
  SplashProgress.Min := 0;
  SplashProgress.Max := 100;
  SplashProgress.Position := 0;

  SplashForm.Show;
  SplashForm.BringToFront;
  SplashForm.Update;

  { Animazione visibile fino all'apertura della pagina della licenza. }
  ProgressValue := 0;
  for I := 0 to 47 do
  begin
    FrameIndex := I mod 12;
    LoadSplashFrame(FrameIndex);

    ProgressValue := ProgressValue + 3;
    if ProgressValue > 96 then
      ProgressValue := 18;
    SplashProgress.Position := ProgressValue;

    SplashForm.Update;
    Sleep(75);
  end;
end;

procedure HidePreparationSplash;
begin
  if SplashForm <> nil then
  begin
    SplashForm.Hide;
    SplashForm.Free;
    SplashForm := nil;
    SplashImage := nil;
    SplashProgress := nil;
    SplashText := nil;
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
  { Compare immediatamente dopo la conferma della lingua. }
  ShowPreparationSplash;

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

  { La schermata viene rimossa soltanto quando il wizard è pronto. }
  HidePreparationSplash;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpLicense then
  begin
    HidePreparationSplash;
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
  HidePreparationSplash;
end;
