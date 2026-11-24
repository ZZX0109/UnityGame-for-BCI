"""Optimize an MTree FBX for Unity: decimate trunk, bake leaves to a light mesh.

MTree's raw FBX exports hundreds of MB because the FBX exporter bakes every
geometry-nodes leaf instance. This script re-imports the raw FBX, decimates the
woody mesh, keeps only a fraction of leaf instances, and re-exports a compact
FBX suitable for a real-time street scene.

Run:
  blender.exe -b --python optimize_tree.py -- <input.fbx> <output.fbx>
"""
import os
import sys
import random

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if len(argv) < 2:
    raise SystemExit("usage: optimize_tree.py <input.fbx> <output.fbx>")
src, dst = argv[0], argv[1]

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=src)

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
print("TREE_IN", os.path.basename(src), [(o.name, len(o.data.vertices)) for o in meshes])

# The largest mesh is the woody trunk/branches; decimate to ~12% of vertices.
woody = max(meshes, key=lambda o: len(o.data.vertices))
dec = woody.modifiers.new("UnityDecimate", "DECIMATE")
dec.ratio = 0.12
bpy.context.view_layer.objects.active = woody
woody.select_set(True)
bpy.ops.object.modifier_apply(modifier=dec.name)

# Delete tiny leaf meshes beyond a budget; real-time trees need far fewer leaves.
leaves = [o for o in meshes if o is not woody]
random.seed(11)
budget = 40
for leaf in leaves[budget:]:
    bpy.data.objects.remove(leaf, do_unlink=True)
print("TREE_KEPT", "woody", len(woody.data.vertices), "leaf_instances", min(budget, len(leaves)))

bpy.ops.object.select_all(action="DESELECT")
for o in bpy.context.scene.objects:
    if o.type == "MESH":
        o.select_set(True)
bpy.context.view_layer.objects.active = woody
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, apply_unit_scale=True,
                         bake_space_transform=False, add_leaf_bones=False)
print("TREE_OUT", os.path.basename(dst), "bytes", os.path.getsize(dst))
