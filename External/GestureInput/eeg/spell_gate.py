# -*- coding: utf-8 -*-
"""Camera gesture + EEG imagination -> ability events (pure logic, no I/O).

The camera says WHICH ability the player is gesturing; the EEG says WHETHER
they also imagine it. Only the combination fires the ability.

Abilities
---------
=========  ==================  ==========  =====================================
CV label   EEG model           kind        events
=========  ==================  ==========  =====================================
Push       push_with_push      instant     ``ability_triggered`` (once per gesture)
Heal       heal                sustained   ``ability_started`` ... ``ability_ended``
Choke      choke_squeeze       sustained   ``ability_started`` ... ``ability_ended``
Lightning  lightning_push      sustained   ``ability_started`` ... ``ability_ended``
=========  ==================  ==========  =====================================

Anti-flicker
------------
* camera: a gesture counts only after it is stable for ``cv_on_s``; while it
  is going on, drop-outs (other label / ``inactive`` / lost hand) shorter than
  ``cv_grace_s`` are ignored. A gesture episode therefore never toggles on a
  single bad frame.
* EEG start: P(imagination) is averaged over all 1 s windows since the gesture
  started (at most ``max_windows``, as in training) and must reach the spell's
  threshold after at least ``min_windows_*`` windows.
* EEG sustain (held abilities): the ability stays on while the gesture lasts.
  The EEG ends it early only if the rolling mean of the last
  ``sustain_windows`` windows stays below ``threshold - hysteresis`` for
  ``release_s`` seconds (``release_s = 0`` disables this), never before
  ``min_active_s``. A re-start within the same gesture needs ``reactivate_gap_s``.
* camera fragments: if the same gesture comes back within ``cv_rejoin_s``
  after a longer drop-out, it continues the same episode (same onset, EEG
  evidence keeps accumulating, a push that fired does not fire again) - unless
  a held ability had already been running and was ended.
* push: fires once per gesture episode; it may still fire up to
  ``instant_tail_s`` after a short gesture ended, from windows that started
  during the gesture.
"""

from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import Callable, Deque, Dict, Iterable, List, Optional
from collections import deque

ABILITIES: Dict[str, tuple] = {
    "Push": ("push_with_push", "instant"),
    "Heal": ("heal", "sustained"),
    "Choke": ("choke_squeeze", "sustained"),
    "Lightning": ("lightning_push", "sustained"),
}


@dataclass
class GateConfig:
    cv_on_s: float = 0.15            # camera gesture must be stable this long
    cv_grace_s: float = 0.35         # camera drop-outs shorter than this are ignored
    cv_rejoin_s: float = 1.0         # same gesture back within this after a longer drop-out = same
                                     # episode (EEG keeps accumulating, a fired push won't fire again)
    eeg_skip_s: float = 0.0          # ignore EEG windows starting < this after gesture onset
    # Decision delay vs false activations (replay of the 4 sessions): held 4 windows ->
    # 1.9 s / 8 of 45 false, 6 -> 2.4 s / 4 of 45, 8 -> 2.9 s / 3 of 45; push 2 -> 1.5 s / 6 of 25, 3 -> 1.7 s / 5 of 25
    min_windows_instant: int = 3     # push: >= 1.5 s of EEG after onset
    min_windows_sustained: int = 6   # held abilities: >= 2.25 s of EEG after onset
    max_windows: int = 16            # activation score = mean of the last <= 16 windows (4 s)
    sustain_windows: int = 8         # rolling mean while an ability is held (2.75 s of EEG)
    hysteresis: float = 0.10         # release threshold = threshold - hysteresis
    release_s: float = 1.5           # EEG below release threshold this long ends a held ability (0 = never)
    min_active_s: float = 1.0        # EEG cannot end an ability earlier than this
    reactivate_gap_s: float = 0.5    # pause before a held ability may restart in the same gesture
    instant_tail_s: float = 1.0      # push may still fire this long after a short gesture ended
    threshold_offset: float = 0.0    # added to every spell threshold (+ = stricter)


