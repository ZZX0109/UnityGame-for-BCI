import bpy, os, math, sys
from mathutils import Vector, Matrix

tag = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.open_mainfile(filepath=os.path.abspath("Hands/hands_source.blend"))

JOINT_WRIST_R = (-4.321, 2.402, 1.773)
def bake_x90(p): return [p[0], -p[2], p[1]]
def mirror(p): return [-p[0], p[1], p[2]]

if tag == "fist":
    for side in ("R", "L"):
        arm = bpy.data.objects[f"Armature.{side}"]
        s = "." + side
        for n, a in [("Index1", 75), ("Index2", 85), ("Index3", 50),
                     ("Middle1", 75), ("Middle2", 85), ("Middle3", 50),
                     ("Ring1", 75), ("Ring2", 85), ("Ring3", 50),
                     ("Little1", 75), ("Little2", 85), ("Little3", 50),
                     ("Thumb1", 30), ("Thumb2", 55), ("Thumb3", 40)]:
            pb = arm.pose.bones.get(n + s)
            if pb is None:
                print("MISSING POSE BONE", n + s)
                continue
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (math.radians(a), 0, 0)
        print("FIST APPLIED", side)

for side in ("R", "L"):
    arm = bpy.data.objects[f"Armature.{side}"]
    wrist = Vector(mirror(bake_x90(JOINT_WRIST_R)) if side == "L" else bake_x90(JOINT_WRIST_R))
    target = Vector((-0.85, 0, 2.4)) if side == "R" else Vector((0.85, 0, 2.4))
    tilt = Matrix.Rotation(math.radians(25 if side == "R" else -25), 4, 'Z')
    T = Matrix.Translation(target) @ tilt
    if side == "L":
        T = T @ Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Translation(-wrist)
    else:
        T = T @ Matrix.Translation(-wrist)
    arm.matrix_world = T @ arm.matrix_world

bpy.context.view_layer.update()

for side in ("R", "L"):
    o = bpy.data.objects["Hand." + side]
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    ys = [p.y for p in pts]
    print("SPAN", side, round(max(ys) - min(ys), 3))

sc = bpy.context.scene
deps = bpy.context.evaluated_depsgraph_get()
allcoords = []
for side in ("R", "L"):
    ev = bpy.data.objects[f"Hand.{side}"].evaluated_get(deps)
    for c in ev.bound_box:
        allcoords.append(ev.matrix_world @ Vector(c))
mn = Vector((min(p.x for p in allcoords), min(p.y for p in allcoords), min(p.z for p in allcoords)))
mx = Vector((max(p.x for p in allcoords), max(p.y for p in allcoords), max(p.z for p in allcoords)))
mid = (mn + mx) / 2
span = max(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z)

cam = bpy.data.cameras.new("c"); cam.lens = 60
co = bpy.data.objects.new("c", cam)
bpy.context.collection.objects.link(co)
d = Vector((0.6, -0.75, 0.28)).normalized()
co.location = mid + d * max(4.0, span * 1.5)
co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
sc.camera = co
sc.render.resolution_x, sc.render.resolution_y = 1280, 960
sc.render.filepath = os.path.abspath(f"Review/hand_two_{tag}.png")
bpy.ops.render.render(write_still=True)
print("SINGLE RENDER DONE", tag)
