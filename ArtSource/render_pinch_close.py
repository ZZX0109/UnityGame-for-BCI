import bpy, os, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
g = {"__file__": os.path.abspath("build_hands.py")}
src = open("build_hands.py", encoding="utf-8").read()
src = src.replace(chr(10) + "main()" + chr(10), chr(10))
exec(compile(src, "build_hands.py", "exec"), g)
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]
bpy.data.objects["Hand.L"].hide_render = True
bpy.data.objects["Armature.L"].hide_render = True
g["pose_actions"](arm, "R")
g["apply_pose"](arm, "R", "pinch")
bpy.context.view_layer.update()
# focus exactly on the pinch contact point (midway between thumb and index tips)
deps = bpy.context.evaluated_depsgraph_get()
pb_i = arm.pose.bones["Index3.R"]
pb_t = arm.pose.bones["Thumb3.R"]
pi = arm.matrix_world @ Vector(pb_i.tail)
pt = arm.matrix_world @ Vector(pb_t.tail)
focus = (pi + pt) / 2
print("CONTACT I", [round(v, 2) for v in pi], "T", [round(v, 2) for v in pt],
      "gap", round((pi - pt).length, 3))
sc = bpy.context.scene
cam = bpy.data.cameras.new("c"); cam.lens = 85
co = bpy.data.objects.new("cp", cam)
bpy.context.collection.objects.link(co)
# view direction: from palm-normal side (+Z is palm in baked space) slightly radial
d = Vector((0.25, 0.35, 1.0)).normalized()
co.location = focus + d * 3.5
co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
sc.camera = co
sc.render.resolution_x, sc.render.resolution_y = 1280, 960
sc.render.filepath = os.path.abspath("Review/preview_pinch_contact.png")
bpy.ops.render.render(write_still=True)
print("PINCH CONTACT DONE gap=", round((pi - pt).length, 3))
