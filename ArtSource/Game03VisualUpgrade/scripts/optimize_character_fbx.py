"""Re-export an Ara 3D Gen character FBX with downscaled embedded textures.

Ara's raw FBX (~68 MB) embeds full-size PNG/JPEG textures. For a background
pedestrian this is excessive. This script keeps the full skeleton and mesh,
resizes every image to <=1024 px, and writes a much smaller FBX that Unity
2022.3 imports natively.

Run:
  blender.exe -b --python optimize_character_fbx.py -- <input.fbx> <output.fbx>
"""
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if len(argv) < 2:
    raise SystemExit("usage: optimize_character_fbx.py <input.fbx> <output.fbx>")
src, dst = argv[0], argv[1]

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=src)

armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
bones = sum(len(a.data.bones) for a in armatures)
print("CHAR_IN", os.path.basename(src), "armatures", len(armatures), "bones", bones,
      "images", len(bpy.data.images))

# Downscale all embedded images in place (keeps material bindings intact).
for img in bpy.data.images:
    if img.source != "FILE" or not img.has_data:
        continue
    w, h = img.size
    if w <= 1024 and h <= 1024:
        continue
    scale = 1024.0 / max(w, h)
    img.scale(int(w * scale), int(h * scale))
    print("RESIZED", img.name, (w, h), "->", tuple(img.size))

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=True,
    apply_unit_scale=True,
    bake_space_transform=False,
    add_leaf_bones=False,
    path_mode="COPY",
    embed_textures=True,
)

armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
bones = sum(len(a.data.bones) for a in armatures)
print("CHAR_OUT", os.path.basename(dst), "armatures", len(armatures), "bones", bones,
      "bytes", os.path.getsize(dst))
