# 학사일정 바탕화면 달력 2.1

Windows 네이티브 달력입니다. WebView2, HTML 화면, 브라우저 프로세스, PNG 캡처를 실행 경로에서 제거했습니다. C# WinForms/GDI+가 투명 비트맵을 직접 그리고, C++ DLL이 Explorer 바탕화면에 합성하고 입력을 전달합니다. DesktopCal의 자체 `dkui.dll`을 사용하는 것은 아닙니다.

## 실행과 조작

`build.cmd`로 빌드한 뒤 `bin/SchoolWidget.exe`를 실행합니다. 기본 모드는 **바탕화면 (아이콘과 함께)**입니다.

- 날짜 한 번 클릭: 선택. 두 번 클릭 또는 Enter/F2: 그 날짜 칸에서 메모 편집.
- 메모는 Windows `EDIT` 컨트롤을 사용합니다. Ctrl+Enter 또는 바깥 클릭으로 저장하고, Esc로 취소합니다. Enter는 줄바꿈입니다.
- 화살표 또는 휠: 월 이동. 월 이름 클릭 또는 Home: 오늘로 이동.
- 제목 막대 드래그: 이동. 가장자리 드래그: 크기 조절.
- **목록**: 선택 날짜의 학사·개인 일정과 모든 할 일. 개인 일정과 할 일은 추가·수정·삭제할 수 있습니다. 할 일의 체크박스로 완료 상태를 바꿉니다.
- **할 일 패널은 항상 표시**됩니다. 넓으면 오른쪽, 좁으면 아래에 배치합니다. 기한 없는 항목도 표시하며 전체·오늘까지·중요 필터, 완료 포함, 페이지/휠 이동, 직접 완료 체크를 지원합니다. 제목을 두 번 클릭하면 수정·삭제, **+ 추가**로 등록합니다.
- 할 일 패널 아래 **학교 일정 연결**: 도입자 본인의 HTTPS JSON 주소와 선택적 읽기 토큰을 입력합니다. 중앙 서버나 학교 등록은 없습니다. 새 설치는 특정 학교에 연결되지 않습니다.
- 날짜 오른쪽 클릭: 메모, 개인 일정 추가, 할 일 추가, 목록 메뉴.
- **···** 또는 트레이 메뉴: 글자 크기, 배경 진하기, 불투명도, 학년 필터, 위치 잠금, 자동 실행, 백업.
- 종료: 트레이 메뉴 또는 `SchoolWidget.exe --quit`.

## 표시 구조

1. x64 `WH_CALLWNDPROC` 훅으로 Explorer 바탕화면 ListView 스레드에 진입하고 `SetWindowSubclass`를 설치합니다. 훅 종류도 원본 DesktopCal과 같습니다.
2. `CalendarRenderer`가 32비트 premultiplied BGRA 비트맵을 직접 만듭니다. 글자·날짜·일정·메모를 그리며, 크기나 내용이 바뀐 경우에만 공유 메모리에 게시합니다.
3. Explorer의 `WM_PAINT`에서 바탕화면 배경과 아이콘을 메모리에 그리고, 아이콘과 이름 영역을 제외해 달력을 `AlphaBlend`합니다. 완성된 화면을 한 번에 표시합니다.
4. ListView가 받은 마우스 입력을 앱의 날짜·버튼·드래그 처리에 전달합니다. 달력 전체를 편집용 창으로 바꾸지 않습니다. 메모를 입력하는 날짜 칸만 네이티브 입력창으로 표시합니다.
5. Per Monitor V2와 픽셀 단위 레이아웃을 사용합니다. 글자 크기는 픽셀로 지정하며, DPI 메시지 때문에 달력 자체의 크기를 바꾸지 않습니다.
6. 종료 시 훅과 서브클래스를 해제합니다. Explorer가 다시 시작되면 다시 연결합니다. 앱이 비정상 종료되면 Explorer의 감시 타이머가 그림과 서브클래스를 정리합니다.

원본은 x86 자체 UI와 x64 도우미를 사용합니다. 이 앱은 x64 WinForms/GDI+와 공유 메모리를 사용하므로 렌더러와 IPC는 원본과 다릅니다. 원본 디컴파일 근거는 `analysis/original-functions.txt` 및 상위 `DesktopCal-분석.md`에 있습니다.

## 데이터

