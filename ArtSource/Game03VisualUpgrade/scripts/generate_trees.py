"""Generate three distinct real-structure trees with MTree 5.6.0 (GPLv3).

Uses the prebuilt cp313 wheel-compatible MTree extension already installed in
Blender 5.2. Each tree is generated via the mtree.quick_generate operator with
a fixed seed and a different botanical preset, then exported as FBX so Unity
2022.3 imports it natively (no glTFast).

Output scale: MTree units are metres; the exported tree is ~5-8 m tall, which
matches the live Game03 street trees (~2.7 m trunk + crown). A scale factor is
applied on export so the FBX root is roughly 5.5 m tall for scene placement.

Run:
  blender.exe -b --python generate_trees.py -- <output_dir>
"""
import os
import sys

import bpy
import addon_utils

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)

addon_utils.enable("bl_ext.user_default.modular_tree", default_set=False)

TREES = [
    ("StreetPlane", "OAK", 11),
    ("StreetGinkgo", "WILLOW", 27),
    ("StreetConifer", "PINE", 43),
]

for name, preset, seed in TREES:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    result = bpy.ops.mtree.quick_generate(seed=seed, preset=preset, add_leaves=True)
    print("MTREE_GEN", name, preset, seed, result)

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"MTree produced no mesh for {name}/{preset}")

    # Normalise height to ~5.5 m (street scale) around the root base.
    min_z = min((o.matrix_world @ __import__("mathutils").Vector(v.co)).z
                for o in meshes for v in o.data.vertices)
    max_z = max((o.matrix_world @ __import__("mathutils").Vector(v.co)).z
                for o in meshes for v in o.data.vertices)
    height = max_z - min_z
    if height <= 0.01:
        raise RuntimeError(f"MTree produced degenerate height for {name}: {height}")
    scale = 5.5 / height
    for o in meshes:
        o.scale *= scale
        o.location.z = (o.location.z - min_z) * scale

    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = meshes[0]
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True,
                             bake_space_transform=False, add_leaf_bones=False)
    print("TREE_EXPORT_OK", path, "raw_height", round(height, 2), "scaled", round(scale, 3),
          "verts", sum(len(o.data.vertices) for o in meshes))
