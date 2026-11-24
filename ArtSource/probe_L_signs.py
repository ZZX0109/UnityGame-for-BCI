# L-side sign compensation test: apply profile fist sample to the L FBX rig
# with three candidate basis conversions, render each, report which matches
# the R-side mirrored fist.
import bpy, os, math, json
from mathutils import Vector

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.abspath("../Assets/VirtualHand/Models/Hand_L.fbx"))
arm = bpy.data.objects["Armature.L"]
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
sc = bpy.context.scene
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")

with open(os.path.abspath("../Assets/VirtualHand/Resources/Rig/hand_rig_profile.json"), encoding="utf-8") as f:
    profile = json.load(f)
channels = profile["channels"]

def apply20(values, mode):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for ci, ch in enumerate(channels):
        pb = arm.pose.bones.get(ch["bone"] + ".L")
        if pb is None: continue
        deg = (values[ci] - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        if mode == "negYZ":   # same as R compensation: negate Y,Z
            e = pb.rotation_euler
            e.x += math.radians(deg)
            e.y -= math.radians(0)  # placeholder, handled below
        elif mode == "posYZ": # keep Y,Z as-is (negate only via sign already)
            pass
        e = pb.rotation_euler
        if mode == "negYZ":
            deg_x, deg_y, deg_z = deg, 0, 0
            if ch["axis"] == "Y": deg_y = deg
            if ch["axis"] == "Z": deg_z = deg
            e.x += math.radians(deg_x)
            e.y += math.radians(deg_y)
            e.z += math.radians(deg_z)
        else:  # posYZ
            e.x += math.radians(deg) if ch["axis"] == "X" else 0
            e.y += math.radians(deg) if ch["axis"] == "Y" else 0
            e.z += math.radians(deg) if ch["axis"] == "Z" else 0
        pb.rotation_euler = e
    bpy.context.view_layer.update()

fist = profile["samples"]["fist"]
for mode in ("negYZ", "posYZ"):
    apply20(fist, mode)
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    xs=[p.x for p in pts]; ys=[p.y for p in pts]; zs=[p.z for p in pts]
    focus = Vector((sum(xs)/8, sum(ys)/8, sum(zs)/8))
    cam = bpy.data.cameras.new("c"+mode); cam.lens = 60
    co = bpy.data.objects.new("c"+mode, cam)
    bpy.context.collection.objects.link(co)
    d = Vector((0.2, -1, 0.35)).normalized()
    co.location = focus + d * 1.2
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.abspath(f"Review/probe_L_fist_{mode}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("L MODE", mode, "RENDERED")
print("L SIGN TEST DONE")
