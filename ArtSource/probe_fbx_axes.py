import bpy, os, math
from mathutils import Vector
# read back the exported FBX to see the local axes Unity actually receives
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.abspath("../Assets/VirtualHand/Models/Hand_R.fbx"))
arm = bpy.data.objects.get("Armature.R") or bpy.data.objects.get("Armature")
o = next((ob for ob in bpy.data.objects if ob.type == 'MESH'), None)
print("IMPORTED objects:", [(ob.name, ob.type) for ob in bpy.data.objects])
if arm:
    for n in ("Index1.R", "Index2.R"):
        b = arm.data.bones.get(n)
        if b:
            print("BONE", n, "head", [round(v, 3) for v in b.head_local], "tail", [round(v, 3) for v in b.tail_local],
                  "parent", b.parent.name if b.parent else None)
    if o:
        print("MESH bbox y", round(min(v.co.y for v in o.data.vertices), 2), round(max(v.co.y for v in o.data.vertices), 2),
              "z", round(min(v.co.z for v in o.data.vertices), 2), round(max(v.co.z for v in o.data.vertices), 2))
