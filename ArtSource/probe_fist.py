
import bpy, os, math, sys
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
g = {"__file__": os.path.abspath("build_hands.py")}
src = open("build_hands.py", encoding="utf-8").read()
src = src.replace("\nmain()\n", "\n")
exec(compile(src, "build_hands.py", "exec"), g)
arm = bpy.data.objects["Armature.R"]
g["pose_actions"](arm, "R")
g["apply_pose"](arm, "R", "fist")
for bn in ("Index1.R", "Middle1.R", "Thumb1.R"):
    print(bn, "euler", [round(v, 2) for v in arm.pose.bones[bn].rotation_euler])
sc = bpy.context.scene
cam = bpy.data.cameras.new("c"); cam.lens = 85
co = bpy.data.objects.new("c", cam); bpy.context.collection.objects.link(co)
focus = Vector((-4.4, 1.8, 2.4))
d = Vector((0.35, -1, 0.25)).normalized()
co.location = focus + d * 10
co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
sc.camera = co
sc.render.filepath = os.path.abspath("Review/probe_fist.png")
bpy.ops.render.render(write_still=True)
print("PROBE2 SAVED")
