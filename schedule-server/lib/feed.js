export const MAX_BYTES = 2 * 1024 * 1024;
export function validDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value) || value < '0001-01-01') return false;
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(date.valueOf()) && date.toISOString().slice(0, 10) === value;
}
function text(value, max, required = false) {
  return typeof value === 'string' && value.length <= max && (!required || value.trim().length > 0);
}
export function validateFeed(feed) {
  if (!feed || feed.schemaVersion !== 1 || !text(feed.name, 100, true) || !text(feed.revision, 200, true) ||
      !text(feed.updatedAt, 100, true) || !Number.isFinite(Date.parse(feed.updatedAt)) ||
      !Array.isArray(feed.events) || feed.events.length > 10000 || Buffer.byteLength(JSON.stringify(feed)) > MAX_BYTES) {
    throw new Error('일정 이름·버전·게시일 또는 JSON 형식을 확인하세요. 최대 2MB, 10,000개입니다.');
  }
  const ids = new Set();
  const events = feed.events.map(event => {
    if (!event || !text(event.id, 200, true) || ids.has(event.id) || !text(event.title, 300, true) ||
        !validDate(event.start) || !validDate(event.end) || event.end < event.start ||
        (event.category != null && !text(event.category, 100)) ||
        (event.grades != null && !text(event.grades, 100)) || (event.note != null && !text(event.note, 20000))) {
      throw new Error('일정의 ID·제목·날짜 또는 중복 ID를 확인하세요.');
    }
    ids.add(event.id);
    return { id: event.id, start: event.start, end: event.end, title: event.title,
      category: event.category ?? '학교', grades: event.grades || '전체', note: event.note ?? '' };
  });
  return { schemaVersion: 1, name: feed.name, revision: feed.revision, updatedAt: feed.updatedAt, events };
}
