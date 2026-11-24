import bpy, os, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.L"]
o = bpy.data.objects["Hand.L"]
pb = arm.pose.bones["Index1.L"]
pb.rotation_mode = "XYZ"
pb.rotation_euler = (math.radians(75), 0, 0)
bpy.context.view_layer.update()
deps = bpy.context.evaluated_depsgraph_get()
ev = o.evaluated_get(deps)
pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
ys = [p.y for p in pts]; zs = [p.z for p in pts]
print("L span y", round(max(ys)-min(ys),3), "z", [round(min(zs),2), round(max(zs),2)])
print("euler", [round(v,2) for v in pb.rotation_euler])
