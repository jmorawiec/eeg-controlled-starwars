# -*- coding: utf-8 -*-
"""End-to-end smoke test of integrations/gesture_pipeline.py without a webcam.

    python -m eeg.tests.smoke_pipeline

* a fake camera returns blank frames, and the hand classifier is replaced by a
  script (Choke for 4 s, pause, Push for 2 s, with flicker),
* the EEG runs through real gpype threads (``--eeg sim``, random signal) and
  then with ``--eeg off`` (camera only),
* events go over real TCP to a local server.

Checks: the loop runs, EEG windows are produced and scored, only the new
``ability_*`` events are sent as one JSON object per line, Choke start/end
pair up, Push is a single trigger. A third run (``--eeg sim --eeg_record``)
checks that the in-game recording is saved and loads as Choke + Push trials.
"""

import glob
import json
import socket
import sys
import tempfile
import threading
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

import integrations.gesture_pipeline as gpipe     # noqa: E402
from model import GesturePrediction                # noqa: E402


def server(port, lines, ready):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind(("127.0.0.1", port))
        s.listen(1)
        ready.set()
        s.settimeout(30)
        conn, _ = s.accept()
        buf = b""
        with conn:
            conn.settimeout(30)
            while True:
                data = conn.recv(4096)
                if not data:
                    break
                buf += data
                while b"\n" in buf:
                    line, buf = buf.split(b"\n", 1)
                    lines.append(json.loads(line.decode("utf-8")))


class FakeCapture:
    """Blank 30 fps frames."""

    def __init__(self, *a, **k):
        self.t0 = time.monotonic()

    def set(self, *a):
        return True

    def isOpened(self):
        return True

    def read(self):
        time.sleep(1 / 30)
        return True, np.zeros((540, 960, 3), np.uint8)

    def release(self):
        pass


def scripted(t0):
    """Replaces GesturePipeline.detect: Choke 1-5 s, Push 6.5-8.5 s, flicker every 0.7 s."""
    def detect(self, image, timestamp_ms=None):
        t = time.monotonic() - t0
        label = None
        if 1.0 <= t < 5.0:
            label = "Choke"
        elif 6.5 <= t < 8.5:
            label = "Push"
        if label and (t % 0.7) < 0.07:
            label = None                                   # 2 dropped frames
        active = label is not None
        return GesturePrediction(hand_sign_id=0, label=label or "inactive", confidence=0.95 if active else 0.0,
                                 both_hands_visible=active, height_threshold_met=active, classified=active,
                                 is_inactive=not active, height_diff=100.0, height_diff_threshold=60,
                                 features=[], hands=[])
    return detect


def run(eeg_mode, port, seconds=10.5, extra=()):
    lines, ready = [], threading.Event()
    th = threading.Thread(target=server, args=(port, lines, ready), daemon=True)
    th.start()
    ready.wait(5)
    gpipe.cv.VideoCapture = FakeCapture
    gpipe.GesturePipeline.detect = scripted(time.monotonic())
    windows = []
    orig_update = gpipe.AbilityBridge.update

    def spy(self, prediction):
        n0 = len(self.gate._windows)
        out = orig_update(self, prediction)
        windows.append(len(self.gate._windows) - n0)
        return out
    gpipe.AbilityBridge.update = spy
    # with random EEG the decisions are random - use camera fallback so events flow
    args = gpipe.get_args(["--port", str(port), "--no_display", "--eeg", eeg_mode,
                           "--eeg_fallback", "cv", "--max_frames", str(int(seconds * 30)),
                           "--eeg_min_windows", "2", "--eeg_min_windows_push", "2", *extra])
    gpipe.run_camera(args)
    gpipe.AbilityBridge.update = orig_update
    th.join(5)
    return lines, sum(max(0, w) for w in windows)


def main():
    ok = True
    for mode, port in (("off", 56301), ("sim", 56302)):
        lines, nwin = run(mode, port)
        print("\n--eeg {}: {} events over TCP, ~{} EEG windows scored".format(mode, len(lines), nwin))
        for ev in lines:
            print("   ", json.dumps(ev))
        names = [e["event"] for e in lines]
        checks = {
            "only ability_* events": all(n.startswith("ability_") for n in names),
            "every event has ability/kind/mode/timestamp": all(
                {"ability", "kind", "mode", "timestamp"} <= set(e) for e in lines),
            "Choke started/ended in pairs": names.count("ability_started") == names.count("ability_ended"),
            "Push at most once": sum(1 for e in lines if e["ability"] == "Push") <= 1,
            "no Choke flicker (<= 1 start)": sum(1 for e in lines if e["ability"] == "Choke"
                                                 and e["event"] == "ability_started") <= 1,
        }
        if mode == "off":
            checks["camera-only fires Choke and Push"] = (
                ("ability_started", "Choke") in [(e["event"], e["ability"]) for e in lines]
                and ("ability_triggered", "Push") in [(e["event"], e["ability"]) for e in lines])
        else:
            checks["EEG windows produced (sim)"] = nwin >= 20
        for name, passed in checks.items():
            print("   {} {}".format("PASS" if passed else "FAIL", name))
            ok &= passed
    ok &= record_check()
    print("\nSMOKE TEST", "PASSED" if ok else "FAILED")
    return 0 if ok else 1


def record_check():
    import pandas as pd
    from eeg.dataset import parse_trials
    with tempfile.TemporaryDirectory() as d:
        run("sim", 56303, extra=("--eeg_record", "--record_label", "imagination", "--record_dir", d))
        files = glob.glob(d + "/sim_game_*.csv.gz")
        checks = {"recording saved (sim_game_*.csv.gz + events)": len(files) == 1
                  and len(glob.glob(d + "/sim_game_*_events.jsonl")) == 1}
        if files:
            df = pd.read_csv(files[0])
            tr = parse_trials(df, "x", "game")
            checks["~10 s of EEG recorded"] = 8 * 250 < len(df) < 13 * 250
            checks["Choke + Push trials, labelled imagination"] = (
                sorted(tr.spell) == ["choke_squeeze", "push_with_push"] and tr.y.tolist() == [1, 1])
            checks["gesture column has the camera labels"] = {"Choke", "Push"} <= set(df.gesture.dropna())
    print("\n--eeg sim --eeg_record")
    for name, passed in checks.items():
        print("   {} {}".format("PASS" if passed else "FAIL", name))
    return all(checks.values())


if __name__ == "__main__":
    raise SystemExit(main())
