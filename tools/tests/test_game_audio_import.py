import importlib.util
import json
import hashlib
from pathlib import Path
from tempfile import mkdtemp

repo = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('game_audio_import', repo/'tools/import_game_audio.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
catalog = json.loads((repo/'docs/content/game-audio-catalog.json').read_text(encoding='utf-8'))
samples = [a for a in catalog['assets'] if a['deliveredFormat']=='mp3'][:2]
data = [(repo/a['localPath']).read_bytes() for a in samples]
scratch = repo/'.artifacts/audio-import-tests'
scratch.mkdir(parents=True, exist_ok=True)
work = Path(mkdtemp(prefix='import-fixture-', dir=scratch))
m.REPO = work
m.OUTPUT = work/'src/CardGame.Wpf/Assets/Audio/Official'
m.CATALOG = work/'docs/content/game-audio-catalog.json'
m.CSV_INDEX = work/'docs/content/game-audio-index.csv'
m.SYNC = work/'sync'
cache = work/'cache'
cache.mkdir()
base = samples[0].copy()
target = work/base['localPath']
target.parent.mkdir(parents=True)
target.write_bytes(data[0])
m.write_json_atomic(m.CATALOG, {'schemaVersion':1, 'assets':[base], 'configSource':{'fixture':True}})
m.public_config = lambda: (None, None, None, {'fixture':True})
m.apply_config_mapping = lambda *args: {'fixture':True}
calls = []
payload = data[0]
def fetch(url, target):
    calls.append(url)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(payload)
    return payload, hashlib.sha256(payload).hexdigest()
m.fetch_public = fetch
alias = 'https://web.sanguosha.com/220/h5_2/res/runtime/pc/voice/spell/test-alias.mp3'
(cache/'data_1').write_bytes(alias.encode())
m.sync_cache(cache, False, False)
result = json.loads(m.CATALOG.read_text(encoding='utf-8'))
assert len(result['assets'])==1 and alias in result['assets'][0]['sourceUrlAliases']
assert not (m.OUTPUT/'OL/spell/test-alias.mp3').exists()
stamp = m.CATALOG.stat().st_mtime_ns
m.sync_cache(cache, False, False)
assert len(calls)==1 and m.CATALOG.stat().st_mtime_ns==stamp

payload = data[1]
conflict = 'https://web.sanguosha.com/220/h5_2/res/runtime/pc/voice/spell/test-conflict.mp3'
conflict_target = m.OUTPUT/'OL/spell/test-conflict.mp3'
conflict_target.parent.mkdir(parents=True, exist_ok=True)
conflict_target.write_bytes(data[0])
(cache/'data_1').write_bytes((alias+' '+conflict).encode())
m.sync_cache(cache, False, False)
failures = json.loads((m.SYNC/'failed-urls.json').read_text(encoding='utf-8'))
assert conflict in failures
assert conflict_target.read_bytes()==data[0]
assert len(json.loads(m.CATALOG.read_text(encoding='utf-8'))['assets'])==1
count = len(calls)
m.sync_cache(cache, False, False)
assert len(calls)==count
print('PASS: duplicate URL becomes an alias without an extra asset; unchanged sync performs no fetch/write; conflicting target is preserved and failure is retained without automatic retry.')
