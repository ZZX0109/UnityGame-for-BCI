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

sc = bpy.context.scene
cam = bpy.data.cameras.new("c"); cam.lens = 85
co = bpy.data.objects.new("cp", cam)
bpy.context.collection.objects.link(co)
focus = Vector((-4.5, 1.95, 2.4))
d = Vector((0.35, -1, 0.25)).normalized()
co.location = focus + d * 14
co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
sc.camera = co
for n in ('Index1.R','Middle1.R','Thumb1.R','Thumb2.R'):
    pb = arm.pose.bones[n]
    print('EULER', n, [round(v,3) for v in pb.rotation_euler])
print('arm loc', [round(v,2) for v in arm.location], 'hand loc', [round(v,2) for v in o.location])
sc.render.filepath = os.path.abspath('Review/probe_applypose.png')
bpy.ops.render.render(write_still=True)
print("APPLYPOSE PROBE DONE")
