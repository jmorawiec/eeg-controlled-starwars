# Echoes of the Force — demo v12

A Heroes of the Brain demo with Light and Dark paths, a third-person camera, a mentor tutorial, two enemy waves and a boss. Supports keyboard and mouse and PlayStation controller layouts. EEG and gesture input are planned for a later stage.

## Clone and restore dependencies

This public repository contains the Unity source project. Large redistributable assets use **Git LFS**:

```sh
git lfs install
git clone https://github.com/jmorawiec/eeg-controlled-starwars.git
cd eeg-controlled-starwars
git lfs pull
```

**Before opening a fresh clone, restore the four Asset Store packages described in [Local dependencies](Documentation/PRIVATE-ASSETS.md).** Their source files remain local and are intentionally excluded from this public repository. The project will not compile or reproduce the complete demo until they are restored. Cache folders and built applications are also excluded.

[Opening preview](Documentation/opening-v12.mp4) · [Verification report](Documentation/verification-v12.txt)

## Run

If you have the separately distributed macOS build, extract `Echoes-of-the-Force-v12-macOS.zip` and open `Echoes of the Force v12.app`. Build archives are not committed here.

Open the source project in **Unity 6000.6.9f1** and load `Assets/Scenes/Temple.unity`. **Input System 1.20.1** is pinned in the package manifest. The environment and actors are created when Play starts. The project uses the Built-in Render Pipeline.

## Opening sequence

Choosing a side starts a 14-second in-engine cinematic: an approach through the valley, the mentor's invitation, the chosen hero's blade reveal, and a transition into the shoulder camera. Auren and Veyr have different English dialogue subtitles. An original ambient musical bed accompanies the sequence; dialogue is subtitled, not voiced.

Skip with **Enter, Space or Escape**, or **×, ○ or Options** on a PlayStation controller. A short input grace period prevents the selection press from immediately skipping the intro. Gameplay input, enemies, resources and the run timer remain frozen until the tutorial begins. The sequence pauses when the application loses focus.

All player-facing interface text, tutorial instructions, notifications and the weapon name are in English. The weapon is now called **Glowing Stick**.

## Interface and environment

The main menu uses an animated, softly blurred scene background and a live character portrait. Side selection fills the entire screen with blue and red halves; the inactive half is dimmed. A confirmation button and brighter text identify the active side. Barlow is used throughout. The HUD contains three resource bars, two powers and short tutorial prompts, without decorative headings or slogans.

The Light path uses the owned **Adventure Female** character; the Dark path uses hooded **MP_Clovis**. Guards and the boss use Terraformer armor; ranged enemies and the mentor use Clovis. Clothing materials are separate from weapon materials.

The temple valley includes a stone colonnade, garden and gallery paths, low cover, statues, aqueduct ruins and distant waterfalls. Enemies check cover collision during movement and knockback. Vegetation instances use individual model variants rather than entire source packs. Stone materials combine scanned albedo, normals and AO/roughness with world-space moss. Lighting uses warm fire against cool stone, restrained bloom and a character fill light.

## Combat and animation

Sword cuts and recoveries come from **Quaternius Universal Animation Library 2 Standard**. Ready, cast, hit and death animations come from **Universal Animation Library Standard**. The retargeted humanoid clips are saved in `Assets/Resources/Generated/Cinematic/`.

Three attacks take **0.76 / 0.80 / 1.02 seconds**, with impact at **0.29 / 0.31 / 0.45 seconds**. The next attack can be buffered. Whole-body attacks have a restrained forward assist; guard and channeling can layer over directional locomotion. Hits trigger recoil, brief hit stop and a limited blade trail.

Basic attacks, block and roll remain available at zero Force energy. An attack costs 18 stamina and a roll costs 25. Melee reach is 2.15 m to a normal enemy's center and 2.35 m for the boss. A roll takes 0.78 seconds and covers about 2.8 m. A timely parry interrupts an enemy; defeated enemies play a fall animation.

Eight-direction walking and running use **Human Basic Motions FREE**. The character controller owns world movement; animation root trajectories must not move the skeleton away from its capsule.

## Controls

| Action | Keyboard and mouse | PlayStation |
| --- | --- | --- |
| Move | WASD | Left stick |
| Camera | Mouse movement | Right stick |
| Basic attack | LMB or J | R1 |
| Hold block | RMB or K | L1 |
| Roll | Space | ○ |
| First power | Q | R2 |
| Second power | E | L2 |
| Toggle lock-on | Tab or middle mouse | △ |
| Sprint | Hold Left Shift | Hold L3 |
| Pause | Escape | Options |
| Navigate menu | ↑ / ↓ or mouse | D-pad / left stick |
| Confirm | Enter or click | × |
| Back | Escape | ○ |
| Adjust settings | ← / → or − / + | D-pad ← / → |
| Skip opening | Enter / Space / Escape | × / ○ / Options |

**Enter / ×** advances after training and starts the boss encounter. At the end, it returns to the menu. Menu shortcuts **1 / 2** choose Light / Dark respectively. The camera captures the cursor during gameplay; pausing releases it. Volume, camera sensitivity and bloom are adjustable in Settings.

## Force powers

Light: **Force Push** — Q / R2; **Force Heal** — E / L2. Dark: hold Q / R2 for **Force Lightning**, or E / L2 for **Force Choke**. Directed powers have a 9 m range.

## Build and verification

`DemoBuild.BuildMac` regenerates assets, the scene and the macOS application. `DemoBuild.BuildPreparedMac` rebuilds the application from existing prepared assets.

`CinematicBuild.BuildAndRelease` refreshes the heroine, combat clips and materials before building. Prepared clips preserve stable identifiers. `CinematicAudit` checks hand movement and foot contact; `MotionAudit` checks that running does not move the model away from its capsule.

Opt-in application checks: `--verify-demo`, `--verify-motion`, `--verify-opening`, `--verify-performance`, `--ui-review` and `--art-review`. Direct reports and captures with `--qa-output=/absolute/path`. The opening verification also records frames and timestamps for review. Normal launches do not run checks or reposition targets for verification. Controller mapping checks use synthetic input and do not constitute physical controller testing.

See `ASSET-CREDITS.md` for asset sources and licenses and `ART-DIRECTION.md` for visual direction.
