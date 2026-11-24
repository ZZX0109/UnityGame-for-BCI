import bpy, os, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
arm = bpy.data.objects["Armature.R"]
o = bpy.data.objects["Hand.R"]
sc = bpy.context.scene
# hide L
bpy.data.objects["Hand.L"].hide_render = True
bpy.data.objects["Armature.L"].hide_render = True
for deg in (0, 45, 75):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    pb = arm.pose.bones["Index1.R"]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(deg), 0, 0)
    bpy.context.view_layer.update()
    cam = bpy.data.cameras.new("c%d" % deg); cam.lens = 85
    co = bpy.data.objects.new("c%d" % deg, cam)
    bpy.context.collection.objects.link(co)
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    focus = Vector((sum(xs)/8, sum(ys)/8, sum(zs)/8))
    d = Vector((0.15, 0.5, -1.0)).normalized()  # from palm side (+Z toward -Z)
    co.location = focus + d * 8
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.abspath(f"Review/probe_index_{deg}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("IDX", deg, "y-span", round(max(ys)-min(ys), 3))
print("INDEX PROBES DONE")
