# Two-hand composite renders from the saved rest .blend (no rebuild).
import bpy, os, math
from mathutils import Vector, Matrix

BLEND = os.path.abspath(os.path.join("Hands", "hands_source.blend"))
REVIEW = os.path.abspath("Review")
JOINT_WRIST_R = (-4.321, 2.402, 1.773)


def bake_x90(p):
    return [p[0], -p[2], p[1]]


def mirror(p):
    return [-p[0], p[1], p[2]]


bpy.ops.wm.open_mainfile(filepath=BLEND)
sc = bpy.context.scene

# pose fists later; first compose at rest
for side in ("R", "L"):
    arm = bpy.data.objects[f"Armature.{side}"]
    wrist = Vector(mirror(bake_x90(JOINT_WRIST_R)) if side == "L" else bake_x90(JOINT_WRIST_R))
    target = Vector((-0.85, 0, 2.4)) if side == "R" else Vector((0.85, 0, 2.4))
    T = Matrix.Translation(target)
    if side == "L":
        T = T @ Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Translation(-wrist)
    else:
        T = T @ Matrix.Translation(-wrist)
    arm.matrix_world = T @ arm.matrix_world
bpy.context.view_layer.update()


def render_two(tag, fist):
    allcoords = []
    for side in ("R", "L"):
        if fist:
            arm = bpy.data.objects[f"Armature.{side}"]
            for pb in arm.pose.bones:
                pb.rotation_euler = (0, 0, 0)
            sfx = "." + side
            # full fist from build POSE_ANGLES (fingers + thumb)
            F = [("Index1", 75), ("Index2", 85), ("Index3", 50),
                 ("Middle1", 75), ("Middle2", 85), ("Middle3", 50),
                 ("Ring1", 75), ("Ring2", 85), ("Ring3", 50),
                 ("Little1", 75), ("Little2", 85), ("Little3", 50)]
            for n, a in F:
                arm.pose.bones[n + sfx].rotation_euler = (math.radians(a), 0, 0)
            for n, a in (("Thumb1", 30), ("Thumb2", 55), ("Thumb3", 40)):
                arm.pose.bones[n + sfx].rotation_euler = (math.radians(a), 0, 0)
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    for side in ("R", "L"):
        ev = bpy.data.objects[f"Hand.{side}"].evaluated_get(deps)
        for c in ev.bound_box:
            allcoords.append(ev.matrix_world @ Vector(c))
    mn = Vector((min(p.x for p in allcoords), min(p.y for p in allcoords), min(p.z for p in allcoords)))
    mx = Vector((max(p.x for p in allcoords), max(p.y for p in allcoords), max(p.z for p in allcoords)))
    mid = (mn + mx) / 2
    span = max(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z)
    dist = max(4.0, span * 1.5)
    cam = bpy.data.cameras.new("c" + tag)
    cam.lens = 60
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector((0.6, -0.75, 0.28)).normalized()
    co.location = mid + d * dist
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.join(REVIEW, f"hand_two_{tag}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)


render_two("open", False)
render_two("fist", True)
print("TWO-HAND RENDERS DONE")
