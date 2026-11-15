# Training, updating and calibrating the EEG spell models

The live models (`eeg/models/live_models.joblib`) are one **logistic regression
per spell** that answers: *is the player imagining the spell while doing the
gesture?* (gesture + imagination vs. gesture only). This folder has everything
to retrain them on all data, add data recorded **in the game**, and
re-calibrate the thresholds when the signal characteristics change.

```
record ──────────────────────────────┐
  cued, no camera:  eeg/tools/record_spells.py      -> eeg/data/spells_*.csv.gz
  in the game:      gesture_pipeline.py --calibrate -> eeg/data/game/game_*.csv.gz
                    gesture_pipeline.py --eeg_record
                                     │
train / update ──────────────────────┤
  python -m eeg.train list           what data there is, bad channels, trials
  python -m eeg.train fit            retrain on ALL data (cued + in-game)
  python -m eeg.train calibrate      same models, thresholds from in-game data
  python -m eeg.train evaluate       how a model does on some recordings
                                     │
use ─────────────────────────────────┘
  python integrations/gesture_pipeline.py --eeg_model eeg/models/live_models_new.joblib
  ... --install                      makes it the default (old one -> eeg/models/backup/)
```

Every command writes `eeg/models/live_models_new.joblib` unless you pass
`--install`, so you can try a new model before replacing the working one.

## Which one do I need?

| Situation | Do |
|---|---|
| New day / headset put on differently, abilities fire too often or too rarely | **Calibrate** (≈ 5 min): `--calibrate 4` in the game, then `python -m eeg.train calibrate --install` |
| The game feels different from the recordings (posture, camera, attention, sounds) and calibration is not enough | Record in-game sessions, then **fit**: `python -m eeg.train fit --install` |
| New spell gesture / new imaginary trigger | Record new data for it (cued or `--calibrate --calibrate_abilities X`), then `fit --spells <spell> --install` |
| Another player | Their own data: `record_spells` + `--calibrate`, set the old sessions to `"use": false` in `eeg/data/manifest.json`, `fit` |

Always run `python -m eeg.tools.check_signal` first. A model can't fix an
electrode that isn't touching the scalp.

## 1. Record in the game

### Calibration session (labelled, recommended)

```bash
python integrations/gesture_pipeline.py --calibrate 4            # 4 x with + 4 x without per ability
python integrations/gesture_pipeline.py --calibrate 5 --calibrate_abilities Choke Push
python integrations/gesture_pipeline.py --calibrate 4 --dry_run  # without Unity
```

The preview window (and the console) prompts each trial in random order:

```
CALIBRATION 3/32  relax 4 s
next: Choke WITH imagination
CALIBRATION 3/32  NOW: Choke gesture only - NO imagination
(hold 3-4 s, then release)
```

Do the gesture when it says `NOW`, with or without the imaginary trigger as
asked, and release. The trial is the **camera gesture** (camera onset → end),
so the EEG windows are aligned with the real movement, not with a cue you
react to late. If the camera recognises another gesture, or the gesture is
too short, the prompt stays; after 20 s without the gesture it is skipped.
The game keeps running (events still go to Unity), so this is real in-game
data. ESC saves `eeg/data/game/game_<date>_<time>.csv.gz` (+ `_events.jsonl`).

### Free play

```bash
python integrations/gesture_pipeline.py --eeg_record
python integrations/gesture_pipeline.py --eeg_record --record_label imagination
```

Everything is recorded. Label gestures with keys in the **preview window**
(it needs focus): `y` = the last gesture was *with* imagination, `n` = gesture
only. Unlabelled gestures are saved as `<spell>_gesture_unlabelled` and are
ignored by training. With `--record_label imagination` every unlabelled gesture
counts as imagined, which is useful for a session where you really cast every
spell. You still need gesture-only trials from elsewhere (calibration / cued)
for the model to learn the contrast.

`--eeg sim` recordings are saved as `sim_game_*` and never used for training.

## 2. Record cued sessions (no camera)

```bash
python -m eeg.tools.record_spells                          # 4 spells x 5 repeats, ~11 min
python -m eeg.tools.record_spells --spells push_with_push --repeats 10
```

