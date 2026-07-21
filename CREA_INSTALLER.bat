@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo CV+ Compilatore Alunno - creazione installer locale
echo ============================================================

echo.
echo Pubblicazione applicazione...
dotnet publish CppStudentClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
if errorlevel 1 exit /b 1

if not exist "publish\compiler\ucrt64\bin\g++.exe" (
  echo.
  echo ERRORE: il compilatore C++17 incorporato non e' presente.
  echo La Release completa deve essere generata dal workflow GitHub Actions,
  echo che prepara automaticamente GCC UCRT64 e lo copia dentro l'installer.
  echo.
  echo Percorso atteso:
  echo publish\compiler\ucrt64\bin\g++.exe
  pause
  exit /b 1
)

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo Installa Inno Setup 6.
  pause
  exit /b 1
)

"%ISCC%" setup_student.iss
if errorlevel 1 exit /b 1
pause
