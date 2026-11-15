# -*- coding: utf-8 -*-
"""EEG troubleshooting: environment, model, data and (optionally) the headset.

    python -m eeg.tools.doctor               # packages, model bundle, recordings
    python -m eeg.tools.doctor --scan        # + which BLE amplifiers are in range
    python -m eeg.tools.doctor --headset     # + connect, measure sample rate, battery, contact
    python -m eeg.tools.doctor --sim         # + the same through gpype's simulator (no headset)

Every problem comes with what to do. Exit code = number of problems found.
For electrode-level problems use ``python -m eeg.tools.check_signal``.
"""

import argparse
import platform
import re
import shutil
import subprocess
import sys
import time
import warnings
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from eeg.features import CH, RATE                                    # noqa: E402
from eeg.headset import DEFAULT_MODEL_PATH, DEFAULT_SERIAL           # noqa: E402

REQ = ROOT / "eeg" / "requirements-eeg.txt"
OK, WARN, BAD = "\033[92m ok \033[0m", "\033[93mwarn\033[0m", "\033[91mFAIL\033[0m"
problems = []


def say(level, what, fix=""):
    print("[{}] {}".format(level, what))
    if fix:
        print("       -> " + fix)
    if level == BAD:
        problems.append(what)


# ------------------------------------------------------------------ checks
def check_packages():
    from importlib import metadata
    print("\n== Python packages ({})".format(REQ.relative_to(ROOT)))
    say(OK, "Python {} on {}".format(platform.python_version(), platform.platform(terse=True)))
    for line in REQ.read_text().splitlines():
        m = re.match(r"\s*([A-Za-z0-9_.\-]+)\s*(==|>=)\s*([0-9][^\s#]*)", line)
        if not m:
            continue
        name, op, want = m.groups()
        try:
            have = metadata.version(name)
        except metadata.PackageNotFoundError:
            optional = name == "ai-edge-litert"
            say(WARN if optional else BAD, "{} not installed".format(name),
                "pip install -r eeg/requirements-eeg.txt" + (" (only needed without tensorflow)" if optional else ""))
            continue
        if op == "==" and have != want:
            say(BAD if name == "scikit-learn" else WARN, "{} {} installed, {} pinned".format(name, have, want),
                "pip install {}=={}".format(name, want)
                + ("  (the pickled models need the exact version)" if name == "scikit-learn" else ""))
        else:
            say(OK, "{} {}".format(name, have))
    try:
        import pandas  # noqa: F401
        say(OK, "pandas {}".format(pandas.__version__))
    except ImportError:
        say(BAD, "pandas not installed (training / recording)", "pip install pandas")


