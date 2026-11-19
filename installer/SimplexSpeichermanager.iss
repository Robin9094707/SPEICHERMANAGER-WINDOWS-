; Inno-Setup-Skript für Simplex Speichermanager
; Aufruf (CI): iscc /DAppVersion=1.1.0 /DArch=x64 /DSourceDir=..\publish\SimplexSpeichermanager installer\SimplexSpeichermanager.iss
; Gleiche AppId wie zuvor → Updates werden einfach „drüber“ installiert, Einstellungen bleiben erhalten.

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\SimplexSpeichermanager"
#endif

[Setup]
AppId={{6F3C2B1E-5A7D-4E8F-9C21-3B5D7E9A1C42}
AppName=Simplex Speichermanager
AppVersion={#AppVersion}
AppVerName=Simplex Speichermanager {#AppVersion}
AppPublisher=Simplex
DefaultDirName={autopf}\Simplex Speichermanager
DefaultGroupName=Simplex Speichermanager
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\SimplexSpeichermanager.exe
UninstallDisplayName=Simplex Speichermanager
OutputDir=..\setup
OutputBaseFilename=SimplexSpeichermanager-Setup-{#AppVersion}-{#Arch}
SetupIconFile=..\src\SimplexSpeichermanager\Assets\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.17763
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Alte Programmdateien entfernen, damit keine veralteten DLLs zurückbleiben (Einstellungen liegen in %LOCALAPPDATA%)
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Simplex Speichermanager"; Filename: "{app}\SimplexSpeichermanager.exe"
Name: "{autodesktop}\Simplex Speichermanager"; Filename: "{app}\SimplexSpeichermanager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SimplexSpeichermanager.exe"; Description: "{cm:LaunchProgram,Simplex Speichermanager}"; Flags: nowait postinstall skipifsilent
