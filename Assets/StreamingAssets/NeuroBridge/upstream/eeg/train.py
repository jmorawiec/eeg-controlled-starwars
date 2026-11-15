# -*- coding: utf-8 -*-
"""Train / update / calibrate the per-spell EEG models (logistic regression).

    python -m eeg.train list                       # what data there is
    python -m eeg.train fit                        # retrain on ALL data in eeg/data (cued + in-game)
    python -m eeg.train calibrate                  # keep the models, re-set thresholds from in-game data
    python -m eeg.train evaluate [recordings...]   # how the current model does on some data

Model (one per spell, same as live): 1 s windows every 0.25 s, causal 8-30 Hz,
log-covariance of the model channels -> StandardScaler -> LogisticRegression.
A trial's score = mean P(imagination) of its windows, like the live gate.

fit
    Every recording in ``eeg/data`` (and ``eeg/data/game``) marked as used in
    ``eeg/data/manifest.json`` (default: used). Channels = good (touching the
    scalp) in every recording used, or ``--channels``. In-game trials can count
    more (``--game_weight``, default 2): the game is where the model has to
    work. Cross-validation holds out one repeat (cued) / one gesture (in-game)
    at a time; the threshold is set for ``--false_rate`` (default 10 %) false
    activations on held-out gesture-only trials - from in-game trials when
    there are at least 5 of them, otherwise from all.
calibrate
    Characteristics changed (new session, other electrode fit, game instead of
    cued recording) but no time to retrain: score labelled in-game trials with
    the existing models and move each threshold to ``--false_rate`` on them.
    Needs gesture-only trials (``--calibrate`` in the pipeline records both).
evaluate
    AUC and caught / false activations at the model's thresholds per spell and
    recording. Recordings the model was trained on are marked ``(trained)``.

Output: ``--out`` (default ``eeg/models/live_models_new.joblib``), so the
running model is not touched; try it with ``gesture_pipeline.py --eeg_model``.
``--install`` replaces ``eeg/models/live_models.joblib`` (old one -> ``eeg/models/backup/``).
"""

from __future__ import annotations

import argparse
import shutil
import sys
import warnings
from datetime import datetime
from pathlib import Path

import joblib
import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from eeg import dataset as D                                   # noqa: E402
from eeg.features import CH, RATE, STEP, WIN                   # noqa: E402
from eeg.headset import DEFAULT_MODEL_PATH, load_bundle        # noqa: E402
from eeg.spell_gate import ABILITIES                           # noqa: E402

SPELLS = ["lightning_push", "heal", "choke_squeeze", "push_with_push"]
assert set(SPELLS) == {s for s, _ in ABILITIES.values()}
ABILITY = {s: a for a, (s, _) in ABILITIES.items()}
KEYS = {"lightning_push": "A", "heal": "S", "choke_squeeze": "D", "push_with_push": "F"}
MODELS_DIR = D.EEG_DIR / "models"
DEFAULT_OUT = MODELS_DIR / "live_models_new.joblib"
MIN_GAME_NEG = 5                 # gesture-only in-game trials needed to set the threshold from them


# ------------------------------------------------------------------ model
def make_model(C=0.1):
    from sklearn.linear_model import LogisticRegression
    from sklearn.pipeline import make_pipeline
    from sklearn.preprocessing import StandardScaler
    return make_pipeline(StandardScaler(), LogisticRegression(C=C, max_iter=3000))


def fit_model(X, y, w=None, C=0.1):
    return make_model(C).fit(X, y, logisticregression__sample_weight=w)


def trial_table(p, y, g, kind):
    """Window probabilities -> one score per trial (mean, as the live gate does)."""
    t = pd.DataFrame(dict(group=g, y=y, score=p, kind=kind)).dropna(subset=["score"])
    return t.groupby(["group", "y", "kind"], sort=False).score.mean().reset_index()


def cross_val(X, y, g, kind, w, C):
    from sklearn.model_selection import GroupKFold, LeaveOneGroupOut
    n = len(np.unique(g))
    split = LeaveOneGroupOut() if n <= 100 else GroupKFold(n_splits=20)
    p = np.full(len(y), np.nan)
    for tr, te in split.split(X, y, g):
        if len(np.unique(y[tr])) == 2:
            p[te] = fit_model(X[tr], y[tr], w[tr], C).predict_proba(X[te])[:, 1]
    return trial_table(p, y, g, kind)


def auc(t):
    from sklearn.metrics import roc_auc_score
    return roc_auc_score(t.y, t.score) if t.y.nunique() == 2 else float("nan")


