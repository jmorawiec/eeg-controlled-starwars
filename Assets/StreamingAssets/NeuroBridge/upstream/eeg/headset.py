# -*- coding: utf-8 -*-
"""EEG acquisition (g.tec BCI Core-8 via gpype) + per-spell scoring.

* :class:`SpellWindowProcessor` - samples in, scored 1 s windows out. No
  hardware; also used to replay recordings (``eeg/tools/replay_recording.py``).
* :class:`EEGSpellStream` - runs the headset (or a simulator) in gpype's
  background threads; call :meth:`EEGSpellStream.poll` from the camera loop.

Signal path (same as the training recordings): BCI Core-8 with OSCAR ->
gpype band-pass 1-30 Hz -> gpype band-stop 48-52 Hz -> causal 8-30 Hz ->
1 s windows every 0.25 s -> log-covariance -> logistic regression per spell.
"""

from __future__ import annotations

import threading
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import joblib
import numpy as np

from eeg.features import ARTIFACT_UV, CH, RATE, STEP, WIN, StreamFilter, features

EEG_DIR = Path(__file__).resolve().parent
DEFAULT_MODEL_PATH = EEG_DIR / "models" / "live_models.joblib"
DEFAULT_SERIAL = "U8-2025.08.21"
CONTACT_R = 0.3          # min |r| with any other channel = electrode touches the scalp
STALE_S = 1.5            # no samples for this long = headset not streaming


@dataclass
class EEGWindow:
    """One scored 1 s window. ``probs`` = P(gesture WITH imagination) per spell."""
    t_start: float
    t_end: float
    probs: Dict[str, float]
    artifact: bool = False


@dataclass
class EEGStatus:
    mode: str
    streaming: bool = False
    battery: Optional[float] = None
    seconds_since_data: float = float("inf")
    contact: List[Tuple[str, str]] = field(default_factory=list)   # problems, all channels
    blocking: List[Tuple[str, str]] = field(default_factory=list)  # problems on MODEL channels

    @property
    def available(self) -> bool:
        """Data is flowing and every channel the models use touches the scalp."""
        return self.streaming and not self.blocking

    def text(self) -> str:
        if self.mode == "off":
            return "EEG off (camera only)"
        if not self.streaming:
            return "EEG: NO DATA - headset connected / charged?"
        bat = "" if self.battery is None else " {:.0f}%".format(self.battery)
        if not self.contact:
            return "EEG{}: contact OK".format(bat)
        return "EEG{}: {}".format(bat, "; ".join("{} {}".format(c, p) for c, p in self.contact))


def load_bundle(model_path=DEFAULT_MODEL_PATH) -> dict:
    bundle = joblib.load(str(model_path))
    for key in ("spells", "channels"):
        if key not in bundle:
            raise ValueError("{} is not a spell model bundle (missing '{}')".format(model_path, key))
    return bundle


class SpellWindowProcessor:
    """Turns a 1-30 Hz EEG stream into scored windows. Pure computation."""

    def __init__(self, bundle: dict):
        self.models = {sp: m["model"] for sp, m in bundle["spells"].items()}
        self.thresholds = {sp: float(m["threshold"]) for sp, m in bundle["spells"].items()}
        self.channels = list(bundle["channels"])
        self.idx = [CH.index(c) for c in self.channels]
        self.filt = StreamFilter(len(self.idx))
        self.buf = np.zeros((0, len(CH)))            # 1-30 Hz, all channels, last 10 s
        self.b830 = np.zeros((0, len(self.idx)))     # 8-30 Hz, model channels, last 2 s
        self.t830 = np.zeros(0)                      # sample times for b830
        self.n = 0                                   # samples received in total
        self.next_end = WIN                          # sample count at which the next window ends

    def push(self, x: np.ndarray, t_last: float) -> List[EEGWindow]:
        """``x``: samples x >=8 channels (CH order), ``t_last``: monotonic time of the last sample."""
        x = np.asarray(x, dtype=float)[:, :len(CH)]
        if len(x) == 0:
            return []
        t = t_last - (len(x) - 1 - np.arange(len(x))) / RATE
        self.n += len(x)
        self.buf = np.vstack([self.buf, x])[-10 * RATE:]
        self.b830 = np.vstack([self.b830, self.filt(x[:, self.idx])])[-2 * RATE:]
        self.t830 = np.concatenate([self.t830, t])[-2 * RATE:]
        out = []
        while self.n >= self.next_end:
            end = len(self.b830) - (self.n - self.next_end)
            if end >= WIN:
                out.append(self._window(end))
            self.next_end += STEP
        return out

    def _window(self, end: int) -> EEGWindow:
        w830 = self.b830[end - WIN:end]
        off = len(self.buf) - len(self.b830)
        raw = self.buf[off + end - WIN:off + end][:, self.idx]
        t0, t1 = float(self.t830[end - WIN]), float(self.t830[end - 1])
        if np.abs(raw).max() >= ARTIFACT_UV:
            return EEGWindow(t0, t1, {}, artifact=True)
        f = features(w830)[None]
        return EEGWindow(t0, t1, {sp: float(m.predict_proba(f)[0, 1]) for sp, m in self.models.items()})

    def contact(self) -> List[Tuple[str, str]]:
        """Electrode problems over the last 10 s: flat / not touching / noisy."""
        if len(self.buf) < 4 * RATE:
            return [("all", "warming up")]
        x = self.buf - self.buf.mean(0)
        rms = np.sqrt((x ** 2).mean(0))
        c = np.nan_to_num(np.abs(np.corrcoef(x.T)))
        np.fill_diagonal(c, 0)
        out = []
        for i, ch in enumerate(CH):
            if rms[i] < 0.5:
                out.append((ch, "flat"))
            elif c[i].max() < CONTACT_R:
                out.append((ch, "not touching"))
            elif rms[i] > 30:
                out.append((ch, "noisy"))
        return out


