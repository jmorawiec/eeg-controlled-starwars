# -*- coding: utf-8 -*-
"""Record EEG + camera gestures while playing, for retraining / calibration.

No hardware here: :class:`SessionRecorder` gets the EEG samples from
:class:`eeg.headset.EEGSpellStream` (``stream.recorder = rec``) and the camera
label every frame (:meth:`SessionRecorder.gesture`). On :meth:`save` it writes
``eeg/data/game/game_<date>_<time>.csv.gz`` in the training format
(see ``eeg/dataset.py``) + ``..._events.jsonl``.

Gesture episodes (camera onset .. end) use the same anti-flicker rules as the
live gate: stable for ``cv_on_s``, gaps < ``cv_grace_s`` ignored, the same
gesture back within ``cv_rejoin_s`` continues it. Every episode becomes a
trial with cue ``<spell>_gesture_<label>``:

* ``imagination`` / ``only`` - labelled (training data),
* ``unlabelled`` - kept for analysis, ignored by training.

Labels come from :class:`Calibration` (prompted trials), from the keys ``y`` /
``n`` in the preview window (last gesture: imagined / gesture only), or from
``default_label`` (e.g. a play session where every cast was imagined).
"""

from __future__ import annotations

import json
import random
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import List, Optional

import numpy as np

from eeg.features import CH, RATE
from eeg.spell_gate import ABILITIES, GateConfig

LABELS = ("imagination", "only")
LABEL_TEXT = {"imagination": "WITH imagination", "only": "gesture only - NO imagination"}


@dataclass
class Episode:
    ability: str
    onset: float
    end: float
    label: Optional[str] = None           # "imagination" | "only" | None
    source: str = ""                      # calibration / key / default

    @property
    def spell(self) -> str:
        return ABILITIES[self.ability][0]


class EpisodeTracker:
    """Camera label per frame -> finished gesture episodes (gate's anti-flicker rules)."""

    def __init__(self, cfg: Optional[GateConfig] = None):
        self.cfg = cfg or GateConfig()
        self._cand, self._cand_since = None, 0.0
        self.current: Optional[Episode] = None
        self._last_seen = 0.0
        self._pending: Optional[Episode] = None      # ended, may still rejoin

    def update(self, now: float, label: Optional[str]) -> List[Episode]:
        c, done = self.cfg, []
        raw = label if label in ABILITIES else None
        if raw != self._cand:
            self._cand, self._cand_since = raw, now
        ep = self.current
        if ep is not None:
            if raw == ep.ability:
                self._last_seen = now
            elif now - self._last_seen >= c.cv_grace_s:
                ep.end, self.current, self._pending = self._last_seen, None, ep
        if self._pending is not None:
            p = self._pending
            if (self._cand == p.ability and now - self._cand_since >= c.cv_on_s
                    and self._cand_since - p.end <= c.cv_rejoin_s):
                self.current, self._pending, self._last_seen = p, None, now      # same gesture again
            elif now - p.end > c.cv_rejoin_s or (self._cand not in (None, p.ability)
                                                 and now - self._cand_since >= c.cv_on_s):
                done.append(p)
                self._pending = None
        if self.current is None and self._pending is None and self._cand is not None \
                and now - self._cand_since >= c.cv_on_s:
            self.current = Episode(self._cand, onset=self._cand_since, end=now)
            self._last_seen = now
        return done

    def flush(self) -> List[Episode]:
        out = [e for e in (self._pending, self.current) if e is not None]
        if self.current is not None:
            self.current.end = self._last_seen
        self._pending = self.current = None
        return out


