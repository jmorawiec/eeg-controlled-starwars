# Gesture + EEG integration (v13)

The teammate project is included directly in `External/GestureInput` at revision `57f56f9dcbaca4f6e177c91bb25299c8be6e2447`. This complete-project branch includes its runtime source, models and the prepared StreamingAssets adapter; no submodule initialization is required. See `External/GestureInput/UPSTREAM.md` for provenance.

The integration keeps the existing movement, camera, attack, block and roll controls. EEG mode owns the four Force powers. The game's interface remains in English.

## Use the demo without hardware

In **Settings**, leave **Input mode → Simulation** selected. Choose **Start training** to practice the existing calibration sequence:

- Four spells, four trials with imagination and four without imagination per spell: **32 balanced trials**, in randomized order.
- Five seconds of rest before each trial. Lower your hands and release the imagined action.
- Perform the requested gesture with or without imagination. Hold for 3–4 seconds, then release. The minimum accepted gesture duration follows the teammate calibration: Push 0.3 s; other spells 1.5 s.
- A missing gesture times out after 20 seconds and is shown as skipped. A wrong gesture or wrong simulated imagination condition does not complete a trial.

Hold **Z / X / C / V** to simulate **Push / Heal / Choke / Lightning**. Hold **Ctrl** simultaneously to simulate imagination. During training, a PlayStation pad can instead hold **R1** for the requested gesture and add **L1** for imagination. Rest means releasing both. The bars display the detected gesture, simulated imagination score, threshold and accepted ability. The avatar channels the selected power as feedback; training freezes combat, enemies, movement and the run timer.

Simulation is deterministic input practice, not a brain signal and not a trained EEG model. Completing it saves only practice completion to the player profile. It does not generate EEG recordings or train a classifier. Normal Q/E and pad trigger shortcuts remain available for ordinary play in Simulation; they are disabled as direct Force shortcuts in EEG mode.

## Real EEG + camera

The real path reuses the teammate project's `GesturePipeline`, `EEGSpellStream`, `SpellGate`, `Calibration`, `SessionRecorder` and `eeg.train.cmd_fit`. It uses the BCI Core-8 headset, its configured serial number and the default camera.

Create a Python **3.11** environment and install the runtime dependencies:

```sh
python3.11 -m venv .venv
.venv/bin/python -m pip install -r tools/NeuroBridge/requirements-eeg.txt
```

On Windows use `.venv/Scripts/python.exe`. The source model was saved with **scikit-learn 1.9.1**; use that pinned version for the hardware runtime. The Unity built-in simulator does not need Python or these packages.

In Settings:

1. Switch **Input mode → EEG + gestures**.
2. Select or create a separate profile for the person wearing the headset.
3. Set **Python interpreter** to the environment's absolute Python executable path, and verify **Headset serial**.
4. Choose **Connect EEG input**. The application starts its own adapter process. Allow camera/Bluetooth access for that interpreter when the operating system requests it.
5. Open **Start training**. Wait for fresh EEG data and correct contact, then choose **Begin session** (Enter / × also works).

The EEG screen displays the camera gesture, each spell's EEG probability and threshold, and electrode contact. The cues are the upstream protocol from `eeg/recorder.py` and `eeg/TRAINING.md`: **gesture + imagination**, **the same gesture without imagination**, and **rest with neither**. It does not invent unsupported extra training classes. The original imaginary triggers come from `eeg/tools/record_spells.py`: lightning push, focused healing without extra tension, choke squeeze and push with push. The written hand instructions describe the spell intent; they are not new gesture-classifier training data.

The shipped upstream model is from another player. It is used initially to acquire/check signals and display probabilities, but it cannot enable gameplay powers for a fresh profile. After a complete session, the adapter fits that person's recordings only and atomically saves `model.joblib`. Each spell must have both classes and a held-out AUC of at least 0.65; otherwise the candidate is rejected and the previous model is retained. AUC is a small-session validation measure, not a guarantee of performance on future sessions.

Fitting runs in a worker thread while telemetry continues. A successful model is loaded without restarting the game. Repeating training adds data for the same person; create a new profile for a different person. Trials use the actual camera gesture's onset and end, as in the teammate implementation. Incomplete/contact-lost sessions go into a separate `incomplete/` folder and are not silently included in later fitting.

## Data and lifecycle

Private recordings, fitted models, reports and the process log are stored under Unity's `Application.persistentDataPath/InputProfiles/`, separated by generated profile IDs. Profile names/selection are in `profiles.json`. No participant recordings are copied to Git or into the application bundle. `NeuroBridgeBuild.Stage` includes the upstream code, model files needed at runtime, notices and protocol documentation; it excludes the upstream recording dataset.

Leaving training saves partial EEG separately and returns to Settings. Losing focus pauses the current trial; Resume restarts it from rest, without labelling an interrupted gesture. In gameplay, stale telemetry (1.5 s), disconnect, bad contact, a menu, or a side change releases held input. EEG mode accepts only confirmed `ability_*` events from the current profile and a ready personal model; camera-only fallback and raw gesture events cannot cast powers.

Push fires once per accepted event. Lightning and Choke channel from started to ended events. Heal remains subject to the game's energy/cooldown rules and can repeat while its accepted held gesture remains active. A side can use only its own two powers. Basic attacks, blocks and rolls continue to work from keyboard/mouse or pad.

## Adapter protocol

Unity listens on loopback TCP **127.0.0.1:5555**, one UTF-8 JSON object per newline. The upstream gate's ability event fields are preserved, with `profile` added by the adapter. In addition, `telemetry` arrives at about 10 Hz with gesture, four scores, contacts and training state. Commands travel back over the same connection: `configure`, `start_training`, `pause_training`, `resume_training`, `cancel_training`, `shutdown`. Input queues and line lengths are bounded. Network threads do not access Unity objects.

The adapter can also be launched explicitly:

```sh
.venv/bin/python tools/NeuroBridge/unity_bridge.py \
  --upstream External/GestureInput \
  --data-root /absolute/path/to/InputProfiles \
  --profile 0123456789abcdef0123456789abcdef \
  --serial U8-2025.08.21
```

`--source sim` is a separate Python transport fixture, not the normal Unity simulator. It reuses the upstream gate/recorder/calibration, marks every recording `sim_`, and never fits a real model. It is intended for protocol checks.

## Build and verification

`DemoBuild.BuildPreparedMac` stages the Python adapter automatically. In the Editor, use **Force Demo → Stage EEG bridge** to regenerate the adapter after editing its source. The prepared StreamingAssets payload is included in this branch.

```sh
python3 tools/NeuroBridge/tests/test_adapter.py
cd External/GestureInput
python -m eeg.tests.test_spell_gate
python -m eeg.tests.test_training
```

The standalone app supports `--verify-neuro --neuro-port=5567 --qa-output=/absolute/path` for isolated synthetic integration checks. Real camera recognition, electrode contact, Bluetooth and headset operation require physical device validation. The integration's automated EEG tests use synthetic inputs and temporary recordings; they do not establish headset accuracy.
