import bpy, os
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
for n in ("IndexMeta.R", "Index1.R", "Index2.R", "Index3.R", "MiddleMeta.R", "Middle1.R"):
    b = arm.data.bones[n]
    parent = b.parent.name if b.parent else None
    print("BONE", n, [round(v, 2) for v in b.head_local], [round(v, 2) for v in b.tail_local], parent)
