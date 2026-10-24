"""Bundle the operator template without dependencies, local keys or machine state."""
from pathlib import Path
import hashlib
import zipfile

root = Path(__file__).resolve().parents[1]
out = root.parent / 'dist' / 'SchoolCalendar-Server-Template-1.0.0.zip'
directories = ('api', 'lib', 'public', 'scripts', 'tests', 'examples')
files = [root / name for name in ('README.md', 'PROTOCOL.md', 'feed.schema.json', 'setup.sql',
         'package.json', 'package-lock.json', 'vercel.json', '.env.example', '.gitignore', 'dev.mjs')]
for directory in directories:
    files.extend(path for path in (root / directory).rglob('*') if path.is_file() and '__pycache__' not in path.parts)
out.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as bundle:
    for path in sorted(files):
        bundle.write(path, Path('schedule-server') / path.relative_to(root))
with zipfile.ZipFile(out) as bundle:
    assert not any('/node_modules/' in name or name.endswith(('/.env', '/.env.local')) for name in bundle.namelist())
out.with_suffix('.zip.sha256').write_text(hashlib.sha256(out.read_bytes()).hexdigest().upper() + '  ' + out.name + '\n', encoding='ascii')
print(f'{out} ({out.stat().st_size:,} bytes)')
