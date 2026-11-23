; Inno Setup script. The workflow passes the version: ISCC /DAppVersion=1.0.0 installer\installer.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{E37877C6-3DCD-48AE-A178-B1A84C289B18}
AppName=RDP Panel
AppVersion={#AppVersion}
AppPublisher=smotserela
DefaultDirName={autopf}\RDP Panel
DefaultGroupName=RDP Panel
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=RdpPanel-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\RdpPanel.exe

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "autostart"; Description: "Start RDP Panel automatically with Windows"; Flags: unchecked

[Files]
Source: "..\bin\Release\net48\RdpPanel.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net48\RdpPanel.exe.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\RDP Panel"; Filename: "{app}\RdpPanel.exe"
Name: "{group}\Uninstall RDP Panel"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RDP Panel"; Filename: "{app}\RdpPanel.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RdpPanel"; ValueData: """{app}\RdpPanel.exe"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\RdpPanel.exe"; Description: "Start RDP Panel"; Flags: nowait postinstall skipifsilent
