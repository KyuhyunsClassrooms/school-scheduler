# 학교별 자체 운영 일정 서버

이 폴더를 도입자의 Vercel 계정에 배포하고 도입자의 Supabase 프로젝트에 연결합니다. 앱 제작자가 운영하는 중앙 서버·학교 등록·중계 API는 없습니다. 학교마다 독립적인 주소, DB, 관리자 계정을 소유합니다.

데스크톱 앱은 Supabase 전용 클라이언트가 아닙니다. [공통 JSON 규격](PROTOCOL.md)을 제공하는 HTTPS 서버라면 정적 JSON 파일이나 다른 기술로 만든 서버도 연결할 수 있습니다.

## 1. 본인 Supabase 프로젝트 준비

1. 도입자 계정으로 Supabase 프로젝트를 만듭니다.
2. SQL Editor에서 `setup.sql` 전체를 **새 프로젝트에 한 번** 실행합니다. 기존 테이블을 삭제하거나 덮어쓰는 스크립트가 아닙니다. 이미 설치했다면 재실행하지 않습니다.
3. Authentication → Users에서 사용할 관리자 이메일·비밀번호 계정을 생성합니다. 이메일 확인이 필요한 계정은 확인을 완료하세요. 이 웹에는 회원 가입 기능이 없습니다. 필요하지 않은 공개 회원 가입은 프로젝트 설정에서 끕니다.
4. 해당 사용자의 UUID를 확인한 다음 SQL Editor에서 아래 SQL의 UUID만 바꿔 실행합니다.

```sql
update auth.users
set raw_app_meta_data = coalesce(raw_app_meta_data, '{}'::jsonb)
  || '{"calendar_admin":true}'::jsonb
where id = '여기에-관리자-UUID';
```

권한을 부여한 뒤 새로 로그인해야 JWT에도 반영됩니다. `user_metadata`나 이메일 문자열을 관리자 권한으로 사용하지 않습니다. 여러 관리자를 둘 수 있지만 각자의 계정을 사용하세요.

5. Project URL, publishable key, 서버 전용 secret key를 확인합니다. secret key는 다음 단계의 **Vercel 서버 환경 변수에만** 입력합니다. 앱·연결 주소·HTML·공개 저장소에 넣지 마세요.

## 2. 본인 Vercel 계정에 배포

1. 이 폴더를 본인 Git 저장소에 올립니다. `.env.local`, `.env`, `.vercel`, `node_modules`는 올리지 않습니다.
2. Vercel에서 저장소를 Import합니다. 이 폴더 자체가 저장소 루트면 Root Directory는 기본값, 상위 앱 소스까지 올렸다면 Root Directory를 `schedule-server`로 지정합니다.
3. Framework Preset: **Other**, Node.js: **24.x**. Build Command는 `npm run build`, Output Directory는 `public`입니다. `vercel.json`에 빌드 설정이 들어 있습니다. 외부 웹 SDK/CDN이 필요하지 않습니다.
4. Production 환경에 다음 변수를 설정하고 배포합니다.

| 변수 | 값 |
|---|---|
| `SUPABASE_URL` | 본인 프로젝트의 `https://…supabase.co` 주소 |
| `SUPABASE_PUBLISHABLE_KEY` | 본인 publishable key |
| `SUPABASE_SECRET_KEY` | 본인 서버 전용 secret key |
| `SCHEDULE_READ_TOKEN` | 공개 일정이면 비워 둠. 제한해서 공유하려면 임의의 32자 이상 문자열 |

읽기 토큰은 학교가 배포하는 **읽기 전용 공유 암호**입니다. 교직원 개별 계정 인증이나 사용자별 폐기를 제공하지 않습니다. 토큰을 아는 사람은 누구나 일정을 읽을 수 있으므로 민감한 자료를 게시하지 마세요. 개별 인증이 필요한 도입자는 공통 JSON 응답 앞에 자체 인증을 구현하면 됩니다. 현재 앱은 Bearer 읽기 토큰을 지원합니다.

Vercel 배포 자체에 방문자 로그인을 요구하는 Deployment Protection이 걸려 있으면 앱이 JSON을 받을 수 없습니다. 배포 정책을 확인하고, 앱에서 접근 가능한 **최종 Production HTTPS 주소**를 안내하세요. 읽기 제한은 위 토큰/API에서 처리합니다. 환경 변수를 바꾼 뒤에는 새로 배포합니다.

## 3. 초기 일정 등록·게시

