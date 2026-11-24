# -*- coding: utf-8 -*-
"""Standalone diag: render the R hand from +Z with ortho_scale ladder."""
import bpy
import mathutils
from pathlib import Path

HERE = Path(__file__).resolve().parent
BASELINE = HERE / "baseline"
OUT = HERE / "output" / "rev4"

bpy.ops.wm.open_mainfile(filepath=str(BASELINE / "hands_source.blend"))
sc = bpy.context.scene
# light
lt = bpy.data.lights.new("k", "AREA")
lt.energy = 1000
lt.size = 2
ob = bpy.data.objects.new("k", lt)
ob.location = (2, -5, 8)
bpy.context.collection.objects.link(ob)
sc.world = bpy.data.worlds.new("w")
sc.world.use_nodes = True
sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.05, 0.06, 1)

obj = bpy.data.objects["Hand.R"]
subd = [b for b in obj.modifiers if b.type == "ARMATURE"]
# mesh bounds via evaluated bbox at rest
deps = bpy.context.evaluated_depsgraph_get()
ev = obj.evaluated_get(deps)
c = sum((ev.matrix_world @ mathutils.Vector(v) for v in ev.bound_box),
        mathutils.Vector()) / 8
print("DIAG hand center:", [round(v, 3) for v in c])
bb = [ev.matrix_world @ mathutils.Vector(v) for v in ev.bound_box]
span = [round(max(p[i] for p in bb) - min(p[i] for p in bb), 3) for i in range(3)]
print("DIAG spans XYZ:", span)

cam = bpy.data.cameras.new("d")
cam.type = "ORTHO"
go = bpy.data.objects.new("d", cam)
bpy.context.collection.objects.link(go)
sc.camera = go

# camera looking from +Z straight at the hand center (simplest known-good path)
for scale in (0.5, 1.0, 2.0, 4.0):
    cam.ortho_scale = scale
    go.location = c + mathutils.Vector((0, 0, 3))
    go.rotation_euler = (0, 0, 0)   # looks down -Z toward the hand
    sc.render.filepath = str(OUT / f"_diag_scale_{scale}.png")
    bpy.ops.render.render(write_still=True)
    print("DIAG rendered scale", scale)
print("DIAGDONE")
