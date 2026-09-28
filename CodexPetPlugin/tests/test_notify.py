# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

"""Isolated real-process adapter/installer checks; never edits the user's config."""
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import time
import tomllib
import unittest

root=pathlib.Path(__file__).resolve().parents[1]
plugin=root/'plugins/codex-pet'
adapter=plugin/'scripts/Invoke-CodexPetNotify.ps1'
installer=root/'Configure-CompletionNotify.py'
ps=['powershell.exe','-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File']

class NotifyTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(prefix='CodexPetNotify-')
        self.data=pathlib.Path(self.tmp.name)
        (self.data/'config.json').write_text(json.dumps({'enabled':False,'pipeName':'CodexPetHub.Test.Notify'}),encoding='utf-8')
    def tearDown(self):
        subprocess.run(ps+[str(plugin/'scripts/CodexPet.ps1'),'-Action','disable','-DataDirectory',str(self.data)],capture_output=True,timeout=10)
        self.tmp.cleanup()
    def invoke(self,payload):
        return subprocess.run(ps+[str(adapter),'-DataDirectory',str(self.data),payload],capture_output=True,text=True,timeout=10)
    def test_original_notifier_receives_exact_argument_even_with_pet_disabled(self):
        spy=self.data/'spy.py'; receipt=self.data/'receipt.json'
        spy.write_text('import sys,json,hashlib,pathlib; pathlib.Path(sys.argv[1]).write_text(json.dumps({"hash":hashlib.sha256(sys.argv[-1].encode()).hexdigest(),"fixed":sys.argv[2:-1]}))',encoding='utf-8')
        fixed=['spaces and "quotes"','trailing\\','$(not code); & literal']
        (self.data/'notify-forward.json').write_text(json.dumps({'command':[sys.executable,str(spy),str(receipt),*fixed]}),encoding='utf-8')
        for payload in [json.dumps({'type':'agent-turn-complete','thread-id':'s1','turn-id':'t1','last-assistant-message':'PRIVATE_SENTINEL "hello"\nTA-DA 😄 \\'}),'invalid JSON with "quotes" and \\']:
            if receipt.exists(): receipt.unlink()
            result=self.invoke(payload); self.assertEqual(result.returncode,0,result.stderr)
            until=time.monotonic()+5
            while not receipt.exists() and time.monotonic()<until: time.sleep(.025)
            actual=json.loads(receipt.read_text())
            self.assertEqual(actual,{'hash':hashlib.sha256(payload.encode()).hexdigest(),'fixed':fixed})
    def test_real_adapter_only_persists_completion_metadata(self):
        (self.data/'config.json').write_text(json.dumps({'enabled':True,'pipeName':'CodexPetHub.Test.Notify.'+self.data.name}),encoding='utf-8')
        payload=json.dumps({'type':'agent-turn-complete','thread-id':'s1','turn-id':'t1','cwd':'PRIVATE_SENTINEL','input-messages':['PRIVATE_SENTINEL'],'last-assistant-message':'PRIVATE_SENTINEL'})
        result=self.invoke(payload); self.assertEqual(result.returncode,0,result.stderr)
        checkpoint=self.data/'semantic-state.json'; until=time.monotonic()+8
        while not checkpoint.exists() and time.monotonic()<until: time.sleep(.05)
        model=json.loads(checkpoint.read_text())['model']
        self.assertEqual((model['state'],model['reaction']),('idle','success'))
        for path in self.data.rglob('*.json'):
            self.assertNotIn('PRIVATE_SENTINEL',path.read_text())
    def test_installer_preserves_config_and_forwarder_on_reinstallation(self):
        config=self.data/'codex.toml'; existing=['some-native-notifier.exe','turn-ended']
        baseline={'model':'example','notify':existing,'features':{'example':True}}
        original='model = "example"\r\nnotify = '+json.dumps(existing)+'\r\n[features]\r\nexample = true\r\n'
        config.write_bytes(original.encode())
        cmd=[sys.executable,str(installer),'--config',str(config),'--data',str(self.data),'--plugin-root',str(plugin)]
        subprocess.run(cmd,check=True,capture_output=True)
        self.assertEqual(config.read_bytes(),original.encode())
        self.assertFalse((self.data/'notify-forward.json').exists())
        for _ in range(2): subprocess.run(cmd+['--apply'],check=True,capture_output=True)
        parsed=tomllib.loads(config.read_text())
        self.assertEqual(parsed['model'],baseline['model']); self.assertEqual(parsed['features'],baseline['features'])
        self.assertEqual(json.loads((self.data/'notify-forward.json').read_text())['command'],existing)
        self.assertEqual(parsed['notify'][-2:],['-DataDirectory',str(self.data.resolve())])
        self.assertTrue(any(p.read_bytes()==original.encode() for p in self.data.glob('config-before-completion-*.toml')))

if __name__=='__main__': unittest.main()
