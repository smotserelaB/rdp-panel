@echo off
rem Run this file AS ADMINISTRATOR on every PC whose CPU/RAM you want to read, or that you
rem want to restart or shut down from the panel (right-click, Run as administrator).
net session >nul 2>&1
if errorlevel 1 (
  echo Run this file as administrator.
  pause
  exit /b 1
)

echo Allowing WMI through the firewall...
powershell -NoProfile -Command "Enable-NetFirewallRule -Name 'WMI-RPCSS-In-TCP','WMI-WINMGMT-In-TCP','WMI-ASYNC-In-TCP' -ErrorAction SilentlyContinue; Set-Service winmgmt -StartupType Automatic; Start-Service winmgmt -ErrorAction SilentlyContinue"

echo Allowing remote administration with a local account...
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f

echo.
echo Done. In the panel, use an administrator account of this PC (with its password).
pause
