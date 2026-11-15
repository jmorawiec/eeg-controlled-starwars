# -*- coding: utf-8 -*-
"""EEG gating of camera gestures (g.tec BCI Core-8).

``SpellGate`` / ``GateConfig`` / ``ABILITIES`` have no hardware dependencies.
``EEGSpellStream`` needs gpype (see ``eeg/requirements-eeg.txt``) and is
imported lazily so camera-only runs work without it.
"""

from eeg.spell_gate import ABILITIES, GateConfig, SpellGate

__all__ = ["ABILITIES", "GateConfig", "SpellGate", "EEGSpellStream", "EEGStatus", "load_bundle"]


def __getattr__(name):
    if name in ("EEGSpellStream", "EEGStatus", "load_bundle", "SpellWindowProcessor"):
        from eeg import headset
        return getattr(headset, name)
    raise AttributeError(name)
