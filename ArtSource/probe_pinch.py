import bpy, os, math
import numpy as np
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]

def set_pinch(t1, t2, t3, tw, i1, i2):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    vals = {
        "Index1.R": (i1, 0, 0), "Index2.R": (i2, 0, 0), "Index3.R": (22, 0, 0),
        "Thumb1.R": (t1, tw, 20), "Thumb2.R": (t2, 0, 0), "Thumb3.R": (t3, 0, 0),
    }
    for n, e in vals.items():
        pb = arm.pose.bones[n]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = tuple(math.radians(x) for x in e)
    bpy.context.view_layer.update()

def tip_world(bone_name):
    pb = arm.pose.bones[bone_name]
    return arm.matrix_world @ Vector(pb.tail)

best = None
for t1 in (45, 60, 75):
    for tw in (10, 30, 50):
        for i2 in (30, 45):
            set_pinch(t1, 95, 70, tw, 45, i2)
            g = (tip_world("Index3.R") - tip_world("Thumb3.R")).length
            print("GAP t1=%d tw=%d i2=%d -> %.3f" % (t1, tw, i2, g))
            if best is None or g < best[0]:
                best = (g, t1, tw, i2)
print("BEST", best)
print("PINCH SEARCH DONE")
