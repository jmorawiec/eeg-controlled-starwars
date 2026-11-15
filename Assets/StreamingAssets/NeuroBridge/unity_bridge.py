#!/usr/bin/env python3
"""Duplex Unity adapter for the pinned upstream camera, EEG, Calibration and trainer.

Normal acquisition uses the real camera + BCI Core-8. --source sim is an
explicit transport/protocol fixture: its recordings are marked sim_ and cannot
be fitted as personal EEG. Unity's built-in practice needs no Python process.
"""
from __future__ import annotations
import argparse
import json
import math
import os
from pathlib import Path
import queue
import re
import shutil
import socket
import sys
import threading
import time


def safe_profile(root, profile):
    if not re.fullmatch(r'[a-f0-9]{32}', profile):
        raise ValueError('Invalid profile identifier')
    return Path(root).resolve() / profile


class Wire:
    def __init__(self, host, port):
        self.sock = socket.create_connection((host, port), timeout=5)
        self.sock.settimeout(None)
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.commands = queue.Queue(maxsize=128)
        self.closed = False
        self.lock = threading.Lock()
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        try:
            with self.sock.makefile('rb') as stream:
                while not self.closed:
                    line = stream.readline(65537)
                    if not line or len(line) > 65536:
                        break
                    try:
                        item = json.loads(line)
                        if isinstance(item, dict):
                            self.commands.put_nowait(item)
                    except (ValueError, queue.Full):
                        continue
        finally:
            self.closed = True

    def send_json(self, payload):
        if self.closed:
            return False
        try:
            data = (json.dumps(payload, allow_nan=False) + '\n').encode()
            with self.lock:
                self.sock.sendall(data)
            return True
        except (OSError, ValueError):
            self.closed = True
            return False

    def close(self):
        self.closed = True
        try:
            self.sock.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        self.sock.close()


