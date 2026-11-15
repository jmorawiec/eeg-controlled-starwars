# -*- coding: utf-8 -*-
"""EEG features - identical to the ones the spell models were trained with.

Input: EEG already filtered by gpype (OSCAR + 1-30 Hz band-pass + 48-52 Hz
band-stop), exactly like the training recordings. On top: a CAUSAL 8-30 Hz
band-pass, 1 s windows every 0.25 s, log-covariance (upper triangle) ->
logistic regression (one model per spell, ``models/live_models.joblib``).
"""

import numpy as np
from scipy.signal import butter, sosfilt, sosfilt_zi

RATE = 250                      # Hz, BCI Core-8
WIN = RATE                      # 1 s window
STEP = RATE // 4                # a new window every 0.25 s
ARTIFACT_UV = 100               # window skipped if any model channel exceeds this
CH = ["Fz", "C3", "Cz", "C4", "Pz", "PO7", "POz", "PO8"]   # BCI Core-8 montage order
SOS = butter(4, [8, 30], btype="band", fs=RATE, output="sos")


def bandpass(x):
    """Causal 8-30 Hz over a whole recording (samples x channels) - what
    :class:`StreamFilter` produces chunk by chunk live (training uses this)."""
    return sosfilt(SOS, x, axis=0)


class StreamFilter:
    """Causal 8-30 Hz band-pass that keeps its state between chunks."""

    def __init__(self, n_ch):
        self.zi = np.repeat(sosfilt_zi(SOS)[:, :, None], n_ch, axis=2) * 0.0

    def __call__(self, chunk):
        y, self.zi = sosfilt(SOS, chunk, axis=0, zi=self.zi)
        return y


def logm_spd(c):
    w, v = np.linalg.eigh(c)
    return (v * np.log(np.maximum(w, 1e-10))) @ v.T


def features(win830):
    """One 1 s window (samples x channels, 8-30 Hz) -> log-covariance vector."""
    n = win830.shape[1]
    c = np.cov(win830.T) + 1e-6 * np.eye(n)
    return logm_spd(c)[np.triu_indices(n)]
