# -*- coding: utf-8 -*-
"""Tests for eeg.instant_check - camera movement + current EEG score -> send or ignore.

    python -m eeg.tests.test_instant_check      (from the project root)
    pytest eeg/tests                            (if pytest is installed)
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from eeg.headset import EEGWindow                  # noqa: E402
from eeg.instant_check import InstantEEGCheck      # noqa: E402
from eeg.spell_gate import ABILITIES               # noqa: E402

SPELLS = [s for s, _ in ABILITIES.values()]
FPS = 30


def run(label_fn, p_fn, duration, eeg_ok=lambda t: True, **kw):
    """30 fps camera, EEG window every 0.25 s; ``p_fn(t, spell)`` = P of the window ending at t."""
    chk = InstantEEGCheck({s: 0.5 for s in SPELLS}, wall_clock=lambda: 0.0, **kw)
    events, ignored, t, next_end = [], [], 0.0, 1.0
    for i in range(int(duration * FPS)):
        t = i / FPS
        while next_end <= t:
            chk.add_windows([EEGWindow(next_end - 1.0, next_end, {s: p_fn(next_end, s) for s in SPELLS})])
            next_end += 0.25
        events += [(e["event"], e["ability"]) for e in chk.update(t, label_fn(t), 0.9, eeg_ok(t))]
        ignored += [(d["ability"], d["reason"]) for d in chk.pop_decisions() if d["event"] == "ignored"]
    events += [(e["event"], e["ability"]) for e in chk.close(t)]
    return events, ignored


def gesture(name, start, end):
    return lambda t: name if start <= t < end else None


def test_push_above_threshold_fires_immediately():
    ev, ign = run(gesture("Push", 3.0, 3.5), lambda t, s: 0.8 if s == "push_with_push" else 0.1, 5)
    assert ev == [("ability_triggered", "Push")] and ign == []


def test_held_ability_starts_and_ends_with_gesture():
    ev, _ = run(gesture("Choke", 3.0, 5.0), lambda t, s: 0.8 if s == "choke_squeeze" else 0.1, 6)
    assert ev == [("ability_started", "Choke"), ("ability_ended", "Choke")]


def test_below_threshold_is_ignored_for_whole_gesture():
    # EEG rises only after the gesture was detected -> still ignored (one decision per gesture)
    ev, ign = run(gesture("Heal", 3.0, 5.0), lambda t, s: 0.9 if t > 3.5 and s == "heal" else 0.2, 6)
    assert ev == [] and ign == [("Heal", "below_threshold")]


def test_other_spell_high_does_not_help():
    ev, ign = run(gesture("Lightning", 3.0, 4.0), lambda t, s: 0.9 if s == "heal" else 0.1, 5)
    assert ev == [] and ign == [("Lightning", "below_threshold")]


def test_flicker_shorter_than_grace_is_one_gesture():
    lab = lambda t: None if 3.5 <= t < 3.7 else gesture("Choke", 3.0, 5.0)(t)
    ev, _ = run(lab, lambda t, s: 0.8 if s == "choke_squeeze" else 0.1, 6)
    assert ev == [("ability_started", "Choke"), ("ability_ended", "Choke")]


def test_eeg_unavailable_block_and_fallback_cv():
    p = lambda t, s: 0.9
    ev, ign = run(gesture("Push", 3.0, 3.5), p, 5, eeg_ok=lambda t: False)
    assert ev == [] and ign == [("Push", "eeg_unavailable")]
    ev, _ = run(gesture("Push", 3.0, 3.5), p, 5, eeg_ok=lambda t: False, fallback="cv")
    assert ev == [("ability_triggered", "Push")]


def test_no_eeg_data_is_ignored():
    ev, ign = run(gesture("Push", 0.3, 0.8), lambda t, s: 0.9, 0.9)   # before the first window
    assert ev == [] and ign == [("Push", "no_eeg_data")]


def test_cv_only_mode_sends_everything():
    ev, _ = run(gesture("Heal", 3.0, 4.0), lambda t, s: 0.0, 5, mode="cv_only")
    assert ev == [("ability_started", "Heal"), ("ability_ended", "Heal")]


if __name__ == "__main__":
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print("ok ", t.__name__)
    print("{} tests passed".format(len(tests)))
