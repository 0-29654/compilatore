#define MyAppName "CV+ Compilatore Alunno"
#ifndef MyAppVersion
  #define MyAppVersion "1.9.20"
#endif
#define MyAppPublisher "Prof. Alessandro Barazzuol"
#define MyAppExeName "CppStudentClient.exe"

[Setup]
LicenseFile=CONDIZIONI_UTILIZZO_CVPLUS.rtf
AppId={{A6C18F0D-6CA6-4D34-9A45-4D3DA754D8C1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Editor e compilatore C++17 per l'invio degli esercizi al docente
VersionInfoCopyright=Copyright (C) Prof. Alessandro Barazzuol
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
Source: "Assets\update_gears\gears_00.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_01.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_02.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_03.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_04.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_05.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_06.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_07.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_08.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_09.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_10.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_11.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_12.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_13.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_14.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_15.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_16.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_17.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_18.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_19.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_20.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_21.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_22.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_23.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_24.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_25.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_26.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_27.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_28.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_29.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_30.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_31.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_32.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_33.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_34.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_35.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_36.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_37.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_38.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_39.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_40.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_41.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_42.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_43.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_44.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_45.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_46.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_47.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_48.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_49.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_50.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_51.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_52.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_53.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_54.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_55.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_56.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_57.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_58.bmp"; Flags: dontcopy
Source: "Assets\update_gears\gears_59.bmp"; Flags: dontcopy
Source: "INFORMATIVA_PRIVACY_CVPLUS.txt"; DestDir: "{app}\Documenti"; Flags: ignoreversion
Source: "INFORMATIVA_PRIVACY_CVPLUS.txt"; Flags: dontcopy
Source: "CONDIZIONI_UTILIZZO_CVPLUS.txt"; DestDir: "{app}\Documenti"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; Check: not IsUpdateMode
Name: "{autoprograms}\{#MyAppName} - Informativa privacy"; Filename: "{app}\Documenti\INFORMATIVA_PRIVACY_CVPLUS.txt"
Name: "{autoprograms}\{#MyAppName} - Condizioni di utilizzo"; Filename: "{app}\Documenti\CONDIZIONI_UTILIZZO_CVPLUS.txt"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  PreparationForm: TSetupForm;
  PreparationProgress: TNewProgressBar;
  PreparationText: TNewStaticText;
  InstallImage: TBitmapImage;
  PrivacyPage: TWizardPage;
  PrivacyMemo: TNewMemo;
  PrivacyCheck: TNewCheckBox;
  UpdateForm: TSetupForm;
  UpdateGearImage: TBitmapImage;
  UpdateProgress: TNewProgressBar;
  UpdateTitle: TNewStaticText;
  UpdateStatus: TNewStaticText;
  UpdateCopyright: TNewStaticText;
  UpdateTimerId: UINT_PTR;
  UpdateFrameIndex: Integer;

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
function SetTimer(hWnd: HWND; nIDEvent: UINT_PTR; uElapse: UINT; lpTimerFunc: NativeInt): UINT_PTR;
  external 'SetTimer@user32.dll stdcall';
function KillTimer(hWnd: HWND; uIDEvent: UINT_PTR): Boolean;
  external 'KillTimer@user32.dll stdcall';

function IsUpdateMode: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/UPDATE') = 0 then
    begin
      Result := True;
      exit;
    end;
  end;
end;

procedure LoadUpdateFrame(FrameNo: Integer);
var
  FrameName: String;
begin
  if UpdateGearImage = nil then
    exit;
  FrameName := Format('gears_%.2d.bmp', [FrameNo]);
  UpdateGearImage.Bitmap.LoadFromFile(ExpandConstant('{tmp}\') + FrameName);
end;

procedure UpdateAnimationTick(hWnd: HWND; uMsg: UINT; idEvent: UINT_PTR; dwTime: DWORD);
begin
  if (UpdateForm = nil) or (not UpdateForm.Visible) then
    exit;
  UpdateFrameIndex := (UpdateFrameIndex + 1) mod 60;
  LoadUpdateFrame(UpdateFrameIndex);
end;

procedure CreateUpdateWindow;
var
  I: Integer;
  FrameName: String;
begin
  if not IsUpdateMode then
    exit;

  { Estrae una sola volta i frame: durante l'installazione il timer li alterna
    senza rieseguire ExtractTemporaryFile e il movimento risulta più fluido. }
  for I := 0 to 59 do
  begin
    FrameName := Format('gears_%.2d.bmp', [I]);
    ExtractTemporaryFile(FrameName);
  end;

  UpdateForm := CreateCustomForm(ScaleX(690), ScaleY(520), False, False);
  UpdateForm.Caption := 'Update';
  UpdateForm.Position := poScreenCenter;
  UpdateForm.BorderStyle := bsSingle;
  UpdateForm.Color := clWhite;
  UpdateForm.ClientWidth := ScaleX(690);
  UpdateForm.ClientHeight := ScaleY(520);

  UpdateGearImage := TBitmapImage.Create(UpdateForm);
  UpdateGearImage.Parent := UpdateForm;
  UpdateGearImage.Left := ScaleX(95);
  UpdateGearImage.Top := ScaleY(25);
  UpdateGearImage.Width := ScaleX(500);
  UpdateGearImage.Height := ScaleY(220);
  UpdateGearImage.Stretch := True;
  LoadUpdateFrame(0);

  UpdateProgress := TNewProgressBar.Create(UpdateForm);
  UpdateProgress.Parent := UpdateForm;
  UpdateProgress.Left := ScaleX(70);
  UpdateProgress.Top := ScaleY(278);
  UpdateProgress.Width := ScaleX(550);
  UpdateProgress.Height := ScaleY(28);
  UpdateProgress.Min := 0;
  UpdateProgress.Max := 1000;
  UpdateProgress.Position := 0;

  UpdateTitle := TNewStaticText.Create(UpdateForm);
  UpdateTitle.Parent := UpdateForm;
  UpdateTitle.AutoSize := False;
  UpdateTitle.Left := ScaleX(70);
  UpdateTitle.Top := ScaleY(330);
  UpdateTitle.Width := ScaleX(550);
  UpdateTitle.Height := ScaleY(50);
  UpdateTitle.Alignment := taCenter;
  UpdateTitle.Caption := 'UPDATE';
  UpdateTitle.Font.Name := 'Segoe UI';
  UpdateTitle.Font.Size := 28;
  UpdateTitle.Font.Style := [fsBold];
  UpdateTitle.Font.Color := $002D2D2D;

  UpdateStatus := TNewStaticText.Create(UpdateForm);
  UpdateStatus.Parent := UpdateForm;
  UpdateStatus.AutoSize := False;
  UpdateStatus.Left := ScaleX(70);
  UpdateStatus.Top := ScaleY(392);
  UpdateStatus.Width := ScaleX(550);
  UpdateStatus.Height := ScaleY(25);
  UpdateStatus.Alignment := taCenter;
  UpdateStatus.Caption := 'Preparazione aggiornamento...';
  UpdateStatus.Font.Name := 'Segoe UI';
  UpdateStatus.Font.Size := 10;
  UpdateStatus.Font.Color := $00585858;

  UpdateCopyright := TNewStaticText.Create(UpdateForm);
  UpdateCopyright.Parent := UpdateForm;
  UpdateCopyright.AutoSize := False;
  UpdateCopyright.Left := ScaleX(70);
  UpdateCopyright.Top := ScaleY(452);
  UpdateCopyright.Width := ScaleX(550);
  UpdateCopyright.Height := ScaleY(32);
  UpdateCopyright.Alignment := taCenter;
  UpdateCopyright.Caption := '© Alessandro Barazzuol';
  UpdateCopyright.Font.Name := 'Segoe UI';
  UpdateCopyright.Font.Size := 13;
  UpdateCopyright.Font.Style := [fsBold];
  UpdateCopyright.Font.Color := $002D2D2D;

  UpdateFrameIndex := 0;
  UpdateTimerId := 0;
  UpdateTimerId := SetTimer(0, 0, 30, CreateCallback(@UpdateAnimationTick));
end;

procedure ShowUpdateWindow;
begin
  if UpdateForm = nil then
    CreateUpdateWindow;
  if UpdateForm <> nil then
  begin
    UpdateForm.Show;
    UpdateForm.BringToFront;
    UpdateForm.Update;
  end;
end;

procedure HideUpdateWindow;
begin
  if UpdateTimerId <> 0 then
  begin
    KillTimer(0, UpdateTimerId);
    UpdateTimerId := 0;
  end;
  if UpdateForm <> nil then
  begin
    UpdateForm.Hide;
    UpdateForm.Free;
    UpdateForm := nil;
    UpdateGearImage := nil;
    UpdateProgress := nil;
    UpdateTitle := nil;
    UpdateStatus := nil;
    UpdateCopyright := nil;
  end;
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
  if IsUpdateMode then
    PreparationText.Caption := 'Attendi - prepara aggiornamento...'
  else
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
  PreparationProgress.Style := npbstMarquee;

  PreparationForm.Show;
  PreparationForm.BringToFront;
  PreparationForm.Update;

  { Avvia esplicitamente il movimento avanti/indietro della barra. }
  SendMessage(PreparationProgress.Handle, PBM_SETMARQUEE, 1, 24);
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

procedure CreatePrivacyPage;
begin
  PrivacyPage := CreateCustomPage(
    wpWelcome,
    'Informativa sulla privacy',
    'Leggi l''informativa e conferma di averne preso visione.'
  );

  PrivacyMemo := TNewMemo.Create(PrivacyPage);
  PrivacyMemo.Parent := PrivacyPage.Surface;
  PrivacyMemo.Left := 0;
  PrivacyMemo.Top := 0;
  PrivacyMemo.Width := PrivacyPage.SurfaceWidth;
  PrivacyMemo.Height := PrivacyPage.SurfaceHeight - ScaleY(42);
  PrivacyMemo.ReadOnly := True;
  PrivacyMemo.ScrollBars := ssVertical;
  PrivacyMemo.WordWrap := True;
  PrivacyMemo.Font.Name := 'Segoe UI';
  PrivacyMemo.Font.Size := 9;
  ExtractTemporaryFile('INFORMATIVA_PRIVACY_CVPLUS.txt');
  PrivacyMemo.Lines.LoadFromFile(
    ExpandConstant('{tmp}\INFORMATIVA_PRIVACY_CVPLUS.txt')
  );

  PrivacyCheck := TNewCheckBox.Create(PrivacyPage);
  PrivacyCheck.Parent := PrivacyPage.Surface;
  PrivacyCheck.Left := 0;
  PrivacyCheck.Top := PrivacyPage.SurfaceHeight - ScaleY(30);
  PrivacyCheck.Width := PrivacyPage.SurfaceWidth;
  PrivacyCheck.Height := ScaleY(24);
  PrivacyCheck.Caption := 'Ho letto l''Informativa sulla Privacy';
  PrivacyCheck.Font.Name := 'Segoe UI';
  PrivacyCheck.Font.Size := 9;
  PrivacyCheck.Font.Style := [fsBold];
  PrivacyCheck.Checked := WizardSilent;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  { In modalità UPDATE l'installazione è automatica: nessuna pagina standard
    (lingua viene già fissata da /LANG=italian nel programma chiamante). }
  if IsUpdateMode then
  begin
    if PageID <> wpInstalling then
      Result := True;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (PrivacyPage <> nil) and (CurPageID = PrivacyPage.ID) and
     (not WizardSilent) and (not PrivacyCheck.Checked) then
  begin
    MsgBox(
      'Per proseguire devi confermare di aver letto l''Informativa sulla Privacy.',
      mbInformation,
      MB_OK
    );
    Result := False;
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
  if IsUpdateMode then
    CreateUpdateWindow
  else
  begin
    { Installazione normale invariata. }
    ShowPreparationWindow;
    CreatePrivacyPage;
  end;

  { Nelle installazioni automatiche di GitHub Actions non esiste interazione utente. }
  if WizardSilent and (not IsUpdateMode) then
  begin
    PrivacyCheck.Checked := True;
    WizardForm.LicenseAcceptedRadio.Checked := True;
  end;

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
    'Leggi attentamente le Condizioni di utilizzo.';
  WizardForm.LicenseLabel1.Font.Color := clNavy;
  WizardForm.LicenseLabel1.Font.Style := [fsBold];

  WizardForm.LicenseAcceptedRadio.Caption :=
    'Accetto le Condizioni di utilizzo';
  WizardForm.LicenseAcceptedRadio.Font.Style := [fsBold];
  WizardForm.LicenseAcceptedRadio.Font.Color := clGreen;

  WizardForm.LicenseNotAcceptedRadio.Caption :=
    'Non accetto le Condizioni di utilizzo';

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
  if IsUpdateMode then
  begin
    if CurPageID = wpInstalling then
      ShowUpdateWindow;
    exit;
  end;

  if ((PrivacyPage <> nil) and (CurPageID = PrivacyPage.ID)) or
     (CurPageID = wpLicense) then
    HidePreparationWindow;

  if (CurPageID = wpLicense) and WizardSilent then
    WizardForm.LicenseAcceptedRadio.Checked := True;

  if (CurPageID = wpSelectTasks) and
     (WizardForm.TasksList.Items.Count > 0) then
    WizardForm.TasksList.Checked[0] := True;

  InstallImage.Visible := CurPageID = wpInstalling;

  if InstallImage.Visible then
    PositionInstallImage;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  P: Integer;
begin
  if IsUpdateMode and (UpdateProgress <> nil) and (MaxProgress > 0) then
  begin
    P := (CurProgress * 1000) div MaxProgress;
    if P < 0 then P := 0;
    if P > 1000 then P := 1000;
    UpdateProgress.Position := P;

    if P < 120 then
      UpdateStatus.Caption := 'Preparazione aggiornamento...'
    else if P < 900 then
      UpdateStatus.Caption := 'Installazione aggiornamento...'
    else
      UpdateStatus.Caption := 'Completamento aggiornamento...';

    UpdateForm.Update;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if IsUpdateMode then
  begin
    if CurStep = ssInstall then
      ShowUpdateWindow
    else if CurStep = ssPostInstall then
    begin
      if UpdateProgress <> nil then
        UpdateProgress.Position := 1000;
      if UpdateStatus <> nil then
        UpdateStatus.Caption := 'Aggiornamento completato';
      if UpdateForm <> nil then
        UpdateForm.Update;
      Sleep(500);
      HideUpdateWindow;

      { Riapre automaticamente il programma aggiornato. }
      ShellExec('', ExpandConstant('{app}\{#MyAppExeName}'), '', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  end;
end;

procedure DeinitializeSetup;
begin
  HidePreparationWindow;
  HideUpdateWindow;
end;
