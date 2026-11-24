"""Generate three distinct real-branch trees, export a game-light FBX.

MTree 5.6.0 (GPLv3) provides the real branch skeleton. Its geometry-nodes leaf
distributor bakes millions of vertices into FBX, so instead we generate the
woody structure with add_leaves=False, decimate it, and add a small number of
low-poly canopy clusters authored here. This keeps genuine branching while
producing a Unity-importable asset (~1-3 MB each).

Run:
  blender.exe -b --python generate_trees_light.py -- <output_dir>
"""
import os
import sys
import math
import random

import bpy
import addon_utils
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)

addon_utils.enable("bl_ext.user_default.modular_tree", default_set=False)

TREES = [
    ("StreetPlane_Oak", "OAK", 11, (0.36, 0.48, 0.20), 14),
    ("StreetGinkgo_Willow", "WILLOW", 27, (0.52, 0.60, 0.22), 16),
    ("StreetConifer_Pine", "PINE", 43, (0.22, 0.34, 0.16), 12),
]


def make_leaf_material(name, colour):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*colour, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.85
    return mat


for name, preset, seed, leaf_rgb, cluster_count in TREES:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    # Real branch/trunk structure from MTree, no geometry-nodes leaves.
    bpy.ops.mtree.quick_generate(seed=seed, preset=preset, add_leaves=False)
    woody = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not woody:
        raise RuntimeError(f"MTree produced no mesh for {name}")
    trunk = max(woody, key=lambda o: len(o.data.vertices))

    # Decimate the dense woody mesh to a game-friendly budget.
    dec = trunk.modifiers.new("UnityDecimate", "DECIMATE")
    dec.ratio = 0.06
    bpy.context.view_layer.objects.active = trunk
    trunk.select_set(True)
    bpy.ops.object.modifier_apply(modifier=dec.name)

    # MTree exports the woody mesh with no material -> white in Unity. Assign bark.
    bark = bpy.data.materials.get("StreetBark") or bpy.data.materials.new("StreetBark")
    bark.diffuse_color = (0.32, 0.25, 0.18, 1.0)
    bark.use_nodes = True
    bsdf = bark.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.32, 0.25, 0.18, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.92
    trunk.data.materials.clear()
    trunk.data.materials.append(bark)

    # Normalise height to ~5.5 m.
    zs = [(trunk.matrix_world @ v.co).z for v in trunk.data.vertices]
    height = max(zs) - min(zs)
    scale = 5.5 / max(height, 0.01)
    trunk.scale *= scale
    trunk.location.z = (trunk.location.z - min(zs)) * scale
    bpy.context.view_layer.update()

    # Lightweight canopy: a handful of low-poly spheres in the upper crown.
    rng = random.Random(seed)
    leaf_mat = make_leaf_material(name + "_foliage", leaf_rgb)
    canopy_objs = []
    top = 5.5
    for i in range(cluster_count):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1.0)
        cl = bpy.context.active_object
        cl.name = f"{name}_leaf_cluster_{i:02d}"
        angle = rng.uniform(0, 2 * math.pi)
        radial = rng.uniform(0.25, 1.35)
        cl.location = (
            math.cos(angle) * radial,
            math.sin(angle) * radial,
            rng.uniform(top * 0.58, top * 0.98),
        )
        cl.scale = (rng.uniform(0.7, 1.2), rng.uniform(0.7, 1.2), rng.uniform(0.5, 0.9))
        cl.data.materials.append(leaf_mat)
        canopy_objs.append(cl)

    bpy.ops.object.select_all(action="DESELECT")
    for o in [trunk] + canopy_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = trunk

    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True,
                             bake_space_transform=False, add_leaf_bones=False)
    print("TREE_OK", path, "woody_verts", len(trunk.data.vertices),
          "clusters", len(canopy_objs), "bytes", os.path.getsize(path))
