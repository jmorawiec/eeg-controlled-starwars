# -*- coding: utf-8 -*-
"""Quick signal check: is every electrode touching, clean and carrying EEG?

    python -m eeg.tools.check_signal            # headset: 5 s settle + 5 s eyes open + 5 s eyes closed
    python -m eeg.tools.check_signal --watch    # live table every 2 s while you fix electrodes (Ctrl+C ends)
    python -m eeg.tools.check_signal --sim      # no headset (plumbing only)
    python -m eeg.tools.check_signal --file eeg/data/game/game_x.csv.gz   # last 10 s of a recording

Run it before recording or playing. Exit code: 0 all good, 1 usable, 2 fix
electrodes, 3 no data from the headset (then: ``python -m eeg.tools.doctor --headset``).

Per channel (1-30 Hz filtered unless noted):
  level      RMS in uV. < 0.5 flat (red), > 30 noisy (red), peak > 100 spikes (orange)
  hum        50 Hz power / EEG power, from the unfiltered signal. > 1 red, > 0.3 orange
  contact    strongest correlation with any other channel. Real EEG always shares
             something with its neighbours; < 0.3 = not on the scalp (red)
  vs others  RMS / median RMS of the good channels, and vs its left/right twin
             (C3-C4, PO7-PO8). < 0.6 = weak contact (orange)
  alpha      eyes closed / eyes open 8-13 Hz power. Back channels (Pz, PO7, POz,
             PO8) should rise >= 1.5x - proof that the signal is brain, not noise

The spell models use the channels in ``eeg/models/live_models.joblib``
(``*`` in the table): a red one there blocks abilities in the game.
"""

import argparse
import functools
import os
import sys
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scipy.signal import butter, filtfilt, iirnotch, sosfiltfilt, welch   # noqa: E402

from eeg.features import CH, RATE                                          # noqa: E402
from eeg.headset import DEFAULT_MODEL_PATH, DEFAULT_SERIAL                 # noqa: E402

SETTLE_S, PHASE_S = 5, 5
BACK = ["Pz", "PO7", "POz", "PO8"]
TWIN = {"C3": "C4", "C4": "C3", "PO7": "PO8", "PO8": "PO7"}
WHERE = {
    "Fz": "front, middle, a few cm above the hairline",
    "C3": "left side, halfway between top of head and left ear",
    "Cz": "top of the head, middle",
    "C4": "right side, halfway between top of head and right ear",
    "Pz": "middle, halfway between top of head and the bump at the back",
    "PO7": "back of head, left, behind and above the left ear",
    "POz": "back of head, middle, just above the bump",
    "PO8": "back of head, right, behind and above the right ear",
}
RED, ORANGE, GREEN, DIM, END = "\033[91m", "\033[93m", "\033[92m", "\033[2m", "\033[0m"
NO_DATA_S = 20


@functools.lru_cache(maxsize=1)
def model_channels():
    try:
        import joblib
        return list(joblib.load(str(DEFAULT_MODEL_PATH))["channels"])
    except Exception:
        return []


# ------------------------------------------------------------------ acquisition
class RawStream:
    """Headset (OSCAR on, NOT filtered - hum is judged on the raw signal) or simulator."""

    def __init__(self, sim=False, serial=DEFAULT_SERIAL):
        import gpype as gp
        from eeg.headset import _core8_class, _make_sink
        self.source = (gp.Generator(sampling_rate=RATE, channel_count=len(CH), signal_frequency=10,
                                    signal_amplitude=10, noise_amplitude=10) if sim
                       else _core8_class(gp)(serial=serial, enable_oscar=True))
        self.sink = _make_sink(gp)()
        self.p = gp.Pipeline()
        self.p.connect(self.source, self.sink)
        self.buf = np.zeros((0, len(CH)))

    @property
    def battery(self):
        return getattr(self.source, "battery", None)

    def start(self):
        self.p.start()

    def pull(self):
        frames = [np.asarray(f, float)[:, :len(CH)] for _, f in self.sink.take()]
        if frames:
            self.buf = np.vstack([self.buf] + frames)
        return len(self.buf)

    def wait_for(self, n, timeout):
        t_end = time.monotonic() + timeout
        while self.pull() < n:
            if time.monotonic() > t_end:
                return False
            time.sleep(0.02)
        return True

    def stop(self):
        try:
            self.p.stop()
        finally:
            self.p.close()


