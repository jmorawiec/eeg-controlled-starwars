# Echoes of the Force — complete Unity project v13

This is the **`unity/full-project-v13`** branch. It contains the complete playable Unity project: character models, textures, humanoid animations, materials, shaders, environment assets, prefabs, scene, settings, and the prepared gesture/EEG adapter. The teammate input source is included directly at its pinned revision; there is no submodule setup step on this branch.

## Open and play

Install **Git LFS** and **Unity 6000.6.9f1**. Then:

```sh
git lfs install
git clone --branch unity/full-project-v13 --single-branch https://github.com/jmorawiec/eeg-controlled-starwars.git
cd eeg-controlled-starwars
git lfs pull
```

Open the cloned folder in Unity Hub using **6000.6.9f1**. Wait for the first import and Unity Package Manager to restore the pinned packages. The playable **Temple** scene opens automatically in a fresh Editor session. Press **Play**.

The game starts in **Simulation** mode. Keyboard/mouse and PlayStation controls work immediately, including the tutorial, both sides, four Force powers, opening cinematic, two waves and boss. **Settings → Start training** runs the 32-trial input practice without Python or a headset.

If Git LFS is missing, large files will remain small text pointers rather than actual models and textures. Run `git lfs install` and `git lfs pull` before opening the Editor. A normal checkout/pull with LFS installed restores the assets automatically.

## Included project content

- Adventure Female, MP_Clovis, Human Basic Motions FREE and Volumetric Lines source files, with their original `.meta` identifiers.
- Prepared Light/Dark characters, armored enemies, humanoid controllers, Quaternius combat clips, weapon, materials and shaders.
- Poly Haven environment models and textures, Unity Companion License sources and generated scene variants.
- The prepared `Assets/StreamingAssets/NeuroBridge/` payload, including runtime hand models and the baseline EEG model.
- The pinned teammate source at `External/GestureInput` (commit `57f56f9dcbaca4f6e177c91bb25299c8be6e2447`), directly included rather than linked as a submodule.

Unity's cache, temporary files, machine-specific Python environments and participant recordings are not project dependencies and are not versioned. Unity recreates its cache on first import. Third-party assets retain their original license terms; see [asset credits](ASSET-CREDITS.md) and the included license notices.

## Controls

| Action | Keyboard and mouse | PlayStation |
| --- | --- | --- |
| Move / camera | WASD / mouse | Left / right stick |
| Attack / block | LMB or J / hold RMB or K | R1 / hold L1 |
| Roll | Space | ○ |
| Force powers | Q / E | R2 / L2 |
| Lock-on | Tab / middle mouse | △ |
| Sprint / pause | Left Shift / Escape | L3 / Options |
| Confirm / back | Enter / Escape | × / ○ |

Light: **Force Push / Force Heal**. Dark: **Force Lightning / Force Choke**. Simulation gestures: hold **Z / X / C / V**, adding **Ctrl** for imagined intent. In training, pad **R1** performs the requested simulated gesture; **L1** adds imagination.

## Optional real EEG mode

The default simulation is self-contained. Real camera/headset input additionally requires the BCI Core-8, an appropriate Python 3.11 environment and OS device access. See [EEG setup and personal training](Documentation/NEURO-INPUT.md). The complete Unity asset snapshot does not replace device drivers or a person's EEG calibration. Personal recordings and models stay under `Application.persistentDataPath/InputProfiles/`.

## Verification and builds

**Force Demo → Verify complete project** checks the included source assets, three prepared character rigs, materials and animation dependencies. `CompleteProjectBootstrap.VerifyAndBuild` performs that check and builds the macOS application. `DemoBuild.BuildPreparedMac` rebuilds from the prepared assets.

The runtime supports `--verify-demo`, `--verify-neuro` and `--verify-opening`. An isolated port can be selected with `--neuro-port=5567` and reports directed with `--qa-output=/absolute/path`.

[Training screen](Documentation/training-v13.png) · [Original v13 integration checks](Documentation/verification-v13.txt) · [Visual direction](ART-DIRECTION.md)
