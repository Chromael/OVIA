#include "ovia_version_build.iss"
#define MyAppName "OVIA"
#define MyAppVersion OviaVersion
#define MyAppPublisher "CELMON"
#define MyAppExeName "OVIA.Desktop.exe"
#define SourceRoot ".."
#define ReleaseDir SourceRoot + "\OVIA.Desktop\bin\x64\Release"
#define AutoCad2024ReleaseDir SourceRoot + "\OVIA.AutoCAD.2024\bin\x64\Release\net48"
#define AutoCad2025ReleaseDir SourceRoot + "\OVIA.AutoCAD.2025\bin\x64\Release\net8.0-windows"
#define AutoCad2026ReleaseDir SourceRoot + "\OVIA.AutoCAD.2026\bin\x64\Release\net8.0-windows"
#define AutoCad2027ReleaseDir SourceRoot + "\OVIA.AutoCAD.2027\bin\x64\Release\net10.0-windows"

[Setup]
; OVIA_CODE_SIGNING_POLICY_20260908
; 정식 배포는 Inno Setup Compile 후 Installer\Sign-OVIA-Setup.ps1로
; OVIA_Setup_{버전}.exe에 Authenticode SHA-256 + RFC3161 타임스탬프를 적용합니다.
; 인증서가 없는 Debug/개발 환경에서 Compile 자체가 실패하지 않도록 서명은 후처리합니다.
VersionInfoDescription=OVIA Setup
AppId={{B22D4E7E-9D42-49B9-8F05-6E31D8262D36}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
UninstallDisplayName={#MyAppName}
AppPublisher=CELMON
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}
VersionInfoProductName=OVIA
VersionInfoCompany=CELMON
DefaultDirName={autopf}\OVIA
DefaultGroupName=OVIA
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=OVIA_Setup_{#MyAppVersion}
SetupIconFile={#SourceRoot}\OVIA.Desktop\Assets\Icons\ovia_symbol.ico
UninstallDisplayIcon={app}\Assets\Icons\ovia_symbol.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
; 설치 시작 전 별도 WMI 프로세스 조회를 하지 않습니다.
; 설치 대상 파일 사용 여부는 Inno Setup Restart Manager가 처리합니다.
; OVIA 실행 감지는 AppMutex, 설치 프로그램 중복 실행은 SetupMutex가 담당합니다.
SetupLogging=yes
SetupMutex=Global\OVIA.Setup.SingleInstance
AppMutex=Local\OVIA.Desktop.SingleInstance
UsePreviousAppDir=yes
UsePreviousGroup=yes
ChangesAssociations=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 OVIA 바로가기 만들기"; GroupDescription: "추가 바로가기:"; Flags: unchecked

[Dirs]
Name: "{app}\Assets\Icons"
Name: "{app}\Assets\Fonts"
Name: "{app}\Data\Mapping"
Name: "{app}\Data\Rebar"
Name: "{app}\Data\Shapes\source_jpg"
Name: "{app}\Data\Version"

; AutoCAD 플러그인 자동 로더 번들. AutoCAD가 아직 없어도 먼저 설치해 둡니다.
; Autodesk 권장 일반 설치 위치 %PROGRAMFILES%\Autodesk\ApplicationPlugins를 사용합니다.
; 이후 AutoCAD 2024/2025/2026/2027 중 어느 버전을 설치/실행해도 해당 Component가 자동 선택됩니다.
Name: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle"; Flags: uninsalwaysuninstall
Name: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents"; Flags: uninsalwaysuninstall
Name: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents\2024"; Flags: uninsalwaysuninstall
Name: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents\2025"; Flags: uninsalwaysuninstall
Name: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents\2026"; Flags: uninsalwaysuninstall
Name: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents\2027"; Flags: uninsalwaysuninstall

[Files]
; Release 빌드의 실행 파일과 런타임 DLL만 포함합니다.
Source: "{#ReleaseDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\{#MyAppExeName}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: DirExists(ExpandConstant('{#ReleaseDir}\runtimes'))

; 설치 및 프로그램 아이콘
Source: "{#SourceRoot}\OVIA.Desktop\Assets\Icons\ovia_symbol.ico"; DestDir: "{app}\Assets\Icons"; Flags: ignoreversion
Source: "{#SourceRoot}\OVIA.Desktop\Assets\Icons\ovia_symbol.png"; DestDir: "{app}\Assets\Icons"; Flags: ignoreversion

; 로그인 화면 기본 브랜드 로고. 회사 로고가 설정되지 않은 경우 반드시 이 파일을 사용합니다.
Source: "{#SourceRoot}\OVIA.Desktop\ovia_logo.png"; DestDir: "{app}"; Flags: ignoreversion

; 승인된 Pretendard 폰트만 포함합니다. NanumSquareNeo와 SUIT는 제외합니다.
Source: "{#SourceRoot}\OVIA.Desktop\Assets\Fonts\Pretendard-*.otf"; DestDir: "{app}\Assets\Fonts"; Flags: ignoreversion
Source: "{#SourceRoot}\OVIA.Desktop\Assets\Fonts\Pretendard-*.ttf"; DestDir: "{app}\Assets\Fonts"; Flags: ignoreversion skipifsourcedoesntexist

; OVIA 필수 데이터
Source: "{#SourceRoot}\OVIA.Desktop\Data\Mapping\barlist_mapping.json"; DestDir: "{app}\Data\Mapping"; Flags: ignoreversion
Source: "{#SourceRoot}\OVIA.Desktop\Data\Rebar\rebar_unit_weight.csv"; DestDir: "{app}\Data\Rebar"; Flags: ignoreversion
Source: "{#SourceRoot}\OVIA.Desktop\Data\Shapes\shape_index.csv"; DestDir: "{app}\Data\Shapes"; Flags: ignoreversion
Source: "{#SourceRoot}\OVIA.Desktop\Data\Shapes\shape_field_overrides.csv"; DestDir: "{app}\Data\Shapes"; Flags: ignoreversion
Source: "{#SourceRoot}\OVIA.Desktop\Data\Shapes\source_jpg\*"; DestDir: "{app}\Data\Shapes\source_jpg"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#SourceRoot}\OVIA.Desktop\Data\Version\ovia_version_history.ovia"; DestDir: "{app}\Data\Version"; Flags: ignoreversion

; AutoCAD ApplicationPlugins 자동 로더 패키지
; 중요: 설치파일은 stage 폴더의 오래된 파일이 아니라 방금 Release 빌드된 실제 DLL을 직접 사용합니다.
; 네 버전 DLL 중 하나라도 없으면 Inno Setup Source 검증에서 컴파일이 실패해야 하며, 누락된 설치파일을 만들지 않습니다.
; Autodesk accoremgd/acdbmgd/acmgd는 AutoCAD 런타임이 제공하므로 배포하지 않습니다.
Source: "{#SourceRoot}\Installer\AutoCADBundle\PackageContents.xml"; DestDir: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle"; Flags: ignoreversion
Source: "{#AutoCad2024ReleaseDir}\OVIA.AutoCAD.2024.dll"; DestDir: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents\2024"; Flags: ignoreversion
Source: "{#AutoCad2025ReleaseDir}\OVIA.AutoCAD.2025.dll"; DestDir: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents\2025"; Flags: ignoreversion
Source: "{#AutoCad2026ReleaseDir}\OVIA.AutoCAD.2026.dll"; DestDir: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents\2026"; Flags: ignoreversion
Source: "{#AutoCad2027ReleaseDir}\OVIA.AutoCAD.2027.dll"; DestDir: "{autopf}\Autodesk\ApplicationPlugins\OVIA.bundle\Contents\2027"; Flags: ignoreversion

[InstallDelete]
; 동일 AppId 업데이트 시 과거 런타임 DLL이 남아 새 버전과 혼용되지 않도록
; 실행 파일 영역만 정리합니다. Data/Version 및 사용자 데이터는 삭제하지 않습니다.
Type: files; Name: "{app}\OVIA.Desktop.exe"
Type: files; Name: "{app}\OVIA.Desktop.exe.config"
Type: files; Name: "{app}\*.dll"
Type: filesandordirs; Name: "{app}\runtimes"

[Icons]
Name: "{group}\OVIA"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\Icons\ovia_symbol.ico"
Name: "{autodesktop}\OVIA"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\Icons\ovia_symbol.ico"; Tasks: desktopicon

[Registry]
; ERP 웹에서 ovia://launch?... 링크로 설치된 OVIA.Desktop.exe를 실행합니다.
; ID/PW/ovia_token은 URI에 넣지 않고 1회용 Launch Ticket만 전달합니다.
Root: HKCR; Subkey: "ovia"; ValueType: string; ValueData: "URL:OVIA Protocol"; Flags: uninsdeletekey
Root: HKCR; Subkey: "ovia"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCR; Subkey: "ovia\DefaultIcon"; ValueType: string; ValueData: "{app}\Assets\Icons\ovia_symbol.ico"
Root: HKCR; Subkey: "ovia\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "OVIA 실행"; Flags: nowait postinstall skipifsilent

[Code]
const
  OviaUninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B22D4E7E-9D42-49B9-8F05-6E31D8262D36}_is1';

function DiagnosticLogPath: String;
begin
  Result := AddBackslash(GetTempDir) + 'OVIA_Install.log';
end;

procedure Diag(const Text: String);
var
  Line: String;
begin
  Log(Text);
  Line := GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + ' ' + Text + #13#10;
  SaveStringToFile(DiagnosticLogPath, Line, True);
end;

function IsDotNet472OrLaterInstalled: Boolean;
var
  ReleaseValue: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release', ReleaseValue) then
  begin
    Result := ReleaseValue >= 461808;
  end;
end;

function IsWebView2RuntimeInstalled: Boolean;
var
  Version: String;
begin
  Result := False;

  if RegQueryStringValue(HKLM32,
    'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F1E7E0B9-0D4F-4A68-A4B9-3E3A80B8D7B5}',
    'pv', Version) then
    Result := (Version <> '') and (Version <> '0.0.0.0');

  if not Result then
  begin
    if RegQueryStringValue(HKLM32,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) then
      Result := (Version <> '') and (Version <> '0.0.0.0');
  end;

  if not Result then
  begin
    if RegQueryStringValue(HKCU,
      'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) then
      Result := (Version <> '') and (Version <> '0.0.0.0');
  end;
end;

function QueryInstalledVersion(var Version: String): Boolean;
begin
  Version := '';
  Result := RegQueryStringValue(HKLM64, OviaUninstallKey, 'DisplayVersion', Version);
  if not Result then
    Result := RegQueryStringValue(HKLM32, OviaUninstallKey, 'DisplayVersion', Version);
  if not Result then
    Result := RegQueryStringValue(HKCU, OviaUninstallKey, 'DisplayVersion', Version);

  Version := Trim(Version);
  Result := Result and (Version <> '');
end;

function ReadNumericToken(const Version: String; var P: Integer): Integer;
var
  S: String;
  C: Char;
begin
  S := '';

  while (P <= Length(Version)) and (Version[P] = '.') do
    P := P + 1;

  while P <= Length(Version) do
  begin
    C := Version[P];
    if (C >= '0') and (C <= '9') then
    begin
      S := S + C;
      P := P + 1;
    end
    else
      Break;
  end;

  while (P <= Length(Version)) and (Version[P] <> '.') do
    P := P + 1;

  if S = '' then
    Result := 0
  else
    Result := StrToIntDef(S, 0);
end;

function CompareOviaVersions(const A, B: String): Integer;
var
  PA, PB, I, VA, VB: Integer;
begin
  PA := 1;
  PB := 1;
  Result := 0;

  for I := 0 to 3 do
  begin
    VA := ReadNumericToken(A, PA);
    VB := ReadNumericToken(B, PB);
    if VA < VB then
    begin
      Result := -1;
      Exit;
    end;
    if VA > VB then
    begin
      Result := 1;
      Exit;
    end;
  end;
end;

function CheckInstalledVersionPolicy: Boolean;
var
  InstalledVersion: String;
  CompareResult: Integer;
begin
  Result := True;

  if not QueryInstalledVersion(InstalledVersion) then
  begin
    Diag('기존 OVIA 설치 없음 - 신규 설치');
    Exit;
  end;

  Diag('기존 설치 버전=' + InstalledVersion + ', 설치 대상 버전={#MyAppVersion}');
  CompareResult := CompareOviaVersions(InstalledVersion, '{#MyAppVersion}');

  if CompareResult > 0 then
  begin
    MsgBox(
      '현재 설치된 OVIA ' + InstalledVersion + '이(가) 설치하려는 버전 {#MyAppVersion}보다 최신입니다.' + #13#10 + #13#10 +
      '안전한 버전 관리를 위해 이전 버전으로의 다운그레이드는 허용하지 않습니다.',
      mbCriticalError, MB_OK);
    Diag('설치 중단: 다운그레이드 차단');
    Result := False;
    Exit;
  end;

  if CompareResult = 0 then
  begin
    if MsgBox(
      'OVIA {#MyAppVersion}이(가) 이미 설치되어 있습니다.' + #13#10 + #13#10 +
      '동일한 버전을 다시 설치하시겠습니까?',
      mbConfirmation, MB_YESNO) <> IDYES then
    begin
      Diag('사용자 취소: 동일 버전 재설치');
      Result := False;
      Exit;
    end;
    Diag('동일 버전 재설치 승인');
  end
  else
  begin
    Diag('자동 업데이트 허용: ' + InstalledVersion + ' -> {#MyAppVersion}');
  end;
end;

function InitializeSetup: Boolean;
begin
  DeleteFile(DiagnosticLogPath);
  Diag('OVIA Setup 시작. 대상 버전={#MyAppVersion}');
  Result := False;

  if not CheckInstalledVersionPolicy then
    Exit;

  if not IsDotNet472OrLaterInstalled then
  begin
    Diag('설치 중단: .NET Framework 4.7.2 이상 없음');
    MsgBox(
      'OVIA를 설치하려면 Microsoft .NET Framework 4.7.2 이상이 필요합니다.' + #13#10 +
      'Windows 업데이트 또는 Microsoft 공식 설치 파일로 .NET Framework를 먼저 설치한 뒤 다시 실행해 주세요.',
      mbCriticalError, MB_OK);
    Exit;
  end;

  if not IsWebView2RuntimeInstalled then
  begin
    if MsgBox(
      'Microsoft Edge WebView2 Runtime이 확인되지 않았습니다.' + #13#10 + #13#10 +
      'OVIA의 ERP 및 웹 화면이 정상적으로 표시되지 않을 수 있습니다.' + #13#10 +
      '설치를 계속하시겠습니까?',
      mbConfirmation, MB_YESNO) = IDNO then
    begin
      Diag('사용자 취소: WebView2 Runtime 미확인');
      Exit;
    end;
  end;

  Diag('사전 검사 통과');
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall:
      Diag('파일 설치 시작');
    ssPostInstall:
      Diag('파일 설치 완료: ' + ExpandConstant('{app}'));
  end;
end;

procedure DeinitializeSetup;
begin
  Diag('OVIA Setup 종료');
end;