def no_data(sim):
    print(RED + "No data from the {} after {} s.".format("simulator" if sim else "headset", NO_DATA_S) + END)
    print("  Headset on and charged? Connected to another computer/app (it then stops advertising)?\n"
          "  Diagnose: python -m eeg.tools.doctor --headset")
    return 3


def acquire(sim, serial):
    s = RawStream(sim, serial)
    print("Connecting ..." if not sim else "Simulated source ...", flush=True)
    s.start()
    try:
        if not s.wait_for(1, NO_DATA_S):
            return None
        print("Settling {} s - sit still, eyes open".format(SETTLE_S), flush=True)
        time.sleep(SETTLE_S)
        s.pull()
        s.buf = s.buf[:0]
        print(">>> EYES OPEN, look at one point, relax ({} s)".format(PHASE_S), flush=True)
        ok = s.wait_for(PHASE_S * RATE, PHASE_S + NO_DATA_S)
        split = len(s.buf)
        print(">>> CLOSE YOUR EYES, relax ({} s)\a".format(PHASE_S), flush=True)
        ok = ok and s.wait_for(split + PHASE_S * RATE, PHASE_S + NO_DATA_S)
        print(">>> done, open your eyes\a", flush=True)
        battery = s.battery
    finally:
        s.stop()
    if not ok:
        return None
    x = s.buf
    return x[:split], x[split:split + PHASE_S * RATE], battery


def watch(sim, serial, every=2.0, span=4.0):
    s = RawStream(sim, serial)
    print("Connecting ..." if not sim else "Simulated source ...", flush=True)
    s.start()
    try:
        if not s.wait_for(int(span * RATE), NO_DATA_S + span):
            return no_data(sim)
        worst = 0
        while True:
            s.pull()
            s.buf = s.buf[-int(span * RATE):]
            print("\033[2J\033[H" + "Live check, last {:.0f} s - Ctrl+C to stop".format(span))
            worst = check(s.buf, battery=s.battery, final=False)
            time.sleep(every)
    except KeyboardInterrupt:
        print()
        return worst
    finally:
        s.stop()


# ------------------------------------------------------------------ checks
def band(x):
    x = sosfiltfilt(butter(4, [1, 30], btype="band", fs=RATE, output="sos"), x, axis=0)
    b, a = iirnotch(50, 30, RATE)
    return filtfilt(b, a, x, axis=0)


def power(x, lo, hi):
    f, p = welch(x, RATE, nperseg=min(len(x), RATE), axis=0)
    return p[(f >= lo) & (f < hi)].sum(0)


