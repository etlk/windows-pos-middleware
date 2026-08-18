; Cloud POS Middleware — Inno Setup installer
; Build:  ISCC.exe /DAppVersion=1.0.0 installer\CloudPOSMiddleware.iss
; Expects the published app in publish\win-x64\ (dotnet publish -r win-x64).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "Cloud POS Middleware"
#define AppExe "Cloud POS Middleware.exe"

[Setup]
AppId={{7D3C2A61-9B4E-4C43-A8A4-52A4D97E0C11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Cloud POS
DefaultDirName={localappdata}\{#AppName}
; Per-user install: no admin/UAC prompt, matches the HKCU autostart the app writes.
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputDir=Output
OutputBaseFilename=CloudPOS-Middleware-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Ask Windows to close a running instance (tray app) before replacing files.
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "autostart"; Description: "Start with Windows (minimized to tray)"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value name the app's own "Start with Windows" toggle uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "CloudPOSMiddleware"; \
  ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; \
  Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Remove the autostart entry even if it was enabled from inside the app.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CloudPOSMiddleware');
end;
