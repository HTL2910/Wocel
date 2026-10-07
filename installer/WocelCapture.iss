#define AppName "Wocel Capture"
#define AppVersion "1.0.0"
#define Publisher "Wocel"
#define PublishDir "..\dist\wocel-capture\publish"

[Setup]
AppId={{A8D99BEE-4513-4C0E-B012-3CC32E5F073A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={localappdata}\Programs\Wocel Capture
DefaultGroupName=Wocel Capture
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist\wocel-capture
OutputBaseFilename=WocelCaptureSetup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Wocel Capture.exe
CloseApplications=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Wocel Capture"; Filename: "{app}\Wocel Capture.exe"
Name: "{userdesktop}\Wocel Capture"; Filename: "{app}\Wocel Capture.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng trên Desktop"; GroupDescription: "Biểu tượng bổ sung:"; Flags: unchecked

[Run]
Filename: "{app}\Wocel Capture.exe"; Description: "Mở Wocel Capture"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "WocelCapture"; Flags: uninsdeletevalue dontcreatekey
