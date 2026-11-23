@echo off
rem Double-click this file: it builds everything and puts the results in the "dist" folder.
rem   dist\RdpPanel.exe                  one single exe (portable, nothing to install)
rem   dist\RdpPanel-x.y.z.zip            the same exe in a zip
rem   dist\RdpPanel-Setup-x.y.z.exe      installer (only if Inno Setup is installed)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
set RC=%ERRORLEVEL%
echo.
if %RC% NEQ 0 (echo The build FAILED. Read the red text above.) else (echo Done. Opening the dist folder...)
if %RC% EQU 0 start "" "%~dp0dist"
pause
exit /b %RC%
