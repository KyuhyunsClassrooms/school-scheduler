# 학교 일정 데스크톱 달력

Windows 바탕화면에 학사일정·개인 메모·할 일을 표시하는 네이티브 앱과, 각 도입자가 자신의 계정으로 운영하는 일정 서버 템플릿입니다.

**현재 앱 버전: 2.2.0** · Windows 10 1809 이상 / Windows 11, Intel·AMD x64.

## 내려받기

[최신 Release](https://github.com/KyuhyunsClassrooms/school-scheduler/releases/latest)에서 다음 파일을 받습니다.

| 대상 | 파일 |
|---|---|
| 달력 사용자 | `SchoolScheduler-2.2.0-x64-Setup.exe` |
| 학교 일정 서버를 구축할 담당자 | `SchoolCalendar-Server-Template-1.0.0.zip` |

앱 설치는 현재 Windows 사용자에게 진행되며, WebView2나 브라우저 런타임을 사용하지 않습니다. 현재 설치 파일은 제품 코드서명 전 버전입니다.

## ‘일정 연결 주소’는 무엇인가요?

**학교 일정 데이터를 받아오는 웹 주소**입니다. 담당자가 서버를 배포하면 아래와 같은 주소가 생깁니다.

```text
https://학교에서-운영하는-도메인/api/calendar
```

담당자는 관리자 웹에서 일정을 편집·게시하고, 선생님들은 위 주소를 앱의 **학교 일정 연결**에 붙여 넣습니다. JSON 파일이나 프로그램 코드를 직접 수정할 필요가 없습니다. 여기서 JSON은 앱과 서버가 일정을 주고받는 데이터 형식의 이름입니다.

```text
각 학교 담당자 → 본인 관리자 웹에서 수정·게시
                           ↓
                 본인 서버의 일정 연결 주소
                           ↓
                  해당 주소를 연결한 PC 달력
```

앱 제작자가 운영하는 중앙 서버나 학교 등록 절차는 없습니다. 특정 학교에 자동 연결되지 않습니다. Vercel·Supabase 이외의 서버도 [공통 일정 규격](schedule-server/PROTOCOL.md)을 제공하면 연결할 수 있습니다.

## 앱 사용

- **학교 일정 연결:** 할 일 패널 아래 버튼에서 학교가 안내한 HTTPS 주소를 입력합니다. 읽기 토큰을 안내받았다면 함께 입력합니다.
- **자동 갱신:** 시작 시와 5분마다 확인합니다. 연결이 끊기면 마지막 정상 일정을 유지합니다.
- **메모:** 날짜를 두 번 클릭합니다. Ctrl+Enter 또는 바깥 클릭으로 저장, Esc로 취소합니다.
- **할 일:** 항상 보이는 오른쪽 패널에서 추가·완료 체크합니다. 제목을 두 번 클릭하면 수정합니다. 좁은 창에서는 달력 아래에 표시됩니다.
- **개인 정보 저장:** 개인 일정·메모·할 일은 PC에 보관하며 일정 서버에 업로드하지 않습니다. 트레이 메뉴에서 JSON 백업을 저장할 수 있습니다.
- **이동·크기:** 제목 막대를 끌어 이동하고 가장자리를 끌어 크기를 바꿉니다. 설정에서 위치 잠금과 투명도를 조절합니다.

자세한 앱 구조와 조작은 [widget 안내](widget/README.md)를 참고하세요.

## 각 도입자의 서버 구축

1. 본인 Supabase 프로젝트에 설치 SQL을 실행하고 일정 관리자 계정을 설정합니다.
2. `schedule-server` 폴더를 본인 Vercel 계정에 배포합니다.
3. 관리자 웹에서 기존 HTML의 일정을 가져오거나 새 일정을 등록한 뒤 게시합니다.
4. 관리자 화면에 표시되는 일정 연결 주소를 사용자에게 안내합니다.

**단계별 안내:** [서버 구축 안내](schedule-server/README.md) · [JSON 규격](schedule-server/PROTOCOL.md) · [예시 일정](schedule-server/examples/calendar.json).

서버 템플릿은 초안 저장·전체 게시·게시 이력·이전 버전 복원과 동시 편집 충돌 검사를 제공합니다. 선택적으로 읽기 전용 공유 토큰을 설정할 수 있습니다. 실제 클라우드 배포와 요금·백업 관리는 각 운영자의 계정에서 진행합니다.

## 2.2 성능 개선

날짜 위로 마우스를 옮길 때 전체 달력을 새로 만들지 않고 테두리 부분만 갱신합니다. 창 이동은 그림 복사 없이 위치 정보만 전송합니다. 화면 버퍼를 재사용하고, 공유 메모리는 실제 프레임 크기만큼만 할당하며, 같은 버전의 서버 응답은 캐시를 다시 쓰지 않습니다.

같은 PC·1885×1616 크기에서 84회 날짜 이동 시험은 **3.76초 → 0.53초**, 해당 시험 직후 앱 메모리 작업집합은 **98MB → 75MB**였습니다. 환경에 따라 달라지는 측정값이며 [측정 방법과 상세 결과](docs/PERFORMANCE.md)를 함께 제공합니다.

## 소스 구성

| 경로 | 내용 |
|---|---|
| `widget/src/` | C# WinForms/GDI+ 앱, C++ Explorer 합성 DLL |
| `widget/installer/` | Windows 설치 패키지와 과거 버전 데이터 이전 도구 |
| `widget/analysis/*.cs`, `*.ps1` | 네이티브 입력·동기화·설치 검사 소스 |
| `schedule-server/` | 독립 배포용 관리자 웹, API, Supabase 설치 SQL |
| `schedule-server/tests/` | SQL·접근 권한·게시 동작 검사 및 로컬 fixture |

빌드 결과·외부 SDK·브라우저 프로필·PC 사용자 데이터는 Git에 포함하지 않습니다. 설치 파일과 서버 템플릿 ZIP은 Release 첨부 파일로 제공합니다.

## 개발·빌드

### Windows 앱

Windows x64, .NET Framework 4.7.2 이상, Visual Studio 2022 Build Tools의 MSVC x64 도구 및 Windows SDK가 필요합니다. 현재 `widget/build-native.cmd`는 Build Tools 기본 설치 경로를 사용합니다.

```powershell
cd widget
.\build.cmd
.\build-installer.ps1
```

설치 빌드는 필요한 Inno Setup과 과거 데이터 이전용 WebView2 SDK를 지정된 NuGet 버전으로 내려받습니다. 해당 SDK는 새 앱의 실행 의존성이 아닙니다. Inno Setup 사용 조건은 내려받은 도구의 `license.txt`를 확인하세요.

### 서버 템플릿

Node.js 24가 필요합니다.

```powershell
cd schedule-server
npm ci
npm test
npm run build
```

개발 서버와 환경 변수 설정은 [서버 안내](schedule-server/README.md#로컬-개발검증)를 참고하세요. 테스트는 메모리 PostgreSQL을 사용하며 실제 운영 Supabase 프로젝트를 수정하지 않습니다.

서버 템플릿 ZIP은 저장소 루트에서 `python schedule-server/scripts/package.py`로 만듭니다.
