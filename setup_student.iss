#define MyAppName "CV+ Compilatore Alunno"
#define MyAppVersion "1.5.2"
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
; UNICA sorgente: tutto ciò che è in publish, compresa publish\compiler\ucrt64, viene installato.
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Caption := 'Benvenuto in CV+ Compilatore Alunno';
  WizardForm.WelcomeLabel2.Caption := 'Il compilatore GCC C++17 è già incluso nel setup. Non servono MSYS2, Dev-C++, configurazioni o diritti di amministratore.' + #13#10 + #13#10 + '© Alessandro Barazzuol';
end;
