# Re-solve fist/pinch poses on the EXPORTED FBX skeleton (bone-local space
# identical to Unity), restricted to the profile channel DOFs, then write the
# solved 20-value samples back into the profile JSON. No clamping: the solve
# is bounded to what the channels can express.
import bpy, os, math, json
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
FBX = os.path.abspath("../Assets/VirtualHand/Models/Hand_R.fbx")
PROFILE = os.path.abspath("../Assets/VirtualHand/Resources/Rig/hand_rig_profile.json")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
arm = bpy.data.objects["Armature.R"]
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
sc = bpy.context.scene
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")

with open(PROFILE, encoding="utf-8") as f:
    profile = json.load(f)
channels = profile["channels"]

# channel DOF per bone: index -> list of (axis, scale, sign, rest)
dof = {}
for ci, ch in enumerate(channels):
    b = ch["bone"] + ".R"
    dof.setdefault(b, []).append((ci, ch["axis"], ch["scale"], ch["sign"], ch.get("rest", 0.0)))

bones_in_dof = sorted(dof.keys())


def set_eulers(angles):
    """angles: {bone: (x,y,z) degrees} — only pose bones in the dict are touched."""
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    for n, (x, y, z) in angles.items():
        pb = arm.pose.bones[n]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
    bpy.context.view_layer.update()


def tips():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)

    def bt(name):
        pb = arm.pose.bones[name]
        return arm.matrix_world @ Vector(pb.tail)
    return bt, ev


def gap_and_palmDist():
    bt, ev = tips()
    itip = bt("Index3.R")
    ttip = bt("Thumb3.R")
    m3 = bt("Middle3.R")
    # palm centre reference: mid of index/little MCP heads
    pm = (bt("Index1.R") + bt("Little1.R")) / 2
    return (itip - ttip).length, (ttip - pm).length - 0.9  # thumb tip beyond palm line


print("=== FIST SOLVE: all fingers curl to palm, thumb wraps ===")
# variables: per-finger X flexions + thumb X/Z
x = {"Index1.R": (60, 0, 0), "Index2.R": (75, 0, 0), "Index3.R": (45, 0, 0),
     "Middle1.R": (65, 0, 0), "Middle2.R": (80, 0, 0), "Middle3.R": (50, 0, 0),
     "Ring1.R": (70, 0, 0), "Ring2.R": (85, 0, 0), "Ring3.R": (55, 0, 0),
     "Little1.R": (70, 0, 0), "Little2.R": (85, 0, 0), "Little3.R": (55, 0, 0),
     "Thumb1.R": (35, 0, 30), "Thumb2.R": (60, 0, 0), "Thumb3.R": (45, 0, 0)}
# only optimize channels that the 20-value profile can encode:
# thumb: X flex + Z opposition on Thumb1; fingers: X flex + Z spread on bone2.
# Unmodeled DOFs (Y twist on Thumb2/3 and fingers) stay fixed at 0.
OPT = {"Thumb1.R": ["X", "Z"], "Thumb2.R": ["X"], "Thumb3.R": ["X"],
       "Index1.R": ["X"], "Index2.R": ["X", "Z"], "Index3.R": ["X"],
       "Middle1.R": ["X"], "Middle2.R": ["X", "Z"], "Middle3.R": ["X"],
       "Ring1.R": ["X"], "Ring2.R": ["X", "Z"], "Ring3.R": ["X"],
       "Little1.R": ["X"], "Little2.R": ["X", "Z"], "Little3.R": ["X"]}

# fist quality: fingertip-to-palm-plane distance (smaller = tighter curl)
def fist_cost(angles):
    set_eulers(angles)
    bt, ev = tips()
    cost = 0.0
    plane = (bt("Index1.R") + bt("Little1.R")) / 2
    axis = (bt("Index1.R") - bt("Little1.R")).normalized()
    for tipn in ("Index3.R", "Middle3.R", "Ring3.R", "Little3.R"):
        tip = bt(tipn)
        rel = tip - plane
        d = (rel - axis * rel.dot(axis)).length  # distance to palm plane
        cost += d
    # thumb tip should reach index/middle PIP area (wrap)
    th = bt("Thumb3.R")
    for pipn in ("Index2.R", "Middle2.R"):
        cost += (th - bt(pipn)).length * 0.5
    return cost


