import bpy, os, math
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]
sc = bpy.context.scene
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")

def render_pose(tag, angles):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    for n, (x, y, z) in angles.items():
        pb = arm.pose.bones[n]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
    bpy.context.view_layer.update()
    cam = bpy.data.cameras.new("c"); cam.lens = 85
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    focus = Vector((-4.5, 1.95, 2.4))
    d = Vector((0.35, -1, 0.25)).normalized()
    co.location = focus + d * 14
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.filepath = os.path.abspath(f"Review/probe_{tag}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)

fingers_only = {}
for f, ks in (("Index", (75, 85, 50)), ("Middle", (75, 85, 50)), ("Ring", (75, 85, 50)), ("Little", (75, 85, 50))):
    for k, a in zip(("1", "2", "3"), ks):
        fingers_only[f + k + ".R"] = (a, 0, 0)
render_pose("fingers_only", fingers_only)

thumb_too = dict(fingers_only)
thumb_too.update({"Thumb1.R": (30, 0, 25), "Thumb2.R": (55, 0, 0), "Thumb3.R": (40, 0, 0)})
render_pose("thumb_too", thumb_too)
print("SPLIT PROBES DONE")