1. `https://본인도메인/`에서 관리자 계정으로 로그인합니다.
2. 달력 이름을 입력하고 일정을 추가합니다. **JSON · 기존 HTML 가져오기**로 기존 `학사일정-스케줄러.html`의 EVENTS 데이터도 가져올 수 있습니다. HTML 스크립트는 실행하지 않고 JSON 배열만 읽습니다.
3. **초안 저장**은 관리자 편집 내용만 저장합니다. 선생님들께는 기존 게시 내용이 유지됩니다.
4. 표에서 내용을 확인하고 **전체 일정 게시**를 누릅니다. 삭제를 포함한 전체 목록이 하나의 버전으로 게시되고, 이전 버전은 게시 이력에 남습니다.
5. 실수했다면 게시 이력에서 이전 내용을 **초안으로 가져오기** → 확인 → 다시 게시합니다.

같은 초안을 다른 창에서 동시에 저장하면 뒤늦은 덮어쓰기를 막습니다. 충돌한 창은 JSON 내보내기로 편집 내용을 보관한 다음 다시 불러와 합치세요. 관리자 로그인 토큰은 브라우저 메모리에만 두며 탭 새로고침/만료 시 재로그인합니다. 초안은 저장하고 나가세요.

## 4. 데스크톱 앱 연결

선생님들께 공통 설치 파일과 **본인 서버의** 다음 주소를 전달합니다.

```text
https://본인도메인/api/calendar
```

앱 할 일 패널 아래의 **학교 일정 연결** → 주소 입력 → 읽기 토큰을 설정했다면 함께 입력 → **확인 후 연결**.

- 첫 연결은 실제 JSON 검증을 통과해야 저장됩니다. 실패하면 기존 연결과 일정이 유지됩니다.
- 시작 시·5분마다 갱신합니다. 트레이의 **학교 일정 지금 새로고침**으로 즉시 확인할 수도 있습니다.
- 통신 실패·잘못된 파일이면 마지막 정상 일정을 유지합니다. 앱 재시작 후에도 캐시를 읽습니다.
- 개인 일정·할 일·메모는 앱 PC에 저장되고 이 서버로 전송되지 않습니다.
- 다른 학교 주소로 바꾸면 공유 일정 전체가 교체됩니다. 연결을 해제해도 개인 데이터는 유지됩니다.

## 운영 확인

- 아직 게시하지 않았다면 `/api/calendar`는 404를 반환합니다. 관리자에서 첫 게시를 하세요.
- 읽기 토큰을 설정했다면 토큰 없는 요청은 401이어야 합니다.
- 일반 Supabase 로그인 사용자에게 관리자 화면과 초안 DB 접근 권한이 없어야 합니다.
- SQL은 RLS와 명시적 GRANT를 사용합니다. 공개 anon 역할은 게시 테이블을 직접 읽을 수 없습니다. 읽기 API만 서버 키로 게시된 스냅샷을 조회합니다.
- 관리자 편집 API는 매 요청 Supabase 사용자와 `app_metadata.calendar_admin` 권한을 확인하고, DB 접근에는 해당 사용자의 JWT를 사용합니다. 게시 이력 트리거는 `SECURITY INVOKER`로 실행됩니다.
- Supabase Security/Performance Advisors를 본인 프로젝트에서 확인하세요. 여기서는 실제 클라우드 프로젝트를 만들거나 수정하지 않았습니다.
- 토큰 교체 시 배포 후 앱의 읽기 토큰도 변경해야 합니다. 기존 PC는 마지막 정상 캐시를 계속 표시하므로 비밀 자료 회수 수단은 아닙니다.
- 요금·휴면 정책·사용량·DB 백업은 각 운영자가 본인 서비스 계정에서 관리합니다.

## 로컬 개발·검증

Node.js 24가 필요합니다. 운영 런타임 의존성은 없고 PGlite는 SQL 검증용 개발 의존성입니다.

```powershell
npm ci
Copy-Item .env.example .env.local
# .env.local에 본인 개발 프로젝트의 값을 입력
npm run dev
npm test
npm run build
```

`http://localhost:3100`에서 확인합니다. 앱은 로컬 테스트에 한해 HTTP loopback 주소를 허용합니다. 실제 배포는 HTTPS입니다.

`npm test`는 메모리 PostgreSQL에서 설치 SQL·RLS·게시 이력·일반 사용자 거부·공개/토큰 조회·동시 편집 충돌을 검증합니다. Supabase 네트워크 경계는 모의 응답을 사용하므로 실제 프로젝트 배포 후 첫 게시와 앱 연결을 한 번 확인해야 합니다.

`node tests/ui-fixture.mjs`는 가짜 계정/일정으로 관리자 UI를 로컬 점검하는 전용 서버입니다. **운영용으로 실행하지 않습니다.** `node tests/feed-fixture.mjs`는 데스크톱 동기화 테스트용입니다.

## 참고 문서

- [Vercel Node.js Functions](https://vercel.com/docs/functions/runtimes/node-js)
- [Supabase API keys](https://supabase.com/docs/guides/api/api-keys)
- [Supabase RLS](https://supabase.com/docs/guides/database/postgres/row-level-security)
- [Supabase 비밀번호 로그인](https://supabase.com/docs/guides/auth/passwords)
