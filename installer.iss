[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName=TodoSidebar
AppVersion=3.2.1
AppPublisher=TodoSidebar
AppPublisherURL=https://github.com/TodoSidebar
DefaultDirName={autopf}\TodoSidebar
DefaultGroupName=TodoSidebar
AllowNoIcons=yes
OutputDir=.\installer
OutputBaseFilename=TodoSidebar-Setup-v1.0.0
SetupIconFile=.\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=TodoSidebar
UninstallDisplayIcon={app}\app.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start with Windows"; GroupDescription: "Additional options:"

[Files]
Source: ".\publish\TodoSidebar.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TodoSidebar"; Filename: "{app}\TodoSidebar.exe"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram,TodoSidebar}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TodoSidebar"; Filename: "{app}\TodoSidebar.exe"; Tasks: desktopicon; IconFilename: "{app}\app.ico"
Name: "{userstartup}\TodoSidebar"; Filename: "{app}\TodoSidebar.exe"; Tasks: startupicon

[Run]
Filename: "{app}\TodoSidebar.exe"; Description: "{cm:LaunchProgram,TodoSidebar}"; Flags: nowait postinstall skipifsilent

[Registry]
; 开机自启动注册表项
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TodoSidebar"; ValueData: """{app}\TodoSidebar.exe"""; Tasks: startupicon; Flags: uninsdeletevalue
