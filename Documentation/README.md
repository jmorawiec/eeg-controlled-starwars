# Project documentation

Start with the [project overview and quick start](../README.md).

## Setup and implementation

- [Camera + EEG setup and personal training](NEURO-INPUT.md)
- [Gesture recognition source and usage](../External/GestureInput/README.md)
- [EEG processing](../External/GestureInput/eeg/README.md)
- [Recorded EEG confusion matrices](../External/GestureInput/eeg/RESULTS.md)
- [EEG training](../External/GestureInput/eeg/TRAINING.md)
- [EEG troubleshooting](../External/GestureInput/eeg/TROUBLESHOOTING.md)

## Game and assets

- [Visual direction](ART-DIRECTION.md)
- [Asset sources and licenses](ASSET-CREDITS.md)
- [Included third-party packages](PRIVATE-ASSETS.md)

## Demo media

- [Main menu](media/menu-v12.png)
- [Opening cinematic](media/opening-v12.mp4)
- [Settings](media/settings-v13.png)
- [Training screen](media/training-v13.png)

## Recorded verification

These are historical reports, not new hardware validation.

- [Complete project verification](COMPLETE-PROJECT-VERIFICATION.md)

To check the Unity project locally, use **Force Demo → Verify complete project** in the editor. `CompleteProjectBootstrap.VerifyAndBuild` verifies dependencies and builds the macOS application; `DemoBuild.BuildPreparedMac` rebuilds the prepared assets.
