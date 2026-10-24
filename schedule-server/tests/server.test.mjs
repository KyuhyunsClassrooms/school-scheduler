import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { PGlite } from '@electric-sql/pglite';
import { Readable } from 'node:stream';
import admin from '../api/admin.js';
import calendar from '../api/calendar.js';
import { validateFeed } from '../lib/feed.js';

const fixture = { schemaVersion:1, name:'테스트 학교', revision:'first', updatedAt:'2026-10-24T00:00:00Z', events:[
  { id:'event-1', start:'2026-10-24', end:'2026-10-24', title:'주말 행사', category:'학교', grades:'전체', note:'' }
] };
test('schema, PostgreSQL RLS, publishing and access controls', async t => {
  const db = new PGlite();
  await db.exec(`create role anon; create role authenticated; create role service_role bypassrls;
    create schema auth; grant usage on schema auth,public to anon,authenticated,service_role;
    create function auth.jwt() returns jsonb language sql stable as $$ select coalesce(nullif(current_setting('request.jwt.claims',true),''),'{}')::jsonb $$;`);
  await db.exec(await readFile(new URL('../setup.sql',import.meta.url),'utf8'));
  async function queryAs(role, sql, params=[], isAdmin=false) {
    await db.exec(`set role ${role}`);
    try {
      await db.query("select set_config('request.jwt.claims',$1,false)",[JSON.stringify({app_metadata:{calendar_admin:isAdmin}})]);
      return await db.query(sql,params);
    } finally {await db.exec('reset role');}
  }
  const fetchOriginal=globalThis.fetch;
  process.env.SUPABASE_URL='https://fixture.supabase.co';process.env.SUPABASE_PUBLISHABLE_KEY='publishable-fixture';process.env.SUPABASE_SECRET_KEY='secret-fixture';
  globalThis.fetch=async (address,options={})=>{
    const url=new URL(address),token=options.headers?.Authorization?.slice(7);
    if(url.pathname==='/auth/v1/token')return Response.json({access_token:'admin',user:{app_metadata:{calendar_admin:true}}});
    if(url.pathname==='/auth/v1/user')return Response.json({app_metadata:{calendar_admin:token==='admin'}});
    const isSecret=options.headers.apikey==='secret-fixture';
    const role=isSecret?'service_role':'authenticated',isAdmin=token==='admin';
    try {
      let result;
      if(url.pathname.endsWith('calendar_versions'))result=await queryAs(role,'select revision,snapshot,created_at from public.calendar_versions order by created_at desc limit 30',[],isAdmin);
      else if(options.method==='PATCH') {
        const input=JSON.parse(options.body),oldVersion=url.searchParams.get('version').slice(3);
        result=input.published ? await queryAs(role,'update public.calendar_state set draft=$1,published=$2,version=$3 where id=\'main\' and version=$4 returning *',[input.draft,input.published,input.version,oldVersion],isAdmin) :
          await queryAs(role,'update public.calendar_state set draft=$1,version=$2 where id=\'main\' and version=$3 returning *',[input.draft,input.version,oldVersion],isAdmin);
      } else result=await queryAs(role,'select * from public.calendar_state where id=\'main\'',[],isAdmin);
      return Response.json(result.rows);
    } catch {return Response.json({error:'denied'},{status:403});}
  };
  async function call(handler,method,input,token) {
    const req=Readable.from([]);req.method=method;req.body=input;req.headers=token?{authorization:`Bearer ${token}`} : {};
    const res={code:200,headers:{},setHeader(key,value){this.headers[key]=value;},status(code){this.code=code;return this;},json(value){this.value=value;}};
    await handler(req,res);return res;
  }
  try {
    await t.test('validates exact dates, unique IDs and empty snapshots',()=>{
      assert.equal(validateFeed(fixture).events.length,1);
      assert.throws(()=>validateFeed({...fixture,events:[{...fixture.events[0],start:'2026-02-30'}]}));
      assert.throws(()=>validateFeed({...fixture,events:[fixture.events[0],fixture.events[0]]}));
      assert.throws(()=>validateFeed({...fixture,schemaVersion:2}));
      assert.equal(validateFeed({...fixture,events:[]}).events.length,0);
    });
    await t.test('anon cannot bypass the API and regular users cannot read or edit',async()=>{
      await assert.rejects(()=>queryAs('anon','select * from public.calendar_state'));
      assert.equal((await queryAs('authenticated','select * from public.calendar_state')).rows.length,0);
      assert.equal((await queryAs('authenticated',"update public.calendar_state set version='bad' returning *")).rows.length,0);
      await assert.rejects(()=>queryAs('authenticated',"insert into public.calendar_versions values ('bad','{}',now())"));
      assert.equal((await call(admin,'POST',{action:'load'},'ordinary')).code,403);
      assert.equal((await call(admin,'POST',{action:'load'})).code,401);
    });
    await t.test('drafts stay unpublished; publishing records history atomically',async()=>{
      assert.equal((await call(calendar,'GET')).code,404);
      let state=(await call(admin,'POST',{action:'load'},'admin')).value;
      const save=await call(admin,'POST',{action:'save',version:state.version,feed:fixture},'admin');assert.equal(save.code,200);state=save.value;
      assert.equal((await call(calendar,'GET')).code,404);
      const publish=await call(admin,'POST',{action:'publish',version:state.version,feed:fixture},'admin');assert.equal(publish.code,200);
      assert.equal((await call(calendar,'GET')).value.events[0].title,'주말 행사');
      assert.equal((await call(admin,'POST',{action:'history'},'admin')).value.length,1);
      assert.equal((await call(admin,'POST',{action:'publish',version:state.version,feed:fixture},'admin')).code,409);
      const empty=await call(admin,'POST',{action:'publish',version:publish.value.version,feed:{...fixture,events:[]}},'admin');assert.equal(empty.code,200);
      assert.equal((await call(calendar,'GET')).value.events.length,0);
      assert.equal((await call(admin,'POST',{action:'history'},'admin')).value.length,2);
    });
    await t.test('private feed token, methods and draft authorization',async()=>{
      process.env.SCHEDULE_READ_TOKEN='a-fixture-token-with-at-least-32-characters';
      assert.equal((await call(calendar,'GET')).code,401);
      assert.equal((await call(calendar,'GET',undefined,'wrong')).code,401);
      const ok=await call(calendar,'GET',undefined,process.env.SCHEDULE_READ_TOKEN);assert.equal(ok.code,200);assert.equal(ok.headers['Cache-Control'],'private, no-store');
      assert.equal((await call(calendar,'POST')).code,405);
      assert.equal((await call(admin,'GET')).code,405);
      assert.equal((await call(admin,'POST',{action:'publish',feed:fixture,version:'anything'},'ordinary')).code,403);
      delete process.env.SCHEDULE_READ_TOKEN;
    });
  } finally {globalThis.fetch=fetchOriginal;await db.close();}
});
