@echo off
setlocal
cd /d "%~dp0"
dotnet publish CppStudentClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
if errorlevel 1 exit /b 1
set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" echo Installa Inno Setup 6.& pause & exit /b 1
"%ISCC%" setup_student.iss
pause
