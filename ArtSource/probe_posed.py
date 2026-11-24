import bpy, os, math
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
for n, a in (("Index1.R", 75), ("Index2.R", 85), ("Index3.R", 50)):
    pb = arm.pose.bones[n]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(a), 0, 0)
bpy.context.view_layer.update()
for n in ("IndexMeta.R", "Index1.R", "Index2.R", "Index3.R"):
    pb = arm.pose.bones[n]
    head = arm.matrix_world @ Vector(pb.head)
    tail = arm.matrix_world @ Vector(pb.tail)
    print("POSED", n, "head", [round(v, 2) for v in head], "tail", [round(v, 2) for v in tail])
