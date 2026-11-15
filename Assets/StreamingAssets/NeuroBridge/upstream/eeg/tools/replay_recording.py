# -*- coding: utf-8 -*-
"""Replay a recorded spells session through the live EEG path + SpellGate.

    python -m eeg.tools.replay_recording path/to/spells_*.csv [more.csv ...]

The recording's EEG (``Fz..PO8`` columns, already OSCAR + 1-30 Hz + notch
filtered, ``cue`` column like ``choke_squeeze_gesture_imagination``) is fed in
small chunks through :class:`eeg.headset.SpellWindowProcessor`, exactly as the
headset would be. The camera is simulated: the matching gesture label is on
from ``--reaction`` s after each cue until the cue ends, with a few dropped
frames (flicker). Prints, per ability, how often it fired with and without
imagination and how long it took.

NOTE: if the recording was used to train the models (all the current ones
were), the numbers are optimistic - this checks the plumbing and timing, not
generalisation.
"""

import argparse
import sys
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from eeg.features import CH, RATE                          # noqa: E402
from eeg.headset import DEFAULT_MODEL_PATH, SpellWindowProcessor, load_bundle  # noqa: E402
from eeg.spell_gate import ABILITIES, GateConfig, SpellGate  # noqa: E402

SPELL_TO_ABILITY = {spell: ability for ability, (spell, _) in ABILITIES.items()}


def trials(df):
    cue = df.cue.fillna("relax").to_numpy()
    starts = np.flatnonzero(np.r_[True, cue[1:] != cue[:-1]])
    ends = np.r_[starts[1:], len(cue)]
    for s, e in zip(starts, ends):
        c = cue[s]
        if "_gesture_" in c:
            spell, kind = c.rsplit("_gesture_", 1)
            if spell in SPELL_TO_ABILITY:
                yield s, e, spell, kind == "imagination"


def replay(path, bundle, cfg, reaction, flicker_every, chunk=8, fps=30):
    df = pd.read_csv(path)
    x = df[CH].to_numpy()
    proc = SpellWindowProcessor(bundle)
    gate = SpellGate(proc.thresholds, cfg, wall_clock=lambda: 0.0)
    tr = list(trials(df))
    label = np.full(len(x), None, dtype=object)
    for s, e, spell, _ in tr:
        label[int(s + reaction * RATE):e] = SPELL_TO_ABILITY[spell]
    rng = np.random.default_rng(0)
    frame_every = RATE // fps
    events = []
    for i in range(0, len(x), chunk):
        t_last = (i + chunk - 1) / RATE
        gate.add_windows(proc.push(x[i:i + chunk], t_last))
        for k in range(i, min(i + chunk, len(x))):
            if k % frame_every:
                continue
            lab = label[k]
            if lab is not None and flicker_every and rng.random() < 1.0 / (flicker_every * fps):
                lab = None                                  # random dropped frame
            for ev in gate.update(k / RATE, lab, 0.9):
                events.append((k / RATE, ev))
    events += [(len(x) / RATE, ev) for ev in gate.close(len(x) / RATE)]

    rows = []
    for s, e, spell, imag in tr:
        t0, t1 = s / RATE, e / RATE + cfg.cv_grace_s + cfg.instant_tail_s
        ab = SPELL_TO_ABILITY[spell]
        fired = [(t, ev) for t, ev in events if ev["ability"] == ab and t0 <= t <= t1
                 and ev["event"] in ("ability_started", "ability_triggered")]
        ends = [ev.get("reason") for t, ev in events if ev["ability"] == ab and t0 <= t <= t1
                and ev["event"] == "ability_ended"]
        rows.append(dict(ability=ab, imagination=imag, fired=bool(fired), starts=len(fired),
                         latency=(fired[0][0] - t0 - reaction) if fired else np.nan,
                         early_end=sum(r == "imagination_lost" for r in ends)))
    return pd.DataFrame(rows), events


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("recordings", nargs="+")
    ap.add_argument("--model", default=str(DEFAULT_MODEL_PATH))
    ap.add_argument("--reaction", type=float, default=0.8, help="camera gesture starts this long after the cue")
    ap.add_argument("--flicker", type=float, default=1.0, help="one dropped camera frame every N s (0 = none)")
    a = ap.parse_args(argv)
    bundle = load_bundle(a.model)
    cfg = GateConfig()
    allr = []
    for p in a.recordings:
        r, _ = replay(p, bundle, cfg, a.reaction, a.flicker)
        r["recording"] = Path(p).name
        allr.append(r)
    r = pd.concat(allr)
    print("Per ability (camera gesture always present; EEG decides):")
    for ab, g in r.groupby("ability", sort=False):
        im, go = g[g.imagination], g[~g.imagination]
        print("  {:<10} with imagination: fired {:>2}/{:<2} (median {:.1f} s after gesture start, "
              "{} ended early)   gesture only: FALSE activation {:>2}/{:<2}".format(
                  ab, int(im.fired.sum()), len(im), np.nanmedian(im.latency) if im.fired.any() else float("nan"),
                  int(im.early_end.sum()), int(go.fired.sum()), len(go)))
        if (g.starts > 1).any():
            print("  {:<10} restarts within one gesture: {}".format("", int((g.starts - 1).clip(0).sum())))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
