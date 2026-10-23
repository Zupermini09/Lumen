; Inno Setup script for the Windows installer.
; Build: iscc /DAppVersion=1.2.3 /DPublishDir=<publish folder> /DOutputDir=<out folder> packaging\windows\lumen.iss
; Per-user install (no admin rights): installs to %LOCALAPPDATA%\Programs\Lumen.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #error PublishDir must be defined
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Never change AppId: Windows uses it to recognise upgrades and the uninstaller.
AppId={{7C89FE33-8E4B-40C9-A407-0D23E0056C63}
AppName=Lumen
AppVersion={#AppVersion}
AppVerName=Lumen {#AppVersion}
AppPublisher=Lumen contributors
AppPublisherURL=https://github.com/Zupermini09/Lumen
AppSupportURL=https://github.com/Zupermini09/Lumen/issues
AppComments=A desktop client for Jellyfin
DefaultDirName={localappdata}\Programs\Lumen
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Lumen-{#AppVersion}-win-x64-setup
SetupIconFile=..\assets\lumen.ico
UninstallDisplayIcon={app}\Lumen.exe
UninstallDisplayName=Lumen
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Remove the previous version's libraries so stale DLLs and VLC plugins never mix with new ones.
; Only Lumen's own file types/folders: the user may have picked a folder that contains other files.
Type: filesandordirs; Name: "{app}\libvlc"
Type: files; Name: "{app}\*.dll"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Lumen"; Filename: "{app}\Lumen.exe"
Name: "{autodesktop}\Lumen"; Filename: "{app}\Lumen.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Lumen.exe"; Description: "{cm:LaunchProgram,Lumen}"; Flags: nowait postinstall skipifsilent
