# -*- coding: utf-8 -*-
"""Tests for in-game recording (eeg.recorder), loading (eeg.dataset) and training (eeg.train).

    python -m eeg.tests.test_training      (from the project root)
    pytest eeg/tests                       (if pytest is installed)

No hardware: EEG is synthetic - correlated noise on 8 channels, and during
"imagination" gestures an extra 12 Hz rhythm on Cz/POz, so a working
pipeline must separate the two conditions.
"""

import sys
import tempfile
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

import joblib                                              # noqa: E402

from eeg import dataset as D                               # noqa: E402
from eeg import train as T                                 # noqa: E402
from eeg.features import CH, RATE, STEP                    # noqa: E402
from eeg.recorder import Calibration, EpisodeTracker, SessionRecorder   # noqa: E402

FPS = 30


def frames(tracker_or_rec, label_fn, duration, t0=0.0):
    out = []
    for i in range(int(duration * FPS)):
        t = t0 + i / FPS
        res = tracker_or_rec.update(t, label_fn(t)) if isinstance(tracker_or_rec, EpisodeTracker) \
            else tracker_or_rec.gesture(t, label_fn(t))
        out += res
    return out


def test_tracker_ignores_flicker_and_rejoins():
    tr = EpisodeTracker()
    # Choke 1-5 s with a 0.2 s drop (grace) and a 0.6 s drop (rejoin), then Heal 7-9 s
    def lab(t):
        if 1 <= t < 5 and not (2.0 <= t < 2.2) and not (3.0 <= t < 3.6):
            return "Choke"
        if 7 <= t < 9:
            return "Heal"
        return None
    eps = frames(tr, lab, 12) + tr.flush()
    assert [e.ability for e in eps] == ["Choke", "Heal"], eps
    assert abs(eps[0].onset - 1.0) < 0.05 and abs(eps[0].end - 5.0) < 0.05


def test_tracker_separate_gestures():
    tr = EpisodeTracker()
    lab = lambda t: "Push" if (1 <= t < 1.6 or 4 <= t < 4.6) else None
    eps = frames(tr, lab, 7) + tr.flush()
    assert [e.ability for e in eps] == ["Push", "Push"]


def synth_eeg(t, imag_mask, rng):
    """samples x 8: common source + noise (all channels 'touching'), 12 Hz on Cz/POz when imagining."""
    common = rng.normal(0, 4, len(t))
    x = common[:, None] * 0.7 + rng.normal(0, 3, (len(t), 8))
    rhythm = 6 * np.sin(2 * np.pi * 12 * t)
    for c in ("Cz", "POz"):
        x[:, CH.index(c)] += rhythm * imag_mask
    return x


def calibrated_session(out_dir, seed, reps=3, abilities=("Choke", "Push")):
    """Simulated calibration session: the player follows every prompt."""
    rng = np.random.default_rng(seed)
    rec = SessionRecorder(out_dir)
    cal = Calibration(rec, reps=reps, abilities=list(abilities), relax_s=2.0, seed=seed)
    dt, t, eeg_t = 1 / FPS, 0.0, 0.0
    doing, start = None, 0.0
    while not cal.finished and t < 600:
        # player: on "go", gesture for 3 s (Push 0.8 s), imagining if asked
        if cal.state == "go" and doing is None and t - cal.since > 0.5:
            doing, start = cal.current(), t
        label = None
        if doing is not None:
            length = 0.8 if doing[0] == "Push" else 3.0
            if t - start < length:
                label = doing[0]
            elif cal.state == "relax" or cal.current() != doing:
                doing = None
        # EEG arrives in 8-sample chunks
        while eeg_t + 8 / RATE <= t:
            ts = eeg_t + np.arange(8) / RATE
            imag = np.array([doing is not None and doing[1] == "imagination" and start <= s < start + 3.2
                             for s in ts], float)
            rec.add(synth_eeg(ts, imag, rng), ts[-1])
            eeg_t += 8 / RATE
        cal.update(t, rec.gesture(t, label))
        t += dt
    return rec, cal


