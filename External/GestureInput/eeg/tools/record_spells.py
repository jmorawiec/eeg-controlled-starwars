# -*- coding: utf-8 -*-
"""Cued spell recording (no camera): every spell N x with and N x without imagination.

    python -m eeg.tools.record_spells                         # all 4 spells, 5 repeats (~11 min)
    python -m eeg.tools.record_spells --spells push_with_push --repeats 10
    python -m eeg.tools.record_spells --sim                   # no headset (plumbing only)

Per spell, ``--repeats`` times (order of the two conditions random per repeat,
``--fixed_order`` = imagination first as in the first sessions)::

    relax (--relax s) -> <spell>: gesture + imagination (--task s)
    relax             -> <spell>: gesture without imagination

then a final relax. The terminal says what comes next while you relax and
beeps when a task starts. Before the protocol: ``--prep`` s with the contact
status every 5 s (run ``python -m eeg.tools.check_signal`` first).

Same signal path as the game (OSCAR + 1-30 Hz + 48-52 Hz, gpype). Saves
``eeg/data/spells_<date>_<time>.csv.gz`` with ``cue`` =
``<spell>_gesture_imagination`` / ``<spell>_gesture_only`` / ``relax`` and
``trial`` = repeat; ``python -m eeg.train fit`` picks it up automatically.
Ctrl+C aborts without saving.
"""

import argparse
import random
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from eeg.dataset import DATA_DIR                              # noqa: E402
from eeg.headset import DEFAULT_MODEL_PATH, DEFAULT_SERIAL    # noqa: E402
from eeg.recorder import SessionRecorder                      # noqa: E402

# (cue label, instruction shown) - instructions exactly as defined by the player
TASKS = [
    ("lightning_push", "lightning push"),
    ("heal", "heal focused with no movement/muscle tension"),
    ("choke_squeeze", "choke squeeze"),
    ("push_with_push", "push with push"),
]
COUNTDOWN_S = 5


def protocol(spells, repeats, task_s, relax_s, shuffle, rng):
    """[(cue, text, seconds, trial)]"""
    text = dict(TASKS)
    steps = []
    for key in spells:
        for trial in range(1, repeats + 1):
            conds = [("imagination", "gesture + imagination"), ("only", "gesture without imagination")]
            if shuffle:
                rng.shuffle(conds)
            for k, (cond, what) in enumerate(conds):
                first = trial == 1 and k == 0
                steps += [("relax", "relax - {}: remember the gesture and the imaginary trigger".format(text[key])
                           if first else "relax", relax_s, trial),
                          ("{}_gesture_{}".format(key, cond),
                           "{}: {}  ({}/{})".format(text[key], what, trial, repeats), task_s, trial)]
    steps.append(("relax", "relax", relax_s, repeats))
    return steps


def banner(text):
    print("\n{}\n  {}\n{}".format("=" * 60, text, "=" * 60), flush=True)


def wait(stream, seconds):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        stream.poll()                     # moves samples to the recorder, keeps contact fresh
        time.sleep(0.02)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sim", action="store_true")
    ap.add_argument("--serial", default=DEFAULT_SERIAL)
    ap.add_argument("--spells", nargs="+", choices=[k for k, _ in TASKS], default=[k for k, _ in TASKS])
    ap.add_argument("--repeats", type=int, default=5)
    ap.add_argument("--task", type=int, default=5, help="seconds per task (5 = what the models expect)")
    ap.add_argument("--relax", type=int, default=10)
    ap.add_argument("--prep", type=int, default=60, help="seconds of contact check before the protocol")
    ap.add_argument("--fixed_order", action="store_true", help="imagination always first")
    ap.add_argument("--out_dir", default=str(DATA_DIR))
    a = ap.parse_args(argv)

    steps = protocol(a.spells, a.repeats, a.task, a.relax, not a.fixed_order, random.Random())
    total = sum(s[2] for s in steps)
    print("Protocol: {} steps, {} min {} s after {} s prep.".format(len(steps), total // 60, total % 60, a.prep))

    from eeg.headset import EEGSpellStream
    stream = EEGSpellStream(model_path=DEFAULT_MODEL_PATH, mode="sim" if a.sim else "headset", serial=a.serial)
    rec = SessionRecorder(a.out_dir, prefix="spells")
    print("Connecting ..." if not a.sim else "Simulated source ...", flush=True)
    stream.start()
    try:
        for left in range(a.prep, 0, -5):
            wait(stream, min(5, left))
            print("  prep {:>3} s left - {}".format(left - min(5, left), stream.status().text()), flush=True)
        if not stream.status().streaming:
            print("No data from the headset - run: python -m eeg.tools.doctor --headset")
            return 3
        banner("GET READY - recording starts in")
        for n in range(COUNTDOWN_S, 0, -1):
            print("  {}".format(n), flush=True)
            wait(stream, 1)
        stream.recorder = rec
        t0 = time.monotonic()
        elapsed = 0
        for i, (cue, text, dur, trial) in enumerate(steps, 1):
            rec.mark(time.monotonic(), cue, trial)
            banner("[{}/{}]  {}".format(i, len(steps), text) + ("\a" if cue != "relax" else ""))
            if cue == "relax" and i < len(steps):
                print("  next: {}".format(steps[i][1]), flush=True)
            for s in range(dur, 0, -1):
                print("  {}".format(s), end=" ", flush=True)
                wait(stream, max(0.0, t0 + elapsed + (dur - s + 1) - time.monotonic()))   # no drift
            elapsed += dur
            print(flush=True)
            st = stream.status()
            if st.blocking:
                print("  ! {}".format(st.text()), flush=True)
        wait(stream, 0.5)
        stream.recorder = None
    except KeyboardInterrupt:
        print("\nAborted - nothing was saved.")
        return 1
    finally:
        stream.stop()
    rec.save(sim=a.sim)
    print("Next: python -m eeg.train list   /   python -m eeg.train fit")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
