"""Hardware-free adapter tests; synthetic data stays in temporary directories."""
import argparse
import contextlib
import io
import json
from pathlib import Path
import queue
import sys
import tempfile
import unittest

PROJECT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(PROJECT/'External/GestureInput'))
sys.path.insert(0,str(PROJECT/'tools/NeuroBridge'))
from unity_bridge import Engine,safe_profile

class FakeWire:
    def __init__(self):self.commands=queue.Queue();self.events=[]
    def send_json(self,payload):self.events.append(payload);return True

class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.wire=FakeWire()
        args=argparse.Namespace(profile='a'*32,data_root=Path(self.tmp.name),source='sim',upstream=PROJECT/'External/GestureInput')
        self.engine=Engine(args,self.wire)
    def tearDown(self):
        with contextlib.redirect_stdout(io.StringIO()):self.engine.close()
        self.tmp.cleanup()
    def test_profile_paths_cannot_escape_data_root(self):
        for name in ('../escape','','player/../../x','a'*31,'A'*32):
            with self.assertRaises(ValueError):safe_profile(self.tmp.name,name)
    def test_complete_upstream_protocol_never_trains_on_simulation(self):
        e=self.engine
        with contextlib.redirect_stdout(io.StringIO()):
            e.start_training();self.assertEqual(len(e.calibration.plan),32)
            for ability in e.abilities:
                for label in ('imagination','only'):self.assertEqual(e.calibration.plan.count((ability,label)),4)
            now=1000.
            for _ in range(20000):
                cal=e.calibration
                if cal.finished:break
                current=cal.current()
                dur=.8 if current[0]=='Push' else 3.2
                e.gesture=current[0] if cal.state=='go' and now-(cal.since or now)<dur else None
                e.confidence=1 if e.gesture else 0
                e.sim_imagination=current[1]=='imagination'
                e.tick(now);now+=.05
            self.assertTrue(e.calibration.finished)
            self.assertEqual(e.calibration.done_trials,32)
            self.assertEqual(e.calibration.missed,0)
            self.assertEqual(e.training_phase,'complete')
        files=list((e.root/'recordings').glob('*.csv.gz'))
        self.assertTrue(files and all(f.name.startswith('sim_') for f in files))
        self.assertFalse((e.root/'model.joblib').exists())
        from eeg.dataset import recordings
        self.assertFalse(recordings([str(e.root/'recordings')]))
    def test_pause_does_not_label_an_interrupted_gesture(self):
        e=self.engine
        with contextlib.redirect_stdout(io.StringIO()):
            e.start_training();e.rec.gesture(1,'Push');e.rec.gesture(1.2,'Push');e.pause()
        self.assertTrue(e.paused)
        self.assertTrue(all(ep.label is None for ep in e.rec.episodes))
        done=e.calibration.done_trials;e.resume()
        self.assertEqual(e.calibration.state,'relax');self.assertEqual(e.calibration.done_trials,done)
    def test_simulated_transport_includes_live_scores_and_protocol_fields(self):
        e=self.engine;e.gesture='Choke';e.sim_imagination=True;e.tick(1000)
        snap=next(x for x in self.wire.events if x['event']=='telemetry')
        self.assertTrue(snap['simulated']);self.assertFalse(snap['personal_model'])
        self.assertEqual(len(snap['scores']),4);self.assertEqual(snap['profile'],'a'*32)
        json.dumps(snap,allow_nan=False)
    def test_profile_switch_isolates_state_and_paths(self):
        e=self.engine;e.configure('b'*32)
        self.assertEqual(e.root,Path(self.tmp.name).resolve()/('b'*32));self.assertIsNone(e.rec)
        self.assertFalse(e.personal)
    def test_personal_fit_uses_only_that_players_data(self):
        from eeg.tests.test_training import calibrated_session
        import joblib
        e=self.engine
        with contextlib.redirect_stdout(io.StringIO()):
            rec,_=calibrated_session(e.root/'recordings',seed=3,reps=3,abilities=tuple(e.abilities));own=rec.save()
            other,_=calibrated_session(Path(self.tmp.name).resolve()/('b'*32)/'recordings',seed=4,reps=3,abilities=tuple(e.abilities));other.save()
            e.fit_personal()
        success,message=e.fit_results.get_nowait();self.assertTrue(success,message)
        bundle=joblib.load(e.root/'model.joblib')
        self.assertEqual(bundle['recordings'],[own.name[:-7]])
        self.assertEqual(len(bundle['spells']),4)
        self.assertTrue((e.root/'training-report.json').exists())

class WireIntegrationTests(unittest.TestCase):
    def test_python_process_transports_start_end_and_telemetry(self):
        import socket,subprocess,time
        with tempfile.TemporaryDirectory() as temporary:
            server=socket.socket();server.bind(('127.0.0.1',0));server.listen();server.settimeout(8)
            process=subprocess.Popen([sys.executable,str(PROJECT/'tools/NeuroBridge/unity_bridge.py'),'--upstream',str(PROJECT/'External/GestureInput'),'--data-root',temporary,'--profile','c'*32,'--source','sim','--port',str(server.getsockname()[1])],stdout=subprocess.PIPE,stderr=subprocess.PIPE)
            connection=None
            try:
                connection,_=server.accept();connection.settimeout(8);reader=connection.makefile('rb')
                def send(**payload):
                    payload['profile']='c'*32;connection.sendall((json.dumps(payload)+'\n').encode())
                def wait_for(kind,ability=None):
                    limit=time.monotonic()+8
                    while time.monotonic()<limit:
                        packet=json.loads(reader.readline())
                        if packet['event']=='error':self.fail(packet['message'])
                        if packet['event']==kind and (ability is None or packet.get('ability')==ability):return packet
                    self.fail('Missing '+kind)
                snap=wait_for('telemetry');self.assertTrue(snap['simulated'])
                send(command='start_training');wait_for('telemetry')
                send(command='simulation_input',gesture='Lightning',imagination=True)
                self.assertEqual(wait_for('ability_started','Lightning')['mode'],'eeg')
                send(command='simulation_input',gesture='',imagination=False)
                wait_for('ability_ended','Lightning')
                send(command='shutdown');process.communicate(timeout=8)
                self.assertEqual(process.returncode,0)
                self.assertFalse(list(Path(temporary).rglob('model.joblib')))
            finally:
                if connection:connection.close()
                server.close()
                if process.poll() is None:process.kill();process.communicate()

if __name__=='__main__':unittest.main(verbosity=2)
