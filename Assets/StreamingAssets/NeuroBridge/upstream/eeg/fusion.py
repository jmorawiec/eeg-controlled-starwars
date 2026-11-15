# -*- coding: utf-8 -*-
"""Ciągła predykcja z kamery, z EEG i predykcja łączona (sama logika, bez I/O).

Służy do wizualizacji: pokazuje na bieżąco, co widzi każde źródło osobno
i co z nich wynika razem. Zdarzenia wysyłane do Unity dalej wyznacza
:class:`eeg.spell_gate.SpellGate` (antymigotanie, histereza, min. liczba okien).

* **Kamera** - pełny wektor prawdopodobieństw klasyfikatora gestów
  (``GesturePrediction.probabilities``) zawężony do umiejętności.
* **EEG** - dla każdej umiejętności średnie P(wyobrażenie) z ostatnich
  ``eeg_windows`` okien 1 s (bez artefaktów). Modele EEG są niezależne
  (każdy ma własny próg), więc do porównań wynik jest przeskalowany tak,
  że próg modelu = 0.5 (:func:`eeg_relative`).
* **Łącznie** - średnia geometryczna ważona:
  ``fused = cv ** w * eeg_rel ** (1 - w)`` (``w = cv_weight``). Wysoka tylko,
  gdy oba źródła się zgadzają - jak w bramce, która wymaga gestu I wyobrażenia.
  Bez EEG (wyłączone / brak danych / brak kontaktu) ``fused = cv``.
  Predykcja łączona = umiejętność z największym ``fused``, o ile
  ``fused >= decision_threshold``.
"""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field
from typing import Deque, Dict, Iterable, Optional, Sequence

from eeg.spell_gate import ABILITIES


@dataclass
class FusionConfig:
    eeg_windows: int = 4              # EEG: średnia z ostatnich N okien (co 0.25 s)
    eeg_stale_s: float = 2.0          # ostatnie okno starsze niż tyle s = brak EEG
    cv_weight: float = 0.5            # waga kamery w predykcji łączonej (0..1)
    decision_threshold: float = 0.5   # min. wynik łączny, by wskazać umiejętność


@dataclass
class FusedPrediction:
    cv: Dict[str, float]                       # ability -> P z kamery
    eeg: Dict[str, Optional[float]]            # ability -> średnie P z EEG (None = brak)
    eeg_threshold: Dict[str, float]            # ability -> próg modelu EEG
    eeg_rel: Dict[str, Optional[float]]        # ability -> EEG przeskalowane (próg = 0.5)
    fused: Dict[str, float]                    # ability -> wynik łączny
    label: Optional[str]                       # predykcja łączna (None = żadna)
    confidence: float
    eeg_used: bool                             # czy EEG weszło do wyniku łącznego
    eeg_artifact: bool = False                 # ostatnie okno EEG odrzucone (artefakt)
    cv_label: Optional[str] = None             # najlepsza umiejętność wg kamery
    eeg_label: Optional[str] = None            # najlepsza umiejętność wg EEG (powyżej progu)
    abilities: Sequence[str] = field(default_factory=lambda: list(ABILITIES))


def eeg_relative(p: Optional[float], threshold: float) -> Optional[float]:
    """Odcinkowo liniowo: 0 -> 0, próg -> 0.5, 1 -> 1."""
    if p is None:
        return None
    thr = min(max(float(threshold), 1e-6), 1 - 1e-6)
    p = min(max(float(p), 0.0), 1.0)
    if p <= thr:
        return 0.5 * p / thr
    return 0.5 + 0.5 * (p - thr) / (1 - thr)


def cv_ability_probs(labels: Sequence[str], probabilities: Optional[Sequence[float]]) -> Dict[str, float]:
    """Prawdopodobieństwa kamery dla umiejętności (brak klasyfikacji = zera)."""
    out = {a: 0.0 for a in ABILITIES}
    if probabilities is None:
        return out
    for label, p in zip(labels, probabilities):
        if label in out:
            out[label] = float(p)
    return out


class EEGMonitor:
    """Przechowuje ostatnie okna EEG i daje bieżący wynik każdej umiejętności."""

    def __init__(self, thresholds: Dict[str, float], n_windows: int = 4, stale_s: float = 2.0):
        self.thresholds = dict(thresholds)          # spell -> próg (z threshold_offset)
        self.n_windows = max(1, int(n_windows))
        self.stale_s = float(stale_s)
        self._windows: Deque = deque(maxlen=self.n_windows)
        self.last_t_end: Optional[float] = None
        self.last_artifact = False

    def add_windows(self, windows: Iterable) -> None:
        for w in windows:
            self.last_t_end = w.t_end
            self.last_artifact = bool(w.artifact)
            if not w.artifact:
                self._windows.append(w)

    def scores(self, now: float) -> Dict[str, Optional[float]]:
        """ability -> średnie P(wyobrażenie) z ostatnich okien; None gdy brak świeżych danych."""
        fresh = self.last_t_end is not None and now - self.last_t_end <= self.stale_s
        out: Dict[str, Optional[float]] = {}
        for ability, (spell, _) in ABILITIES.items():
            ps = [w.probs[spell] for w in self._windows if spell in w.probs] if fresh else []
            out[ability] = sum(ps) / len(ps) if ps else None
        return out

    def count(self, now: float) -> int:
        """Liczba świeżych okien (bez artefaktów), z których liczony jest wynik."""
        fresh = self.last_t_end is not None and now - self.last_t_end <= self.stale_s
        return len(self._windows) if fresh else 0

    def ability_thresholds(self) -> Dict[str, float]:
        return {a: self.thresholds.get(spell, 0.5) for a, (spell, _) in ABILITIES.items()}


def fuse(cv: Dict[str, float], eeg: Dict[str, Optional[float]], eeg_threshold: Dict[str, float],
         eeg_available: bool = True, config: Optional[FusionConfig] = None,
         eeg_artifact: bool = False) -> FusedPrediction:
    cfg = config or FusionConfig()
    w = min(max(float(cfg.cv_weight), 0.0), 1.0)
    rel = {a: eeg_relative(eeg.get(a), eeg_threshold.get(a, 0.5)) for a in ABILITIES}
    eeg_used = eeg_available and any(v is not None for v in rel.values())

    fused = {}
    for a in ABILITIES:
        c = max(0.0, float(cv.get(a, 0.0)))
        r = rel[a]
        if not eeg_used:
            fused[a] = c                      # sama kamera
        elif r is None:
            fused[a] = 0.0
        else:
            fused[a] = c ** w * r ** (1 - w)

    best = max(fused, key=fused.get)
    label = best if fused[best] >= cfg.decision_threshold else None

    cv_best = max(cv, key=cv.get) if cv else None
    cv_label = cv_best if cv_best is not None and cv[cv_best] > 0 else None
    eeg_cands = {a: r for a, r in rel.items() if r is not None and r >= 0.5}
    eeg_label = max(eeg_cands, key=eeg_cands.get) if eeg_cands else None

    return FusedPrediction(cv=dict(cv), eeg=dict(eeg), eeg_threshold=dict(eeg_threshold), eeg_rel=rel,
                           fused=fused, label=label, confidence=fused[best], eeg_used=eeg_used,
                           eeg_artifact=eeg_artifact, cv_label=cv_label, eeg_label=eeg_label)
