#define MyAppName "CV+ Compilatore Alunno"
#define MyAppVersion "1.4.7"
#define MyAppPublisher "Alessandro Barazzuol"
#define MyAppExeName "CppStudentClient.exe"

[Setup]
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
WizardSizePercent=110
SetupIconFile=Assets\app.ico
WizardImageFile=Assets\wizard.bmp
WizardSmallImageFile=Assets\wizard_small.bmp
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
; Il workflow copia prima applicazione e toolchain GCC dentro publish.
; Inno Setup installa quindi un unico albero completo, evitando percorsi separati mancanti.
Source: "publish\*"; DestDir: "{app}"; Excludes: "compiler\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\compiler\ucrt64\*"; DestDir: "{app}\compiler\ucrt64"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Caption := 'Benvenuto in CV+ Compilatore Alunno';
  WizardForm.WelcomeLabel2.Caption := 'Il setup installerà automaticamente anche il compilatore GCC C++17 nel profilo dell''utente. Non servono MSYS2, Dev-C++, configurazioni o diritti di amministratore.' + #13#10 + #13#10 + '© Alessandro Barazzuol';
  WizardForm.FinishedHeadingLabel.Caption := 'Installazione completata';
  WizardForm.FinishedLabel.Caption := 'CV+ e il compilatore C++17 sono stati installati. Lo studente non deve scegliere alcun g++.exe.';
end;

function CompilerIncluded(): Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\compiler\ucrt64\bin\g++.exe')) and
            DirExists(ExpandConstant('{app}\compiler\ucrt64\include')) and
            DirExists(ExpandConstant('{app}\compiler\ucrt64\lib'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not CompilerIncluded()) then
    RaiseException('Installazione incompleta: il compilatore GCC C++17 non è stato copiato.');
end;
