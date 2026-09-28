# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

"""Configure the read-only stdio completion reader; restore the original notify.

Preview by default. Does not install hooks, modify trust, or start a Codex turn.
"""
import argparse
import datetime
import json
import pathlib
import re
import tomllib

parser = argparse.ArgumentParser()
parser.add_argument('--config', type=pathlib.Path, default=pathlib.Path.home()/'.codex/config.toml')
parser.add_argument('--data', type=pathlib.Path, default=pathlib.Path.home()/'.codex-pet-plugin/v1.1')
parser.add_argument('--codex', type=pathlib.Path, required=True)
parser.add_argument('--apply', action='store_true')
args = parser.parse_args()
if not args.codex.is_file():
    raise SystemExit('Codex executable missing; no changes made.')
raw = args.config.read_bytes()
text = raw.decode('utf-8-sig')
config = tomllib.loads(text)
existing = config.get('notify', [])
ours = any(pathlib.PureWindowsPath(x).name.lower() == 'invoke-codexpetnotify.ps1' for x in existing)
proposed = text
if ours:
    forward = json.loads((args.data/'notify-forward.json').read_text(encoding='utf-8-sig'))['command']
    if not isinstance(forward, list) or not all(isinstance(x, str) for x in forward):
        raise SystemExit('Invalid original notify receipt; no changes made.')
    matches = list(re.finditer(r'^notify\s*=\s*\[[^\r\n]*\][ \t]*(?:#[^\r\n]*)?\r?$', text, re.M))
    if len(matches) != 1:
        raise SystemExit('Expected one single-line notify setting; no changes made.')
    match = matches[0]
    proposed = text[:match.start()] + 'notify = ' + json.dumps(forward, ensure_ascii=False) + text[match.end():]
    assert tomllib.loads(proposed)['notify'] == forward
assert {k:v for k,v in tomllib.loads(proposed).items() if k != 'notify'} == {k:v for k,v in config.items() if k != 'notify'}
runtime_path = args.data/'config.json'
runtime_raw = runtime_path.read_bytes()
runtime = json.loads(runtime_raw.decode('utf-8-sig'))
runtime['completionCodexPath'] = str(args.codex.resolve())
if args.apply:
    if args.config.read_bytes() != raw or runtime_path.read_bytes() != runtime_raw:
        raise SystemExit('Configuration changed concurrently; no changes made.')
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    (args.data/('config-before-completion-api-'+stamp+'.toml')).write_bytes(raw)
    (args.data/('runtime-before-completion-api-'+stamp+'.json')).write_bytes(runtime_raw)
    tmp = runtime_path.with_suffix('.api.tmp')
    tmp.write_text(json.dumps(runtime, indent=2), encoding='utf-8')
    tmp.replace(runtime_path)
    if proposed != text:
        tmp = args.config.with_name('config.toml.codexpet-api.tmp')
        tmp.write_bytes(proposed.encode('utf-8'))
        tmp.replace(args.config)
    print('Completion API configured. Original notify preserved. No threads started.')
else:
    print('PREVIEW ONLY. Read-only Codex executable:', args.codex.resolve())
    print('Restore previous notifier:', ours)
