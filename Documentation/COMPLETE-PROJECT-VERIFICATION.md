# Complete project verification

Branch: `unity/full-project-v13`, Unity **6000.6.9f1**.

- All 866 asset files from the original complete local project are present. Source contents match, with standard Git text line-ending normalization.
- A copy without a pre-existing Unity Library imported successfully and produced a macOS build. LightHero, Clovis and Explorer meshes, avatars, materials and animation controllers resolved; motion and combat audits passed.
- An interactive Editor session opened `Assets/Scenes/Temple.unity` automatically.
- A separate shallow clone of this branch from GitHub passed `CompleteProjectBootstrap.Verify` with an initially empty Unity Library. Existing local Git LFS object bytes were reused as a download cache; the Git tree itself came from the remote repository.
- The runtime built from the complete snapshot passed the checks below. Physical camera, EEG headset and controllers were not tested.

> PASS: 49 integration checks. Third-person camera, imported assets, menu, four Force powers, weapon combat, directional block, guard break, stamina, roll, lock-on, progression and synthetic DualSense / DualShock 4 HID input. Physical controllers not tested.

> PASS: 26 neuro integration checks. Balanced scenarios, no-gesture/negative examples, simulated casting, training isolation, pause/resume, fragmented TCP JSON, strict EEG gating, untrained profiles, contact loss and disconnect. EEG and controller inputs simulated; physical headset/camera not tested.

> PASS: 16 opening checks. Both paths, camera continuity, protected simulation, automatic completion, keyboard/controller skip and cleanup. Controller input simulated.

All large asset payloads, including the original TGA character textures and TIFF source textures, are stored through Git LFS. Caches, Python environments and participant recordings are excluded. Simulation needs no Python runtime or headset.
