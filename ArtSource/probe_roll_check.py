# Small positive flexion on L/R index: verify curl direction with new roll.
import bpy, os, math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
sc = bpy.context.scene
bpy.context.view_layer.objects.active = bpy.data.objects["Armature.R"]
bpy.ops.object.mode_set(mode="POSE")

def render_side(side, deg):
    arm = bpy.data.objects[f"Armature.{side}"]
    o = bpy.data.objects[f"Hand.{side}"]
    other_o = bpy.data.objects[f"Hand.{'L' if side=='R' else 'R'}"]
    other_a = bpy.data.objects[f"Armature.{'L' if side=='R' else 'R'}"]
    other_o.hide_render = True; other_a.hide_render = True
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    pb = arm.pose.bones[f"Index1.{side}"]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(deg), 0, 0)
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    zs = [p.z for p in pts]
    cam = bpy.data.cameras.new("c"); cam.lens = 85
    co = bpy.data.objects.new("c", cam)
    bpy.context.collection.objects.link(co)
    xs = [p.x for p in pts]
    focus = Vector((sum(xs)/8, sum(pts, Vector()).y/8, sum(zs)/8))
    # camera from palm side (+Z), slightly front
    d = Vector((0.15, 0.4, 1.0)).normalized()
    co.location = focus + d * 8
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.abspath(f"Review/rollcheck_{side}_{deg}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("SIDE", side, "deg", deg, "z-min", round(min(zs), 2), "z-max", round(max(zs), 2))
    other_o.hide_render = False; other_a.hide_render = False

render_side("R", 60)
render_side("L", 60)
print("ROLL CHECK DONE")
