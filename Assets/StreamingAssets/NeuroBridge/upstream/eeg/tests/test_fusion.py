# -*- coding: utf-8 -*-
"""Tests for eeg.fusion - continuous camera / EEG / combined prediction.

    python -m eeg.tests.test_fusion      (from the project root)
    pytest eeg/tests                     (if pytest is installed)
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from eeg.fusion import EEGMonitor, FusionConfig, cv_ability_probs, eeg_relative, fuse  # noqa: E402
from eeg.headset import EEGWindow                                                       # noqa: E402
from eeg.spell_gate import ABILITIES                                                    # noqa: E402

LABELS = ["Push", "Heal", "Choke", "Lightning", "inactive"]
SPELLS = {a: s for a, (s, _) in ABILITIES.items()}
THR = {s: 0.6 for s in SPELLS.values()}


def window(t_end, **probs_by_ability):
    probs = {SPELLS[a]: probs_by_ability.get(a, 0.1) for a in ABILITIES}
    return EEGWindow(t_end - 1.0, t_end, probs)


def test_eeg_relative_maps_threshold_to_half():
    assert eeg_relative(0.6, 0.6) == 0.5
    assert eeg_relative(0.0, 0.6) == 0.0
    assert abs(eeg_relative(1.0, 0.6) - 1.0) < 1e-9
    assert eeg_relative(0.3, 0.6) == 0.25
    assert eeg_relative(None, 0.6) is None


def test_cv_probs_without_classification_are_zero():
    assert cv_ability_probs(LABELS, None) == {a: 0.0 for a in ABILITIES}
    cv = cv_ability_probs(LABELS, [0.1, 0.0, 0.8, 0.05, 0.05])
    assert cv["Choke"] == 0.8 and "inactive" not in cv


def test_monitor_averages_last_windows_and_skips_artifacts():
    m = EEGMonitor(THR, n_windows=2, stale_s=2.0)
    m.add_windows([window(1.0, Choke=0.2), window(1.25, Choke=0.8), window(1.5, Choke=1.0)])
    m.add_windows([EEGWindow(0.75, 1.75, {}, artifact=True)])
    assert m.last_artifact
    assert abs(m.scores(now=1.8)["Choke"] - 0.9) < 1e-9       # mean of 0.8 and 1.0
    assert m.scores(now=5.0)["Choke"] is None                  # stale


def test_both_agree_gives_prediction():
    m = EEGMonitor(THR)
    m.add_windows([window(1.0, Choke=0.9)])
    cv = cv_ability_probs(LABELS, [0.05, 0.0, 0.9, 0.0, 0.05])
    f = fuse(cv, m.scores(1.1), m.ability_thresholds())
    assert f.eeg_used and f.label == "Choke" and f.cv_label == "Choke" and f.eeg_label == "Choke"


def test_camera_without_imagination_is_not_predicted():
    m = EEGMonitor(THR)
    m.add_windows([window(1.0, Choke=0.1)])
    cv = cv_ability_probs(LABELS, [0.0, 0.0, 0.95, 0.0, 0.05])
    f = fuse(cv, m.scores(1.1), m.ability_thresholds())
    assert f.label is None and f.cv_label == "Choke" and f.eeg_label is None


def test_imagination_without_gesture_is_not_predicted():
    m = EEGMonitor(THR)
    m.add_windows([window(1.0, Heal=0.95)])
    f = fuse(cv_ability_probs(LABELS, None), m.scores(1.1), m.ability_thresholds())
    assert f.label is None and f.eeg_label == "Heal"


def test_no_eeg_falls_back_to_camera():
    cv = cv_ability_probs(LABELS, [0.8, 0.1, 0.0, 0.0, 0.1])
    f = fuse(cv, {}, {}, eeg_available=False)
    assert not f.eeg_used and f.label == "Push" and abs(f.confidence - 0.8) < 1e-9


def test_cv_weight_shifts_balance():
    m = EEGMonitor(THR)
    m.add_windows([window(1.0, Lightning=0.65)])                 # just above threshold
    cv = cv_ability_probs(LABELS, [0.0, 0.0, 0.0, 0.9, 0.1])
    low = fuse(cv, m.scores(1.1), m.ability_thresholds(), config=FusionConfig(cv_weight=0.2))
    high = fuse(cv, m.scores(1.1), m.ability_thresholds(), config=FusionConfig(cv_weight=0.8))
    assert high.fused["Lightning"] > low.fused["Lightning"]


if __name__ == "__main__":
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print("ok ", t.__name__)
    print("{} tests passed".format(len(tests)))
