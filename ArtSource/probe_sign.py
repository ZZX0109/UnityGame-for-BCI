import bpy, os, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]
bpy.data.objects["Hand.L"].hide_render = True
bpy.data.objects["Armature.L"].hide_render = True
sc = bpy.context.scene
for sgn in (1, -1):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    pb = arm.pose.bones["Index1.R"]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(75 * sgn), 0, 0)
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    ys = [p.y for p in pts]
    print("SIGN", sgn, "y-span", round(max(ys) - min(ys), 3), "y-min", round(min(ys), 2))
print("SIGN PROBE DONE")
