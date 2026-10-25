#ifndef AppVersion
  #define AppVersion "2.2.0"
#endif
#ifndef PackageAppId
  #define PackageAppId "SchoolSchedulerWidget"
#endif

[Setup]
AppId={#PackageAppId}
AppName=학사일정 스케줄러
AppVersion={#AppVersion}
AppVerName=학사일정 스케줄러 {#AppVersion}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=학사일정 스케줄러
DefaultDirName={localappdata}\Programs\SchoolScheduler
DefaultGroupName=학사일정 스케줄러
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
WizardStyle=modern
OutputDir=..\..\dist
OutputBaseFilename=SchoolScheduler-{#AppVersion}-x64-Setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\bin\SchoolWidget.exe
UninstallDisplayName=학사일정 스케줄러
CloseApplications=no
RestartApplications=no
SetupLogging=yes
InfoAfterFile=사용안내.txt

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로 가기 만들기"; Flags: unchecked
Name: "startup"; Description: "Windows 로그인 시 자동 실행"; Flags: unchecked

[Files]
Source: "..\bin\SchoolWidget.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "..\bin\SchoolDockHook.dll"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "사용안내.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\MigrateWebData.exe"; Flags: dontcopy

[InstallDelete]
Type: files; Name: "{app}\bin\WebView2Loader.dll"
Type: files; Name: "{app}\bin\Microsoft.Web.WebView2.Core.dll"
Type: files; Name: "{app}\bin\Microsoft.Web.WebView2.WinForms.dll"
Type: files; Name: "{app}\학사일정-스케줄러.html"
Type: files; Name: "{app}\licenses\WebView2-LICENSE.txt"
Type: files; Name: "{app}\licenses\WebView2-NOTICE.txt"

[Icons]
Name: "{group}\학사일정 스케줄러"; Filename: "{app}\bin\SchoolWidget.exe"; WorkingDir: "{app}"
Name: "{group}\사용 안내"; Filename: "{app}\사용안내.txt"
Name: "{group}\학사일정 스케줄러 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\학사일정 스케줄러"; Filename: "{app}\bin\SchoolWidget.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SchoolSchedulerWidget"; ValueData: """{app}\bin\SchoolWidget.exe"""; Tasks: startup

[Run]
Filename: "{app}\bin\SchoolWidget.exe"; Description: "학사일정 스케줄러 실행"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
const
  AppMutexName = 'SchoolSchedulerWidget.single';
  QuitEventName = 'Local\SchoolSchedulerWidget.Quit';

function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function DotNetInstalled: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32,
    'Software\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and
    (Release >= 461808);
end;

function StopWidget: Boolean;
var
  QuitEvent: THandle;
  Attempt: Integer;
begin
  Result := True;
  if not CheckForMutexes(AppMutexName) then Exit;
  Log('Requesting graceful widget shutdown before changing installed files.');
  QuitEvent := OpenEvent($0002, False, QuitEventName);
  if QuitEvent <> 0 then begin
    SetEvent(QuitEvent);
    CloseHandle(QuitEvent);
  end;
  for Attempt := 1 to 100 do begin
    if not CheckForMutexes(AppMutexName) then begin
      Sleep(300); { Let Explorer finish releasing the hook DLL. }
      Exit;
    end;
    Sleep(100);
  end;
  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if not DotNetInstalled then begin
    Result := '.NET Framework 4.7.2 이상이 필요합니다. Windows 업데이트 후 다시 설치하세요.';
    Exit;
  end;
  if not StopWidget then begin
    Result := '실행 중인 위젯을 종료하지 못했습니다. 트레이 메뉴에서 종료한 뒤 다시 시도하세요.';
    Exit;
  end;
  if FileExists(ExpandConstant('{app}\bin\Microsoft.Web.WebView2.Core.dll')) and
    FileExists(ExpandConstant('{app}\학사일정-스케줄러.html')) and
    DirExists(ExpandConstant('{localappdata}\SchoolScheduler\WebView2')) then begin
    Log('Migrating existing browser data before removing legacy runtime files.');
    ExtractTemporaryFile('MigrateWebData.exe');
    if not Exec(ExpandConstant('{tmp}\MigrateWebData.exe'),
      '"' + ExpandConstant('{app}') + '" "' + ExpandConstant('{localappdata}\SchoolScheduler\WebView2') +
      '" "' + ExpandConstant('{localappdata}\SchoolScheduler\calendar-data.json') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
      Result := '이전 데이터 변환 도구를 실행하지 못했습니다. 기존 데이터는 보존됩니다.';
      Exit;
    end;
    if ExitCode <> 0 then begin
      Result := '이전 데이터를 변환하지 못했습니다. 기존 데이터는 보존됩니다. 이전 버전에서 백업을 저장한 뒤 다시 시도하세요.';
      Exit;
    end;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := StopWidget;
  if not Result then
    MsgBox('위젯의 트레이 메뉴에서 종료한 뒤 제거를 다시 실행하세요.', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'SchoolSchedulerWidget', StartupCommand) and
      (CompareText(StartupCommand, '"' + ExpandConstant('{app}\bin\SchoolWidget.exe') + '"') = 0) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SchoolSchedulerWidget');
  end;
  { Native JSON data, settings and any legacy browser profile are retained. }
end;