def threshold_for(neg, false_rate):
    return float(np.quantile(neg, 1 - false_rate)) + 1e-3


def rates(t, thr):
    pos, neg = t.score[t.y == 1], t.score[t.y == 0]
    return "{}/{}".format(int((pos >= thr).sum()), len(pos)), "{}/{}".format(int((neg >= thr).sum()), len(neg))


def collect(recs, spell, chans):
    """-> X, y, group, kind per window (and the recordings that had this spell)."""
    parts, used = [], []
    for r in recs:
        if spell in r.skip_spells:
            continue
        L = r.load()
        if not (L.trials.spell == spell).any():
            continue
        X, y, g = D.spell_windows(L, spell, chans)
        if len(y):
            parts.append((X, y, g, np.full(len(y), r.kind, object)))
            used.append(r)
    if not parts:
        return None, used
    return tuple(np.concatenate([p[i] for p in parts]) for i in range(4)), used


def bad_channels(recs, chans):
    """{recording: [model channels not touching the scalp there]}"""
    return {r.name: [c for c in chans if c not in r.load().good] for r in recs
            if any(c not in r.load().good for c in chans)}


# ------------------------------------------------------------------ commands
def cmd_list(a):
    recs = D.recordings(a.recordings, include_unused=True)
    if not recs:
        print("no recordings in {}".format(D.DATA_DIR))
        return 1
    print("{:<34}{:<6}{:<5}{:>6}  {:<30}{}".format("recording", "kind", "use", "min", "bad channels",
                                                   "trials imagination/only per spell"))
    for r in recs:
        L = r.load()
        tr = L.trials
        cnt = "  ".join("{} {}/{}".format(ABILITY.get(sp, sp), int((g.y == 1).sum()), int((g.y == 0).sum()))
                        for sp, g in tr.groupby("spell", sort=False))
        bad = ",".join(c for c in CH if c not in L.good) or "-"
        print("{:<34}{:<6}{:<5}{:>6.1f}  {:<30}{}".format(r.name, r.kind, "yes" if r.use else "NO",
                                                          L.seconds / 60, bad, cnt or "(no labelled trials)"))
        if r.note:
            print("{:<34}{}".format("", r.note))
    print("\nkind: cued = record_spells.py, game = gesture_pipeline.py --eeg_record/--calibrate."
          "  use: NO = left out by eeg/data/manifest.json")
    return 0


def cmd_fit(a):
    recs = D.recordings(a.recordings)
    if not recs:
        raise SystemExit("no recordings to train on")
    spells = a.spells or SPELLS
    base = None
    if set(spells) != set(SPELLS):                     # partial refit: keep the other spells
        base = load_bundle(a.base)
        print("keeping {} from {}".format([s for s in SPELLS if s not in spells], a.base))
    users = [r for r in recs if any(sp not in r.skip_spells and (r.load().trials.spell == sp).any()
                                    for sp in spells)]
    if a.channels and base is not None and set(a.channels) != set(base["channels"]):
        raise SystemExit("--spells keeps the other spells from --base, so the channels must stay {}; "
                         "refit all spells to change channels".format(base["channels"]))
    if a.channels:
        chans = [c for c in CH if c in a.channels]
    elif base is not None:
        chans = list(base["channels"])
    else:
        chans = [c for c in CH if all(c in r.load().good for r in users)]
        dropped = {c: [r.name for r in users if c not in r.load().good] for c in CH if c not in chans}
        for c, names in dropped.items():
            print("channel {:<4} left out - not touching the scalp in: {}".format(c, ", ".join(names)))
    if len(chans) < 3:
        raise SystemExit("only {} usable channels - fix the recordings or pass --channels".format(chans))
    for name, bad in bad_channels(users, chans).items():
        print("WARNING {}: {} not touching the scalp but used (--channels)".format(name, bad))
    print("channels: {}\n".format(chans))

    out = dict(base["spells"]) if base else {}
    for sp in spells:
        data, used = collect(recs, sp, chans)
        if data is None or len(np.unique(data[1])) < 2:
            print("{:<16} not enough data (needs imagination AND gesture-only trials) - skipped".format(sp))
            if sp not in out:
                raise SystemExit("cannot build a complete model without {}".format(sp))
            continue
        X, y, g, kind = data
        w = np.where(kind == "game", a.game_weight, 1.0)
        t = cross_val(X, y, g, kind, w, a.C)
        game = t[t.kind == "game"]
        src = a.threshold_from
        if src == "auto":
            src = "game" if (game.y == 0).sum() >= MIN_GAME_NEG else "all"
        tt = game if src == "game" else t
        if (tt.y == 0).sum() == 0:
            raise SystemExit("{}: no gesture-only trials in '{}' to set the threshold".format(sp, src))
        thr = threshold_for(tt.score[tt.y == 0], a.false_rate)
        caught, false = rates(tt, thr)
        n_pos, n_neg = int((t.y == 1).sum()), int((t.y == 0).sum())
        n_gp, n_gn = int((game.y == 1).sum()), int((game.y == 0).sum())
        print("{:<16} {} recordings, trials {} imagination / {} only ({} / {} in-game)".format(
            sp, len(used), n_pos, n_neg, n_gp, n_gn))
        print("{:<16} held-out AUC {:.2f} all{}".format(
            "", auc(t), "" if game.empty else ", {:.2f} in-game".format(auc(game))))
        print("{:<16} threshold {:.3f} (from {} trials): caught {}, false activations {}\n".format(
            "", thr, src, caught, false))
        out[sp] = dict(model=fit_model(X, y, w, a.C), threshold=thr, key=KEYS[sp],
                       sessions=[r.name for r in used], cv_auc=float(auc(t)),
                       cv_auc_game=None if game.empty else float(auc(game)),
                       cv_caught=caught, cv_false=false, threshold_from=src,
                       n_trials=dict(imagination=n_pos, only=n_neg, game_imagination=n_gp, game_only=n_gn))
    bundle = dict(spells={sp: out[sp] for sp in SPELLS}, channels=chans, rate=RATE, win=WIN, step=STEP,
                  task=D.CUED_TASK, game_task=D.GAME_TASK, band=(8, 30), **meta(a))
    bundle["recordings"] = sorted({n for m in out.values() for n in m.get("sessions", [])})
    save(bundle, a)
    return 0


