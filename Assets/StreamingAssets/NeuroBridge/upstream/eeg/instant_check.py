# -*- coding: utf-8 -*-
"""Natychmiastowe sprawdzenie EEG w chwili wykrycia ruchu z kamery (sama logika, bez I/O).

Alternatywa dla :class:`eeg.spell_gate.SpellGate` (``--decision instant``):

1. kamera wykrywa ruch (gest stabilny przez ``cv_on_s``; przerwy krótsze niż
   ``cv_grace_s`` są ignorowane - antymigotanie),
2. w tej chwili brany jest wynik regresji logistycznej EEG dla TEGO typu ruchu
   (średnie P(wyobrażenie) z ostatnich ``n_windows`` okien 1 s,
   :class:`eeg.fusion.EEGMonitor`),
3. wynik >= próg modelu (+ ``threshold_offset``) -> zdarzenie do wysłania przez TCP
   (Push: ``ability_triggered``; Heal/Choke/Lightning: ``ability_started``,
   a po puszczeniu gestu ``ability_ended``),
   wynik < próg -> ruch jest ignorowany (nic nie idzie do Unity) aż do
   zakończenia gestu.

Jedna decyzja na gest. W odróżnieniu od ``SpellGate`` nie czeka na okna EEG
z czasu trwania gestu, więc reaguje od razu, ale opiera się na EEG sprzed
chwili wykrycia (ostatnie ~1.75 s przy 4 oknach).

Interfejs jak ``SpellGate`` (``add_windows`` / ``update`` / ``close`` / ``view`` /
``threshold``), więc :class:`integrations.gesture_pipeline.AbilityBridge`
używa obu zamiennie.
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Callable, Dict, Iterable, List, Optional

from eeg.fusion import EEGMonitor
from eeg.spell_gate import ABILITIES, GateConfig


@dataclass
class _Gesture:
    ability: str
    spell: str
    kind: str
    onset: float
    last_seen: float
    cv_conf: float
    mode: str = "eeg"                 # "eeg" | "cv" (bez EEG)
    score: Optional[float] = None
    n: int = 0
    accepted: bool = False
    rejected: Optional[str] = None    # powód zignorowania
    active: bool = False
    active_since: float = 0.0
    triggered: bool = False


class InstantEEGCheck:
    """Ruch z kamery + bieżący wynik EEG tego ruchu -> wyślij albo zignoruj.

    ``mode``: ``"eeg"`` (EEG wymagane) / ``"cv_only"`` (każdy ruch przechodzi).
    ``fallback`` gdy EEG niedostępne: ``"block"`` = ignoruj, ``"cv"`` = przepuść.
    """

    def __init__(self, thresholds: Dict[str, float], config: Optional[GateConfig] = None,
                 mode: str = "eeg", fallback: str = "block", n_windows: int = 4,
                 stale_s: float = 2.0, wall_clock: Callable[[], float] = time.time):
        if mode not in ("eeg", "cv_only"):
            raise ValueError("mode must be 'eeg' or 'cv_only'")
        if fallback not in ("block", "cv"):
            raise ValueError("fallback must be 'block' or 'cv'")
        self.thresholds = dict(thresholds)
        self.cfg = config or GateConfig()
        self.mode, self.fallback = mode, fallback
        self.monitor = EEGMonitor(self.thresholds, n_windows=n_windows, stale_s=stale_s)
        self._wall = wall_clock
        self._cand: Optional[str] = None
        self._cand_since = 0.0
        self._cand_conf = 0.0
        self.gesture: Optional[_Gesture] = None
        self.decisions: List[dict] = []   # każda decyzja (wysłana i zignorowana) - do logów

    # ------------------------------------------------------------------ inputs
    def add_windows(self, windows: Iterable) -> None:
        self.monitor.add_windows(windows)

    def update(self, now: float, label: Optional[str], confidence: float = 0.0,
               eeg_available: bool = True) -> List[dict]:
        cfg = self.cfg
        events: List[dict] = []
        raw = label if label in ABILITIES else None

        if raw != self._cand:
            self._cand, self._cand_since, self._cand_conf = raw, now, 0.0
        if raw is not None:
            self._cand_conf = max(self._cand_conf, float(confidence or 0.0))

        g = self.gesture
        if g is not None:
            if raw == g.ability:
                g.last_seen = now
                g.cv_conf = max(g.cv_conf, float(confidence or 0.0))
            elif now - g.last_seen >= cfg.cv_grace_s:          # ruch się skończył
                if g.active:
                    g.active = False
                    events.append(self._event("ability_ended", g, now, reason="gesture_released",
                                              duration=round(g.last_seen - g.active_since, 3)))
                self.gesture = g = None

        if g is None and self._cand is not None and now - self._cand_since >= cfg.cv_on_s:
            events += self._decide(now, eeg_available)
        return events

    def close(self, now: float) -> List[dict]:
        g, self.gesture = self.gesture, None
        if g is not None and g.active:
            return [self._event("ability_ended", g, now, reason="shutdown",
                                duration=round(now - g.active_since, 3))]
        return []

    # ------------------------------------------------------------------ logic
    def threshold(self, spell: str) -> float:
        return self.thresholds[spell] + self.cfg.threshold_offset

    def _decide(self, now: float, eeg_available: bool) -> List[dict]:
        spell, kind = ABILITIES[self._cand]
        g = self.gesture = _Gesture(self._cand, spell, kind, onset=self._cand_since,
                                    last_seen=now, cv_conf=self._cand_conf)
        use_cv = self.mode == "cv_only" or (not eeg_available and self.fallback == "cv")
        if use_cv:
            g.mode = "cv"
        else:
            g.score = self.monitor.scores(now)[g.ability]
            g.n = self.monitor.count(now)
            thr = self.threshold(spell)
            if not eeg_available:
                g.rejected = "eeg_unavailable"
            elif g.score is None:
                g.rejected = "no_eeg_data"
            elif g.score < thr:
                g.rejected = "below_threshold"

        if g.rejected is not None:
            self.decisions.append(self._event("ignored", g, now, reason=g.rejected))
            return []

        g.accepted = True
        if kind == "instant":
            g.triggered = True
            ev = self._event("ability_triggered", g, now)
        else:
            g.active, g.active_since = True, now
            ev = self._event("ability_started", g, now)
        self.decisions.append(ev)
        return [ev]

    def _event(self, name: str, g: _Gesture, now: float, **extra) -> dict:
        ev = {
            "event": name,
            "timestamp": round(self._wall(), 3),
            "ability": g.ability,
            "kind": g.kind,
            "mode": g.mode,
            "decision": "instant",
            "cv_confidence": round(g.cv_conf, 3),
            "since_gesture_start": round(now - g.onset, 3),
        }
        if g.mode == "eeg":
            ev.update(eeg_score=None if g.score is None else round(g.score, 3),
                      eeg_threshold=round(self.threshold(g.spell), 3), eeg_windows=g.n)
        ev.update(extra)
        return ev

    def pop_decisions(self) -> List[dict]:
        out, self.decisions = self.decisions, []
        return out

    # ------------------------------------------------------------------ display
    def view(self) -> Optional[dict]:
        g = self.gesture
        if g is None:
            return None
        return dict(ability=g.ability, kind=g.kind, mode=g.mode, score=g.score, rolling=None,
                    n=g.n, threshold=self.threshold(g.spell), active=g.active,
                    triggered=g.triggered, gesture_on=True, rejected=g.rejected)
