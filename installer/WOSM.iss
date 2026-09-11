; WOSM per-user installer (Inno Setup 6.3 or newer).
; The release workflow passes AppVersion, VersionInfoVersion, SourceDir, OutputDir and
; OutputBaseFilename on the ISCC command line; the defaults below target local builds
; (see installer/README.md for the expected layout).

#ifndef AppVersion
  #define AppVersion "dev"
#endif

#ifndef VersionInfoVersion
  #define VersionInfoVersion "0.0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\WOSM-win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "WOSM-Setup-" + AppVersion + "-win-x64"
#endif

[Setup]
AppId={{A5210189-852C-48BD-BE23-23CDA6117501}
AppName=WOSM
AppVerName=WOSM {#AppVersion}
AppPublisher=CAFEMDDSTEM
AppPublisherURL=https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor
AppSupportURL=https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/issues
AppUpdatesURL=https://github.com/CAFEMDDSTEM/WPF-OpenStreetmap-Editor/releases
DefaultDirName={autopf}\WOSM
DefaultGroupName=WOSM
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Per-user install: no administrator rights, no elevation prompt. Existing user data
; under %LOCALAPPDATA%\WPF-OpenStreetmap-Editor is intentionally left in place.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\WPF-OpenStreetmap-Editor.exe
VersionInfoCompany=CAFEMDDSTEM
VersionInfoDescription=WOSM (WPF OpenStreetMap Editor) installer
VersionInfoProductName=WOSM
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#VersionInfoVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "Languages\ChineseTraditional.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WOSM"; Filename: "{app}\WPF-OpenStreetmap-Editor.exe"
Name: "{autodesktop}\WOSM"; Filename: "{app}\WPF-OpenStreetmap-Editor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WPF-OpenStreetmap-Editor.exe"; Description: "{cm:LaunchProgram,WOSM}"; Flags: nowait postinstall skipifsilent
