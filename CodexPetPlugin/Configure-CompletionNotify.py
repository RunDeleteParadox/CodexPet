# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

"""Prepare or apply the one Codex notify setting, preserving its current consumer."""
import argparse
import datetime
import json
import pathlib
import re
import tomllib

parser = argparse.ArgumentParser()
parser.add_argument('--config', type=pathlib.Path, default=pathlib.Path.home()/'.codex/config.toml')
parser.add_argument('--data', type=pathlib.Path, default=pathlib.Path.home()/'.codex-pet-plugin/v1.1')
parser.add_argument('--plugin-root', type=pathlib.Path, required=True)
parser.add_argument('--apply', action='store_true')
args = parser.parse_args()
entry = (args.plugin_root/'scripts/Invoke-CodexPetNotify.ps1').resolve()
if not entry.is_file():
    raise SystemExit('Completion adapter missing; no changes made.')
raw = args.config.read_bytes()
text = raw.decode('utf-8-sig')
config = tomllib.loads(text)
existing = config.get('notify', [])
if not isinstance(existing, list) or not all(isinstance(x, str) for x in existing):
    raise SystemExit('Unexpected notify command; no changes made.')
forward_file = args.data/'notify-forward.json'
already_ours = any(pathlib.PureWindowsPath(x).name.lower() == 'invoke-codexpetnotify.ps1' for x in existing)
if already_ours and not forward_file.exists():
    raise SystemExit('Existing adapter has no forwarding receipt; refusing to lose the original notifier.')
forward = json.loads(forward_file.read_text(encoding='utf-8-sig'))['command'] if already_ours else existing
command = ['powershell.exe', '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', str(entry), '-DataDirectory', str(args.data.resolve())]
# The documented JSON notification is appended by Codex as the positional argument.
replacement = 'notify = ' + json.dumps(command, ensure_ascii=False)
matches = list(re.finditer(r'^notify\s*=\s*\[[^\r\n]*\]\s*(?:#[^\r\n]*)?$', text, re.M))
if existing and len(matches) != 1:
    raise SystemExit('Only a single-line top-level notify setting is supported; no changes made.')
if matches:
    match = matches[0]
    proposed = text[:match.start()] + replacement + text[match.end():]
else:
    proposed = replacement + '\n' + text
parsed = tomllib.loads(proposed)
assert parsed['notify'] == command
assert {k:v for k,v in parsed.items() if k!='notify'} == {k:v for k,v in config.items() if k!='notify'}
if args.apply:
    args.data.mkdir(parents=True, exist_ok=True)
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    backup = args.data/('config-before-completion-'+stamp+'.toml')
    backup.write_bytes(raw)
    # Never overwrite unrelated config edits made during preparation.
    if args.config.read_bytes() != raw:
        raise SystemExit('Codex config changed concurrently; no config change made.')
    temporary = forward_file.with_suffix('.json.tmp')
    temporary.write_text(json.dumps({'command':forward}, indent=2), encoding='utf-8')
    temporary.replace(forward_file)
    temp_config = args.config.with_name(args.config.name+'.codexpet.tmp')
    temp_config.write_text(proposed, encoding='utf-8', newline='')
    temp_config.replace(args.config)
    print('Completion notify installed; previous notify consumer preserved. Backup:', backup)
else:
    print('PREVIEW ONLY. Existing notify consumers:', 1 if forward else 0)
    print(replacement)
