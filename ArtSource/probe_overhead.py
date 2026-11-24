import bpy, os, math
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
g = {"__file__": os.path.abspath("build_hands.py")}
src = open("build_hands.py", encoding="utf-8").read()
src = src.replace("\nmain()\n", "\n")
exec(compile(src, "build_hands.py", "exec"), g)
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]
g["pose_actions"](arm, "R")
g["apply_pose"](arm, "R", "fist")

sc = bpy.context.scene
cam = bpy.data.cameras.new("c"); cam.lens = 50
co = bpy.data.objects.new("ctop", cam)
bpy.context.collection.objects.link(co)
# overhead view of the whole scene
co.location = Vector((-4.5, -8, 8))
co.rotation_euler = (math.radians(15), 0, 0)
sc.camera = co
sc.render.resolution_x, sc.render.resolution_y = 1280, 720
sc.render.filepath = os.path.abspath("Review/probe_overhead.png")
bpy.ops.render.render(write_still=True)
print("OVERHEAD DONE")
