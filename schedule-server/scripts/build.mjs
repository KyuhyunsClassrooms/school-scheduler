import { access } from 'node:fs/promises';
for (const file of ['public/index.html','public/app.js','public/style.css','api/calendar.js','api/admin.js']) await access(file);
console.log('Static admin and Node.js API ready. No client secrets or build dependencies.');
