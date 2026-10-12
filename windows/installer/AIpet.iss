; Keep AppId and per-user mode stable: reinstallations replace the same product.
#ifndef AppVersion
  #define AppVersion "1.0.11"
#endif
[Setup]
AppId={{4F55BDF9-B918-4231-9C02-25C17F960CD2}
AppName=AIpet
AppVersion={#AppVersion}
AppPublisher=jinoisfree
AppPublisherURL=https://github.com/jinoisfree/AIpet
DefaultDirName={localappdata}\Programs\AIpet
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=AIpet-Setup-win-x64
SetupIconFile=..\src\AIpet.App\Assets\Generated\AIpet.ico
UninstallDisplayIcon={app}\AIpet.exe
UninstallDisplayName=AIpet
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
CloseApplicationsFilter=AIpet.exe
RestartApplications=no
SetupMutex=AIpet.Windows.Setup

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; ignoreversion also replaces preview builds whose assembly version is unchanged.
Source: "..\artifacts\win-x64\AIpet.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\AIpet"; Filename: "{app}\AIpet.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\AIpet"; Filename: "{app}\AIpet.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\AIpet.exe"; Description: "AIpet 실행"; Flags: nowait postinstall skipifsilent

; No wildcard deletion: user settings/logs live outside {app} and survive upgrades/uninstall.
