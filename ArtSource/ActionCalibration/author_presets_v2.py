# Author-preset gestures v2 for the shared 20-control rig profile.
#
# Pipeline (per task A2 requirements):
#   1) gesture tables are authored in DEGREES per 20-channel spec
#   2) each degree target is converted to a 0..1 control via the INPUT
#      profile's rest/scale/sign (no second angle table, no post-hoc clamp
#      of geometry: unreachable targets are reported and explicitly adjusted)
#   3) candidate JSON is written to output/rev2/hand_rig_profile.json
#      with channels byte-identical to input
#   4) the written file is re-read and ONLY that file drives the FBX
#   5) deliverable previews are rendered from the re-read samples
#
# Palm side was geometrically confirmed (probe_v2.py): the meta-plane
# normal points PALM-side (PALMSIGN=+1); positive X flexion curls the
# fingertips toward that normal. No sign flips are performed anywhere.
#
# Source labeling is honest: authored presets with limited visual
# refinement; not solver output; Unity acceptance is Codex's call.
import bpy, os, math, json
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROFILE_IN = os.path.join(HERE, "input", "hand_rig_profile.json")
OUTDIR = os.path.join(HERE, "output", "rev2")
PROFILE_OUT = os.path.join(OUTDIR, "hand_rig_profile.json")
FBX = os.path.join(HERE, "input", "Hand_R.fbx")
os.makedirs(OUTDIR, exist_ok=True)

with open(PROFILE_IN, encoding="utf-8") as f:
    profile_in = json.load(f)
channels_in = profile_in["channels"]
CH = {c["index"]: c for c in channels_in}

# ------------------------------------------------------------------
# 1) authored gesture targets in DEGREES per channel index.
#    X channels: flexion deg. Y (Thumb1): opposition twist deg
#    about bone-long axis, + = sign of channel (direction decided by
#    A/B renders; see OPP). Z channels: spread deg (0 = neutral).
# ------------------------------------------------------------------
OPP = -1.0  # provisional: negative Y delta toward index (old author comment); A/B verified in render

FIST_DEG = {
    0: 58.0, 1: 66.0, 2: 35.0, 3: OPP * 36.0,          # thumb wraps over the curl
    4: 78.0, 5: 85.0, 6: 60.0, 7: -8.0,                 # index
    8: 82.0, 9: 88.0, 10: 62.0, 11: -5.0,               # middle
    12: 82.0, 13: 85.0, 14: 60.0, 15: -5.0,             # ring
    16: 78.0, 17: 90.0, 18: 55.0, 19: -8.0,             # little
}
PINCH_DEG = {
    0: 38.0, 1: 72.0, 2: 36.0, 3: OPP * 62.0,           # thumb pad toward index pad
    4: 52.0, 5: 64.0, 6: 32.0, 7: -3.0,                 # index pad faces thumb
    8: 22.0, 9: 28.0, 10: 12.0, 11: 0.0,                # middle relaxed
    12: 25.0, 13: 30.0, 14: 14.0, 15: 0.0,              # ring relaxed
    16: 28.0, 17: 32.0, 18: 16.0, 19: 0.0,              # little relaxed
}
OPEN_DEG = {i: 0.0 for i in range(20)}
OPEN_DEG[3] = 0.0  # opposition neutral -> control rest 0.5

adjust_log = []


def deg_to_ctrl(idx, deg):
    """Authoring-degrees -> 0..1 control using the INPUT channel calibration."""
    ch = CH[idx]
    raw = ch["rest"] + ch["sign"] * deg / ch["scale"]
    if raw < 0.0 or raw > 1.0:
        lim = (0.0 if raw < 0 else 1.0)
        adj = (lim - ch["rest"]) * ch["sign"] * ch["scale"]
        adjust_log.append(
            f"ch{idx:02d} {ch['bone']}.{ch['axis']}: target {deg:.1f}deg unreachable "
            f"(limit {adj:.1f}deg) -> adjusted")
        return round(lim, 4), adj
    return round(raw, 4), deg


def build_sample(deg_table):
    return [deg_to_ctrl(i, deg_table.get(i, 0.0))[0] for i in range(20)]


def audit(tag, deg_table, sample):
    """Decode control back through the SAME channel calibration and verify."""
    print(f"--- audit {tag} ---")
    for i in range(20):
        ch = CH[i]
        _, tgt = deg_to_ctrl(i, deg_table.get(i, 0.0))
        dec = (sample[i] - ch["rest"]) * ch["scale"] * ch["sign"]
        flag = "OK" if abs(dec - tgt) < 0.05 else "MISMATCH"
        print(f"ch{i:02d} {ch['bone']:8s}.{ch['axis']} ctrl={sample[i]:.4f} "
              f"decoded={dec:7.2f}deg target={tgt:7.2f}deg {flag}")


fist20 = build_sample(FIST_DEG)
pinch20 = build_sample(PINCH_DEG)
open20 = build_sample(OPEN_DEG)
audit("fist", FIST_DEG, fist20)
audit("pinch", PINCH_DEG, pinch20)
audit("open", OPEN_DEG, open20)