def check(raw_open, raw_closed=None, battery=None, final=True):
    raw = raw_open if raw_closed is None else np.vstack([raw_open, raw_closed])
    x = band(raw)
    rms = np.sqrt((x ** 2).mean(0))
    peak = np.abs(x).max(0)
    hum = power(raw - raw.mean(0), 48, 52) / (power(raw - raw.mean(0), 1, 30) + 1e-12)
    c = np.corrcoef(x.T)
    np.fill_diagonal(c, 0)
    contact = np.nan_to_num(np.abs(c)).max(1)
    good = (rms > 0.5) & (contact >= 0.3)
    ref = np.median(rms[good]) if good.any() else np.median(rms)
    alpha = None
    if raw_closed is not None:
        ao, ac = power(band(raw_open), 8, 13), power(band(raw_closed), 8, 13)
        alpha = ac / (ao + 1e-12)
    used = model_channels()

    rows, worst = [], 0
    for i, ch in enumerate(CH):
        issues, level = [], 0          # 0 ok, 1 check, 2 bad

        def flag(lv, msg):
            nonlocal level
            issues.append(msg)
            level = max(level, lv)

        if rms[i] < 0.5:
            flag(2, "flat - no signal")
        elif contact[i] < 0.3:
            flag(2, "not touching scalp (moves with no other channel)")
        if rms[i] > 30:
            flag(2, "very noisy")
        elif peak[i] > 100:
            flag(1, "spikes - movement or loose pin")
        if hum[i] > 1:
            flag(2, "50 Hz hum stronger than EEG - bad contact")
        elif hum[i] > 0.3:
            flag(1, "some 50 Hz hum")
        rel = rms[i] / ref
        if good[i] and rel < 0.6:
            flag(1, "weak ({:.1f}x the others) - more gel / press pin through hair".format(rel))
        tw = TWIN.get(ch)
        if tw and good[i] and good[CH.index(tw)]:
            r = rms[i] / rms[CH.index(tw)]
            if r < 0.6:
                flag(1, "only {:.1f}x its twin {}".format(r, tw))
        if alpha is not None and ch in BACK and good[i] and alpha[i] < 1.2:
            flag(1, "no alpha rise with eyes closed")
        worst = max(worst, level)
        rows.append((ch, level, rms[i], peak[i], hum[i], contact[i],
                     None if alpha is None else alpha[i], issues))

    # ---- print
    colour = {0: GREEN + "OK   ", 1: ORANGE + "CHECK", 2: RED + "BAD  "}
    print("\n{:<6}{:6}{:>9}{:>7}{:>6}{:>9}".format("ch", "", "level uV", "peak", "hum", "contact")
          + ("{:>11}".format("alpha c/o") if alpha is not None else "") + "   notes")
    for ch, lv, r, pk, h, ct, al, iss in rows:
        name = ch + ("*" if ch in used else "")
        print("{:<6}{}{}{:9.1f}{:7.0f}{:6.2f}{:9.2f}".format(name, colour[lv], END, r, pk, h, ct)
              + ("{:10.1f}x".format(al) if al is not None else "") + "   " + "; ".join(iss))
    if used:
        print(DIM + "* = used by the spell models" + END)

    if alpha is not None:
        back = [alpha[CH.index(c)] for c in BACK if good[CH.index(c)]]
        a = np.median(back) if back else 0
        verdict = (GREEN + "yes" if a >= 1.5 else ORANGE + "weak" if a >= 1.2 else RED + "no") + END
        print("\nBrain signal present (alpha rises with eyes closed at the back): {}"
              "  - median {:.1f}x over {} back channel(s)".format(verdict, a, len(back)))
    if battery is not None:
        print("Battery: {:.0f}%{}".format(battery, "  - charge soon" if battery < 20 else ""))
    blocking = [ch for ch, lv, *_ in rows if lv == 2 and ch in used]
    if blocking:
        print(RED + "Model channel(s) {} bad - abilities will be blocked in the game".format(blocking) + END)
    fix = [(ch, iss) for ch, lv, *_, iss in rows if lv]
    if fix and final:
        print("\nFix these electrodes:")
        for ch, iss in fix:
            print("  {:<4} {}".format(ch, WHERE[ch]))
    print("\n" + (GREEN + "ALL GOOD - ready to record" if worst == 0 else
                  ORANGE + "Usable, but check the electrodes above" if worst == 1 else
                  RED + "Fix the electrodes above, then run the check again") + END)
    return worst


def main(argv=None):
    try:
        import colorama
        colorama.just_fix_windows_console()
    except Exception:
        pass
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sim", action="store_true", help="simulated signal (tests the plumbing only)")
    ap.add_argument("--serial", default=DEFAULT_SERIAL)
    ap.add_argument("--watch", action="store_true", help="refresh every 2 s, no eyes test")
    ap.add_argument("--file", help="check the last 10 s of a recording (.csv / .csv.gz) instead")
    a = ap.parse_args(argv)
    if a.file:
        import pandas as pd
        df = pd.read_csv(a.file)
        print("{}: last {} s (recordings are already filtered; hum not meaningful)".format(
            os.path.basename(a.file), 2 * PHASE_S))
        return check(df[CH].to_numpy()[-2 * PHASE_S * RATE:])
    if a.watch:
        return watch(a.sim, a.serial)
    got = acquire(a.sim, a.serial)
    if got is None:
        return no_data(a.sim)
    return check(*got)


if __name__ == "__main__":
    raise SystemExit(main())