@dataclass
class _Episode:
    ability: str
    spell: str
    kind: str
    onset: float
    last_seen: float
    cv_conf: float = 0.0
    ended_at: Optional[float] = None
    n: int = 0
    score: Optional[float] = None
    rolling: Optional[float] = None
    active: bool = False
    active_since: float = 0.0
    starts: int = 0
    last_end: Optional[float] = None
    below_since: Optional[float] = None
    triggered: bool = False
    ended_active: bool = False       # a held ability was running when the gesture ended
    mode: str = "eeg"


class SpellGate:
    """Feed camera labels every frame (:meth:`update`) and EEG windows as they come
    (:meth:`add_windows`); get back the events to send.

    ``mode``: ``"eeg"`` (EEG required), ``"cv_only"`` (EEG ignored).
    ``fallback``: what to do when the EEG is unavailable (no data / electrode
    off): ``"block"`` = no abilities, ``"cv"`` = camera only for that gesture.
    """

    def __init__(self, thresholds: Dict[str, float], config: Optional[GateConfig] = None,
                 mode: str = "eeg", fallback: str = "block",
                 wall_clock: Callable[[], float] = time.time):
        if mode not in ("eeg", "cv_only"):
            raise ValueError("mode must be 'eeg' or 'cv_only'")
        if fallback not in ("block", "cv"):
            raise ValueError("fallback must be 'block' or 'cv'")
        self.thresholds = dict(thresholds)
        self.cfg = config or GateConfig()
        self.mode, self.fallback = mode, fallback
        self._wall = wall_clock
        self._windows: Deque = deque()
        self._cand: Optional[str] = None
        self._cand_since = 0.0
        self._cand_conf = 0.0
        self.episode: Optional[_Episode] = None
        self._last_ended: Optional[_Episode] = None

    # ------------------------------------------------------------------ inputs
    def add_windows(self, windows: Iterable) -> None:
        for w in windows:
            self._windows.append(w)
        while self._windows and self._windows[0].t_end < self._windows[-1].t_end - 20:
            self._windows.popleft()

    def update(self, now: float, label: Optional[str], confidence: float = 0.0,
               eeg_available: bool = True) -> List[dict]:
        """One camera frame. ``label``: recognised gesture or None/'inactive'."""
        cfg = self.cfg
        events: List[dict] = []
        raw = label if label in ABILITIES else None

        if raw != self._cand:
            self._cand, self._cand_since, self._cand_conf = raw, now, 0.0
        if raw is not None:
            self._cand_conf = max(self._cand_conf, float(confidence or 0.0))

        ep = self.episode
        if ep is not None and ep.ended_at is None:
            if raw == ep.ability:
                ep.last_seen = now
                ep.cv_conf = max(ep.cv_conf, float(confidence or 0.0))
            elif now - ep.last_seen >= cfg.cv_grace_s:
                ep.ended_at = ep.last_seen
                self._last_ended = ep
                events += self._gesture_ended(ep, now)

        stable_new = (self._cand is not None and now - self._cand_since >= cfg.cv_on_s
                      and (ep is None or ep.ended_at is not None))
        if ep is not None and ep.ended_at is not None:
            keep_tail = (ep.kind == "instant" and not ep.triggered and ep.mode != "cv"
                         and now <= ep.ended_at + cfg.instant_tail_s and not stable_new)
            if not keep_tail:
                self.episode = ep = None
        if stable_new:
            use_cv = self.mode == "cv_only" or (not eeg_available and self.fallback == "cv")
            prev = self._last_ended
            if (prev is not None and prev.ability == self._cand and not prev.ended_active
                    and prev.mode == ("cv" if use_cv else "eeg")
                    and self._cand_since - prev.ended_at <= cfg.cv_rejoin_s):
                # camera lost the gesture for a moment: continue the same episode
                prev.ended_at, prev.last_seen = None, now
                prev.cv_conf = max(prev.cv_conf, self._cand_conf)
                self.episode = ep = prev
            else:
                spell, kind = ABILITIES[self._cand]
                self.episode = ep = _Episode(self._cand, spell, kind, onset=self._cand_since,
                                             last_seen=now, cv_conf=self._cand_conf,
                                             mode="cv" if use_cv else "eeg")
            self._last_ended = None

        if ep is not None:
            events += self._evaluate(ep, now, eeg_available)
        return events

    def close(self, now: float) -> List[dict]:
        """End a held ability (e.g. on shutdown)."""
        ep, self.episode = self.episode, None
        if ep is not None and ep.active:
            ep.active = False
            return [self._event("ability_ended", ep, now, reason="shutdown",
                                duration=round(now - ep.active_since, 3))]
        return []

    # ------------------------------------------------------------------ logic
    def threshold(self, spell: str) -> float:
        return self.thresholds[spell] + self.cfg.threshold_offset

    def _gesture_ended(self, ep: _Episode, now: float) -> List[dict]:
        if ep.kind == "sustained" and ep.active:
            ep.active, ep.ended_active = False, True
            return [self._event("ability_ended", ep, now, reason="gesture_released",
                                duration=round(ep.ended_at - ep.active_since, 3))]
        return []

    def _evaluate(self, ep: _Episode, now: float, eeg_available: bool) -> List[dict]:
        cfg = self.cfg
        if ep.mode == "cv":                                   # camera only
            if ep.ended_at is None and ep.kind == "instant" and not ep.triggered:
                ep.triggered = True
                return [self._event("ability_triggered", ep, now)]
            if ep.ended_at is None and ep.kind == "sustained" and not ep.active and ep.starts == 0:
                return [self._start(ep, now)]
            return []

        ps = [w.probs[ep.spell] for w in self._windows
              if not w.artifact and ep.spell in w.probs
              and w.t_start >= ep.onset + cfg.eeg_skip_s
              and (ep.ended_at is None or w.t_start <= ep.ended_at)]
        ep.n = len(ps)
        ep.score = sum(ps[-cfg.max_windows:]) / len(ps[-cfg.max_windows:]) if ps else None
        ep.rolling = sum(ps[-cfg.sustain_windows:]) / len(ps[-cfg.sustain_windows:]) if ps else None
        thr = self.threshold(ep.spell)

        if ep.kind == "instant":
            if (not ep.triggered and eeg_available and ep.n >= cfg.min_windows_instant
                    and ep.score >= thr):
                ep.triggered = True
                return [self._event("ability_triggered", ep, now)]
            return []

        if ep.ended_at is not None:                           # held ability: gesture over
            return []
        if not ep.active:
            score = ep.score if ep.starts == 0 else ep.rolling
            gap_ok = ep.last_end is None or now - ep.last_end >= cfg.reactivate_gap_s
            if (eeg_available and ep.n >= cfg.min_windows_sustained and score is not None
                    and score >= thr and gap_ok):
                return [self._start(ep, now)]
            return []
        if cfg.release_s > 0 and ep.rolling is not None:
            if ep.rolling < thr - cfg.hysteresis:
                if ep.below_since is None:
                    ep.below_since = now
                elif (now - ep.below_since >= cfg.release_s
                      and now - ep.active_since >= cfg.min_active_s):
                    ep.active, ep.last_end, ep.below_since = False, now, None
                    return [self._event("ability_ended", ep, now, reason="imagination_lost",
                                        duration=round(now - ep.active_since, 3))]
            else:
                ep.below_since = None
        return []

    def _start(self, ep: _Episode, now: float) -> dict:
        ep.active, ep.active_since, ep.below_since = True, now, None
        ep.starts += 1
        return self._event("ability_started", ep, now)

    def _event(self, name: str, ep: _Episode, now: float, **extra) -> dict:
        ev = {
            "event": name,
            "timestamp": round(self._wall(), 3),
            "ability": ep.ability,
            "kind": ep.kind,
            "mode": ep.mode,
            "cv_confidence": round(ep.cv_conf, 3),
            "since_gesture_start": round(now - ep.onset, 3),
        }
        if ep.mode == "eeg":
            ev.update(eeg_score=None if ep.score is None else round(ep.score, 3),
                      eeg_threshold=round(self.threshold(ep.spell), 3), eeg_windows=ep.n)
        ev.update(extra)
        return ev

    # ------------------------------------------------------------------ display
    def view(self) -> Optional[dict]:
        ep = self.episode
        if ep is None:
            return None
        return dict(ability=ep.ability, kind=ep.kind, mode=ep.mode, score=ep.score,
                    rolling=ep.rolling, n=ep.n, threshold=self.threshold(ep.spell),
                    active=ep.active, triggered=ep.triggered, gesture_on=ep.ended_at is None)