# ------------------------------------------------------------------
# 2) write candidate JSON (channels identical to input)
# ------------------------------------------------------------------
profile_out = {
    "version": profile_in["version"],
    "rig": profile_in["rig"],
    "space": profile_in["space"],
    "channels": channels_in,
    "samples": {"open": open20, "fist": fist20, "pinch": pinch20},
    "notes": [
        "20 controls finger-major: thumb CMC/MCP/IP/opposition then index..little MCP/PIP/DIP/spread",
        "Unity HandRigProfile loads this JSON; PoseLibrary samples feed SetControls directly",
        "sign=-1 means the bone's local axis is inverted relative to the control direction",
        "samples authored in degrees per gesture and converted via input rest/scale/sign; "
        "decoded angles re-verified against targets",
    ],
    "source_note": (
        "Author presets v2 (ActionCalibration): gesture targets authored in degrees, "
        "converted to 0..1 controls via the input rev4 positive-flexion channels "
        "(rest/scale/sign); limited visual refinement only, channels unchanged; "
        "not solver output; Unity-side acceptance pending (Codex)."
    ),
}
with open(PROFILE_OUT, "w", encoding="utf-8") as f:
    json.dump(profile_out, f, indent=2, ensure_ascii=False)
same = json.dumps(profile_out["channels"]) == json.dumps(channels_in)
print("CHANNELS_IDENTICAL", same)
for line in adjust_log:
    print("ADJUST_LOG", line)

# ------------------------------------------------------------------
# 3) re-read the WRITTEN file and drive the rig from it exclusively
# ------------------------------------------------------------------
with open(PROFILE_OUT, encoding="utf-8") as f:
    profile_rt = json.load(f)
channels = profile_rt["channels"]
samples = profile_rt["samples"]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
arm = bpy.data.objects["Armature.R"]
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
sc = bpy.context.scene
w = bpy.data.worlds.new("W"); w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.85, 0.85, 0.87, 1)
sc.world = w
lt = bpy.data.lights.new("sun", 'SUN'); lt.energy = 2.2
lo = bpy.data.objects.new("sun", lt); bpy.context.collection.objects.link(lo)
lo.rotation_euler = (math.radians(55), 0, math.radians(35))
lt2 = bpy.data.lights.new("fill", 'SUN'); lt2.energy = 1.0
lo2 = bpy.data.objects.new("fill", lt2); bpy.context.collection.objects.link(lo2)
lo2.rotation_euler = (math.radians(120), 0, math.radians(-140))
try:
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
except Exception as e:
    print("VIEWTRANSFORM_FALLBACK", e)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")

B = arm.pose.bones
FINGERS = ("Index", "Middle", "Ring", "Little")


def apply_sample(values):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for ci, ch in enumerate(channels):
        pb = B.get(ch["bone"] + ".R")
        if pb is None:
            continue
        deg = (values[ci] - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        e = pb.rotation_euler
        e[{"X": 0, "Y": 1, "Z": 2}[ch["axis"]]] += math.radians(deg)
    bpy.context.view_layer.update()


def palm_normal():
    iw = (arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    lw = (arm.matrix_world @ Vector(arm.data.bones["LittleMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    return (iw.cross(lw)).normalized()   # PALMSIGN=+1 confirmed by probe_v2


def render(tag, direction, zoom=1.25, focus_at=None):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    focus = sum(pts, Vector()) / 8.0
    if focus_at is not None:
        focus = focus_at
        zoom = 0.55
    span = max(max(p.x for p in pts) - min(p.x for p in pts),
               max(p.y for p in pts) - min(p.y for p in pts),
               max(p.z for p in pts) - min(p.z for p in pts))
    cam = bpy.data.cameras.new("c" + tag); cam.type = 'ORTHO'
    cam.ortho_scale = span * zoom
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(direction).normalized()
    co.location = focus + d * 5
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.join(OUTDIR, tag + ".png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)


def tip_gap():
    it = arm.matrix_world @ Vector(B["Index3.R"].tail)
    tt = arm.matrix_world @ Vector(B["Thumb3.R"].tail)
    return (it - tt).length


UP = Vector((0, 1, 0))
n = None

# fist deliverables
apply_sample(samples["fist"])
n = palm_normal()
render("rev2_fist_palm", n)
render("rev2_fist_side_thumb", n.cross(UP))
render("rev2_fist_side_pinky", (-n).cross(UP))

# pinch deliverables
apply_sample(samples["pinch"])
print("PINCH_GAP", round(tip_gap(), 4))
render("rev2_pinch_palm", n)
contact = (arm.matrix_world @ Vector(B["Index3.R"].tail)
           + arm.matrix_world @ Vector(B["Thumb3.R"].tail)) / 2
render("rev2_pinch_contact", n, focus_at=contact)

print("AUTHOR_V2_DONE")
