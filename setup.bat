@echo off
rem Creates the two RDP interop DLLs (MSTSCLib.dll and AxMSTSCLib.dll) in the lib\ folder
setlocal
set AX=
for /f "delims=" %%i in ('dir /b /s "%ProgramFiles(x86)%\Microsoft SDKs\Windows\aximp.exe" 2^>nul') do set AX=%%i

if "%AX%"=="" (
  echo aximp.exe not found.
  echo Install Visual Studio Community or "Build Tools for Visual Studio" with the
  echo components ".NET Framework 4.8 SDK" and "Windows SDK", then run this script again.
  pause
  exit /b 1
)

if not exist lib mkdir lib
pushd lib
"%AX%" "%windir%\system32\mstscax.dll"
popd

if exist lib\AxMSTSCLib.dll (
  echo Done. You can now build with: dotnet build -c Release
) else (
  echo Creating the DLLs failed.
)
pause
