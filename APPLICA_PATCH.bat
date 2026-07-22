@echo off
setlocal
echo.
echo PATCH COMPLETA CV+ COMPILATORE ALUNNO 1.6.5
echo.
set /p REPO=Incolla il percorso completo della repository compilatore: 
echo.
py "%~dp0APPLICA_PATCH_COMPLETA_1_6_5.py" "%REPO%"
echo.
pause
