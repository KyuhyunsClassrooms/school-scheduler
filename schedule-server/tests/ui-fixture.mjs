// Local UI fixture only. Production APIs have no test bypass.
import { createServer } from 'node:http';
import { randomUUID } from 'node:crypto';
const feed={schemaVersion:1,name:'예시학교 학사일정',revision:'fixture',updatedAt:'2026-10-24T00:00:00Z',events:[
  {id:'a',start:'2026-10-26',end:'2026-10-26',title:'교직원 회의',category:'교직원',grades:'전체',note:'회의실'},
  {id:'b',start:'2026-11-02',end:'2026-11-06',title:'학부모 상담 주간',category:'학교',grades:'전체',note:''}
]};
let state={draft:structuredClone(feed),published:feed,version:randomUUID()},history=[];
createServer(async(req,res)=>{
  res.setHeader('Content-Type','application/json');
  const url=new URL(req.url,'http://localhost');
  if(url.pathname.startsWith('/auth/v1/'))return res.end(JSON.stringify(url.pathname.endsWith('/user')?{app_metadata:{calendar_admin:true}}:{access_token:'fixture',expires_in:3600,user:{app_metadata:{calendar_admin:true}}}));
  if(req.method==='PATCH') {
    const chunks=[];for await(const chunk of req)chunks.push(chunk);
    if(url.searchParams.get('version')!==`eq.${state.version}`)return res.end('[]');
    const change=JSON.parse(Buffer.concat(chunks));state={...state,...change};
    if(change.published)history.unshift({revision:change.published.revision,snapshot:change.published,created_at:change.published.updatedAt});
  }
  res.end(JSON.stringify(url.pathname.endsWith('calendar_versions')?history:[state]));
}).listen(3102,'127.0.0.1',async()=>{
  process.env.SUPABASE_URL='http://127.0.0.1:3102';process.env.SUPABASE_PUBLISHABLE_KEY='fixture';process.env.SUPABASE_SECRET_KEY='fixture';process.env.PORT='3100';await import('../dev.mjs');
});
