const $ = id => document.getElementById(id);
let accessToken = '', version = '', dirty = false, activeId = null, history = [], published = null;
let feed = { schemaVersion: 1, name: '', revision: 'draft', updatedAt: new Date().toISOString(), events: [] };
const status = (message, error = false) => { $('status').textContent = message; $('status').classList.toggle('error', error); };
async function api(action, data = {}) {
  const response = await fetch('/api/admin', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) }, body: JSON.stringify({ action, ...data }) });
  const value = await response.json();
  if (!response.ok) {
    if (response.status === 401) logout();
    throw new Error(value.error || '요청 처리 실패');
  }
  return value;
}
function logout() {
  accessToken = ''; $('workspace').hidden = true; $('loginPanel').hidden = false; $('logout').hidden = true;
  status('다시 로그인하세요. 저장하지 않은 편집 내용은 이 탭에 남아 있습니다.');
}
async function action(work) {
  const buttons = [...document.querySelectorAll('button')]; buttons.forEach(button => button.disabled = true);
  try { await work(); } catch (error) { status(error.message, true); }
  finally { buttons.forEach(button => button.disabled = false); }
}
function render() {
  $('name').value = feed.name;
  $('events').replaceChildren();
  const sorted = [...feed.events].sort((a,b) => a.start.localeCompare(b.start) || a.title.localeCompare(b.title));
  for (const event of sorted) {
    const tr = document.createElement('tr');
    for (const value of [event.start === event.end ? event.start : `${event.start} ~ ${event.end}`, event.title, event.category || '학교', event.grades || '전체']) {
      const td = document.createElement('td'); td.textContent = value; tr.append(td);
    }
    const td = document.createElement('td'), edit = document.createElement('button'); edit.textContent = '수정'; edit.onclick = () => editEvent(event); td.append(edit); tr.append(td); $('events').append(tr);
  }
  $('count').textContent = `${feed.events.length}개 일정${dirty ? ' · 저장하지 않은 변경 있음' : ''}`;
  $('published').textContent = published ? `최근 게시 ${new Date(published.updatedAt).toLocaleString('ko-KR')}` : '아직 게시하지 않음';
}
function acceptState(state) { version = state.version; feed = state.draft; published = state.published; dirty = false; render(); }
$('login').onsubmit = event => { event.preventDefault(); action(async () => {
  const result = await api('login', { email: $('email').value.trim(), password: $('password').value }); accessToken = result.access_token; $('password').value = '';
  $('workspace').hidden = false; $('loginPanel').hidden = true; $('logout').hidden = false;
  // Re-login retains unsaved changes. Reload only when there is no working draft.
  if (!dirty || !version) acceptState(await api('load'));
  status('로그인했습니다. 초안을 편집한 뒤 게시하세요.');
}); };
$('logout').onclick = () => { if (!dirty || confirm('저장하지 않은 편집 내용이 있습니다. 로그아웃할까요?')) logout(); };
$('connection').value = `${location.origin}/api/calendar`;
$('copy').onclick = () => action(async () => { await navigator.clipboard.writeText($('connection').value); status('연결 주소를 복사했습니다.'); });
$('name').oninput = () => { feed.name = $('name').value; dirty = true; $('count').textContent = `${feed.events.length}개 일정 · 저장하지 않은 변경 있음`; };
$('reload').onclick = () => action(async () => { if (dirty && !confirm('저장하지 않은 편집 내용을 버리고 다시 불러올까요?')) return; acceptState(await api('load')); status('서버의 초안을 불러왔습니다.'); });
for (const command of ['save', 'publish']) $(command).onclick = () => action(async () => {
  if (!feed.name.trim()) throw new Error('달력 이름을 입력하세요.');
  if (command === 'publish' && !confirm(`${feed.name}의 전체 일정 ${feed.events.length}개를 게시할까요?${feed.events.length === 0 ? '\n연결된 앱에서 모든 학교 일정이 지워집니다.' : '\n삭제한 일정도 연결된 앱에 반영됩니다.'}`)) return;
  acceptState(await api(command, { version, feed })); status(command === 'save' ? '초안을 저장했습니다. 앱에는 아직 반영되지 않습니다.' : '게시했습니다. 연결된 앱이 다음 갱신 때 받아갑니다.');
});
function editEvent(existing) {
  activeId = existing?.id ?? null; $('editorTitle').textContent = existing ? '일정 수정' : '일정 추가';
  $('eventTitle').value = existing?.title ?? '';
  $('start').value = existing?.start ?? new Date().toLocaleDateString('sv-SE'); $('end').value = existing?.end ?? $('start').value;
  for (const id of ['category','grades']) {
    const value = existing?.[id] || (id === 'category' ? '학교' : '전체');
    if (![...$(id).options].some(option => option.value === value)) $(id).add(new Option(value,value));
    $(id).value = value;
  }
  $('note').value = existing?.note ?? ''; $('delete').hidden = !existing; $('editor').showModal();
}
$('add').onclick = () => editEvent(null);
$('cancel').onclick = () => $('editor').close();
$('start').onchange = () => { if ($('end').value < $('start').value) $('end').value = $('start').value; };
$('eventForm').onsubmit = event => {
  event.preventDefault();
  if (!$('eventTitle').value.trim()) return;
  if ($('end').value < $('start').value) { alert('종료일은 시작일 이후여야 합니다.'); return; }
  const value = { id: activeId || crypto.randomUUID(), title: $('eventTitle').value.trim(), start: $('start').value, end: $('end').value, category: $('category').value, grades: $('grades').value, note: $('note').value };
  const index = feed.events.findIndex(item => item.id === activeId); if (index < 0) feed.events.push(value); else feed.events[index] = value;
  dirty = true; render(); $('editor').close();
};
$('delete').onclick = () => { if (confirm('이 일정을 초안에서 삭제할까요?')) { feed.events = feed.events.filter(event => event.id !== activeId); dirty = true; render(); $('editor').close(); } };
function importFeed(value) {
  // Original HTML data is parsed as JSON only. Imported scripts are never run.
  const events = Array.isArray(value) ? value : value.events;
  if (!Array.isArray(events)) throw new Error('events 배열이 있는 일정 JSON을 선택하세요.');
  const ids = new Set();
  const mapped = events.map(event => {
    const row = { id: event.id || crypto.randomUUID(), start: event.start ?? event.s, end: event.end ?? event.e ?? event.start ?? event.s,
      title: event.title ?? event.t, category: event.category ?? event.c ?? '학교', grades: event.grades ?? event.g ?? '전체', note: event.note ?? event.n ?? '' };
    const validDate = value => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0,10) === value;
    if (typeof row.id !== 'string' || ids.has(row.id) || typeof row.title !== 'string' || !row.title.trim() || row.title.length > 300 || !validDate(row.start) || !validDate(row.end) || row.end < row.start) throw new Error('일정의 제목·날짜·중복 ID를 확인하세요.');
    ids.add(row.id); return row;
  });
  if (!confirm(`초안을 가져온 일정 ${mapped.length}개로 교체할까요? 서버에는 게시 전까지 반영되지 않습니다.`)) return;
  feed = { schemaVersion:1, name: value.name || feed.name, revision:'draft', updatedAt:new Date().toISOString(), events:mapped };
  dirty = true; render(); status('초안으로 가져왔습니다. 확인한 뒤 저장·게시하세요.');
}
$('import').onchange = () => action(async () => {
  const file = $('import').files[0]; if (!file) return;
  if (file.size > 2 * 1024 * 1024) throw new Error('파일은 2MB 이하여야 합니다.');
  let text = (await file.text()).replace(/^\uFEFF/,'');
  if (/\.html?$/i.test(file.name)) { const match = /const\s+EVENTS\s*=\s*(\[[\s\S]*?\])\s*;/.exec(text); if (!match) throw new Error('기존 HTML에서 EVENTS 일정 배열을 찾지 못했습니다.'); text = match[1]; }
  importFeed(JSON.parse(text)); $('import').value = '';
});
$('export').onclick = () => {
  const blob = new Blob([JSON.stringify({ ...feed, revision:crypto.randomUUID(), updatedAt:new Date().toISOString() },null,2)],{ type:'application/json' });
  const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download='school-calendar.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
};
$('historyLoad').onclick = () => action(async () => {
  history=await api('history');$('history').replaceChildren(...history.map((item,index)=>new Option(`${new Date(item.created_at).toLocaleString('ko-KR')} · ${item.snapshot.name} · ${item.snapshot.events.length}개`,String(index))));
  status(`${history.length}개의 게시 이력을 불러왔습니다.`);
});
$('restore').onclick = () => { const item=history[Number($('history').value)];if(!item)return;if(confirm('선택한 게시 내용을 현재 초안으로 가져올까요? 현재 서버 게시 내용은 유지됩니다.')){feed=structuredClone(item.snapshot);dirty=true;render();status('이전 버전을 초안으로 가져왔습니다. 확인하고 게시하세요.');} };
addEventListener('beforeunload',event=>{if(dirty){event.preventDefault();event.returnValue='';}});
