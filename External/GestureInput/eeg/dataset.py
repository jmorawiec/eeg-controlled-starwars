# -*- coding: utf-8 -*-
"""EEG recordings -> labelled 1 s feature windows for the spell models.

Two kinds of recordings, same CSV format (``.csv`` or ``.csv.gz``)::

    time_s, Fz, C3, Cz, C4, Pz, PO7, POz, PO8, cue, trial[, gesture]

EEG already filtered like live (OSCAR + 1-30 Hz + 48-52 Hz band-stop, gpype).
``cue`` marks the trials: ``<spell>_gesture_imagination`` (positive) and
``<spell>_gesture_only`` (negative); anything else (``relax``,
``<spell>_gesture_unlabelled``) is not used.

* **cued** (``eeg/tools/record_spells.py``, the original sessions): the cue
  is the instruction, 5 s long; windows start 1-4 s after it (1 s reaction).
  ``trial`` = repeat number, the imagination and gesture-only trial of one
  repeat are held out together in cross-validation.
* **game** (``game_*``, recorded by ``integrations/gesture_pipeline.py
  --eeg_record`` / ``--calibrate``): the cue spans the camera gesture, from
  the camera onset to its end. Windows start from the onset while the gesture
  is on, at most 16 (4 s) - exactly the windows the live gate averages.

Per-recording notes live in ``eeg/data/manifest.json`` (``use``,
``skip_spells``, ``kind``); files not listed there are used by default.
"""

from __future__ import annotations

import glob
import json
import os
from dataclasses import dataclass, field
from functools import lru_cache
from pathlib import Path
from typing import Iterable, List, Optional, Sequence

import numpy as np
import pandas as pd

from eeg.features import ARTIFACT_UV, CH, RATE, STEP, WIN, bandpass, features

EEG_DIR = Path(__file__).resolve().parent
DATA_DIR = EEG_DIR / "data"
GAME_DIR = DATA_DIR / "game"
MANIFEST = DATA_DIR / "manifest.json"
CONTACT_R = 0.3                    # min |r| with any other channel = electrode touches the scalp
CUED_TASK = (1.0, 5.0)             # s after the cue: skip 1 s reaction, end with the 5 s cue
GAME_TASK = (0.0, 4.0)             # s after camera onset: window STARTS in this span (<= gesture end)
CONDITIONS = {"imagination": 1, "only": 0}


@dataclass
class Recording:
    name: str
    path: Path
    kind: str = "cued"             # "cued" | "game"
    use: bool = True
    skip_spells: Sequence[str] = ()
    note: str = ""
    electrodes: str = ""

    def load(self) -> "Loaded":
        return _load(str(self.path), self.name, self.kind)


@dataclass
class Loaded:
    name: str
    kind: str
    x: np.ndarray                  # samples x 8, CH order, 1-30 Hz
    good: List[str]                # channels touching the scalp
    trials: pd.DataFrame           # spell, y, onset, end, group
    seconds: float = 0.0
    _x830: dict = field(default_factory=dict, repr=False)

    def x830(self, chans: Sequence[str]) -> np.ndarray:
        key = tuple(chans)
        if key not in self._x830:
            self._x830[key] = bandpass(self.x[:, [CH.index(c) for c in chans]])
        return self._x830[key]


def stem(path) -> str:
    n = os.path.basename(str(path))
    for ext in (".csv.gz", ".csv"):
        if n.endswith(ext):
            return n[:-len(ext)]
    return n


def is_recording_file(path) -> bool:
    n = os.path.basename(str(path))
    return (n.endswith((".csv", ".csv.gz")) and "_events" not in n and not n.startswith("sim_"))


def manifest(path=MANIFEST) -> dict:
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f).get("recordings", {})
    except FileNotFoundError:
        return {}


def recordings(paths: Optional[Iterable[str]] = None, include_unused: bool = False,
               data_dir=DATA_DIR) -> List[Recording]:
    """Recordings from files / folders / globs (default: everything in ``eeg/data``).

    Files given explicitly are used even if the manifest says ``use: false``.
    """
    explicit = paths is not None and len(list(paths)) > 0
    files, missing = [], []
    for p in (paths if explicit else [str(data_dir)]):
        if os.path.isdir(p):
            files += glob.glob(os.path.join(p, "**", "*.csv*"), recursive=True)
        else:
            found = glob.glob(p) or ([p] if os.path.exists(p) else [])
            files += found
            missing += [] if found else [p]
    if missing:
        raise SystemExit("recording(s) not found: {}".format(missing))
    notes = manifest()
    out, seen = [], set()
    for f in sorted(files):
        if not is_recording_file(f) or stem(f) in seen:
            continue
        seen.add(stem(f))
        n = notes.get(stem(f), {})
        rec = Recording(name=stem(f), path=Path(f),
                        kind=n.get("kind", "game" if stem(f).startswith("game_") else "cued"),
                        use=bool(n.get("use", True)) or explicit,
                        skip_spells=tuple(n.get("skip_spells", ())), note=n.get("note", ""),
                        electrodes=n.get("electrodes", ""))
        if rec.use or include_unused:
            out.append(rec)
    return out


