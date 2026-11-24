# Minimal acceptance v2: L/R rest vs Index1 +30deg, palm & side views.
# Orthographic camera auto-fitted to the projected bounds (20% margin) so the
# whole hand — wrist, full index and fingertip — is always in frame.
import bpy, os, math
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))
sc = bpy.context.scene

def render_ortho(tag, focus, direction, span):
    cam = bpy.data.cameras.new("c" + tag)
    cam.type = 'ORTHO'
    cam.ortho_scale = span * 1.20  # 20% margin total
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(direction).normalized()
    co.location = focus + d * 30  # ortho: distance only affects clipping
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.abspath(f"Review/min2_{tag}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)

for side in ("R", "L"):
    arm = bpy.data.objects[f"Armature.{side}"]
    o = bpy.data.objects[f"Hand.{side}"]
    other_o = bpy.data.objects[f"Hand.{'L' if side == 'R' else 'R'}"]
    other_a = bpy.data.objects[f"Armature.{'L' if side == 'R' else 'R'}"]
    other_o.hide_render = True
    other_a.hide_render = True

    for pose_label, bend in (("rest", 0.0), ("index30", 30.0)):
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode="POSE")
        for pb in arm.pose.bones:
            pb.rotation_euler = (0, 0, 0)
        if bend:
            pb = arm.pose.bones[f"Index1.{side}"]
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (math.radians(bend), 0, 0)
        bpy.context.view_layer.update()
        deps = bpy.context.evaluated_depsgraph_get()
        ev = o.evaluated_get(deps)
        pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
        focus = sum(pts, Vector()) / 8.0

        # geometric palm normal in world space, per side, from posed bones
        iw = (arm.matrix_world @ Vector(arm.data.bones["IndexMeta." + side].tail_local)
              - arm.matrix_world @ Vector(arm.data.bones["Wrist." + side].head_local))
        lw = (arm.matrix_world @ Vector(arm.data.bones["LittleMeta." + side].tail_local)
              - arm.matrix_world @ Vector(arm.data.bones["Wrist." + side].head_local))
        n = (iw.cross(lw)).normalized()
        span = max(
            max(p.x for p in pts) - min(p.x for p in pts),
            max(p.y for p in pts) - min(p.y for p in pts),
            max(p.z for p in pts) - min(p.z for p in pts),
        )
        render_ortho(f"{side}_{pose_label}_palm", focus, n, span)
        # side view: from the thumb-edge direction (palm normal rotated 90 deg about index axis)
        side_dir = n.cross(iw.normalized()).normalized()
        render_ortho(f"{side}_{pose_label}_side", focus, side_dir, span)
    other_o.hide_render = False
    other_a.hide_render = False
print("MIN ACCEPTANCE V2 DONE")
