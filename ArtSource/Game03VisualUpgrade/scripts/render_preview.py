"""Render simple studio previews of generated FBX/OBJ assets for visual QA."""
import os
import sys
import math

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)

def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

def studio(name):
    world = bpy.data.worlds.new("Studio")
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.80, 0.84, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    ldata = bpy.data.lights.new(name + " key", "AREA")
    ldata.energy = 900
    ldata.shape = "DISK"
    ldata.size = 8
    lamp = bpy.data.objects.new(name + " key", ldata)
    bpy.context.collection.objects.link(lamp)
    lamp.location = (6, -8, 10)
    lamp.rotation_euler = (Vector((0, 0, 3)) - lamp.location).to_track_quat("-Z", "Y").to_euler()
    camdata = bpy.data.cameras.new(name + " cam")
    cam = bpy.data.objects.new(name + " cam", camdata)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam

def render_asset(path, stem, cam_pos, target):
    reset()
    bpy.ops.import_scene.fbx(filepath=path)
    cam = studio(stem)
    cam.location = cam_pos
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 42
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 960
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = os.path.join(OUT, stem + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDER_OK", scene.render.filepath)

base = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(base, "..", "output")
building = os.path.join(out, "NearFieldResidence_RealOpenings.fbx")
if os.path.exists(building):
    render_asset(building, "preview_building", (14, -16, 9), (0, 0, 4))