class SessionRecorder:
    """EEG samples (1-30 Hz, as the models get them) + gestures + events -> CSV."""

    def __init__(self, out_dir, prefix: str = "game", default_label: Optional[str] = None,
                 cfg: Optional[GateConfig] = None):
        if default_label not in (None,) + LABELS:
            raise ValueError("default_label must be None, 'imagination' or 'only'")
        self.out_dir = Path(out_dir)
        self.prefix = prefix
        self.default_label = default_label
        self.started = datetime.now()
        self.tracker = EpisodeTracker(cfg)
        self._chunks: List[np.ndarray] = []
        self._times: List[np.ndarray] = []
        self.frames: List[tuple] = []             # (t, camera label)
        self.episodes: List[Episode] = []
        self.events: List[dict] = []
        self.marks: List[tuple] = []              # (t, cue, trial): cued recordings
        self.n_samples = 0

    # ---------------------------------------------------------------- inputs
    def add(self, frame, t_arrival: float) -> None:
        """EEG chunk (samples x >= 8 channels, CH order); ``t_arrival`` = monotonic time of its last sample."""
        x = np.asarray(frame, dtype=float)[:, :len(CH)]
        if not len(x):
            return
        self._chunks.append(x)
        self._times.append(t_arrival - (len(x) - 1 - np.arange(len(x))) / RATE)
        self.n_samples += len(x)

    def gesture(self, now: float, label: Optional[str]) -> List[Episode]:
        """Camera label of one frame. Returns gesture episodes that just finished."""
        if not self.frames or self.frames[-1][1] != label:
            self.frames.append((now, label))
        done = self.tracker.update(now, label)
        for ep in done:
            self._finish(ep)
        return done

    def event(self, now: float, ev: dict) -> None:
        self.events.append(dict(ev, t=round(now, 3)))

    def mark(self, now: float, cue: str, trial: int = 0) -> None:
        """Cue from ``now`` on (cued recordings, ``eeg/tools/record_spells.py``)."""
        self.marks.append((now, cue, trial))

    def label_last(self, label: str, now: float, max_age_s: float = 15.0) -> Optional[Episode]:
        """``y`` / ``n`` key: label the current or the most recent gesture."""
        ep = self.tracker.current or self.tracker._pending
        if ep is None and self.episodes and now - self.episodes[-1].end <= max_age_s:
            ep = self.episodes[-1]
        if ep is not None:
            ep.label, ep.source = label, "key"
            self.event(now, dict(event="label", ability=ep.ability, label=label, onset=round(ep.onset, 3)))
        return ep

    def _finish(self, ep: Episode) -> None:
        if ep.label is None and self.default_label:
            ep.label, ep.source = self.default_label, "default"
        self.episodes.append(ep)

    # ---------------------------------------------------------------- output
    def counts(self) -> dict:
        out = {}
        for e in self.episodes:
            k = (e.ability, e.label or "unlabelled")
            out[k] = out.get(k, 0) + 1
        return out

    def to_frame(self):
        import pandas as pd
        for ep in self.tracker.flush():
            self._finish(ep)
        if not self._chunks:
            return None
        x = np.vstack(self._chunks)
        t = np.concatenate(self._times)
        df = pd.DataFrame(x, columns=CH)
        df.insert(0, "time_s", np.round(np.arange(len(df)) / RATE, 3))
        cue = np.full(len(df), "relax", dtype=object)
        trial = np.zeros(len(df), int)
        if self.marks:
            mt = np.array([m[0] for m in self.marks])
            k = np.searchsorted(mt, t, side="right") - 1
            ok = k >= 0
            cue[ok] = np.array([m[1] for m in self.marks], dtype=object)[k[ok]]
            trial[ok] = np.array([m[2] for m in self.marks])[k[ok]]
        for i, ep in enumerate(self.episodes, 1):
            sel = (t >= ep.onset) & (t <= ep.end)
            cue[sel] = "{}_gesture_{}".format(ep.spell, ep.label or "unlabelled")
            trial[sel] = i
        df["cue"], df["trial"] = cue, trial
        if self.frames:
            ft = np.array([f[0] for f in self.frames])
            k = np.searchsorted(ft, t, side="right") - 1
            lab = np.array([f[1] or "" for f in self.frames], dtype=object)
            df["gesture"] = np.where(k >= 0, lab[np.maximum(k, 0)], "")
        return df

    def save(self, sim: bool = False) -> Optional[Path]:
        df = self.to_frame()
        if df is None or len(df) < RATE:
            print("[EEG record] no EEG samples - nothing saved")
            return None
        self.out_dir.mkdir(parents=True, exist_ok=True)
        name = "{}{}_{:%Y%m%d_%H%M%S}".format("sim_" if sim else "", self.prefix, self.started)
        base, k = name, 1
        while (self.out_dir / (name + ".csv.gz")).exists():         # two sessions in the same second
            k += 1
            name = "{}_{}".format(base, k)
        path = self.out_dir / (name + ".csv.gz")
        df.to_csv(path, index=False, float_format="%.4f")
        with open(self.out_dir / (name + "_events.jsonl"), "w", encoding="utf-8") as f:
            for e in self.episodes:
                f.write(json.dumps(dict(event="gesture", ability=e.ability, onset=round(e.onset, 3),
                                        end=round(e.end, 3), label=e.label, source=e.source)) + "\n")
            for e in self.events:
                f.write(json.dumps(e, default=str) + "\n")
        n = self.counts()
        what = ", ".join("{} {} x{}".format(a, l, c) for (a, l), c in sorted(n.items()))
        if not what and self.marks:
            cued = [m[1] for m in self.marks if m[1] != "relax"]
            what = "{} cued trials".format(len(cued))
        print("[EEG record] saved {} ({:.1f} min): {}".format(path, len(df) / RATE / 60, what or "no gestures"))
        if sim:
            print("[EEG record] simulated EEG - file starts with sim_ and is ignored by training")
        return path


