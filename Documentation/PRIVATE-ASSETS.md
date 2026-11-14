# Local Asset Store dependencies

This repository is public. Asset Store package source files are excluded from Git; owning a package for game development does not grant permission to publish its source assets. The complete local v12 project and build still contain these dependencies. No original files were removed locally.

The repository includes the game's code, shaders, Unity scene and settings, prepared animation/controller data, project-authored prefab/material wrappers, CC0 environment and animation sources, and Unity Companion License sources with their notices. Prepared wrappers retain references to the excluded packages; they are not substitutes for those packages.

## Required packages

Acquire the following through your own Unity account and import them using **Window > Package Manager > My Assets**:

| Package | Publisher | Version used | Expected folder |
| --- | --- | --- | --- |
| Adventure Female (Free) | Maksim Bugrimov | 1.0 | `Assets/Adventures_woman/` |
| Human Basic Motions FREE | Kevin Iglesias | 2.4.2 | `Assets/Kevin Iglesias/` |
| Sci-Fi Post-Apocalyptic Young Male / MP_Clovis | MPowered Games | 1.0 | `Assets/Package Ready Assets/MP_Clovis/` |
| Volumetric Lines | Johannes Unterguggenberger | 3.1.1 | `Assets/VolumetricLines/` |

The copied `Assets/Resources/Generated/BladeVolumetric.mat` is also excluded. `StoreAssetsBuild.Prepare()` recreates it from Volumetric Lines. Keep the original package `.meta` files when importing. Source model GUIDs link the prepared characters and materials to their meshes and textures.

**A fresh clone requires these packages before it can compile and reproduce the v12 demo.** In particular, the character code directly references Volumetric Lines, and the locomotion controller references Human Basic Motions clips. This repository is not a replacement source for the excluded packages.

## Restore from your own complete project

If you already have the complete v12 Unity project (or have extracted your private source archive), close Unity on the destination clone and run:

```sh
python3 tools/restore-private-assets.py --source /absolute/path/to/complete/ForceAwakening
```

This copies only the excluded paths, including their `.meta` files, from that project into the current clone. It does not download assets, copy cache folders or stage anything in Git. Existing dependency files are skipped by default; `--overwrite` replaces them explicitly. Missing paths are reported before any copying begins. Only use a source project whose assets you are entitled to use.

Then open the destination in Unity **6000.6.9f1** and open `Assets/Scenes/Temple.unity`. The prepared assets are already included. For a code-only macOS rebuild, run `DemoBuild.BuildPreparedMac`.

If importing packages rather than restoring the prepared private files, run `DemoBuild.Prepare` after compilation succeeds to regenerate the dependent materials and characters, followed by `DemoBuild.BuildPreparedMac`. Full regeneration reimports the world and character assets and may take several minutes.

## References

- [Unity Asset Store Terms and EULA](https://unity.com/legal/as-terms)
- [Asset sources and license notices](../ASSET-CREDITS.md)
