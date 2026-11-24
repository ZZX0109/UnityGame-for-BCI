import bpy, os, math
from mathutils import Vector, Matrix
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]

def span(tag):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    ys = [p.y for p in pts]
    print(tag, "y-span", round(max(ys) - min(ys), 3))

# 1. set fist first
pb = arm.pose.bones["Index1.R"]
pb.rotation_mode = "XYZ"
pb.rotation_euler = (math.radians(75), 0, 0)
bpy.context.view_layer.update()
span("fist-no-compose")
# 2. compose (same as render_two_single)
wrist = Vector((-4.321, 1.7725, 2.402))  # baked wrist
target = Vector((-0.85, 0, 2.4))
T = Matrix.Translation(target) @ Matrix.Translation(-wrist)
arm.matrix_world = T @ arm.matrix_world
bpy.context.view_layer.update()
span("fist-after-compose")
