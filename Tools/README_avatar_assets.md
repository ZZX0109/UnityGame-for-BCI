# Game04 avatar reference assets

Run `python Tools/setup_avatar_assets.py` to list the catalog without network access. Select downloads with resource IDs, for example:

```text
python Tools/setup_avatar_assets.py --only makehuman_system_assets skins02
python Tools/setup_avatar_assets.py --only mpfb2
```

Downloads and extracted files stay under `ArtSource/_download/avatar-resources/`. The two MakeHuman asset packs are CC0 according to the official catalog. MPFB2 source code is GPLv3; its bundled graphic assets are CC0. `manifest.json` is written as UTF-8 JSON with LF line endings. Its SHA-256 values identify the locally stored ZIP bytes and are not upstream checksum verification.

After downloading, use the extracted `mpfb2_v2.0.18/` source tree's own documented local-source installation instructions for the Blender instance used by the prototype. This script does not install a global Blender add-on or assume Blender APIs. The asset packs are extracted into `makehuman_system_assets_cc0/` and `skins02_cc0/`; the script prints the `.mhmat`, `.mhclo`, and `.mhm` candidates for parent selection.

The candidate head's custom shell currently reads like a helmet. Once the packs are installed and a parent is selected, replace it with actual system hair and a natural skin texture. This workflow is for the Game04 prototype only.

`python -m unittest Tools.test_setup_avatar_assets` runs the local ZIP fixture test; it makes no network requests.
