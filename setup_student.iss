#define MyAppName "CV+ Compilatore Alunno"
#define MyAppVersion "1.5.0"
#define MyAppPublisher "Alessandro Barazzuol"
#define MyAppExeName "CppStudentClient.exe"

[Setup]
AppId={{7AE8F6E5-2DD3-4DF5-96F9-671869FBA148}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\CVPlusCompilatoreAlunno
DefaultGroupName={#MyAppName}
OutputDir=installer
OutputBaseFilename=CppStudentClient_Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti:"; Flags: unchecked

[Files]
; Applicazione: esclude il toolchain, che viene incluso esplicitamente sotto.
Source: "publish\*"; DestDir: "{app}"; Excludes: "compiler\*"; Flags: ignoreversion recursesubdirs createallsubdirs

; Toolchain GCC UCRT64: cartelle dichiarate separatamente per garantire l'inclusione reale.
Source: "publish\compiler\ucrt64\compiler_ready.marker"; DestDir: "{app}\compiler\ucrt64"; Flags: ignoreversion
Source: "publish\compiler\ucrt64\bin\*"; DestDir: "{app}\compiler\ucrt64\bin"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\compiler\ucrt64\include\*"; DestDir: "{app}\compiler\ucrt64\include"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\compiler\ucrt64\lib\*"; DestDir: "{app}\compiler\ucrt64\lib"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\compiler\ucrt64\libexec\*"; DestDir: "{app}\compiler\ucrt64\libexec"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\compiler\ucrt64\share\*"; DestDir: "{app}\compiler\ucrt64\share"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  GppPath: string;
  MarkerPath: string;
begin
  if CurStep = ssPostInstall then
  begin
    GppPath := ExpandConstant('{app}\compiler\ucrt64\bin\g++.exe');
    MarkerPath := ExpandConstant('{app}\compiler\ucrt64\compiler_ready.marker');

    if (not FileExists(GppPath)) or (not FileExists(MarkerPath)) then
    begin
      MsgBox('Installazione incompleta: GCC C++17 non è stato copiato. Il setup verrà annullato.', mbError, MB_OK);
      RaiseException('GCC C++17 assente dopo installazione');
    end;
  end;
end;