def score(bundle, recs, spell):
    """Trial scores of the EXISTING model for ``spell`` on ``recs``."""
    data, used = collect(recs, spell, list(bundle["channels"]))
    if data is None:
        return None, used
    X, y, g, kind = data
    p = bundle["spells"][spell]["model"].predict_proba(X)[:, 1]
    t = trial_table(p, y, g, kind)
    t["recording"] = t.group.str.split("#").str[0]
    return t, used


def cmd_calibrate(a):
    bundle = load_bundle(a.model)
    recs = D.recordings(a.recordings or [str(D.GAME_DIR)])
    if not recs:
        raise SystemExit("no in-game recordings in {} - record some first:\n"
                         "  python integrations/gesture_pipeline.py --calibrate 4".format(D.GAME_DIR))
    print("model {} - channels {}".format(a.model, bundle["channels"]))
    for name, bad in bad_channels(recs, bundle["channels"]).items():
        print("WARNING {}: {} not touching the scalp - its scores are unreliable".format(name, bad))
    changed = False
    for sp in SPELLS:
        m = bundle["spells"][sp]
        t, used = score(bundle, recs, sp)
        if t is None or t.empty:
            print("{:<16} no labelled trials - threshold stays {:.3f}".format(sp, m["threshold"]))
            continue
        trained = sorted(set(r.name for r in used) & set(m.get("sessions", [])))
        old = float(m["threshold"])
        c_old, f_old = rates(t, old)
        print("{:<16} {} trials{}; AUC {:.2f}; at threshold {:.3f}: caught {}, false {}".format(
            sp, len(t), " (some were TRAINING data: {})".format(trained) if trained else "", auc(t),
            old, c_old, f_old))
        neg = t.score[t.y == 0]
        if len(neg) < a.min_trials:
            pos = t.score[t.y == 1]
            hint = ("" if pos.empty else "; {:.0f} % of casts would pass at {:.3f}".format(
                100 * (1 - a.false_rate), float(np.quantile(pos, a.false_rate))))
            print("{:<16} only {} gesture-only trials (need {}) - threshold unchanged{}".format(
                "", len(neg), a.min_trials, hint))
            continue
        new = threshold_for(neg, a.false_rate)
        c_new, f_new = rates(t, new)
        print("{:<16} -> new threshold {:.3f}: caught {}, false {}".format("", new, c_new, f_new))
        if t.y.nunique() == 2 and auc(t) < 0.65:
            print("{:<16} AUC < 0.65: the model barely separates in-game trials - refit with them:"
                  " python -m eeg.train fit".format(""))
        m.update(threshold=new, threshold_before_calibration=m.get("threshold_before_calibration", old),
                 calibrated_on=[r.name for r in used], calibrated_at=datetime.now().isoformat(timespec="seconds"))
        changed = True
    if not changed:
        print("\nnothing changed - not saved")
        return 1
    bundle.update(meta(a, keep=bundle))
    save(bundle, a)
    return 0


