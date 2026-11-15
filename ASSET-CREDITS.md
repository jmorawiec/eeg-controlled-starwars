# Asset sources and licenses

## Poly Haven — CC0 1.0

Source: https://polyhaven.com/
License: https://polyhaven.com/license
All listed Poly Haven assets are provided under CC0. Texture resolution was chosen for a real-time demo; plant alpha masks were assembled from the author's diffuse and alpha maps.

- Boulder 01 — https://polyhaven.com/a/boulder_01
- Coastal Cliff 01 — https://polyhaven.com/a/coastal_cliff_01
- Rock Moss Set 01 — https://polyhaven.com/a/rock_moss_set_01
- Fern 02 — https://polyhaven.com/a/fern_02
- Grass Medium 01 — https://polyhaven.com/a/grass_medium_01
- Fir Sapling — https://polyhaven.com/a/fir_sapling
- Mossy Rock — https://polyhaven.com/a/mossy_rock
- Rock Face 03 — https://polyhaven.com/a/rock_face_03
- Forest Floor — https://polyhaven.com/a/forest_floor
- Aerial Rocks 02 — https://polyhaven.com/a/aerial_rocks_02
- Kiara 1 Dawn — https://polyhaven.com/a/kiara_1_dawn
- Fabric Pattern 07 — https://polyhaven.com/a/fabric_pattern_07

- Monastery Stone Floor — https://polyhaven.com/a/monastery_stone_floor
- Mossy Cobblestone — https://polyhaven.com/a/mossy_cobblestone
- Rabdentse Ruins Wall — https://polyhaven.com/a/rabdentse_ruins_wall
- Mossy Stone Wall — https://polyhaven.com/a/mossy_stone_wall
- Forest Ground 06 — https://polyhaven.com/a/forest_ground_06
- Rock Wall 02 — https://polyhaven.com/a/rock_wall_02
- Stony Dirt Path — https://polyhaven.com/a/stony_dirt_path
- Belfast Sunset (Pure Sky) — https://polyhaven.com/a/belfast_sunset_puresky
- Gothic Statue — https://polyhaven.com/a/gothic_statue
- Marble Bust 01 — https://polyhaven.com/a/marble_bust_01
- Wooden Lantern 01 — https://polyhaven.com/a/wooden_lantern_01
- Rock Moss Set 02 — https://polyhaven.com/a/rock_moss_set_02
- Rock Face 01 — https://polyhaven.com/a/rock_face_01
- Dead Tree Trunk 02 — https://polyhaven.com/a/dead_tree_trunk_02
- Tree Stump 01 — https://polyhaven.com/a/tree_stump_01
- Shrub 01 — https://polyhaven.com/a/shrub_01
- Shrub 03 — https://polyhaven.com/a/shrub_03
- Celandine 01 — https://polyhaven.com/a/celandine_01

## Unity FPS Sample — Unity Companion License

Copyright (c) 2018 Unity Technologies ApS.

- Source: https://github.com/Unity-Technologies/FPSSample
- License: https://github.com/Unity-Technologies/FPSSample/blob/master/LICENSE.md
- Unity Companion License: https://unity.com/legal/licenses/unity-companion-license
- Included: Terraformer_A character model, its humanoid mapping, associated character textures, and melee, guard and directional locomotion clips, adapted to Standard materials and a 2K texture budget for this Unity-dependent project.
- Original source license notice is retained in `Assets/Resources/WorldAssets/FPS-Sample-License.md`.

## Unity Standard Assets Characters — Unity Companion License

- Source: https://github.com/Unity-Technologies/Standard-Assets-Characters
- License: https://github.com/Unity-Technologies/Standard-Assets-Characters/blob/master/LICENSE.md
- Included: DefaultMale reference model and textures, male Idle / Walk / Run / landing-roll and strafe source animation files. Locomotion is retargeted to the Terraformer humanoid rig.
- Source license notice is retained in `Assets/Resources/WorldAssets/Unity-Companion-License.md`.

## Fonts — SIL Open Font License 1.1

Barlow by Jeremy Tribby and Rajdhani by Indian Type Foundry.

- https://github.com/google/fonts/tree/main/ofl/barlow
- https://github.com/google/fonts/tree/main/ofl/rajdhani
- Full OFL notices are included alongside the fonts in `Assets/Resources/Fonts`.

## Project content

The level arrangement, procedural ruins, weapon named “Glowing Stick”, Force effects, game logic, UI, shaders and synthesized audio were created for this demo. The visual and gameplay reference is Star Wars Jedi: Fallen Order; no assets extracted from that game are included.

## Unity Asset Store — user's My Assets library (v4)

Downloaded through the signed-in Unity Package Manager on 2026-11-14:

- Human Basic Motions FREE — Kevin Iglesias, version 2.4.2. Selected male in-place humanoid locomotion clips are retargeted to the playable Clovis and the armored characters. Original animation metadata and the included author documentation are retained.
- Sci-Fi Post-Apocalyptic Young Male / MP_Clovis — MPowered Games, version 1.0. Playable hooded character. Source model and textures are retained; a new humanoid prefab and Standard materials repair the package's incomplete prefab references for this project's pipeline.
- Volumetric Lines — Johannes Unterguggenberger, version 3.1.1. Runtime volumetric blade rendering. Author headers and documentation are retained in Assets/VolumetricLines.

These packages retain their Asset Store licensing; they are not covered by the project's CC0 environment credits. The source archive is the user's Unity project, containing the assets acquired in that user's library.

## Quaternius — CC0 (v10)

- Universal Animation Library Standard: https://quaternius.itch.io/universal-animation-library
- Universal Animation Library 2 Standard: https://quaternius.itch.io/universal-animation-library-2
- Downloaded from the author's free Standard files on 2026-11-14. Only these free files are included; the paid Source/Pro content was not acquired.
- Native humanoid clips are retargeted and combined into the prepared combat motions. The author's CC0 notice is retained in Assets/AnimationLibrary/License.txt.

## Additional Asset Store character (v10)

Adventure Female (Free), Maksim Bugrimov, version 1.0, downloaded through the user's signed-in My Assets library. Selected body model, one colour variant and supporting textures are included in Assets/Adventures_woman. The prepared humanoid and Built-in materials are in Resources/Generated/LightHero.

Additional AO/roughness/metalness maps for Rabdentse Ruins Wall, Monastery Stone Floor and Mossy Cobblestone came from the same Poly Haven CC0 assets credited above.

## Public repository packaging

Asset Store package source folders and the copied BladeVolumetric material are excluded from the public Git repository. The local complete project and private source archive retain them. See `Documentation/PRIVATE-ASSETS.md` for required packages and restoration. Project-authored wrappers reference the locally imported packages; screenshots and the gameplay preview show the assembled demo.

## Teammate gesture and EEG pipeline (v13)

KrzysztofEmerling/hand-gesture-recognition-mediapipe-for-game-input, commit 57f56f9dcbaca4f6e177c91bb25299c8be6e2447, Apache License 2.0 (notice retained in the submodule and staged runtime payload). The Unity adapter uses its gate, recorder, calibration and training code; the built-in simulator adapts its balanced training protocol. See Documentation/NEURO-INPUT.md. No new participant recordings are published.