class Calibration:
    """Prompted in-game trials: per ability ``reps`` x WITH and x WITHOUT imagination.

    Order is random (balanced). Each prompt waits for a camera gesture of that
    ability; when it ends, it is labelled and the next prompt follows after a
    relax pause. Gestures of other abilities stay unlabelled.
    """

    def __init__(self, recorder: SessionRecorder, reps: int = 4, abilities=None,
                 relax_s: float = 5.0, timeout_s: float = 20.0, seed=None):
        self.rec = recorder
        abilities = list(abilities or ABILITIES)
        self.plan = [(a, lab) for a in abilities for lab in LABELS for _ in range(reps)]
        random.Random(seed).shuffle(self.plan)
        self.i = 0
        self.relax_s, self.timeout_s = relax_s, timeout_s
        self.state, self.since = "relax", None
        self.done_trials, self.missed = 0, 0
        self.message = ""

    @property
    def finished(self) -> bool:
        return self.i >= len(self.plan)

    def current(self):
        return None if self.finished else self.plan[self.i]

    def update(self, now: float, finished_episodes: List[Episode]) -> None:
        if self.since is None:
            self.since = now
            self._say(now)
        if self.finished:
            return
        ability, label = self.plan[self.i]
        if self.state == "relax" and now - self.since >= self.relax_s:
            self.state, self.since = "go", now
            self._say(now)
        elif self.state == "go":
            for ep in finished_episodes:
                if ep.ability != ability:
                    self.message = "camera saw {} - expected {}".format(ep.ability, ability)
                    continue
                if ep.onset < self.since - 2.0:
                    self.message = "start the gesture after the prompt - again"
                    continue
                min_s = 0.3 if ABILITIES[ability][1] == "instant" else 1.5
                if ep.end - ep.onset < min_s:
                    self.message = "too short ({:.1f} s) - again".format(ep.end - ep.onset)
                    continue
                ep.label, ep.source = label, "calibration"
                self.done_trials += 1
                self._next(now, "ok: {} {} ({:.1f} s)".format(ability, LABEL_TEXT[label], ep.end - ep.onset))
                return
            cur = self.rec.tracker.current
            if (cur is None or cur.ability != ability) and now - self.since >= self.timeout_s:
                self.missed += 1
                self._next(now, "no {} gesture seen - skipped".format(ability))

    def _next(self, now, msg):
        self.message = msg
        print("[calibration] " + msg)
        self.i += 1
        self.state, self.since = "relax", now
        self._say(now)

    def _say(self, now):
        if self.finished:
            print("[calibration] DONE: {} trials, {} skipped. Keep playing or press ESC to save.".format(
                self.done_trials, self.missed))
            return
        ability, label = self.plan[self.i]
        if self.state == "relax":
            print("[calibration] {}/{} relax - next: {} {}".format(self.i + 1, len(self.plan), ability,
                                                                 LABEL_TEXT[label]))
        else:
            print("[calibration] {}/{} >>> {} {} <<<".format(self.i + 1, len(self.plan), ability,
                                                            LABEL_TEXT[label]))

    def lines(self, now: float) -> List[str]:
        """Text for the preview overlay."""
        if self.finished:
            return ["CALIBRATION DONE ({} trials) - ESC saves".format(self.done_trials)]
        ability, label = self.plan[self.i]
        hold = "push once" if ABILITIES[ability][1] == "instant" else "hold 3-4 s"
        head = "CALIBRATION {}/{}".format(self.i + 1, len(self.plan))
        if self.state == "relax":
            left = max(0.0, self.relax_s - (now - (self.since or now)))
            out = ["{}  relax {:.0f} s".format(head, left), "next: {} {}".format(ability, LABEL_TEXT[label])]
        else:
            out = ["{}  NOW: {} {}".format(head, ability, LABEL_TEXT[label]), "({}, then release)".format(hold)]
        if self.message:
            out.append(self.message)
        return out
