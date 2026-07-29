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
DisableWelcomePage=no

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti:"; Flags: checkedonce

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Assets\A.png"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "Assets\installing_a.bmp"; Flags: dontcopy
Source: "Assets\installer_header.bmp"; Flags: dontcopy
Source: "Assets\installer_splash.bmp"; Flags: dontcopy

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
  PreparationImage: TBitmapImage;
  PreparationTimer: LongWord;
  PreparationStep: Integer;
  InstallImage: TBitmapImage;
  HeaderImage: TBitmapImage;

const
  PBM_SETMARQUEE = $040A;
  PBM_SETSTATE = $0410;
  PBST_NORMAL = $0001;
  WM_TIMER = $0113;

function SendMessage(hWnd: HWND; Msg: Cardinal; wParam: Longint; lParam: Longint): Longint;
  external 'SendMessageW@user32.dll stdcall';
function SetTimer(hWnd: HWND; nIDEvent, uElapse: LongWord; lpTimerFunc: LongWord): LongWord;
  external 'SetTimer@user32.dll stdcall';
function KillTimer(hWnd: HWND; uIDEvent: LongWord): Boolean;
  external 'KillTimer@user32.dll stdcall';
function CreateRoundRectRgn(nLeftRect, nTopRect, nRightRect, nBottomRect, nWidthEllipse, nHeightEllipse: Integer): LongWord;
  external 'CreateRoundRectRgn@gdi32.dll stdcall';
function SetWindowRgn(hWnd: HWND; hRgn: LongWord; bRedraw: Boolean): Integer;
  external 'SetWindowRgn@user32.dll stdcall';

procedure AnimatePreparation(h: LongWord; uMsg, idEvent, dwTime: LongWord);
begin
  PreparationStep := PreparationStep + 2;
  if PreparationStep > 100 then
    PreparationStep := 0;
  if PreparationProgress <> nil then
  begin
    PreparationProgress.Position := PreparationStep;
    PreparationProgress.Update;
  end;
end;

procedure ShowPreparationWindow;
var
  Rgn: LongWord;
begin
  if PreparationForm <> nil then Exit;

  ExtractTemporaryFile('installer_splash.bmp');
  PreparationForm := CreateCustomForm(ScaleX(440), ScaleY(170), False, False);
  PreparationForm.Position := poScreenCenter;
  PreparationForm.BorderStyle := bsNone;
  PreparationForm.Color := $00F8F9FC;
  PreparationForm.ClientWidth := ScaleX(440);
  PreparationForm.ClientHeight := ScaleY(170);
  Rgn := CreateRoundRectRgn(0, 0, PreparationForm.ClientWidth + 1,
    PreparationForm.ClientHeight + 1, ScaleX(22), ScaleY(22));
  SetWindowRgn(PreparationForm.Handle, Rgn, True);

  PreparationImage := TBitmapImage.Create(PreparationForm);
  PreparationImage.Parent := PreparationForm;
  PreparationImage.Left := ScaleX(10);
  PreparationImage.Top := ScaleY(8);
  PreparationImage.Width := ScaleX(420);
  PreparationImage.Height := ScaleY(110);
  PreparationImage.Stretch := True;
  PreparationImage.Bitmap.LoadFromFile(ExpandConstant('{tmp}\installer_splash.bmp'));

  PreparationText := TNewStaticText.Create(PreparationForm);
  PreparationText.Parent := PreparationForm;
  PreparationText.Left := ScaleX(18);
  PreparationText.Top := ScaleY(119);
  PreparationText.Width := ScaleX(404);
  PreparationText.Height := ScaleY(18);
  PreparationText.Caption := 'Verifica dei componenti e preparazione guidata';
  PreparationText.Font.Name := 'Segoe UI';
  PreparationText.Font.Size := 9;
  PreparationText.Font.Color := $005A6170;

  PreparationProgress := TNewProgressBar.Create(PreparationForm);
  PreparationProgress.Parent := PreparationForm;
  PreparationProgress.Left := ScaleX(18);
  PreparationProgress.Top := ScaleY(143);
  PreparationProgress.Width := ScaleX(404);
  PreparationProgress.Height := ScaleY(10);
  PreparationProgress.Min := 0;
  PreparationProgress.Max := 100;
  PreparationProgress.Position := 4;
  SendMessage(PreparationProgress.Handle, PBM_SETSTATE, PBST_NORMAL, 0);

  PreparationForm.Show;
  PreparationForm.BringToFront;
  PreparationForm.Update;
  PreparationStep := 4;
  PreparationTimer := SetTimer(0, 0, 35, CreateCallback(@AnimatePreparation));
