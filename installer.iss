[Setup]
AppId={{B8F3A2E1-4C5D-6E7F-8A9B-0C1D2E3F4A5B}
AppName=A.I. Usage Tracker
AppVersion=2.15.0
AppVerName=A.I. Usage Tracker 2.15.0
AppPublisher=zitify
AppPublisherURL=https://zitify.co.kr
DefaultDirName={autopf}\AI_usage_tracker
DefaultGroupName=A.I. Usage Tracker
OutputDir=installer_output
OutputBaseFilename=AI_usage_tracker_Setup_v2.15.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=A.I. Usage Tracker
UninstallDisplayIcon={app}\AI_usage_tracker.exe
SetupIconFile=Assets\icon.ico
SetupLogging=yes
CloseApplications=yes
CloseApplicationsFilter=*AI_usage_tracker*
RestartApplications=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "taskbarpin"; Description: "작업 표시줄에 고정"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 옵션:"

[Files]
; publish 폴더 전체 (self-contained, 모든 .NET DLL + 네이티브 DLL 포함)
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\A.I. Usage Tracker"; Filename: "{app}\AI_usage_tracker.exe"
Name: "{group}\Uninstall A.I. Usage Tracker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\A.I. Usage Tracker"; Filename: "{app}\AI_usage_tracker.exe"; Tasks: desktopicon
Name: "{userstartup}\A.I. Usage Tracker"; Filename: "{app}\AI_usage_tracker.exe"; Tasks: startupicon

[Run]
Filename: "{app}\AI_usage_tracker.exe"; Description: "A.I. Usage Tracker 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{userappdata}\AI_usage_tracker"

[Code]
procedure PinToTaskbar;
var
  ResultCode: Integer;
  PsCmd: string;
begin
  PsCmd := 'powershell -ExecutionPolicy Bypass -Command "' +
    '$shell = New-Object -ComObject Shell.Application; ' +
    '$folder = $shell.Namespace(''' + ExpandConstant('{app}') + '''); ' +
    '$item = $folder.ParseName(''AI_usage_tracker.exe''); ' +
    '$verb = $item.Verbs() | Where-Object { $_.Name -match ''작업 표시줄에 고정|Pin to Tas'' }; ' +
    'if ($verb) { $verb.DoIt() }"';
  Exec('cmd.exe', '/c ' + PsCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('taskbarpin') then
    PinToTaskbar;
end;
