import { createServer } from 'node:http';
let mode='first';
const feed = () => ({schemaVersion:1,name:'연결 검증 학교',revision:mode,updatedAt:'2026-10-24T00:00:00Z',events:mode==='empty'?[]:[
  {id:'shared',start:'2026-10-24',end:'2026-10-24',title:mode==='changed'?'수정된 일정':'첫 일정',category:'학교',grades:'전체',note:''},
  ...(mode==='first'?[{id:'removed',start:'2026-10-25',end:'2026-10-25',title:'삭제될 일정'}]:[])
]});
createServer((req,res)=>{
  const url=new URL(req.url,'http://localhost');
  if(url.pathname==='/control'){mode=url.searchParams.get('mode');return res.end('OK');}
  if(url.pathname==='/auth'&&req.headers.authorization!=='Bearer fixture-token'){res.statusCode=401;return res.end();}
  if(url.pathname==='/redirect'){res.statusCode=302;res.setHeader('Location','/feed');return res.end();}
  if(mode==='offline'){res.statusCode=503;return res.end();}
  res.setHeader('Content-Type','application/json');
  if(mode==='invalid')return res.end('{"schemaVersion":99}');
  if(mode==='huge')return res.end('x'.repeat(2*1024*1024+1));
  res.end(JSON.stringify(feed()));
}).listen(3101,'127.0.0.1',()=>console.log('Fixture ready http://127.0.0.1:3101'));
