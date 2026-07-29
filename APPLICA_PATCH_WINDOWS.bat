@echo off
setlocal
set "HERE=%~dp0"
set /p "REPO=Incolla il percorso della cartella locale della repository: "
if not exist "%REPO%\.git" (
  echo La cartella scelta non contiene .git
  pause
  exit /b 1
)
if not exist "%REPO%\.github\workflows" mkdir "%REPO%\.github\workflows"
if not exist "%REPO%\macos\scripts" mkdir "%REPO%\macos\scripts"
if not exist "%REPO%\macos\input" mkdir "%REPO%\macos\input"
del /q "%REPO%\.github\workflows\build-macos-arm64.yml" 2>nul
del /q "%REPO%\.github\workflows\build-release.yml" 2>nul
copy /y "%HERE%PATCH_FILES\workflows\windows-auto.yml" "%REPO%\.github\workflows\windows-auto.yml"
copy /y "%HERE%PATCH_FILES\workflows\macos-auto.yml" "%REPO%\.github\workflows\macos-auto.yml"
copy /y "%HERE%PATCH_FILES\setup_student.iss" "%REPO%\setup_student.iss"
copy /y "%HERE%PATCH_FILES\macos\scripts\rebuild_signed_dmg.sh" "%REPO%\macos\scripts\rebuild_signed_dmg.sh"
echo Patch applicata. Ora esegui git add -A, commit e push.
pause