class Engine:
    def __init__(self, args, wire):
        self.args, self.wire = args, wire
        self.profile = args.profile
        self.root = safe_profile(args.data_root, self.profile)
        self.root.mkdir(parents=True, exist_ok=True)
        self.stream = self.camera = self.capture = None
        self.rec = self.calibration = None
        self.training_phase, self.training_message = 'ready', ''
        self.personal = False
        self.paused = False
        self.had_bad_contact = False
        self.stop = False
        self.last_sent = 0
        self.gesture = None
        self.confidence = 0
        self.sim_imagination = False
        self.sim_last_window = 0
        self.fit_results = queue.Queue()
        self.training_thread = None
        self.generation = 0
        from eeg.spell_gate import ABILITIES, GateConfig, SpellGate
        from eeg.recorder import Calibration, SessionRecorder
        self.abilities, self.Gate, self.Config = ABILITIES, SpellGate, GateConfig
        self.Calibration, self.Recorder = Calibration, SessionRecorder
        self.thresholds = {spell: .5 for spell, _ in ABILITIES.values()}
        self.probs = {spell: .1 for spell, _ in ABILITIES.values()}
        if args.source == 'eeg':
            from integrations.gesture_pipeline import GesturePipeline
            from eeg.headset import EEGSpellStream
            import cv2
            model = self.root / 'model.joblib'
            self.personal = model.is_file()
            if not self.personal:
                model = Path(args.upstream) / 'eeg/models/live_models.joblib'
            self.stream = EEGSpellStream(model_path=model, serial=args.serial, mode='headset')
            self.stream.start()
            self.thresholds = self.stream.thresholds
            self.camera = GesturePipeline()
            self.capture = cv2.VideoCapture(args.camera)
            if not self.capture.isOpened():
                raise RuntimeError('Camera unavailable. Grant camera access to the selected Python interpreter.')
        self.gate = SpellGate(self.thresholds, GateConfig(), mode='eeg', fallback='block')

    def send(self, event):
        return self.wire.send_json(dict(event, profile=self.profile))

    def fail(self, text):
        self.training_message = text
        self.send(dict(event='error', message=text))

    def configure(self, profile):
        if profile == self.profile:
            return
        if self.training_thread and self.training_thread.is_alive():
            self.fail('Wait for model fitting to finish before changing profiles.')
            return
        self.cancel()
        root = safe_profile(self.args.data_root, profile)
        root.mkdir(parents=True, exist_ok=True)
        self.profile, self.root = profile, root
        self.personal = (root / 'model.joblib').is_file()
        if self.stream:
            from eeg.headset import SpellWindowProcessor, load_bundle
            model = root / 'model.joblib' if self.personal else Path(self.args.upstream) / 'eeg/models/live_models.joblib'
            self.stream.processor = SpellWindowProcessor(load_bundle(model))
            self.thresholds = self.stream.thresholds = self.stream.processor.thresholds
            self.stream._contact = [("all", "warming up")]
            self.stream._contact_at = 0
        self.reset_gate()

    def reset_gate(self):
        for event in self.gate.close(time.monotonic()):
            self.send(event)
        self.gate = self.Gate(self.thresholds, self.Config(), mode='eeg', fallback='block')

    def start_training(self):
        if self.training_thread and self.training_thread.is_alive():
            self.fail('A model is already being fitted.')
            return
        if self.stream and not self.stream.status().available:
            self.fail('Wait for usable EEG data and correct electrode contact.')
            return
        self.cancel()
        self.reset_gate()
        self.rec = self.Recorder(self.root / 'recordings', cfg=self.Config())
        self.calibration = self.Calibration(self.rec, reps=4, relax_s=5, timeout_s=20)
        self.training_phase = 'rest'
        self.training_message = ''
        self.had_bad_contact = False
        self.paused = False
        if self.stream:
            self.stream.recorder = self.rec

    def save_recording(self, usable=False):
        if self.stream:
            self.stream.recorder = None
        path = None
        if self.rec:
            path = self.rec.save(sim=self.args.source != 'eeg')
            self.rec = None
        if path and self.args.source == 'eeg' and not usable:
            quarantine = self.root / 'incomplete'
            quarantine.mkdir(parents=True, exist_ok=True)
            events = path.parent / (path.name[:-7] + '_events.jsonl')
            shutil.move(str(path), str(quarantine / path.name))
            if events.exists():
                shutil.move(str(events), str(quarantine / events.name))
            path = quarantine / path.name
        return path

    def cancel(self):
        if self.rec:
            # An interrupted gesture must never become a labelled training example.
            for ep in self.rec.tracker.flush():
                ep.label, ep.source = None, 'cancelled'
                self.rec._finish(ep)
            self.save_recording()
        self.calibration = None
        if self.training_phase != 'fitting':
            self.training_phase = 'ready'
        self.paused = False

    def pause(self):
        self.paused = True
        if self.rec:
            for ep in self.rec.tracker.flush():
                ep.label, ep.source = None, 'paused'
                self.rec._finish(ep)
        self.reset_gate()

    def resume(self):
        self.paused = False
        if self.calibration and not self.calibration.finished:
            self.calibration.state, self.calibration.since = 'relax', time.monotonic()

    def command(self, item):
        command = item.get('command')
        if command == 'shutdown':
            self.stop = True
            return
        if command == 'configure':
            self.configure(item.get('profile', ''))
            return
        if item.get('profile') != self.profile:
            return
        if command == 'start_training':
            self.start_training()
        elif command == 'cancel_training':
            self.cancel()
            self.reset_gate()
        elif command == 'pause_training':
            self.pause()
        elif command == 'resume_training':
            self.resume()
        elif command == 'simulation_input' and self.args.source == 'sim':
            self.gesture = item.get('gesture') if item.get('gesture') in self.abilities else None
            self.confidence = 1. if self.gesture else 0.
            self.sim_imagination = bool(item.get('imagination'))

    def finish_training(self):
        calibration = self.calibration
        usable = not calibration.missed and not self.had_bad_contact
        path = self.save_recording(usable=usable)
        if not path or not usable:
            self.training_phase = 'failed'
            self.training_message = 'Incomplete session or electrode contact lost. Check the headset and repeat. Previous model unchanged.'
            return
        if self.args.source != 'eeg':
            self.training_phase = 'complete'
            self.training_message = 'Simulation complete. No personal EEG model was fitted.'
            return
        self.training_phase = 'fitting'
        self.training_message = 'Fitting this player’s recordings only.'
        self.training_thread = threading.Thread(target=self.fit_personal, daemon=True)
        self.training_thread.start()

    def fit_personal(self):
        try:
            from eeg.train import cmd_fit, get_args
            import joblib
            temporary = self.root / 'model.pending.joblib'
            # Explicit directory: never silently include the upstream player's dataset.
            cmd_fit(get_args(['fit', str(self.root / 'recordings'), '--out', str(temporary)]))
            bundle = joblib.load(temporary)
            if set(bundle['spells']) != {s for s, _ in self.abilities.values()}:
                raise ValueError('The fitted bundle does not contain all four spells.')
            if any(not math.isfinite(m.get('cv_auc', 0)) or m.get('cv_auc', 0) < .65 or min(m['n_trials']['imagination'],m['n_trials']['only']) < 3 for m in bundle['spells'].values()):
                raise ValueError('The signal did not separate both conditions reliably. Adjust contact and repeat training.')
            model = self.root / 'model.joblib'
            if model.exists():
                shutil.copy2(model, self.root / 'model.previous.joblib')
            os.replace(temporary, model)
            report = dict(channels=bundle['channels'], spells={k:dict(threshold=m['threshold'], held_out_auc=m['cv_auc'], trials=m['n_trials']) for k,m in bundle['spells'].items()})
            (self.root / 'training-report.json').write_text(json.dumps(report, indent=2))
            self.fit_results.put((True, 'Personal EEG model saved and loaded.'))
        except BaseException as error:
            self.fit_results.put((False, str(error)))

    def snapshot(self, now, available, contacts):
        cal = self.calibration
        phase, ability, condition, index, total, done, missed, countdown = self.training_phase, '', '', 0, 32, 0, 0, 0
        if cal:
            index, total, done, missed = cal.i, len(cal.plan), cal.done_trials, cal.missed
            if not cal.finished:
                ability, condition = cal.current()
                phase = ('rest' if cal.state == 'relax' else 'act') if self.training_phase not in ('failed','fitting') else self.training_phase
                countdown = max(0, (cal.relax_s if cal.state=='relax' else cal.timeout_s) - (now - (cal.since or now)))
        if self.paused:
            phase = 'paused'
        scores=[]
        for ability_name, (spell, _) in self.abilities.items():
            scores.append(dict(ability=ability_name, cv=self.confidence if self.gesture==ability_name else 0., eeg=float(self.probs.get(spell,.0)), threshold=float(self.thresholds[spell])))
        spell = self.abilities[self.gesture][0] if self.gesture in self.abilities else None
        status = 'Simulation fixture. No real EEG.' if self.args.source != 'eeg' else self.stream.status().text()
        if not self.personal and self.args.source == 'eeg':
            status += ' · Complete personal training to enable powers.'
        self.send(dict(event='telemetry', source=self.args.source, simulated=self.args.source!='eeg', available=available, personal_model=self.personal,
                       gesture=self.gesture or '', eeg_score=float(self.probs.get(spell,.0)), eeg_threshold=float(self.thresholds.get(spell,.5)),
                       scores=scores, contacts=contacts, message=status, training_active=bool(self.rec), training_phase=phase,
                       training_ability=ability, training_condition=condition, trial=index,total=total,done_trials=done,missed=missed,
                       countdown=countdown,training_message=self.training_message or (cal.message if cal else '')))

    def tick(self, now):
        while not self.wire.commands.empty():
            try:
                self.command(self.wire.commands.get_nowait())
            except Exception as error:
                self.fail(str(error))
        available=True
        contacts=[]
        if self.args.source == 'eeg':
            import cv2
            ok, image = self.capture.read()
            if not ok:
                self.gesture, self.confidence = None, 0.
                available=False
                self.fail('Camera stopped delivering frames. Check camera access.')
            else:
                prediction = self.camera.detect(cv2.flip(image,1))
                self.gesture=prediction.label if prediction.active and prediction.label in self.abilities else None
                self.confidence=prediction.confidence
            windows=self.stream.poll()
            status=self.stream.status()
            available=available and status.available
            problems=dict(status.contact)
            from eeg.features import CH
            contacts=[dict(channel=c,status=problems.get(c,'OK')) for c in CH]
            if 'all' in problems:
                contacts=[dict(channel=c,status=problems['all']) for c in CH]
        else:
            from types import SimpleNamespace
            windows=[]
            if now-self.sim_last_window>=.25:
                self.sim_last_window=now
                probs={spell:.9 if self.sim_imagination else .1 for spell,_ in self.abilities.values()}
                windows=[SimpleNamespace(t_start=now-1,t_end=now,probs=probs,artifact=False)]
                if self.rec:
                    import numpy as np
                    self.rec.add(np.zeros((63,8)),now)
        self.gate.add_windows(windows)
        for window in windows:
            if not window.artifact:
                self.probs.update(window.probs)
        events=self.gate.update(now,self.gesture,self.confidence,available and not self.paused)
        for event in events:
            # Baseline models aid signal checks; only a player's own fitted model can cast.
            if self.personal or self.args.source!='eeg' or event['event']=='ability_ended':
                self.send(event)
        if self.rec and not self.paused:
            finished=self.rec.gesture(now,self.gesture if available else None)
            if not available:
                self.had_bad_contact=True
            for event in events:
                self.rec.event(now,event)
            self.calibration.update(now,finished)
            if self.calibration.finished:
                self.finish_training()
        if not self.fit_results.empty():
            success,message=self.fit_results.get_nowait()
            self.training_phase='complete' if success else 'failed'
            self.training_message=message
            if success:
                from eeg.headset import SpellWindowProcessor,load_bundle
                self.stream.processor=SpellWindowProcessor(load_bundle(self.root/'model.joblib'))
                self.thresholds=self.stream.thresholds=self.stream.processor.thresholds
                self.personal=True
                self.reset_gate()
        if now-self.last_sent>=.1:
            self.last_sent=now
            self.snapshot(now,available,contacts)

    def close(self):
        self.cancel()
        if hasattr(self,'gate'):
            self.reset_gate()
        if self.stream:
            self.stream.stop()
        if self.capture:
            self.capture.release()
        if self.camera:
            self.camera.close()


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream',type=Path,required=True)
    parser.add_argument('--data-root',type=Path,required=True)
    parser.add_argument('--profile',required=True)
    parser.add_argument('--source',choices=['eeg','sim'],default='eeg')
    parser.add_argument('--serial',default='U8-2025.08.21')
    parser.add_argument('--host',default='127.0.0.1')
    parser.add_argument('--port',type=int,default=5555)
    parser.add_argument('--camera',type=int,default=0)
    args=parser.parse_args(argv)
    args.upstream=args.upstream.resolve()
    safe_profile(args.data_root,args.profile)
    sys.path.insert(0,str(args.upstream))
    wire=Wire(args.host,args.port)
    engine=None
    try:
        engine=Engine(args,wire)
        while not wire.closed and not engine.stop:
            engine.tick(time.monotonic())
            time.sleep(.015)
    except BaseException as error:
        wire.send_json(dict(event='error',profile=args.profile,message=str(error)))
        print(type(error).__name__+': '+str(error),file=sys.stderr,flush=True)
        return 1
    finally:
        if engine:
            engine.close()
        wire.close()
    return 0

if __name__=='__main__':
    raise SystemExit(main())
