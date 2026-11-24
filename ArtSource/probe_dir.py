
import bpy, os, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
sc = bpy.context.scene
def render(tag, ang_deg):
    pb = arm.pose.bones["Index1.R"]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(ang_deg), 0, 0)
    bpy.context.view_layer.update()
    cam = bpy.data.cameras.new("c"); cam.lens = 85
    co = bpy.data.objects.new("c"+tag, cam); bpy.context.collection.objects.link(co)
    focus = Vector((-4.5, 1.95, 2.6))
    d = Vector((0.35, -1, 0.25)).normalized()
    co.location = focus + d * 7
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.filepath = os.path.abspath(f"Review/probe_dir_{tag}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    pb.rotation_euler = (0,0,0)
render("p30", +30)
render("m30", -30)
print("DIR PROBES SAVED")
