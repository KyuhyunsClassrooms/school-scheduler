import { checkReadToken, upstream, HttpError, fail } from '../lib/backend.js';
import { validateFeed } from '../lib/feed.js';

export default async function handler(req, res) {
  try {
    if (req.method !== 'GET') { res.setHeader('Allow', 'GET'); throw new HttpError(405, 'GET 요청만 지원합니다.'); }
    checkReadToken(req);
    const rows = await upstream('/rest/v1/calendar_state?id=eq.main&select=published', { secret: true });
    if (!rows[0]?.published) throw new HttpError(404, '관리자가 아직 일정을 게시하지 않았습니다.');
    const feed = validateFeed(rows[0].published);
    res.setHeader('Cache-Control', process.env.SCHEDULE_READ_TOKEN ? 'private, no-store' : 'public, max-age=60, s-maxage=60');
    res.status(200).json(feed);
  } catch (error) { fail(res, error); }
}