- 학사일정: 사용자가 입력한 HTTPS 서버에서 공통 JSON 스냅샷을 읽습니다. 시작 시·5분마다 갱신하며 수정·삭제를 함께 반영합니다. 토·일요일 일정도 실제 날짜대로 표시합니다.
- 서버 설정: `%LOCALAPPDATA%\SchoolScheduler\schedule-source.json`. 읽기 토큰은 현재 Windows 사용자에게 묶인 DPAPI 암호문으로 저장합니다.
- 마지막 정상 학교 일정: 같은 폴더의 `schedule-cache.json`. 응답 실패·형식 오류 시 유지하고 재시작 후에도 읽습니다. 개인 데이터와 별도 파일입니다.
- 기존 2.0 사용자는 첫 업데이트에서 기존 127건을 **로컬 일정**으로 보존합니다. 이 리소스는 이전용이며 새 설치의 기본 학교가 아닙니다. 서버 주소는 자동 지정하지 않습니다. 새 PC는 연결 전 학교 일정이 비어 있습니다.
- 일정 서버 구축 템플릿·JSON 규격·예시: 상위 `schedule-server/`의 `README.md`, `PROTOCOL.md`. Vercel·Supabase는 각 도입자의 계정으로 배포합니다. 정적 HTTPS JSON 서버도 가능합니다.
- 개인 일정·할 일·날짜 메모: `%LOCALAPPDATA%\SchoolScheduler\calendar-data.json`.
- 저장할 때 이전 파일을 `.bak`으로 보존하고 임시 파일을 원자적으로 교체합니다.
- 위치·크기·불투명도·글자 설정: 같은 폴더의 `widget.ini`.
- 백업 가져오기는 기존 HTML 버전의 `personal`, `todos` JSON도 읽고 ID로 합칩니다. 네이티브 백업에는 `memos`도 포함합니다.
- 과거 WebView2 사용자 프로필은 삭제하지 않습니다. 1.2 설치본에서 업데이트할 때만 이전 SDK를 사용하는 임시 도구로 저장소를 읽어 JSON으로 이전합니다. 같은 ID의 네이티브 데이터가 이미 있으면 덮어쓰지 않습니다. 이 도구와 WebView SDK는 새 설치의 실행 파일 폴더에 설치하지 않습니다.

## 빌드와 배포

앱은 Windows x64와 .NET Framework 4.7.2 이상에서 동작합니다. 네이티브 DLL 빌드에는 `build-native.cmd`에 지정한 Visual Studio 2022 Build Tools의 MSVC x64 도구가 필요합니다. C++ 런타임은 정적으로 연결합니다.

`build-installer.cmd` 또는 `./build-installer.ps1`을 실행하면 `dist/SchoolScheduler-2.1.0-x64-Setup.exe`와 SHA-256 파일을 만듭니다. `-SkipBuild`는 현재 바이너리로 패키지만 다시 만듭니다. 버전은 실행 파일에서 읽습니다.

- Intel·AMD x64 Windows 10 1809 이상 / Windows 11. ARM64는 현재 Explorer DLL 패키지의 대상이 아닙니다.
- 기본 설치 경로는 `%LOCALAPPDATA%\Programs\SchoolScheduler`이며 관리자 권한을 요청하지 않습니다.
- 새 설치는 인터넷과 WebView2가 필요 없습니다. 실행 파일 폴더에는 `SchoolWidget.exe`, `SchoolDockHook.dll`만 설치합니다.
- 시작 메뉴·프로그램 제거를 등록합니다. 바탕화면 바로 가기와 로그인 자동 실행은 선택 사항입니다.
- 업데이트·제거 전에 앱의 정상 종료를 요청합니다. Explorer를 종료하지 않습니다.
- 제거 후에도 사용자 데이터와 설정은 남깁니다. 현재 패키지는 제품 코드서명 전 버전입니다.

설치 컴파일러는 `tools/InnoSetupPortable`의 Inno Setup을 사용합니다. Windows에 컴파일러를 설치하지 않습니다. 도구의 사용 조건은 포함된 `license.txt`를 참조하세요. 업데이트용 데이터 이전 도구를 빌드할 때만 과거 WebView SDK 개발 DLL이 필요합니다.

## 검증

`analysis/NativeSmoke.cs`는 실제 Explorer 훅과 네이티브 화면을 함께 검사합니다. 원본 자료 127건, WebView 참조 없음, 투명 픽셀 형식, 첫 클릭, 날짜 칸과 편집창 좌표 일치, Windows EDIT 문자 입력, 저장·취소, JSON 왕복·기존 백업 합치기, 이동·크기 조절, DPI 메시지, 유휴 시 재그리기 없음 등을 검사합니다.

- 현재 기능 검사: `analysis/native-smoke/result.txt`.
- 서버 연결/캐시/읽기 토큰/삭제 반영 검사: `analysis/FeedSmoke.cs`, `analysis/feed-smoke/*/result.txt`.
- 서버 SQL·권한·게시 검사: `../schedule-server/tests/server.test.mjs` (`npm test`). 실제 Supabase 배포는 별도 확인이 필요합니다.
- 2.0 설치·1.2에서 업데이트·제거 검사: `analysis/native-installer-smoke/result.json`. 최신 설치 검사는 `analysis/feed-installer-smoke/`.
- 과거 WebView 구현 및 시험 소스: `legacy-webview/`. 현재 빌드와 설치에 포함하지 않습니다.
