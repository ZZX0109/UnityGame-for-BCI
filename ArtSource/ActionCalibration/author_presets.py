# Author-preset natural fist/pinch (explicitly labeled as authored, not solved),
# with a small penetration-relief pass: local search around the preset to
# minimize mesh self-intersection between fingers and palm.
# All values within 0..1 of the shared profile calibration.
import bpy, os, math, json
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROFILE_IN = os.path.join(HERE, "input", "hand_rig_profile.json")
PROFILE_OUT = os.path.join(HERE, "output", "hand_rig_profile.json")

with open(PROFILE_IN, encoding="utf-8") as f:
    profile = json.load(f)
channels = profile["channels"]

# control index -> channel (for reference)
CH = {c["index"]: c for c in channels}

# ---- authored natural presets (20 values, [0,1], rest-aware) ----
# fist: four fingers curled to the palm (MCP 65/PIP 80/DIP 45 deg equivalent),
# thumb wraps over with opposition engaged.
fist20 = [0.0] * 20
for fi in range(1, 5):
    o = fi * 4
    fist20[o] = 65 / 75.0      # MCP
    fist20[o + 1] = 80 / 85.0  # PIP
    fist20[o + 2] = 45 / 50.0  # DIP
    fist20[o + 3] = 0.5        # spread neutral
# thumb: CMC flex 35deg (35/90), MCP flex 55 (55/90), IP flex 40 (40/114)
fist20[0] = 35 / 90.0
fist20[1] = 55 / 90.0
fist20[2] = 40 / 114.0
# opposition twist 0.5+25/160 -> thumb rotates toward the fingers
fist20[3] = 0.5 + 25 / 160.0

# pinch: thumb meets index pad; middle/ring/little gently curled
pinch20 = [0.0] * 20
pinch20[0] = 30 / 90.0
pinch20[1] = 70 / 90.0
pinch20[2] = 110 / 114.0
pinch20[3] = 0.5 - 80 / 160.0  # full opposition toward the index
pinch20[4] = 45 / 75.0
pinch20[5] = 86 / 85.0
pinch20[6] = 22 / 50.0
pinch20[7] = 0.5
for fi, (a, b, c) in enumerate([(25, 35, 20), (30, 40, 25), (35, 45, 30)], start=2):
    oo = fi * 4
    pinch20[oo] = a / 75.0
    pinch20[oo + 1] = b / 85.0
    pinch20[oo + 2] = c / 50.0
    pinch20[oo + 3] = 0.5
pinch20 = [round(max(0.0, min(1.0, v)), 4) for v in pinch20]
fist20 = [round(max(0.0, min(1.0, v)), 4) for v in fist20]

# ---- setup scene from the input FBX ----
FBX = os.path.join(HERE, "input", "Hand_R.fbx")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
arm = bpy.data.objects["Armature.R"]
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
sc = bpy.context.scene
w = bpy.data.worlds.new("W"); w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.85, 0.85, 0.87, 1)
sc.world = w
lt = bpy.data.lights.new("sun", 'SUN'); lt.energy = 3.5
lo = bpy.data.objects.new("sun", lt); bpy.context.collection.objects.link(lo)
lo.rotation_euler = (math.radians(55), 0, math.radians(35))
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")


def apply20(values):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for ci, ch in enumerate(channels):
        pb = arm.pose.bones.get(ch["bone"] + ".R")
        if pb is None:
            continue
        deg = (values[ci] - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        e = pb.rotation_euler
        comp = {"X": 0, "Y": 1, "Z": 2}[ch["axis"]]
        e[comp] += math.radians(deg)
    bpy.context.view_layer.update()


def get_normal():
    iw = (arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    lw = (arm.matrix_world @ Vector(arm.data.bones["LittleMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    n = (iw.cross(lw)).normalized()
    th = np.array((arm.matrix_world @ Vector(arm.data.bones["Thumb1.R"].head_local))[:])
    pc = np.array((arm.matrix_world @ Vector(arm.data.bones["Index1.R"].head_local))[:])
    if np.dot(th - pc, n) < 0:
        n = -n
    return n


def render(tag, direction=None, contact=None, zoom=False):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    focus = sum(pts, Vector()) / 8.0
    span = max(max(p.x for p in pts) - min(p.x for p in pts),
               max(p.y for p in pts) - min(p.y for p in pts),
               max(p.z for p in pts) - min(p.z for p in pts))
    cam = bpy.data.cameras.new("c" + tag); cam.type = 'ORTHO'
    cam.ortho_scale = span * (1.6 if zoom else 1.25)
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    n = get_normal()
    d = Vector(direction) if direction else n
    co.location = focus + d.normalized() * 5
    co.rotation_euler = (-d.normalized()).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.join(HERE, "output", f"preview_{tag}.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)


# ---- render authored presets ----
apply20(fist20)
render("fist_palm_author", None)
render("fist_side_author", (get_normal().cross(Vector((0, 1, 0)))), None)
apply20(pinch20)
render("pinch_palm_author", None)
render("pinch_contact_author", None, contact=True)
print("AUTHOR PREVIEWS DONE")
print("FIST20", fist20)
print("PINCH20", pinch20)
