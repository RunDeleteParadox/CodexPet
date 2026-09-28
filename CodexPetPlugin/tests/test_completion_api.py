# SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

"""Offline real-process regression tests, isolated from production config and pipe."""
import json
import pathlib
import shutil
import subprocess
import tempfile
import time
import tomllib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
SCRIPTS = ROOT/'plugins/codex-pet/scripts'
PS = ['powershell.exe', '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass']

class CompletionApiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        (ROOT/'artifacts').mkdir(exist_ok=True)
        cls.compiler = tempfile.TemporaryDirectory(prefix='ApiCompiler-',dir=ROOT/'artifacts')
        cls.exe = pathlib.Path(cls.compiler.name)/'CompletionApiStub.exe'
        subprocess.run(['dotnet','build',str(ROOT/'tests/CompletionApiStub'),'-o',cls.compiler.name,'--nologo'], check=True, capture_output=True)
    @classmethod
    def tearDownClass(cls):
        cls.compiler.cleanup()
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='ApiTest-',dir=ROOT/'artifacts')
        self.path = pathlib.Path(self.tmp.name)
        for file in pathlib.Path(self.compiler.name).iterdir():
            if file.is_file():shutil.copy2(file,self.path/file.name)
        self.data = self.path/'runtime'; self.data.mkdir()
        self.config = {'enabled':True, 'pipeName':'CodexPetHub.ApiTest.'+self.path.name,
                       'completionCodexPath':str(self.path/'CompletionApiStub.exe')}
        (self.data/'config.json').write_text(json.dumps(self.config))
        self.sequence = 0; self.worker = None; self.hub = None; self.process_logs = []
        self.page(date=None)
    def tearDown(self):
        try:
            self.config['enabled'] = False
            self.atomic(self.data/'config.json', self.config)
            if self.worker:
                self.worker.wait(timeout=8)
                self.assertEqual(self.worker.returncode, 0)
        finally:
            # Always reap our own processes, even if an assertion or shutdown fails.
            for process in (self.worker, self.hub):
                if process and process.poll() is None:
                    process.kill(); process.wait(timeout=5)
            for log in self.process_logs: log.close()
            self.tmp.cleanup()
    def spawn(self, name, args):
        # Real files keep Windows child-process diagnostics available without NUL handles.
        stdout = (self.path/(name+'.stdout.log')).open('w',encoding='utf-8')
        stderr = (self.path/(name+'.stderr.log')).open('w',encoding='utf-8')
        self.process_logs.extend([stdout,stderr])
        return subprocess.Popen(args,stdout=stdout,stderr=stderr,creationflags=subprocess.CREATE_NO_WINDOW)
    def atomic(self, path, obj):
        tmp = path.with_suffix('.tmp'); tmp.write_text(json.dumps(obj), encoding='utf-8'); tmp.replace(path)
    def page(self, status='completed', date=1790535038, turn='t1'):
        self.atomic(self.path/'page.json', {'data':[{'id':turn, 'status':status, 'completedAt':date, 'error':None, 'items':[]}]})
    def event(self, hook, turn='t1', tool=''):
        self.sequence += 1
        queue = self.data/'events'; queue.mkdir(exist_ok=True)
        self.atomic(queue/('%04d.json'%self.sequence), {
            'schema':2,'id':str(self.sequence),'timestamp':'2026-09-27T20:00:00.0000000Z',
            'hook':hook,'session':'s1','turn':turn,'tool':tool,'toolName':'Bash','source':'','outcome':'',
            'ownerPid':0,'ownerStartUtc':''})
    def start(self):
        self.event('UserPromptSubmit'); self.event('Stop')
        self.worker = self.spawn('worker',PS+['-File',str(SCRIPTS/'Start-CodexPetWorker.ps1'),'-DataDirectory',str(self.data),'-MaxSeconds','25'])
    def model(self):
        try:return json.loads((self.data/'semantic-state.json').read_text())['model']
        except (OSError,json.JSONDecodeError):return {}
    def wait(self, condition):
        until=time.monotonic()+8
        while time.monotonic()<until:
            if condition():return
            time.sleep(.05)
        diagnostic = {path.name:path.read_text(errors='replace')[-2000:] for path in self.path.glob('*.log')}
        self.fail('Condition not reached; model='+str(self.model())+'; process logs='+str(diagnostic))
    def queried(self):return (self.path/'requests.txt').exists()
    def test_stop_waits_for_explicit_completion_and_never_reads_messages(self):
        self.start(); self.wait(self.queried)
        self.assertEqual(self.model()['reaction'],'stop')
        self.assertFalse((self.data/'last-completion.json').exists())
        self.page(); self.wait(lambda:(self.data/'last-completion.json').exists())
        self.assertEqual(self.model()['reaction'],'success')
        self.assertEqual(self.model()['state'],'idle')
        self.assertEqual(json.loads((self.data/'last-completion.json').read_text())['turn'],'t1')
        self.assertIn('notLoaded limit=1',(self.path/'requests.txt').read_text())
        self.wait(lambda:self.model()['sessions']['s1']['ended'])
        self.event('Stop'); time.sleep(.5)
        self.assertEqual(self.model()['state'],'idle')
        self.assertFalse(self.model()['sessions']['s1']['stopped'])
    def test_stop_continuation_cancels_probe_and_next_stop_rearms_it(self):
        self.start(); self.wait(self.queried)
        self.event('PreToolUse',tool='continued'); self.wait(lambda:self.model().get('state')=='working')
        self.page(); time.sleep(.8)
        self.assertFalse((self.data/'last-completion.json').exists())
        self.event('Stop'); self.wait(lambda:(self.data/'last-completion.json').exists())
    def test_new_turn_and_interrupt_cannot_celebrate_old_completion(self):
        self.start(); self.wait(self.queried)
        self.event('UserPromptSubmit',turn='t2'); self.wait(lambda:self.model().get('state')=='thinking')
        self.page(); time.sleep(.8)
        self.assertFalse((self.data/'last-completion.json').exists())
        self.event('Stop',turn='t2'); self.page(date=None,turn='t2'); time.sleep(.8)
        self.event('Interrupt',turn='t2'); time.sleep(.3); self.page(turn='t2'); time.sleep(.8)
        self.assertFalse((self.data/'last-completion.json').exists())
    def test_unavailable_api_does_not_fabricate_success(self):
        self.config['completionCodexPath']=str(self.path/'missing.exe')
        self.atomic(self.data/'config.json',self.config)
        self.start(); self.wait(lambda:self.model().get('state')=='idle'); time.sleep(1)
        self.assertFalse((self.data/'last-completion.json').exists())
        self.assertEqual(self.model()['reaction'],'stop')
    def test_completion_flows_through_real_pipe_to_hub_and_keeps_success_protected(self):
        hub_exe=ROOT.parent/'CodexPetHub/src/CodexPetHub.Windows/bin/Release/net10.0-windows/CodexPetHub.exe'
        self.assertTrue(hub_exe.is_file(),'Build CodexPetHub.Windows in Release before running integration tests')
        hub_data=self.path/'hub';hub_data.mkdir()
        self.hub=self.spawn('hub',[str(hub_exe),'--headless','--no-serial','--write-status','--seconds','35',
            '--pipe',self.config['pipeName'],'--data',str(hub_data)])
        def status():
            try:return json.loads((hub_data/'status.json').read_text())
            except (OSError,json.JSONDecodeError):return {}
        self.start();self.wait(lambda:status().get('pluginConnected'))
        self.page();self.wait(lambda:status().get('requested',{}).get('reaction')=='success')
        self.event('UserPromptSubmit',turn='t2')
        self.wait(lambda:status().get('codexState')==1)
        self.assertEqual(status()['requested']['reaction'],'success')
        self.wait(lambda:status().get('requested',{}).get('reaction') is None)
        self.assertEqual(status()['requested']['expression'],'thinking')
    def test_installer_restores_original_notifier_and_preserves_other_settings(self):
        config=self.path/'config.toml'
        relay=['powershell.exe',str(SCRIPTS/'Invoke-CodexPetNotify.ps1')]
        original='model="example"\nnotify='+json.dumps(relay)+'\n[features]\nhooks=true\n'
        config.write_text(original)
        forward=['native-notifier.exe','turn-ended']
        (self.data/'notify-forward.json').write_text(json.dumps({'command':forward}))
        cmd=['python',str(ROOT/'Configure-CompletionApi.py'),'--config',str(config),'--data',str(self.data),'--codex',str(self.path/'CompletionApiStub.exe')]
        subprocess.run(cmd,check=True,capture_output=True)
        self.assertEqual(config.read_text(),original)
        for _ in range(2):subprocess.run(cmd+['--apply'],check=True,capture_output=True)
        actual=tomllib.loads(config.read_text())
        self.assertEqual(actual,{'model':'example','notify':forward,'features':{'hooks':True}})
        self.config['completionCodexPath']=str((self.path/'CompletionApiStub.exe').resolve())
        self.assertEqual(json.loads((self.data/'config.json').read_text()),self.config)

if __name__=='__main__':unittest.main()