def good_channels(x: np.ndarray) -> List[str]:
    """Not flat and moving with at least one other channel (max |r| >= 0.3).
    An open electrode is quiet but uncorrelated; an amplitude rule alone misses
    it and drops quiet but good gel channels."""
    xc = x - x.mean(0)
    rms = np.sqrt((xc ** 2).mean(0))
    c = np.nan_to_num(np.abs(np.corrcoef(xc.T)))
    np.fill_diagonal(c, 0)
    return [ch for ch, r, k in zip(CH, rms, c.max(1)) if r >= 0.5 and k >= CONTACT_R]


def parse_trials(df: pd.DataFrame, name: str, kind: str) -> pd.DataFrame:
    cue = df["cue"].fillna("relax").astype(str).to_numpy()
    trial = df["trial"].fillna(0).astype(int).to_numpy() if "trial" in df else np.zeros(len(df), int)
    starts = np.flatnonzero(np.r_[True, cue[1:] != cue[:-1]])
    ends = np.r_[starts[1:], len(cue)]
    rows = []
    for k, (s, e) in enumerate(zip(starts, ends)):
        if "_gesture_" not in cue[s]:
            continue
        spell, cond = cue[s].rsplit("_gesture_", 1)
        if cond not in CONDITIONS:
            continue                                   # unlabelled in-game gesture
        # cued: one repeat = imagination + gesture-only trial, held out together
        group = "{}#{}".format(name, trial[s] if kind == "cued" and trial[s] else "run{}".format(k))
        rows.append(dict(spell=spell, y=CONDITIONS[cond], onset=int(s), end=int(e), group=group))
    return pd.DataFrame(rows, columns=["spell", "y", "onset", "end", "group"])


@lru_cache(maxsize=32)
def _load(path: str, name: str, kind: str) -> Loaded:
    df = pd.read_csv(path)
    missing = [c for c in CH + ["cue"] if c not in df]
    if missing:
        raise ValueError("{}: missing columns {}".format(path, missing))
    x = df[CH].to_numpy(float)
    return Loaded(name=name, kind=kind, x=x, good=good_channels(x),
                  trials=parse_trials(df, name, kind), seconds=len(df) / RATE)


def window_starts(onset: int, end: int, kind: str, n: int) -> List[int]:
    if kind == "game":
        a = onset + int(GAME_TASK[0] * RATE)
        b = min(onset + int(GAME_TASK[1] * RATE) - 1, end)       # starts while the gesture is on
        return [s for s in range(a, b + 1, STEP) if s + WIN <= n]
    a, b = onset + int(CUED_TASK[0] * RATE), onset + int(CUED_TASK[1] * RATE)
    return [s for s in range(a, b - WIN + 1, STEP) if s + WIN <= n]


def spell_windows(rec: Loaded, spell: str, chans: Sequence[str]):
    """-> X (windows x features), y, group per window. Artifact windows dropped."""
    idx = [CH.index(c) for c in chans]
    raw = rec.x[:, idx]
    x830 = rec.x830(chans)
    X, y, g = [], [], []
    for t in rec.trials[rec.trials.spell == spell].itertuples():
        for s in window_starts(t.onset, t.end, rec.kind, len(raw)):
            if np.abs(raw[s:s + WIN]).max() >= ARTIFACT_UV:
                continue
            X.append(features(x830[s:s + WIN]))
            y.append(t.y)
            g.append(t.group)
    n_feat = len(chans) * (len(chans) + 1) // 2
    return (np.array(X).reshape(-1, n_feat), np.array(y, int), np.array(g, object))


def spells_in(recs: Sequence[Recording]) -> List[str]:
    out = []
    for r in recs:
        for sp in r.load().trials.spell:
            if sp not in out and sp not in r.skip_spells:
                out.append(sp)
    return out