def test_calibration_labels_every_prompt():
    with tempfile.TemporaryDirectory() as d:
        rec, cal = calibrated_session(d, seed=1, reps=2)
        assert cal.finished and cal.done_trials == 8 and cal.missed == 0, (cal.done_trials, cal.missed)
        n = rec.counts()
        assert n == {("Choke", "imagination"): 2, ("Choke", "only"): 2,
                     ("Push", "imagination"): 2, ("Push", "only"): 2}, n


def test_saved_recording_loads_as_game_trials():
    with tempfile.TemporaryDirectory() as d:
        rec, _ = calibrated_session(d, seed=2, reps=2)
        path = rec.save()
        assert path.name.startswith("game_") and path.name.endswith(".csv.gz")
        assert (path.parent / (path.name[:-7] + "_events.jsonl")).exists()
        (r,) = D.recordings([str(path)])
        assert r.kind == "game"
        L = r.load()
        assert L.good == CH, L.good
        tr = L.trials
        assert sorted(tr.spell.unique()) == ["choke_squeeze", "push_with_push"]
        assert (tr.groupby("spell").y.agg(["sum", "count"]).to_numpy() == [[2, 4], [2, 4]]).all()
        X, y, g = D.spell_windows(L, "choke_squeeze", CH)
        # 3 s gesture -> window starts 0 .. 3 s after onset = 13 windows, 16 at most
        per_trial = np.unique(g, return_counts=True)[1]
        assert len(per_trial) == 4 and per_trial.min() >= 11 and per_trial.max() <= 16, per_trial
        X, y, g = D.spell_windows(L, "push_with_push", CH)
        assert np.unique(g, return_counts=True)[1].max() <= int(0.8 * RATE / STEP) + 2


def test_sim_recordings_are_not_training_data():
    assert not D.is_recording_file("sim_game_20260101_000000.csv.gz")
    assert not D.is_recording_file("game_20260101_000000_events.jsonl")
    assert D.is_recording_file("game_20260101_000000.csv.gz")


def test_fit_calibrate_evaluate_on_game_data():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        abilities = ("Lightning", "Heal", "Choke", "Push")
        files = [str(calibrated_session(d, seed=s, reps=3, abilities=abilities)[0].save()) for s in (3, 4)]
        out = d / "m.joblib"
        assert T.main(["fit", *files, "--out", str(out)]) == 0
        b = joblib.load(out)
        assert b["channels"] == CH and set(b["spells"]) == set(T.SPELLS)
        for sp, m in b["spells"].items():
            assert m["cv_auc_game"] > 0.9, (sp, m["cv_auc_game"])
            assert m["threshold_from"] == "game"
        # calibrate on a new session; thresholds move, models stay
        new = str(calibrated_session(d, seed=5, reps=3, abilities=abilities)[0].save())
        out2 = d / "m2.joblib"
        assert T.main(["calibrate", new, "--model", str(out), "--out", str(out2)]) == 0
        b2 = joblib.load(out2)
        for sp in T.SPELLS:
            assert b2["spells"][sp]["calibrated_on"] == [Path(new).name[:-7]]
            assert "threshold_before_calibration" in b2["spells"][sp]
        assert T.main(["evaluate", new, "--model", str(out2)]) == 0


def test_partial_refit_keeps_other_spells():
    with tempfile.TemporaryDirectory() as d:
        d = Path(d)
        f = str(calibrated_session(d, seed=6, reps=3, abilities=("Choke",))[0].save())
        base = ROOT / "eeg" / "models" / "live_models.joblib"
        out = d / "m.joblib"
        assert T.main(["fit", f, "--spells", "choke_squeeze", "--base", str(base), "--out", str(out)]) == 0
        b, old = joblib.load(out), joblib.load(base)
        assert b["channels"] == old["channels"]
        assert b["spells"]["heal"]["threshold"] == old["spells"]["heal"]["threshold"]
        assert b["spells"]["choke_squeeze"]["sessions"] == [Path(f).name[:-7]]


if __name__ == "__main__":
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    for t in tests:
        t()
        print("ok ", t.__name__)
    print("{} tests passed".format(len(tests)))