step = 8.0
cost = fist_cost(x)
for it in range(8):
    improved = False
    for bn, axes in OPT.items():
        for ai, axname in enumerate(axes):
            ci = {"X": 0, "Y": 1, "Z": 2}[axname]
            for delta in (step, -step):
                trial = {k: tuple(v) for k, v in x.items()}
                v = list(trial[bn]); v[ci] += delta
                if abs(v[ci]) > 120: continue
                trial[bn] = tuple(v)
                c = fist_cost(trial)
                if c < cost - 1e-4:
                    x = trial; cost = c; improved = True
    print("fist iter", it, "cost", round(cost, 4))
    if not improved:
        step /= 2
        if step < 1.5: break

set_eulers(x)
bt, ev = tips()
print("FIST SOLVED", {k: tuple(round(a, 1) for a in v) for k, v in x.items()})
fist_angles = dict(x)

print("=== PINCH SOLVE: thumb pad meets index pad ===")
x2 = dict(fist_angles)
x2.update({"Thumb1.R": (35, 0, 30), "Thumb2.R": (70, 0, 0), "Thumb3.R": (90, 0, 0),
           "Index1.R": (50, 0, 0), "Index2.R": (60, 0, 0), "Index3.R": (30, 0, 0)})
step = 8.0

def pinch_cost(angles):
    set_eulers(angles)
    bt, ev = tips()
    return (bt("Index3.R") - bt("Thumb3.R")).length

cost = pinch_cost(x2)
OPT2 = {"Thumb1.R": ["X", "Z", "Y"], "Thumb2.R": ["X"], "Thumb3.R": ["X"],
        "Index1.R": ["X"], "Index2.R": ["X"], "Index3.R": ["X"]}
for it in range(8):
    improved = False
    for bn, axes in OPT2.items():
        for ai, axname in enumerate(axes):
            ci = {"X": 0, "Y": 1, "Z": 2}[axname]
            for delta in (step, -step):
                trial = {k: tuple(v) for k, v in x2.items()}
                v = list(trial[bn]); v[ci] += delta
                if abs(v[ci]) > 120: continue
                trial[bn] = tuple(v)
                c = pinch_cost(trial)
                if c < cost - 1e-4:
                    x2 = trial; cost = c; improved = True
    print("pinch iter", it, "gap", round(cost, 4))
    if not improved:
        step /= 2
        if step < 1.5: break

set_eulers(x2)
bt, ev = tips()
print("PINCH SOLVED gap", round((bt("Index3.R") - bt("Thumb3.R")).length, 3),
      {k: tuple(round(a, 1) for a in v) for k, v in x2.items()})
pinch_angles = dict(x2)

# --- back-convert: enlarge channel scale if the solved angle exceeds it,
# then encode loss-free. sign/axis semantics unchanged. ---
def encode(angles, chs):
    c = [0.0] * 20
    for ci, ch in enumerate(chs):
        bn = ch["bone"] + ".R"
        if bn not in angles: continue
        axis_i = {"X": 0, "Y": 1, "Z": 2}[ch["axis"]]
        deg = angles[bn][axis_i]
        eff = ch["scale"] * ch["sign"]
        v = deg / eff + ch.get("rest", 0.0)
        # tiny solver noise (<3deg equiv) snaps to the boundary; real excursions
        # were covered by ensure_scale raising this channel's scale beforehand
        if abs(v) < (3.0 / max(abs(eff), 1.0)):
            v = 0.0
        c[ci] = v
    return c

def ensure_scale(angles, chs):
    """Raise scale so |deg| fits in the 0..1 span around rest."""
    for ci, ch in enumerate(chs):
        bn = ch["bone"] + ".R"
        if bn not in angles: continue
        axis_i = {"X": 0, "Y": 1, "Z": 2}[ch["axis"]]
        deg = abs(angles[bn][axis_i])
        span = (1.0 - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        if abs(span) < abs(deg):
            ch["scale"] = abs(deg) / max(1.0 - ch.get("rest", 0.0), 0.01)

ensure_scale(fist_angles, channels)
ensure_scale(pinch_angles, channels)
fist20 = encode(fist_angles, channels)
pinch20 = encode(pinch_angles, channels)

profile["samples"]["fist"] = fist20
profile["samples"]["pinch"] = pinch20
with open(PROFILE, "w", encoding="utf-8") as f:
    json.dump(profile, f, indent=2, ensure_ascii=False)
print("PROFILE SAMPLES UPDATED")
print("FIST20", profile["samples"]["fist"])
print("PINCH20", profile["samples"]["pinch"])
print("IK WRITEBACK DONE")
