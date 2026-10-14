#define AppName "VoicePTT"
#define AppVersion "2.1.0"
#define AppExeName "VoicePTT.exe"

[Setup]
AppId={{7E83A932-79D2-4C5A-843A-E6CB6CC88E61}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=VoicePTT
AppMutex=Local\VoicePTT-7E83A932-79D2-4C5A-843A-E6CB6CC88E61
DefaultDirName={autopf}\VoicePTT
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=VoicePTT-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "publish\VoicePTT.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "docs\VoicePTT-Quick-Start.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "docs\VoicePTT-Third-Party-Notices.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\VoicePTT"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\VoicePTT"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch VoicePTT"; Flags: postinstall nowait skipifsilent
