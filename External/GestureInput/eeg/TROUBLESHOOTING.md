# EEG troubleshooting

Two tools, both safe to run any time:

```bash
python -m eeg.tools.doctor              # packages, model, data   (+ --scan, --headset, --sim)
python -m eeg.tools.check_signal        # electrodes: 15 s test with eyes open / closed
python -m eeg.tools.check_signal --watch   # live table every 2 s while you fix electrodes
```

## By symptom

| Symptom | Check | Fix |
|---|---|---|
| `EEG: NO DATA`, pipeline hangs at "Łączenie z EEG" | `doctor --scan` then `doctor --headset` | Headset on and charged? It stops advertising while connected to another computer or app (close other gpype / g.tec apps, power-cycle). Wrong serial → `--eeg_serial U8-...`. A `UN-...` device is a Unicorn and needs `gp.HybridBlack`, not this code. |
| `Bluetooth service (BlueZ) not running` (Linux) | `doctor --scan` | `systemctl start bluetooth` / `rc-service bluetooth start`, adapter on (`bluetoothctl power on`, `rfkill unblock bluetooth`) |
| `OPEN_DEVICE_FAILED` | `doctor --headset` | Wrong device class / serial, or the headset is held by another host |
| Sample rate far from 250 Hz | `doctor --headset` | BLE drops: move closer, away from Wi-Fi routers / USB 3 hubs |
| `EEG …: Cz not touching` in the preview, abilities blocked | `check_signal --watch` | Gel the electrode, push the pin through the hair until `contact` > 0.3 and the row turns green |
| `flat` | `check_signal` | Electrode not plugged in / broken lead |
| `very noisy`, `spikes` | `check_signal --watch` | Sit still, relax jaw and neck, check the cable isn't pulling |
| `50 Hz hum stronger than EEG` | `check_signal` | Bad contact (more gel); move away from chargers and mains cables |
| `Brain signal present: no` | `check_signal` | Back electrodes (Pz, PO7, POz, PO8) not on skin. Real EEG shows more alpha with eyes closed. |
| Model channel bad in the preview, the rest fine | `check_signal` (`*` = model channel) | Fix that electrode, or retrain without it: `python -m eeg.train fit --channels ...` |
| Abilities fire without imagination / never fire | `python -m eeg.train evaluate eeg/data/game` | Calibrate: `--calibrate 4`, then `python -m eeg.train calibrate --install`. Quick test: `--eeg_threshold_offset +0.05` (stricter) / `-0.05` |
| `cannot load` / `InconsistentVersionWarning` for the model | `doctor` | `pip install scikit-learn==1.9.1` (pinned in `requirements-eeg.txt`), or retrain: `python -m eeg.train fit --install` |
| Pipeline: `utils.draw_prediction_panel not found` | – | The prediction panel module isn't in the repo yet; the old overlay is used instead |

## What the numbers mean (`check_signal`)

| column | good | |
|---|---|---|
| `level uV` | 0.5 – 30 | RMS of 1–30 Hz EEG |
| `peak` | < 100 | windows above 100 µV are skipped by the models |
| `hum` | < 0.3 | 50 Hz power / EEG power, from the unfiltered signal |
| `contact` | ≥ 0.3 | strongest correlation with another channel; an open electrode moves with nothing |
| `alpha c/o` | ≥ 1.5 at the back | eyes closed / eyes open 8–13 Hz power |

Exit codes: 0 = all good, 1 = usable, 2 = fix electrodes, 3 = no data.
`check_signal --file <recording.csv.gz>` checks the last 10 s of a recording.
For example, it finds the dead C3 in `eeg/data/spells_20261115_020754.csv.gz`.