class EEGSpellStream:
    """Headset (``mode='headset'``) or gpype simulator (``mode='sim'``) -> scored windows.

    The simulator produces random EEG: use it to test the plumbing only, its
    spell decisions are meaningless.
    """

    def __init__(self, model_path=DEFAULT_MODEL_PATH, mode: str = "headset",
                 serial: str = DEFAULT_SERIAL):
        if mode not in ("headset", "sim"):
            raise ValueError("mode must be 'headset' or 'sim'")
        import gpype as gp                           # imported here: camera-only runs need no gpype

        self.mode = mode
        self.bundle = load_bundle(model_path)
        self.processor = SpellWindowProcessor(self.bundle)
        self.thresholds = self.processor.thresholds
        self._last_data = None
        self._contact: List[Tuple[str, str]] = [("all", "warming up")]
        self._contact_at = 0.0

        if mode == "sim":
            self.source = gp.Generator(sampling_rate=RATE, channel_count=len(CH), signal_frequency=10,
                                       signal_amplitude=10, noise_amplitude=10)
        else:
            self.source = _core8_class(gp)(serial=serial, enable_oscar=True)
        self._sink = _make_sink(gp)()
        self.pipeline = gp.Pipeline()
        bandpass = gp.Bandpass(f_lo=1, f_hi=30)
        notch = gp.Bandstop(f_lo=48, f_hi=52)
        self.pipeline.connect(self.source, bandpass)
        self.pipeline.connect(bandpass, notch)
        self.pipeline.connect(notch, self._sink)
        self._started = False
        self.recorder = None                         # eeg.recorder.SessionRecorder: gets every sample

    def start(self) -> None:
        self.pipeline.start()
        self._started = True

    def poll(self) -> List[EEGWindow]:
        """Process everything that arrived since the last call. Call it often (every frame)."""
        out = []
        for t_arrival, frame in self._sink.take():
            if self.recorder is not None:
                self.recorder.add(frame, t_arrival)
            out += self.processor.push(frame, t_arrival)
            self._last_data = t_arrival
        now = time.monotonic()
        if now - self._contact_at >= 2.0:
            self._contact = self.processor.contact()
            self._contact_at = now
        return out

    def status(self) -> EEGStatus:
        now = time.monotonic()
        since = float("inf") if self._last_data is None else now - self._last_data
        used = set(self.processor.channels) | {"all"}
        blocking = [(c, p) for c, p in self._contact
                    if c in used and p in ("flat", "not touching", "warming up")]
        return EEGStatus(mode=self.mode, streaming=since < STALE_S,
                         battery=getattr(self.source, "battery", None),
                         seconds_since_data=since, contact=list(self._contact), blocking=blocking)

    def stop(self) -> None:
        if self._started:
            self._started = False
            try:
                self.pipeline.stop()
            finally:
                self.pipeline.close()


def _make_sink(gp):
    IN = gp.Constants.Defaults.PORT_IN

    class _Sink(gp.INode):
        """Collects (arrival time, frame) in gpype's thread."""

        def __init__(self, **kw):
            super().__init__(input_ports=[gp.IPort.Configuration(
                name=IN, timing=gp.Constants.Timing.INHERITED)], **kw)
            self._lock = threading.Lock()
            self._pending = []

        def step(self, data):
            with self._lock:
                self._pending.append((time.monotonic(), np.array(data[IN])))
            return {}

        def take(self):
            with self._lock:
                p, self._pending = self._pending, []
            return p

    return _Sink


def _core8_class(gp):
    class Core8(gp.BCICore):
        """BCICore that also keeps the battery level (%), which gpype otherwise drops."""

        battery = None

        def _resolve_channels(self, device):
            super()._resolve_channels(device)
            self._bat_idx = self._channel_of(list(device.channel_types), "BAT")

        def _data_callback(self, data):
            try:
                rows = np.asarray(data).reshape(-1, self._sample_width)
                self.battery = float(rows[-1, self._bat_idx])
            except Exception:
                pass
            super()._data_callback(data)

    return Core8
