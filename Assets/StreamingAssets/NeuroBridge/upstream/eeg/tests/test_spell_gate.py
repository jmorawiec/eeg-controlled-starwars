# -*- coding: utf-8 -*-
"""Tests for eeg.spell_gate - synthetic camera labels + EEG windows.

    python -m eeg.tests.test_spell_gate      (from the project root)
    pytest eeg/tests                         (if pytest is installed)
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from eeg.headset import EEGWindow                     # noqa: E402  (dataclass only, no gpype import)
from eeg.spell_gate import ABILITIES, GateConfig, SpellGate  # noqa: E402

SPELLS = [s for s, _ in ABILITIES.values()]
FPS = 30


def run(label_fn, p_fn, duration, mode="eeg", fallback="block", eeg_ok=lambda t: True, cfg=None):
    """Simulate ``duration`` s of camera frames (30 fps) and EEG windows (every 0.25 s).
    ``p_fn(t, spell)`` = P(imagination) of the window ending at t."""
    gate = SpellGate({s: 0.5 for s in SPELLS}, cfg, mode=mode, fallback=fallback, wall_clock=lambda: 0.0)
    events, t, next_end = [], 0.0, 1.0
    for i in range(int(duration * FPS)):
        t = i / FPS
        while next_end <= t:
            gate.add_windows([EEGWindow(next_end - 1.0, next_end, {s: p_fn(next_end, s) for s in SPELLS})])
            next_end += 0.25
        for e in gate.update(t, label_fn(t), 0.9, eeg_ok(t)):
            events.append((round(t, 2), e))
    events += [(round(t, 2), e) for e in gate.close(t)]
    return events


def names(events):
    return [(e["event"], e["ability"]) for _, e in events]


def flicker(label, start, end, every=0.5, frames=3):
    """``label`` between start and end, but ``frames`` bad frames every ``every`` s."""
    def fn(t):
        if not start <= t < end:
            return None
        return None if (t - start) % every < frames / FPS else label
    return fn


HIGH = lambda t, s: 0.8                               # noqa: E731
LOW = lambda t, s: 0.3                                # noqa: E731


def test_choke_held_survives_camera_flicker():
    ev = run(flicker("Choke", 1.0, 6.0), HIGH, 8)
    assert names(ev) == [("ability_started", "Choke"), ("ability_ended", "Choke")], ev
    (t0, start), (t1, end) = ev
    # flicker starts with 3 bad frames -> onset ~1.1 s; 6th window starting after it ends at 3.5 s
    assert 3.4 <= t0 <= 3.6, t0
    assert end["reason"] == "gesture_released" and 6.0 <= t1 <= 6.5, (t1, end)
    assert start["mode"] == "eeg" and start["eeg_score"] >= 0.5


def test_gesture_without_imagination_is_blocked():
    for ability in ABILITIES:
        ev = run(flicker(ability, 1.0, 6.0), LOW, 8)
        assert ev == [], (ability, ev)


def test_push_triggers_once_per_gesture():
    ev = run(lambda t: "Push" if 1.0 <= t < 4.0 else None, HIGH, 6)
    assert names(ev) == [("ability_triggered", "Push")], ev
    assert 2.45 <= ev[0][0] <= 2.6, ev                # 3 windows after onset -> ~1.5 s


def test_short_push_still_fires_in_tail():
    ev = run(lambda t: "Push" if 1.0 <= t < 1.6 else None, HIGH, 4)
    assert names(ev) == [("ability_triggered", "Push")], ev


def test_two_pushes_two_triggers():
    lab = lambda t: "Push" if (1.0 <= t < 3.0 or 5.0 <= t < 7.0) else None  # noqa: E731
    ev = run(lab, HIGH, 9)
    assert names(ev) == [("ability_triggered", "Push")] * 2, ev


def test_camera_blip_is_ignored():
    ev = run(lambda t: "Heal" if 1.0 <= t < 1.1 else None, HIGH, 4)
    assert ev == [], ev


def test_short_eeg_dip_keeps_ability_long_dip_ends_it():
    def p(t, s):
        if 5.0 <= t < 5.5 or 7.0 <= t < 10.5:
            return 0.1
        return 0.8
    ev = run(lambda t: "Lightning" if 1.0 <= t < 14.0 else None, p, 16)
    kinds = [(e["event"], e.get("reason")) for _, e in ev]
    assert kinds[0] == ("ability_started", None), kinds
    first_end = next(t for t, e in ev if e["event"] == "ability_ended")
    assert first_end > 7.0, ("short dip ended the ability", ev)
    assert ("ability_ended", "imagination_lost") in kinds, kinds
    assert kinds[-1] == ("ability_ended", "gesture_released"), kinds
    assert kinds.count(("ability_started", None)) == 2, kinds   # re-started after recovery


def test_release_disabled_keeps_ability_for_whole_gesture():
    p = lambda t, s: 0.1 if t >= 4.0 else 0.8          # noqa: E731
    ev = run(lambda t: "Heal" if 1.0 <= t < 9.0 else None, p, 10, cfg=GateConfig(release_s=0))
    assert [(e["event"], e.get("reason")) for _, e in ev] == [
        ("ability_started", None), ("ability_ended", "gesture_released")], ev


def test_switching_gestures_ends_then_starts():
    lab = lambda t: "Choke" if 1.0 <= t < 4.0 else ("Heal" if 4.0 <= t < 7.0 else None)  # noqa: E731
    ev = run(lab, HIGH, 9)
    assert names(ev) == [("ability_started", "Choke"), ("ability_ended", "Choke"),
                         ("ability_started", "Heal"), ("ability_ended", "Heal")], ev


def test_eeg_is_spell_specific():
    p = lambda t, s: 0.8 if s == "choke_squeeze" else 0.2   # noqa: E731
    assert run(flicker("Heal", 1.0, 6.0), p, 8) == []
    assert names(run(flicker("Choke", 1.0, 6.0), p, 8))[0] == ("ability_started", "Choke")


def test_cv_only_mode_ignores_eeg():
    ev = run(lambda t: "Choke" if 1.0 <= t < 3.0 else ("Push" if 4.0 <= t < 5.0 else None), LOW, 6,
             mode="cv_only")
    assert names(ev) == [("ability_started", "Choke"), ("ability_ended", "Choke"),
                         ("ability_triggered", "Push")], ev
    assert all(e["mode"] == "cv" and "eeg_score" not in e for _, e in ev)
    assert ev[0][0] < 1.2                             # camera only: no EEG wait


def test_eeg_unavailable_block_vs_camera_fallback():
    lab = lambda t: "Heal" if 1.0 <= t < 4.0 else None   # noqa: E731
    assert run(lab, HIGH, 6, eeg_ok=lambda t: False, fallback="block") == []
    ev = run(lab, HIGH, 6, eeg_ok=lambda t: False, fallback="cv")
    assert names(ev) == [("ability_started", "Heal"), ("ability_ended", "Heal")]
    assert ev[0][1]["mode"] == "cv"


def test_artifact_windows_are_skipped():
    gate = SpellGate({s: 0.5 for s in SPELLS}, wall_clock=lambda: 0.0)
    gate.update(0.0, "Choke", 0.9)
    gate.update(0.2, "Choke", 0.9)
    gate.add_windows([EEGWindow(0.0 + i * 0.25, 1.0 + i * 0.25, {}, artifact=True) for i in range(10)])
    assert gate.update(3.5, "Choke", 0.9) == []
    assert gate.view()["n"] == 0


def test_fragmented_camera_gesture_still_starts_held_ability():
    # camera splits a 4 s choke into pieces with 0.4-0.5 s gaps (longer than cv_grace)
    lab = lambda t: "Choke" if (1.0 <= t < 1.7 or 2.1 <= t < 2.9 or 3.4 <= t < 5.0) else None  # noqa: E731
    ev = run(lab, HIGH, 7)
    assert names(ev) == [("ability_started", "Choke"), ("ability_ended", "Choke")], ev
    assert ev[0][1]["since_gesture_start"] >= 2.2      # evidence accumulated from the first piece


def test_fragmented_push_fires_once():
    lab = lambda t: "Push" if (1.0 <= t < 1.5 or 1.95 <= t < 2.6) else None  # noqa: E731
    ev = run(lab, HIGH, 5)
    assert names(ev) == [("ability_triggered", "Push")], ev


def test_pushes_far_apart_fire_twice_but_held_restart_needs_new_evidence():
    lab = lambda t: "Push" if (1.0 <= t < 2.0 or 3.5 <= t < 4.5) else None  # noqa: E731
    assert names(run(lab, HIGH, 7)) == [("ability_triggered", "Push")] * 2
    # held ability that was running: a gap > grace ends it; coming back is a new episode
    lab = lambda t: "Heal" if (1.0 <= t < 5.0 or 5.6 <= t < 9.0) else None  # noqa: E731
    ev = run(lab, HIGH, 11)
    assert names(ev) == [("ability_started", "Heal"), ("ability_ended", "Heal")] * 2, ev


def main():
    tests = [(n, f) for n, f in sorted(globals().items()) if n.startswith("test_") and callable(f)]
    failed = 0
    for n, f in tests:
        try:
            f()
            print("PASS", n)
        except AssertionError as e:
            failed += 1
            print("FAIL", n, "->", e)
    print("\n{} passed, {} failed".format(len(tests) - failed, failed))
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
