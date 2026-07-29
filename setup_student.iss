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
  PreparationTimer: TTimer;
  PreparationDirection: Integer;
  InstallImage: TBitmapImage;

const
  PBM_SETMARQUEE = $040A;
  GWL_EXSTYLE = -20;
  WS_EX_LAYERED = $00080000;
  LWA_ALPHA = $00000002;

function SendMessage(hWnd: HWND; Msg: Cardinal; wParam: Longint; lParam: Longint): Longint;
  external 'SendMessageW@user32.dll stdcall';
function GetWindowLong(hWnd: HWND; nIndex: Integer): Longint;
  external 'GetWindowLongW@user32.dll stdcall';
function SetWindowLong(hWnd: HWND; nIndex: Integer; dwNewLong: Longint): Longint;
  external 'SetWindowLongW@user32.dll stdcall';
function SetLayeredWindowAttributes(hWnd: HWND; crKey: Cardinal; bAlpha: Byte; dwFlags: Cardinal): Boolean;
  external 'SetLayeredWindowAttributes@user32.dll stdcall';


procedure AnimatePreparation(Sender: TObject);
begin
  if PreparationProgress = nil then
    exit;

  PreparationProgress.Position :=
    PreparationProgress.Position + PreparationDirection;

  if PreparationProgress.Position >= 100 then
    PreparationDirection := -2
  else if PreparationProgress.Position <= 2 then
    PreparationDirection := 2;
end;

procedure ShowPreparationWindow;
var
  ExStyle: Longint;
begin
  if PreparationForm <> nil then
    exit;

  { Mostrata come prima operazione subito dopo la scelta della lingua. }
  PreparationForm := CreateCustomForm(ScaleX(350), ScaleY(62), False, False);
  PreparationForm.Position := poScreenCenter;
  PreparationForm.BorderStyle := bsNone;
  PreparationForm.Color := $00F4F4F4;
  PreparationForm.ClientWidth := ScaleX(350);
  PreparationForm.ClientHeight := ScaleY(62);

  { Leggera trasparenza dell'intera finestrella. }
  ExStyle := GetWindowLong(PreparationForm.Handle, GWL_EXSTYLE);
  SetWindowLong(PreparationForm.Handle, GWL_EXSTYLE, ExStyle or WS_EX_LAYERED);
  SetLayeredWindowAttributes(PreparationForm.Handle, 0, 238, LWA_ALPHA);

  PreparationText := TNewStaticText.Create(PreparationForm);
  PreparationText.Parent := PreparationForm;
  PreparationText.Left := ScaleX(10);
  PreparationText.Top := ScaleY(7);
  PreparationText.Width := ScaleX(330);
  PreparationText.Height := ScaleY(18);
  PreparationText.Caption := 'Attendere - preparazione dell''installazione...';
  PreparationText.Font.Name := 'Segoe UI';
  PreparationText.Font.Size := 9;
  PreparationText.Font.Style := [fsBold];
  PreparationText.Font.Color := $003C3C3C;

  PreparationProgress := TNewProgressBar.Create(PreparationForm);
  PreparationProgress.Parent := PreparationForm;
  PreparationProgress.Left := ScaleX(10);
  PreparationProgress.Top := ScaleY(31);
  PreparationProgress.Width := ScaleX(330);
  PreparationProgress.Height := ScaleY(18);
  PreparationProgress.Style := npbstNormal;
  PreparationProgress.Min := 0;
  PreparationProgress.Max := 100;
  PreparationProgress.Position := 2;
  PreparationDirection := 2;

  PreparationForm.Show;
  PreparationForm.BringToFront;
  PreparationForm.Update;

  { Timer dell'interfaccia: la barra avanza e torna indietro durante l'attesa. }
  PreparationTimer := TTimer.Create(PreparationForm);
  PreparationTimer.Interval := 35;
  PreparationTimer.OnTimer := @AnimatePreparation;
  PreparationTimer.Enabled := True;
end;

procedure HidePreparationWindow;
begin
  if PreparationForm <> nil then
  begin
    if PreparationTimer <> nil then
    begin
      PreparationTimer.Enabled := False;
      PreparationTimer.Free;
      PreparationTimer := nil;
    end;

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