This is the protocol the first sessions used: relax 10 s, 5 s gesture with
imagination, relax, 5 s gesture without (random order per repeat). Windows
1–5 s after the cue are used (the first second is reaction time).

## 3. Train / update

```bash
python -m eeg.train list
python -m eeg.train fit                    # all used recordings in eeg/data + eeg/data/game
python -m eeg.train fit --game_weight 3    # in-game trials count 3x
python -m eeg.train fit --spells choke_squeeze           # only Choke, others kept from the live model
python -m eeg.train fit --channels Fz Cz C4 PO7 POz PO8  # force channels
python -m eeg.train evaluate eeg/data/game               # current model on all in-game data
python -m eeg.train fit --install                        # use it
```

What `fit` does, per spell:

1. **Data:** every recording in `eeg/data/**` that has trials of that spell
   and is not switched off in `eeg/data/manifest.json`.
2. **Channels:** those touching the scalp in *every* used recording (contact
   = max |r| with another channel ≥ 0.3). It prints which recording drops a
   channel. If one bad in-game recording costs you a channel, mark it
   `"use": false` or pass `--channels`.
3. **Features:** causal 8–30 Hz, 1 s windows every 0.25 s, log-covariance of
   the model channels. These are identical to live (`eeg/features.py`). Windows
   with > 100 µV are skipped.
4. **Model:** `StandardScaler` → `LogisticRegression(C=0.1)`. In-game windows
   get `--game_weight` (default 2).
5. **Held-out check:** leave one repeat (cued) / one gesture (in-game) out;
   a trial's score is the mean P over its windows, as in the gate. It prints AUC
   for all trials and for in-game trials.
6. **Threshold:** just above the 90th percentile of held-out gesture-only
   scores (`--false_rate 0.10` → ~10 % false activations). It comes from in-game
   trials when there are ≥ 5 gesture-only in-game trials, otherwise from all
   (`--threshold_from` to force it).

With the 4 gel sessions shipped here, `fit` reproduces the current
`live_models.joblib` exactly (same coefficients and thresholds).

### Calibrate (keep models, move thresholds)

```bash
python -m eeg.train calibrate                       # all of eeg/data/game
python -m eeg.train calibrate eeg/data/game/game_20261116_101500.csv.gz --install
```

It scores the labelled in-game trials with the existing models and sets each
threshold to `--false_rate` on their gesture-only trials (needs ≥ 3 per spell,
`--min_trials`). It prints AUC and caught / false before and after. If the AUC
on the new data is < 0.65, a threshold can't fix it: refit with the in-game
data (`fit`). The bundle remembers `threshold_before_calibration`,
`calibrated_on` and `calibrated_at`.

## Data

`eeg/data/` holds the training recordings, gzipped CSV, one row per sample
(250 Hz):

| column | |
|---|---|
| `time_s` | seconds from the start of the recording |
| `Fz … PO8` | EEG in µV, OSCAR + 1–30 Hz + 48–52 Hz band-stop (exactly what the live models get) |
| `cue` | `<spell>_gesture_imagination`, `<spell>_gesture_only`, `<spell>_gesture_unlabelled`, `relax` |
| `trial` | cued: repeat number (imagination and gesture-only of one repeat are held out together); in-game: gesture number |
| `gesture` | in-game only: camera label at that sample |

Spells: `lightning_push` (Lightning), `heal` (Heal), `choke_squeeze` (Choke),
`push_with_push` (Push).

| recording | | notes |
|---|---|---|
| `spells_20261114_214421` | dry electrodes | noisy, `use: false` (made every model worse) |
| `spells_20261114_232939` | gel | Pz flat |
| `spells_20261115_020754` | gel | C3 not touching the whole session |
| `spells_20261115_024652` | gel | all channels good |
| `spells_push_20261115_030348` | gel | push only, 10 + 10 trials |

All from one player. Earlier findings: lightning / heal / choke separate well
(held-out AUC 0.83–0.87); push is the weakest across sessions. Imagination
shows mostly as 8–30 Hz changes over posterior channels.

`eeg/data/manifest.json`: per-recording `use`, `skip_spells`, `kind`
(`cued` / `game`, otherwise taken from the name), `note`. Files not listed are
used.
