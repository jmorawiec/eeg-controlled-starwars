# hand-gesture-recognition-mediapipe-for-game-input

Two-hand gesture recognition from a webcam (MediaPipe + a simple MLP classifier) that streams events about performed gestures to the **Unity** engine over **TCP**. This lets camera gestures drive a game like any other input.

This project is a fork of [hand-gesture-recognition-mediapipe](https://github.com/Kazuhito00/hand-gesture-recognition-using-mediapipe) (Kazuhito Takahashi; English translation and improvements by [Nikita Kiselov](https://github.com/kinivi)), adapted for game input.

## What changed compared to the original

- **Two-hand recognition** – the classifier receives features from both hands at once (2 hands × 21 landmarks × (x, y) = 84 features) instead of a single hand. Hands are always put in a fixed order (left before right), so the feature vector is deterministic.
- **Custom gestures** – instead of the default open / close / pointing, the model recognizes game gestures: `Push`, `Heal`, `Choke`, `Lightning`, plus an `inactive` class.
- **Filtering out empty movements** – a gesture is treated as active only when:
  1. both hands are visible,
  2. the vertical difference between the hands reaches `--height_diff_threshold` (default 30 px, `0` disables the check),
  3. the classifier returns a class other than `inactive` with confidence of at least `--min_gesture_confidence`.
- **Unity integration over TCP** (`integrations/` folder) – the pipeline turns classification results into `gesture_started` and `gesture_ended` events and sends them as JSON.
- **Running from a notebook** (`integrations/run_pipeline.ipynb`) and a local test server (`integrations/test_server.py`).
- **MediaPipe Tasks hand model** (`hand_landmarker.task`) instead of the legacy `mp.solutions` API.
- **EEG gating** (`eeg/` folder) – a g.tec BCI Core-8 headset checks that the player also *imagines* the spell. An ability fires only for gesture **+** imagination, and Unity receives only ability events: `ability_triggered` (Push) and `ability_started` / `ability_ended` (Heal, Choke, Lightning – they last while the gesture is held). Camera and EEG flicker are filtered out. Details: [`eeg/README.md`](eeg/README.md). Retraining / in-game calibration of the EEG models: [`eeg/TRAINING.md`](eeg/TRAINING.md); headset and electrode troubleshooting: [`eeg/TROUBLESHOOTING.md`](eeg/TROUBLESHOOTING.md).

## Requirements

- Python 3.10+ (developed on Python 3.14)
- A webcam
- Dependencies from `requirements.txt` (including `mediapipe`, `opencv-python`, `numpy`, `tensorflow` / `tflite`)

## Installation

```bash
git clone <repository-url>
cd hand-gesture-recognition-mediapipe-for-game-input

python -m venv .venv
# Linux / macOS
source .venv/bin/activate
# Windows
.venv\Scripts\activate

pip install -r requirements.txt
pip install -r eeg/requirements-eeg.txt   # EEG headset (skip if you only use --eeg off)
```

Without full TensorFlow the `.tflite` classifier runs on the lightweight `ai-edge-litert` runtime (picked automatically).
## Usage

### Gesture preview without Unity

```bash
python app.py
```

Options:

| Option | Default | Description |
|---|---|---|
| `--device` | `0` | camera index |
| `--width`, `--height` | `960`, `540` | capture resolution |
| `--num_hands` | `2` | number of hands to detect |
| `--model_path` | `model/hand_landmarker.task` | hand detection model |
| `--classifier_path` | `model/keypoint_classifier/keypoint_classifier.tflite` | classifier (`.tflite` or `.keras`) |
| `--labels_path` | `model/keypoint_classifier/keypoint_classifier_label.csv` | gesture labels |
| `--min_detection_confidence` | `0.7` | hand detection threshold |
| `--min_tracking_confidence` | `0.5` | hand tracking threshold |
| `--height_diff_threshold` | `30` | minimum vertical difference (px) between the hands; smaller means `inactive`, `0` disables the check |
| `--no_classification` | – | hand detection and sample recording only, no classifier |

### Sending gestures to Unity

1. Start a TCP server in Unity listening on the chosen port (default `127.0.0.1:5555`).
2. Run the pipeline:

```bash
python integrations/gesture_pipeline.py --host 127.0.0.1 --port 5555
```

Relative paths (e.g. `--model_path model/hand_landmarker.task`) are resolved against the current directory first, and if the file is not found there, against the project root. The script therefore works regardless of where it is launched from.

Additional pipeline options:

| Option | Default | Description |
|---|---|---|
| `--host`, `--port` | `127.0.0.1`, `5555` | address of the TCP server in Unity |
| `--reconnect_delay` | `2.0` | delay (s) before retrying the connection |
| `--min_gesture_confidence` | `0.0` | minimum confidence required to send a gesture |
| `--no_display` | – | no OpenCV preview window |
| `--no_flip` | – | do not mirror the camera image |
| `--max_frames` | `0` | stop after N frames (`0` = unlimited) |
| `--dry_run` | – | do not connect to Unity, print events to the console |
| `--verbose` | – | extra logging |
| `--eeg` | `headset` | `headset` (BCI Core-8), `sim` (random signal, plumbing test), `off` (camera only) |
| `--eeg_fallback` | `block` | when the EEG has no data / an electrode is off: `block` abilities or use the camera only (`cv`) |
| `--eeg_min_windows`, `--eeg_min_windows_push` | `6`, `3` | EEG evidence needed before a held ability starts / Push fires (more = fewer false activations, slower) |
| `--eeg_threshold_offset`, `--eeg_release` | `0.0`, `1.5` | stricter thresholds; seconds of lost imagination that end a held ability (`0` = only the gesture ends it) |
| `--cv_stable`, `--cv_grace`, `--cv_rejoin` | `0.15`, `0.35`, `1.0` | camera anti-flicker: stable time to start a gesture, ignored drop-out length, re-joining a fragmented gesture |
| `--decision` | `gate` | `gate` = wait for EEG windows recorded during the gesture; `instant` = when the camera detects a movement, compare the current EEG regression score of that movement with its threshold: above → TCP event, below → ignored |
| `--eeg_check_windows` | `4` | `--decision instant`: EEG windows averaged at the moment of detection |
| `--legacy_events` | – | old `gesture_started` / `gesture_ended` protocol without EEG |
| `--no_panel` | – | hide the prediction panel (CAMERA / EEG / COMBINED) next to the preview |
| `--fusion_cv_weight`, `--fusion_threshold`, `--eeg_display_windows` | `0.5`, `0.5`, `4` | panel only: camera weight in the combined score, minimum combined score for a prediction, EEG windows averaged for the EEG bars |

All EEG options are described in [`eeg/README.md`](eeg/README.md).

The remaining options (`--device`, `--width`, `--height`, `--model_path`, `--classifier_path`, `--labels_path`, `--num_hands`, confidence thresholds) work the same as in `app.py`. Press **ESC** to close the preview window.

### Running from a notebook

Open `integrations/run_pipeline.ipynb`, set the parameters in the first cell (host, port, camera, thresholds, `DRY_RUN` / `NO_DISPLAY` flags, etc.) and run the cells from top to bottom. The notebook detects the project root on its own and calls `integrations/gesture_pipeline.py` with those parameters.

### Testing without Unity

In one terminal, start the test server, which prints the messages it receives:

```bash
python integrations/test_server.py
```

In another, run the pipeline (with a camera) or send a single event:

```bash
python integrations/gesture_pipeline.py
python integrations/send_event.py ability_triggered --ability Push
```

To check recognition alone, without any server, use `--dry_run`. Tests for the EEG part: `python -m eeg.tests.test_spell_gate` and `python -m eeg.tests.smoke_pipeline`.

## TCP protocol

Each event is **one line of JSON terminated with `\n`** (UTF-8). The client connects to the server in Unity and tries to reconnect if the connection drops.

Only ability events are sent – camera gesture **and** EEG imagination together:

```json
{"event": "ability_triggered", "timestamp": 1794710658.909, "ability": "Push", "kind": "instant", "mode": "eeg", "cv_confidence": 0.95, "since_gesture_start": 1.72, "eeg_score": 0.71, "eeg_threshold": 0.56, "eeg_windows": 3}
{"event": "ability_started", "timestamp": 1794710652.249, "ability": "Choke", "kind": "sustained", "mode": "eeg", "cv_confidence": 0.95, "since_gesture_start": 2.41, "eeg_score": 0.66, "eeg_threshold": 0.58, "eeg_windows": 6}
{"event": "ability_ended", "timestamp": 1794710656.416, "ability": "Choke", "kind": "sustained", "mode": "eeg", "cv_confidence": 0.95, "since_gesture_start": 4.34, "reason": "gesture_released", "duration": 1.9}
```

`Push` is `instant` (one `ability_triggered` per gesture). `Heal`, `Choke` and `Lightning` are `sustained`: `ability_started` when the EEG confirms, `ability_ended` when the gesture ends (`reason: gesture_released`), the imagination fades (`imagination_lost`) or the pipeline stops (`shutdown`). Every `ability_started` gets exactly one `ability_ended`. Field reference and a C# example: [`eeg/README.md`](eeg/README.md#tcp-events).

### Legacy protocol (`--legacy_events`)

Camera only, the previous events:

Gesture start:

```json
{"event": "gesture_started", "timestamp": 1731612345.12, "gesture": "Push", "confidence": 0.94, "hand_sign_id": 0}
```

Gesture end:

```json
{"event": "gesture_ended", "timestamp": 1731612346.02, "gesture": "Push", "duration": 0.9, "confidence": 0.94}
```

Events come in pairs: `gesture_started` opens a gesture and `gesture_ended` closes it (also when the gesture changes to a different one, or when the pipeline shuts down). The `inactive` class is never sent. On the Unity side, read events line by line (`ReadLine`) and parse them, e.g. with `JsonUtility`.

## Available gestures

Labels are stored in `model/keypoint_classifier/keypoint_classifier_label.csv` (the order matches the class IDs in the model):

| ID | Gesture |
|---|---|
| 0 | `Push` |
| 1 | `Heal` |
| 2 | `Choke` |
| 3 | `Lightning` |
| 4 | `inactive` |

## Adding custom gestures and training

Training data goes to `model/keypoint_classifier/keypoint.csv`. The first column is the class number and the rest are the 84 normalized coordinates of both hands.

1. Run `python app.py` and press **k** to enable key point logging.
2. Show the gesture with both hands and press the class number key (**0–9**) to append samples.
3. Add the gesture name to `keypoint_classifier_label.csv` (in the same order as the IDs; `inactive` should stay a separate class – it is used to suppress movements that are not gestures).
4. Train the model in the training notebook (set `NUM_CLASSES` to the current number of classes) and save the result as `.tflite` / `.keras` in `model/keypoint_classifier/`.

## Troubleshooting

- **`Nie znaleziono pliku (...)` (file not found)** – check the paths to the model, classifier and labels; the message shows the full path that was searched.
- **`Unity niedostępne` (Unity unavailable)** – no TCP server is listening on the given host and port. The pipeline keeps running and retries the connection when sending gestures. Use `integrations/test_server.py` for testing.
- **Gestures are always `inactive`** – lower `--height_diff_threshold` (or set it to `0`) and make sure both hands are visible in the frame.
- **Cannot open the camera** – change `--device` (e.g. to `1`).
- **`EEG: NO DATA`** – headset off, discharged, or still connected to another program (close other g.tec / gpype apps). Abilities are blocked meanwhile (`--eeg_fallback cv` = camera only).
- **`EEG: Cz not touching`** (any electrode name) – that electrode has no scalp contact: add gel / press it through the hair. If it is one of Fz, Cz, C4, PO7, POz, PO8, abilities are blocked until it is fixed.
- **Gesture shown but the ability never fires** – the preview shows `waiting for EEG` and the score below the white threshold line: the imagination was not detected. `--eeg_threshold_offset -0.05` loosens it; `--eeg off` tests the game without EEG.

## License

This project is released under the **Apache License 2.0**, the same as the original project – see the [LICENSE](LICENSE) file.

## Credits

- Original project: [Kazuhito Takahashi](https://twitter.com/KzhtTkhs) – [hand-gesture-recognition-using-mediapipe](https://github.com/Kazuhito00/hand-gesture-recognition-using-mediapipe)
- English translation and improvements: [Nikita Kiselov](https://github.com/kinivi)
- [MediaPipe](https://mediapipe.dev/)