def cmd_evaluate(a):
    bundle = load_bundle(a.model)
    recs = D.recordings(a.recordings)
    print("model {} - channels {}\n".format(a.model, bundle["channels"]))
    for name, bad in bad_channels(recs, bundle["channels"]).items():
        print("WARNING {}: {} not touching the scalp".format(name, bad))
    for sp in SPELLS:
        m = bundle["spells"][sp]
        t, used = score(bundle, recs, sp)
        if t is None or t.empty:
            continue
        thr = float(m["threshold"])
        print("{} ({}), threshold {:.3f}".format(sp, ABILITY[sp], thr))
        for name, tr in t.groupby("recording", sort=False):
            c, f = rates(tr, thr)
            print("  {:<34} AUC {:>5}  caught {:>6}  false {:>6}{}".format(
                name, "{:.2f}".format(auc(tr)) if tr.y.nunique() == 2 else "-", c, f,
                "  (trained)" if name in m.get("sessions", []) else ""))
        c, f = rates(t, thr)
        print("  {:<34} AUC {:>5}  caught {:>6}  false {:>6}\n".format(
            "ALL", "{:.2f}".format(auc(t)) if t.y.nunique() == 2 else "-", c, f))
    return 0


# ------------------------------------------------------------------ output
def meta(a, keep=None):
    import sklearn
    m = dict(sklearn=sklearn.__version__, created=datetime.now().isoformat(timespec="seconds"))
    if keep is None:
        m.update(false_rate=a.false_rate, C=a.C, game_weight=a.game_weight)
    return m


def save(bundle, a):
    out = Path(DEFAULT_MODEL_PATH if a.install else a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    if a.install and out.exists():
        bak = MODELS_DIR / "backup" / "live_models_{:%Y%m%d_%H%M%S}.joblib".format(datetime.now())
        bak.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(out, bak)
        print("previous model backed up to {}".format(bak))
    joblib.dump(bundle, str(out))
    print("saved {}".format(out))
    if not a.install:
        print("try it:   python integrations/gesture_pipeline.py --eeg_model {}".format(out))
        print("use it:   rerun with --install (or copy it over {})".format(DEFAULT_MODEL_PATH))


def get_args(argv=None):
    ap = argparse.ArgumentParser(prog="python -m eeg.train", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p, model=True, out=True):
        p.add_argument("recordings", nargs="*", help="files / folders / globs (default: eeg/data)")
        if model:
            p.add_argument("--model", default=str(DEFAULT_MODEL_PATH), help="model bundle to start from")
        if out:
            p.add_argument("--out", default=str(DEFAULT_OUT))
            p.add_argument("--install", action="store_true",
                           help="write eeg/models/live_models.joblib (old one backed up)")
            p.add_argument("--false_rate", type=float, default=0.10,
                           help="target false activations on gesture-only trials (default 0.10)")
        return p

    common(sub.add_parser("list", help="recordings, bad channels, trials"), model=False, out=False)
    f = common(sub.add_parser("fit", help="retrain on all data"), model=False)
    f.add_argument("--spells", nargs="+", choices=SPELLS, help="refit only these (others kept from --base)")
    f.add_argument("--base", default=str(DEFAULT_MODEL_PATH), help="with --spells: bundle the rest comes from")
    f.add_argument("--channels", nargs="+", choices=CH, help="force these channels")
    f.add_argument("--game_weight", type=float, default=2.0, help="weight of in-game trials (default 2)")
    f.add_argument("--threshold_from", choices=("auto", "game", "all"), default="auto")
    f.add_argument("--C", type=float, default=0.1, help="logistic regression C (default 0.1)")
    c = common(sub.add_parser("calibrate", help="new thresholds from in-game data, same models"))
    c.add_argument("--min_trials", type=int, default=3, help="gesture-only trials needed per spell")
    common(sub.add_parser("evaluate", help="AUC / caught / false per recording"), out=False)
    return ap.parse_args(argv)


def main(argv=None):
    warnings.filterwarnings("ignore", category=UserWarning, module="sklearn")
    a = get_args(argv)
    return {"list": cmd_list, "fit": cmd_fit, "calibrate": cmd_calibrate, "evaluate": cmd_evaluate}[a.cmd](a)


if __name__ == "__main__":
    raise SystemExit(main())
