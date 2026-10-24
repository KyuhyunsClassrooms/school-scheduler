import { createHash, timingSafeEqual } from 'node:crypto';
import { MAX_BYTES } from './feed.js';

export class HttpError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}
export async function body(req) {
  if (req.body != null) {
    const value = typeof req.body === 'string' ? JSON.parse(req.body) : req.body;
    if (Buffer.byteLength(JSON.stringify(value)) > MAX_BYTES) throw new HttpError(413, '요청이 너무 큽니다.');
    return value;
  }
  let size = 0; const chunks = [];
  for await (const chunk of req) {
    size += chunk.length;
    if (size > MAX_BYTES) throw new HttpError(413, '요청이 너무 큽니다.');
    chunks.push(chunk);
  }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw new HttpError(400, '올바른 JSON 요청이 아닙니다.'); }
}
function config() {
  const url = process.env.SUPABASE_URL?.replace(/\/$/, '');
  if (!url || !process.env.SUPABASE_PUBLISHABLE_KEY) throw new HttpError(503, '서버 환경 변수를 설정하세요.');
  const parsed = new URL(url);
  if (parsed.protocol !== 'https:' && !['localhost', '127.0.0.1', '[::1]'].includes(parsed.hostname)) throw new HttpError(503, 'Supabase HTTPS 주소를 확인하세요.');
  return { url, key: process.env.SUPABASE_PUBLISHABLE_KEY };
}
export async function upstream(path, { token, secret = false, method = 'GET', data, prefer } = {}) {
  const { url, key } = config();
  const apiKey = secret ? process.env.SUPABASE_SECRET_KEY : key;
  if (!apiKey) throw new HttpError(503, '서버의 Supabase 키를 설정하세요.');
  const headers = { apikey: apiKey, 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (prefer) headers.Prefer = prefer;
  let response;
  try { response = await fetch(`${url}${path}`, { method, headers, body: data == null ? undefined : JSON.stringify(data), signal: AbortSignal.timeout(12000), redirect: 'error' }); }
  catch { throw new HttpError(502, 'Supabase에 연결하지 못했습니다. 잠시 후 다시 시도하세요.'); }
  if (!response.ok) {
    if (path.startsWith('/auth/v1/token') && response.status === 400) throw new HttpError(401, '이메일·비밀번호와 이메일 확인 상태를 확인하세요.');
    if ([401, 403].includes(response.status)) throw new HttpError(response.status, '로그인 또는 관리자 권한을 확인하세요.');
    throw new HttpError(502, 'Supabase 요청에 실패했습니다. 서버 설정과 SQL 설치를 확인하세요.');
  }
  return response.status === 204 ? null : response.json();
}
export async function administrator(req) {
  const match = /^Bearer ([^\r\n]+)$/.exec(req.headers.authorization ?? '');
  if (!match) throw new HttpError(401, '관리자 로그인이 필요합니다.');
  const user = await upstream('/auth/v1/user', { token: match[1] });
  if (user.is_anonymous || user.app_metadata?.calendar_admin !== true) throw new HttpError(403, '이 계정에는 일정 관리자 권한이 없습니다.');
  return match[1];
}
export function checkReadToken(req) {
  const expected = process.env.SCHEDULE_READ_TOKEN;
  if (!expected) return;
  if (expected.length < 32) throw new HttpError(503, '읽기 토큰은 32자 이상으로 설정하세요.');
  const actual = /^Bearer ([^\r\n]+)$/.exec(req.headers.authorization ?? '')?.[1] ?? '';
  const digest = value => createHash('sha256').update(value).digest();
  if (!timingSafeEqual(digest(expected), digest(actual))) throw new HttpError(401, '학교에서 안내한 읽기 토큰이 필요합니다.');
}
export function fail(res, error) {
  res.setHeader('Cache-Control', 'no-store');
  const status = error instanceof HttpError ? error.status : 400;
  res.status(status).json({ error: error instanceof HttpError || status === 400 ? error.message : '요청 처리 실패' });
}
