#define MyAppName "CV+ Compilatore Alunno"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Alessandro Barazzol"
#define MyAppExeName "CppStudentClient.exe"

[Setup]
AppId={{A6C18F0D-6CA6-4D34-9A45-4D3DA754D8C1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Editor e compilatore C++ per l'invio degli esercizi al docente
VersionInfoCopyright=Copyright (C) Alessandro Barazzol
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
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Caption := 'Benvenuto in CV+ Compilatore Alunno';
  WizardForm.WelcomeLabel2.Caption := 'Installa l''editor C++ leggero per compilare gli esercizi e inviarli al docente nella rete del laboratorio.' + #13#10 + #13#10 + '© Alessandro Barazzol';
  WizardForm.FinishedHeadingLabel.Caption := 'Installazione completata';
  WizardForm.FinishedLabel.Caption := 'CV+ Compilatore Alunno è pronto. Puoi avviarlo e configurare IP, porta e codice sessione comunicati dal docente.';
end;
