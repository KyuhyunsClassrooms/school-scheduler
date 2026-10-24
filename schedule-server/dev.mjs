import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { resolve, sep } from 'node:path';
import calendar from './api/calendar.js';
import admin from './api/admin.js';
try {
  const env = await readFile('.env.local','utf8');
  for (const line of env.split(/\r?\n/)) {
    const match = /^([A-Z_]+)=(.*)$/.exec(line);
    if (match && !process.env[match[1]]) process.env[match[1]] = match[2].replace(/^['"]|['"]$/g,'');
  }
} catch (error) { if (error.code !== 'ENOENT') throw error; }
const root = resolve('public');
createServer(async (req,res) => {
  res.status = status => { res.statusCode=status;return res; };
  res.json = value => { res.setHeader('Content-Type','application/json; charset=utf-8');res.end(JSON.stringify(value)); };
  try {
    const path = new URL(req.url,'http://localhost').pathname;
    if (path === '/api/calendar') return await calendar(req,res);
    if (path === '/api/admin') return await admin(req,res);
    const file=resolve(root,'.'+(path==='/'?'/index.html':path));
    if (!file.startsWith(root+sep)) {res.statusCode=403;return res.end();}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html; charset=utf-8');
    res.end(await readFile(file));
  } catch {res.statusCode=404;res.end('Not found');}
}).listen(process.env.PORT||3100,'127.0.0.1',()=>console.log('Admin: http://localhost:'+(process.env.PORT||3100)));
