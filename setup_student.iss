#define MyAppName "CV+ Compilatore Alunno"
#define MyAppVersion "1.8.5"
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
Source: "Assets\cpp_animated.gif"; DestDir: "{app}\Assets"; Flags: ignoreversion
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
const
  PromoFrameCount = 12;

var
  PromoImage: TBitmapImage;
  PromoFrame: Integer;
  PromoProgressTick: Integer;

function PromoFrameName(Index: Integer): String;
begin
  if Index < 10 then
    Result := 'cpp_anim_0' + IntToStr(Index) + '.bmp'
  else
    Result := 'cpp_anim_' + IntToStr(Index) + '.bmp';
end;

procedure LoadPromoFrame;
begin
  try
    PromoImage.Bitmap.LoadFromFile(
      ExpandConstant('{tmp}\') + PromoFrameName(PromoFrame)
    );
  except
    { Se un singolo fotogramma non è disponibile, l'installazione continua. }
  end;
end;

procedure PositionPromoOnInstallingPage;
var
  AvailableWidth: Integer;
begin
  AvailableWidth := WizardForm.InstallingPage.Surface.Width;

  PromoImage.Left :=
    (AvailableWidth - PromoImage.Width) div 2;

  PromoImage.Top :=
    WizardForm.ProgressGauge.Top +
    WizardForm.ProgressGauge.Height +
    ScaleY(22);
end;

procedure UpdatePromoVisibility(CurPageID: Integer);
begin
  PromoImage.Visible := CurPageID = wpInstalling;

  if PromoImage.Visible then
  begin
    PositionPromoOnInstallingPage;
    LoadPromoFrame;
  end;
end;

procedure InitializeWizard;
var
  I: Integer;
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

  for I := 0 to PromoFrameCount - 1 do
    ExtractTemporaryFile(PromoFrameName(I));

  PromoImage := TBitmapImage.Create(WizardForm);
  PromoImage.Parent := WizardForm.InstallingPage.Surface;
  PromoImage.Width := ScaleX(390);
  PromoImage.Height := ScaleY(165);
  PromoImage.Stretch := True;
  PromoImage.Center := True;
  PromoImage.Visible := False;

  PromoFrame := 0;
  PromoProgressTick := 0;
  PositionPromoOnInstallingPage;
  LoadPromoFrame;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpLicense then
    WizardForm.LicenseAcceptedRadio.Checked := True;

  if (CurPageID = wpSelectTasks) and
     (WizardForm.TasksList.Items.Count > 0) then
    WizardForm.TasksList.Checked[0] := True;

  UpdatePromoVisibility(CurPageID);
end;

procedure CurInstallProgressChanged(
  CurProgress, MaxProgress: Integer);
begin
  if not PromoImage.Visible then
    Exit;

  PromoProgressTick := PromoProgressTick + 1;

  if (PromoProgressTick mod 2) = 0 then
  begin
    PromoFrame := (PromoFrame + 1) mod PromoFrameCount;
    LoadPromoFrame;
  end;
end;
