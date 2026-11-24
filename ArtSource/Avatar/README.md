# Game04 MPFB avatar prototype

`build_mpfb_avatar.py` assembles a generic adult avatar from the existing MPFB 2.0.18 source and the extracted CC0 MakeHuman asset packs. It uses MPFB for the body, rig, skin, hair, brows, eyes, and clothing. It does not create substitute body or accessory meshes.

Run in Blender 5.2 background mode from the Game04 project root:

```powershell
blender -b --python ArtSource/Avatar/build_mpfb_avatar.py -- --assets-root ArtSource/_download/avatar-resources
```

The script discovers assets below directories named `makehuman_system_assets_cc0` and `skins02_cc0`. If discovery cannot find a required item, pass its file explicitly with `--skin`, `--eyes`, `--eyebrows`, `--hair`, `--shirt`, `--trousers`, or `--shoes`. Missing or invalid shirt, trousers, or shoes stop the build before rendering or export; there is no primitive fallback. `--mpfb-src` can point to the existing extracted MPFB `src` directory.

Outputs go under `ArtSource/Avatar/generated/neutral_adult` by default: a Blender scene, FBX, GLB, front and three-quarter PNG renders, and `avatar_manifest.json`. The assembled avatar, rig, and assets are scaled together to 1.70 m. Clothing is loaded through MPFB so its existing body delete-group masks are used.

The FBX contains the MPFB `game_engine` deformation rig and skin weights, without leaf bones or animation clips. Blender shader nodes are preserved for authoring; adapt materials for Unity's Built-in Render Pipeline after import.

Keep the MPFB source package and its GPL license provenance with the downloaded source. Assets selected from the named packs are recorded as CC0 in the manifest. Explicit files outside those packs are recorded with their license as unverified. The builder does not download files, save Blender preferences, or write outside `ArtSource/Avatar`; MPFB runtime data is redirected to `ArtSource/Avatar/.mpfb-runtime` for this prototype.
