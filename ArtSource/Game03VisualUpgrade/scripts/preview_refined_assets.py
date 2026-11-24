"""Quick Blender preview renders for the refined tree/building assets.

Run:
  blender.exe -b --python preview_refined_assets.py -- <output_dir> <asset_dir>
"""
import os
import sys
import math

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT, ASSETS = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)

ASSETS_TO_SHOW = [
    ("StreetPlane_Oak_Refined.fbx", (0, 0, 0), 7.2, "oak_preview.png"),
    ("StreetGinkgo_Willow_Refined.fbx", (0, 0, 0), 6.8, "willow_preview.png"),
    ("StreetConifer_Pine_Refined.fbx", (0, 0, 0), 7.6, "pine_preview.png"),
    ("NearFieldResidence_Refined.fbx", (0, 0, 0), 8.1, "building_preview.png"),
]

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 900
scene.render.resolution_y = 700
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.color = (0.72, 0.78, 0.82)

for filename, loc, height, png in ASSETS_TO_SHOW:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ASSETS, filename))

    bpy.ops.object.light_add(type="SUN", location=(4, -6, 10))
    sun = bpy.context.active_object
    sun.rotation_euler = (math.radians(35), math.radians(-25), math.radians(-20))
    sun.data.energy = 2.0

    target = Vector(loc) + Vector((0, 0, height * 0.48))
    bpy.ops.object.camera_add(location=(height * 0.85, -height * 1.25, height * 0.65))
    cam = bpy.context.active_object
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 42
    scene.camera = cam

    scene.render.filepath = os.path.join(OUT, png)
    bpy.ops.render.render(write_still=True)
    print("PREVIEW_OK", scene.render.filepath)
