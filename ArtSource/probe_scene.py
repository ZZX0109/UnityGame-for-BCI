import bpy, os, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
o = bpy.data.objects["Hand.R"]
# evaluated bbox
deps = bpy.context.evaluated_depsgraph_get()
ev = o.evaluated_get(deps)
pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
xs=[p.x for p in pts]; ys=[p.y for p in pts]; zs=[p.z for p in pts]
print("MESH bbox x",round(min(xs),2),round(max(xs),2),"y",round(min(ys),2),round(max(ys),2),"z",round(min(zs),2),round(max(zs),2))
print("L visible", not bpy.data.objects["Hand.L"].hide_render)
print("lights", [ob.name for ob in bpy.data.objects if ob.type=='LIGHT'])
print("world", bpy.context.scene.world.name if bpy.context.scene.world else None)
