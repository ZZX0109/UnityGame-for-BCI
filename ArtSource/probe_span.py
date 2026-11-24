import bpy, os, math
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]

def span():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]
    return (max(xs) - min(xs), max(ys) - min(ys))

print("SPAN rest", [round(v, 3) for v in span()])
for pb in arm.pose.bones:
    pb.rotation_euler = (0, 0, 0)
F = [("Index1.R", 75), ("Index2.R", 85), ("Index3.R", 50)]
for n, a in F:
    pb = arm.pose.bones[n]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(a), 0, 0)
bpy.context.view_layer.update()
deps = bpy.context.evaluated_depsgraph_get()
ev = o.evaluated_get(deps)
pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
print("SPAN index-fist", [round(max(xs)-min(xs),3), round(max(ys)-min(ys),3)], "z", [round(min(zs),2), round(max(zs),2)])
print("EULER check", [round(v, 2) for v in arm.pose.bones["Index1.R"].rotation_euler])
print("DIAG DONE")