def check_model(path):
    print("\n== Model bundle {}".format(path))
    if not Path(path).exists():
        say(BAD, "model file missing", "git checkout eeg/models/live_models.joblib, or train: python -m eeg.train fit --install")
        return None
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        try:
            from eeg.headset import load_bundle
            b = load_bundle(path)
        except Exception as e:
            say(BAD, "cannot load: {}".format(e), "pip install -r eeg/requirements-eeg.txt (scikit-learn version!)")
            return None
    version_warn = [w for w in caught if "version" in str(w.message).lower()]
    if version_warn:
        say(BAD, "pickled with another scikit-learn version: {}".format(str(version_warn[0].message)[:160]),
            "install the pinned scikit-learn, or retrain: python -m eeg.train fit --install")
    import sklearn
    if b.get("sklearn") and b["sklearn"] != sklearn.__version__:
        say(WARN, "trained with scikit-learn {}, running {}".format(b["sklearn"], sklearn.__version__))
    say(OK, "channels {}  {}".format(b["channels"], "created " + b["created"] if "created" in b else ""))
    import numpy as np
    n = len(b["channels"])
    f = np.zeros((1, n * (n + 1) // 2))
    for sp, m in b["spells"].items():
        try:
            p = float(m["model"].predict_proba(f)[0, 1])
        except Exception as e:
            say(BAD, "{}: model does not run ({})".format(sp, e), "retrain: python -m eeg.train fit --install")
            continue
        extra = ""
        if "calibrated_on" in m:
            extra = "  calibrated {} on {}".format(m.get("calibrated_at", ""), ", ".join(m["calibrated_on"]))
        say(OK, "{:<15} threshold {:.3f}  held-out AUC {}  trained on {} recording(s){}".format(
            sp, m["threshold"], "{:.2f}".format(m["cv_auc"]) if m.get("cv_auc") is not None else "?",
            len(m.get("sessions", [])), extra))
        if not 0.0 < p < 1.0:
            say(WARN, "{}: P = {} on a zero input".format(sp, p))
    from eeg.spell_gate import ABILITIES
    missing = [s for s, _ in ABILITIES.values() if s not in b["spells"]]
    if missing:
        say(BAD, "bundle has no model for {}".format(missing), "python -m eeg.train fit --install")
    return b


def check_data():
    from eeg import dataset as D
    print("\n== Recordings in {}".format(D.DATA_DIR.relative_to(ROOT)))
    try:
        recs = D.recordings(include_unused=True)
    except Exception as e:
        say(BAD, "cannot list recordings: {}".format(e))
        return
    used = [r for r in recs if r.use]
    game = [r for r in used if r.kind == "game"]
    say(OK, "{} recordings ({} used, {} in-game)".format(len(recs), len(used), len(game)))
    sims = list(D.DATA_DIR.rglob("sim_*.csv*"))
    if sims:
        say(WARN, "{} simulated recordings (sim_*) - ignored by training, can be deleted".format(len(sims)))
    for name in D.manifest():
        if not any(r.name == name for r in recs):
            say(WARN, "manifest lists {} but the file is missing".format(name))
    if not game:
        say(WARN, "no in-game recordings yet",
            "python integrations/gesture_pipeline.py --calibrate 4, then python -m eeg.train calibrate")
    print("       details: python -m eeg.train list")


def ble_hint(msg):
    """What to do about a gpype / gtec-ble error message."""
    if "org.bluez" in msg or "BlueZ" in msg:
        return "the Bluetooth service (BlueZ) is not running: start it (systemctl start bluetooth / rc-service bluetooth start)"
    if "OPEN_DEVICE_FAILED" in msg:
        return "OPEN_DEVICE_FAILED: wrong device class/serial (U8- = BCI Core, gp.BCICore) or held by another host"
    if "Registered" in msg or "Attest" in msg or "usage key" in msg:
        return "g.tec licensing/attestation problem (gtec_licensing) - the headset may be registered to another computer"
    if "heard no Bluetooth" in msg or "Could not discover" in msg:
        return "headset off / flat / out of range, or connected elsewhere (it stops advertising)"
    return "headset on and charged? right serial? close other apps using it; power-cycle the headset"


def check_bluetooth():
    if platform.system() != "Linux" or not shutil.which("bluetoothctl"):
        return
    try:
        out = subprocess.run(["bluetoothctl", "show"], capture_output=True, text=True, timeout=5).stdout
    except Exception:
        return
    if not out.strip():
        say(BAD, "no Bluetooth adapter found", "plug in / enable the adapter (rfkill unblock bluetooth)")
    elif "Powered: yes" not in out:
        say(BAD, "Bluetooth adapter is off", "bluetoothctl power on")
    else:
        say(OK, "Bluetooth adapter powered")


def scan(serial, seconds=8):
    print("\n== BLE scan ({} s)".format(seconds))
    check_bluetooth()
    try:
        import gpype as gp
        from gpype.common._private import driver_usage               # gpype's own pre-scan step
        driver_usage.register()
        try:
            names = gp.BCICore._scan_window(seconds, None)           # every amplifier heard
        except Exception:
            import gtec_ble
            names = [str(n) for n in gtec_ble.Amplifier.get_connected_devices(rescan=True, scan_timeout=seconds)]
    except Exception as e:
        say(BAD, "scan failed: {}".format(str(e)[:200]), ble_hint(str(e)))
        return
    if not names:
        say(BAD, "no amplifier in range", "headset on (LED)? charged? not connected to another computer/app?")
        return
    for n in names:
        kind = "BCI Core" if n.startswith("U8-") else "Unicorn (gp.HybridBlack, not this code)" \
            if n.startswith("UN-") else "?"
        say(OK, "heard {}  [{}]{}".format(n, kind, "  <- ours" if n == serial else ""))
    if serial not in names:
        say(BAD, "{} not heard".format(serial),
            "wrong serial (--eeg_serial)? or it is connected elsewhere - it stops advertising while connected")


def check_stream(mode, serial, model, seconds=6, timeout=25):
    print("\n== {} stream".format("Headset " + serial if mode == "headset" else "Simulated"))
    from eeg.headset import EEGSpellStream
    try:
        s = EEGSpellStream(model_path=model, mode=mode, serial=serial)
    except Exception as e:
        say(BAD, "cannot set up the stream: {}".format(e), "pip install -r eeg/requirements-eeg.txt")
        return
    t0 = time.monotonic()
    try:
        s.start()
    except Exception as e:
        msg = str(e)
        say(BAD, "connect failed after {:.1f} s: {}".format(time.monotonic() - t0, msg[:300]), ble_hint(msg))
        return
    try:
        while s.processor.n == 0 and time.monotonic() - t0 < timeout:
            s.poll()
            time.sleep(0.05)
        if s.processor.n == 0:
            say(BAD, "connected but no samples within {} s".format(timeout),
                "power-cycle the headset; move closer; fewer BLE devices around")
            return
        say(OK, "first samples after {:.1f} s".format(time.monotonic() - t0))
        n0, t1 = s.processor.n, time.monotonic()
        artifacts = windows = 0
        while time.monotonic() - t1 < seconds:
            w = s.poll()
            windows += len(w)
            artifacts += sum(x.artifact for x in w)
            time.sleep(0.05)
        rate = (s.processor.n - n0) / (time.monotonic() - t1)
        (say(OK, "sample rate {:.0f} Hz".format(rate)) if abs(rate - RATE) < 0.1 * RATE else
         say(BAD, "sample rate {:.0f} Hz, expected {}".format(rate, RATE),
             "BLE drops samples: move closer, away from Wi-Fi routers / USB3 hubs"))
        say(OK if windows else BAD, "{} model windows scored, {} skipped as artifacts (> 100 uV)".format(
            windows, artifacts))
        if windows and artifacts > windows / 2:
            say(WARN, "mostly artifacts", "sit still, check electrodes: python -m eeg.tools.check_signal")
        st = s.status()
        if st.battery is not None:
            say(OK if st.battery >= 20 else WARN, "battery {:.0f}%".format(st.battery))
        s._contact_at = 0
        s.poll()
        st = s.status()
        if st.blocking:
            say(BAD, "electrodes the models need: {}".format(st.blocking),
                "python -m eeg.tools.check_signal --watch while fixing them")
        elif st.contact:
            say(WARN, "electrodes: {}".format(st.contact))
        else:
            say(OK, "contact OK on all channels {}".format(CH))
    finally:
        s.stop()


def main(argv=None):
    try:
        import colorama
        colorama.just_fix_windows_console()
    except Exception:
        pass
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scan", action="store_true", help="list BLE amplifiers in range")
    ap.add_argument("--headset", action="store_true", help="connect to the headset and measure")
    ap.add_argument("--sim", action="store_true", help="same through the gpype simulator")
    ap.add_argument("--serial", default=DEFAULT_SERIAL)
    ap.add_argument("--model", default=str(DEFAULT_MODEL_PATH))
    a = ap.parse_args(argv)
    check_packages()
    check_model(a.model)
    check_data()
    if a.scan:
        scan(a.serial)
    if a.headset or a.sim:
        check_stream("sim" if a.sim else "headset", a.serial, a.model)
    print("\n{} problem(s){}".format(len(problems), "" if problems else " - all good"))
    for p in problems:
        print("  - " + p)
    return len(problems)


if __name__ == "__main__":
    raise SystemExit(main())
