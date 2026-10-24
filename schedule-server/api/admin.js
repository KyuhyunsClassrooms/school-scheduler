import { randomUUID } from 'node:crypto';
import { administrator, body, upstream, HttpError, fail } from '../lib/backend.js';
import { validateFeed } from '../lib/feed.js';

export default async function handler(req, res) {
  res.setHeader('Cache-Control', 'no-store');
  try {
    if (req.method !== 'POST') { res.setHeader('Allow', 'POST'); throw new HttpError(405, 'POST 요청만 지원합니다.'); }
    const input = await body(req);
    if (input.action === 'login') {
      if (typeof input.email !== 'string' || typeof input.password !== 'string') throw new HttpError(400, '이메일과 비밀번호를 입력하세요.');
      const session = await upstream('/auth/v1/token?grant_type=password', { method: 'POST', data: { email: input.email, password: input.password } });
      if (session.user?.is_anonymous || session.user?.app_metadata?.calendar_admin !== true) throw new HttpError(403, '일정 관리자 계정으로 로그인하세요.');
      return res.status(200).json({ access_token: session.access_token, expires_in: session.expires_in });
    }
    const token = await administrator(req);
    if (input.action === 'load') {
      const rows = await upstream('/rest/v1/calendar_state?id=eq.main&select=draft,published,version', { token });
      if (!rows[0]) throw new HttpError(503, '설치 SQL을 먼저 실행하세요.');
      return res.status(200).json(rows[0]);
    }
    if (input.action === 'history') {
      const rows = await upstream('/rest/v1/calendar_versions?select=revision,snapshot,created_at&order=created_at.desc&limit=30', { token });
      return res.status(200).json(rows);
    }
    if (!['save', 'publish'].includes(input.action)) throw new HttpError(400, '지원하지 않는 작업입니다.');
    if (typeof input.version !== 'string' || !/^[a-zA-Z0-9-]{1,100}$/.test(input.version)) throw new HttpError(400, '편집 버전이 없습니다. 다시 불러오세요.');
    const feed = validateFeed(input.feed);
    const version = randomUUID();
    const change = { draft: feed, version };
    if (input.action === 'publish') {
      change.published = validateFeed({ ...feed, revision: randomUUID(), updatedAt: new Date().toISOString() });
      change.draft = change.published;
    }
    const rows = await upstream(`/rest/v1/calendar_state?id=eq.main&version=eq.${input.version}`, {
      token, method: 'PATCH', data: change, prefer: 'return=representation'
    });
    if (!rows.length) throw new HttpError(409, '다른 창에서 수정되었습니다. 현재 내용을 JSON으로 내보낸 뒤 다시 불러오세요.');
    return res.status(200).json(rows[0]);
  } catch (error) { fail(res, error); }
}