end;

procedure HidePreparationWindow;
begin
  if PreparationTimer <> 0 then
  begin
    KillTimer(0, PreparationTimer);
    PreparationTimer := 0;
  end;
  if PreparationForm <> nil then
  begin
    PreparationForm.Hide;
    PreparationForm.Free;
    PreparationForm := nil;
    PreparationProgress := nil;
    PreparationText := nil;
    PreparationImage := nil;
  end;
end;

procedure PositionInstallImage;
begin
  InstallImage.Left := (WizardForm.InstallingPage.Width - InstallImage.Width) div 2;
  InstallImage.Top := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(14);
end;

procedure StyleWizard;
var
  Rgn: LongWord;
begin
  WizardForm.Color := $00F8F9FC;
  WizardForm.Font.Name := 'Segoe UI';
  WizardForm.Font.Size := 10;
  WizardForm.InnerPage.Color := $00FFFFFF;
  WizardForm.OuterNotebook.Color := $00FFFFFF;
  WizardForm.NextButton.Width := ScaleX(110);
  WizardForm.BackButton.Width := ScaleX(110);
  WizardForm.CancelButton.Width := ScaleX(110);
  WizardForm.NextButton.Font.Style := [fsBold];
  WizardForm.ProgressGauge.Height := ScaleY(14);
  SendMessage(WizardForm.ProgressGauge.Handle, PBM_SETSTATE, PBST_NORMAL, 0);
  Rgn := CreateRoundRectRgn(0, 0, WizardForm.Width + 1, WizardForm.Height + 1,
    ScaleX(18), ScaleY(18));
  SetWindowRgn(WizardForm.Handle, Rgn, True);
end;

procedure InitializeWizard;
begin
  ShowPreparationWindow;
  StyleWizard;

  WizardForm.WelcomeLabel1.Caption := 'CV+ Compilatore Alunno';
  WizardForm.WelcomeLabel1.Font.Name := 'Segoe UI';
  WizardForm.WelcomeLabel1.Font.Size := 20;
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];
  WizardForm.WelcomeLabel1.Font.Color := $00603316;
  WizardForm.WelcomeLabel2.Caption :=
    'Installazione guidata del compilatore C++17 per gli studenti.' + #13#10#13#10 +
    '• Installazione senza privilegi di amministratore' + #13#10 +
    '• Compilatore e componenti inclusi' + #13#10 +
    '• Collegamento sul desktop' + #13#10#13#10 +
    '© Alessandro Barazzuol';

  WizardForm.LicenseLabel1.Caption :=
    'Leggi e accetta le condizioni d''uso, copyright e privacy.';
  WizardForm.LicenseLabel1.Font.Style := [fsBold];
  WizardForm.LicenseAcceptedRadio.Caption :=
    'Accetto integralmente le condizioni d''uso, copyright e privacy';
  WizardForm.LicenseAcceptedRadio.Font.Style := [fsBold];
  WizardForm.LicenseAcceptedRadio.Font.Color := clGreen;
  WizardForm.LicenseNotAcceptedRadio.Caption := 'Non accetto le condizioni d''uso';

  ExtractTemporaryFile('installing_a.bmp');
  InstallImage := TBitmapImage.Create(WizardForm);
  InstallImage.Parent := WizardForm.InstallingPage;
  InstallImage.Width := ScaleX(560);
  InstallImage.Height := ScaleY(270);
  InstallImage.Stretch := True;
  InstallImage.Center := True;
  InstallImage.Visible := False;
  InstallImage.Bitmap.LoadFromFile(ExpandConstant('{tmp}\installing_a.bmp'));
  PositionInstallImage;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
    HidePreparationWindow;

  if CurPageID = wpLicense then
    WizardForm.LicenseAcceptedRadio.Checked := True;

  if (CurPageID = wpSelectTasks) and (WizardForm.TasksList.Items.Count > 0) then
    WizardForm.TasksList.Checked[0] := True;

  InstallImage.Visible := CurPageID = wpInstalling;
  if InstallImage.Visible then PositionInstallImage;
end;

procedure DeinitializeSetup;
begin
  HidePreparationWindow;
end;
