# Playable solve v4 — on the rev4 canonical FBX (B's refined mesh, frozen rig).
# Fixes vs solve_playable3 (Codex 06:40 feedback):
#   * starts from the REAL profile open sample (twist/spread at 0.5, flex at 0)
#   * twist/spread channels stay pinned at rest during the fist solve;
#     only thumb opposition is optimized over its full 0..1 range for pinch
#   * runs on ArtSource/Refinement/output/rev4/refined_hands.blend if present,
#     else falls back to canonical Models FBX. Read-only on the source file.
import bpy, os, math, json
import numpy as np
from mathutils import Vector
COST_DEBUG = [0]

HERE = os.path.dirname(os.path.abspath(__file__))
PROFILE = os.path.abspath("../Assets/VirtualHand/Resources/Rig/hand_rig_profile.json")

REV4_BLEND = os.path.abspath("../ArtSource/Refinement/output/rev4/refined_hands.blend")
FBX = os.path.abspath("../Assets/VirtualHand/Models/Hand_R.fbx")

# rev4 refined_hands.blend has no vertex groups / different scale — the
# canonical FBX (used by run19) is the solvable source. Read-only either way.
source = FBX
print("SOURCE:", os.path.basename(source))

if source.endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=source)
    arm = bpy.data.objects.get("Armature.R") or bpy.data.objects.get("Armature")
    o = next((ob for ob in bpy.data.objects if ob.type == 'MESH'), None)
else:
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
samples = profile["samples"]

# ---- fixed rest references ----
for pb in arm.pose.bones:
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()


def mesh_world():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    evm = ev.to_mesh()
    W = np.array([list(ev.matrix_world @ v.co) for v in evm.vertices])
    ev.to_mesh_clear()
    return W


REST_W = mesh_world()

iw = (np.array((arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].tail_local))[:])
      - np.array((arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))[:]))
lw = (np.array((arm.matrix_world @ Vector(arm.data.bones["LittleMeta.R"].tail_local))[:])
      - np.array((arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))[:]))
palm_n = np.cross(iw, lw)
palm_n /= np.linalg.norm(palm_n)
palm_c = np.array((arm.matrix_world @ Vector(arm.data.bones["Index1.R"].head_local))[:])
th1 = np.array((arm.matrix_world @ Vector(arm.data.bones["Thumb1.R"].head_local))[:])
if np.dot(th1 - palm_c, palm_n) < 0:
    palm_n = -palm_n
wrist_w = np.array((arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))[:])
HAND_LEN = float(np.linalg.norm(
    np.array((arm.matrix_world @ Vector(arm.data.bones["Middle3.R"].tail_local))[:]) - wrist_w))
print("palm normal", np.round(palm_n, 3), "hand len", round(HAND_LEN, 3))
i_mcp = np.array((arm.matrix_world @ Vector(arm.data.bones["Index1.R"].head_local))[:])

# pad sets: bone-ownership, mutually exclusive, radius by real hand scale
vg = {g.index: g.name for g in o.vertex_groups}
t_tail = np.array((arm.matrix_world @ Vector(arm.pose.bones["Thumb3.R"].tail))[:])
thumb_set, index_set = [], []
for vi, v in enumerate(o.data.vertices):
    best_w, best_g = -1.0, ""
    for g in v.groups:
        nm = vg.get(g.group, "")
        if g.weight > best_w:
            best_w, best_g = g.weight, nm
    if best_g in ("Thumb3.R", "Thumb2.R") and \
            np.linalg.norm(REST_W[vi] - t_tail) < 0.16 * HAND_LEN:
        thumb_set.append(vi)
    if best_g == "Index3.R":
        index_set.append(vi)
print("pad sets", len(thumb_set), len(index_set))

TIP_BONES = ["Index3.R", "Middle3.R", "Ring3.R", "Little3.R"]
tip_rest = {n: np.array((arm.matrix_world @ Vector(arm.pose.bones[n].tail))[:]) for n in TIP_BONES}
tracked = {n: np.argsort(np.linalg.norm(REST_W - tip_rest[n], axis=1))[:25] for n in TIP_BONES}
THUMB_TRACKED = np.argsort(np.linalg.norm(REST_W - t_tail, axis=1))[:25]

# in-palm targets (fixed): pull each fingertip toward the palm plane, near the MCP line,
# with a slight stagger so fingers don't collide
targets = {}
stagger = [0.012, 0.004, -0.004, -0.012]
for k, n in enumerate(TIP_BONES):
    p = tip_rest[n]
    lat = (p - palm_c) - np.dot(p - palm_c, palm_n) * palm_n
    targets[n] = palm_c + lat * 0.5 + palm_n * (0.010 + stagger[k])
# thumb target: near index PIP, on the palm side
i_pip = np.array((arm.matrix_world @ Vector(arm.data.bones["Index2.R"].head_local))[:])
thumb_target = i_pip + palm_n * 0.02 * HAND_LEN

# channel bookkeeping
CENTERED = {ci for ci, ch in enumerate(channels) if abs(ch.get("rest", 0.0) - 0.5) < 0.01}
FLEX = [ci for ci in range(20) if ci not in CENTERED]


def apply20(c20):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for ci, ch in enumerate(channels):
        pb = arm.pose.bones.get(ch["bone"] + ".R")
        if pb is None:
            continue
        deg = (c20[ci] - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        e = pb.rotation_euler
        comp = {"X": 0, "Y": 1, "Z": 2}[ch["axis"]]
        e[comp] += math.radians(deg)


def posed_W():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    evm = ev.to_mesh()
    W = np.array([list(ev.matrix_world @ v.co) for v in evm.vertices])
    ev.to_mesh_clear()
    return W


def cost_fist(c20):
    """Fist: tracked fingertip pads land ON the palm surface (normal dist ~0)
    near their own MCP line (halfway to the palm centre). Thumb pad lands on
    the palm near the index PIP. All references are fixed rest geometry."""
    apply20(c20)
    W = posed_W()
    cost = 0.0
    for n in TIP_BONES:
        ids = tracked[n]
        cur = W[ids].mean(axis=0)
        d = float(np.dot(cur - palm_c, palm_n))
        cost += abs(d) / HAND_LEN
        lat_now = (cur - palm_c) - np.dot(cur - palm_c, palm_n) * palm_n
        lat_rest = (tip_rest[n] - palm_c) - np.dot(tip_rest[n] - palm_c, palm_n) * palm_n
        lat_target = lat_rest * 0.45
        cost += float(np.linalg.norm(lat_now - lat_target)) / HAND_LEN
    tc = W[THUMB_TRACKED].mean(axis=0)
    d_t = float(np.dot(tc - palm_c, palm_n))
    cost += abs(d_t) / HAND_LEN * 1.5
    lat_t = (tc - palm_c) - np.dot(tc - palm_c, palm_n) * palm_n
    lat_i = (i_mcp - palm_c) - np.dot(i_mcp - palm_c, palm_n) * palm_n
    cost += float(np.linalg.norm(lat_t - lat_i)) / HAND_LEN
    return cost


def gap_pinch(c20):
    apply20(c20)
    W = posed_W()
    T = W[thumb_set]
    I = W[index_set]
    d = np.linalg.norm(T[:, None, :] - I[None, :, :], axis=2)
    return float(d.min())


# ---- fist solve: stage 1 fingers only, stage 2 thumb wrap ----
def cost_thumb_only(c20):
    """Stage 2: thumb wrap cost alone."""
    apply20(c20)
    W = posed_W()
    tc = W[THUMB_TRACKED].mean(axis=0)
    d_t = float(np.dot(tc - palm_c, palm_n))
    cost = abs(d_t) / HAND_LEN * 1.5
    lat_t = (tc - palm_c) - np.dot(tc - palm_c, palm_n) * palm_n
    lat_i = (i_mcp - palm_c) - np.dot(i_mcp - palm_c, palm_n) * palm_n
    cost += float(np.linalg.norm(lat_t - lat_i)) / HAND_LEN
    return cost


x = list(samples["open"])
for ci in CENTERED:
    x[ci] = 0.5
FINGER_FLEX = [ci for ci in FLEX if ci < 4 or ci >= 4]
cost = cost_fist(x)
print("FIST start", round(cost, 4))

# DEBUG: scan Index1 X over range, print cost
print("=== INDEX1 X SCAN ===")
for deg20 in (0.0, 0.25, 0.5, 0.75, 1.0):
    t = list(x)
    t[4] = deg20
    print("SCAN c4=", deg20, "cost=", round(cost_fist(t), 4))
print("=== SCAN END ===")

step = 0.15
for it in range(12):
    improved = False
    for ci in FLEX:
        for delta in (step, -step):
            t = x[:]
            t[ci] = max(0.0, min(1.0, t[ci] + delta))
            c = cost_fist(t)
            if c < cost - 1e-6:
                x = t
                cost = c
                improved = True
    print("fist iter", it, round(cost, 4))
    if not improved:
        step /= 2
        if step < 0.005:
            break
fist20 = [round(v, 4) for v in x]

# stage 2: thumb wrap (fingers stay at their fist pose)
x_t = list(fist20)
cost_t = cost_thumb_only(x_t)
print("THUMB start", round(cost_t, 4))
step = 0.15
for it in range(10):
    improved = False
    for ci in (0, 1, 2, 3):
        for delta in (step, -step):
            t = x_t[:]
            t[ci] = max(0.0, min(1.0, t[ci] + delta))
            c = cost_thumb_only(t)
            if c < cost_t - 1e-6:
                x_t = t
                cost_t = c
                improved = True
    print("thumb iter", it, round(cost_t, 4))
    if not improved:
        step /= 2
        if step < 0.005:
            break
fist20 = [round(v, 4) for v in x_t]

# ---- pinch solve: full opposition range for the thumb, index meets it ----
x2 = list(fist20)
x2[3] = 0.0    # full opposition toward index
x2[0] = 0.35   # thumb CMC flex
x2[4] = 0.55   # index MCP relaxed
x2[5] = 0.85
x2[6] = 0.5
cost = gap_pinch(x2)
print("PINCH start", round(cost, 5))
step = 0.15
PINCH_VAR = [ci for ci in range(20) if ci not in CENTERED or ci == 3]
for it in range(12):
    improved = False
    for ci in (0, 1, 2, 3, 4, 5, 6):
        for delta in (step, -step):
            t = x2[:]
            t[ci] = max(0.0, min(1.0, t[ci] + delta))
            c = gap_pinch(t)
            if c < cost - 1e-5:
                x2 = t
                cost = c
                improved = True
    print("pinch iter", it, round(cost, 5))
    if not improved:
        step /= 2
        if step < 0.01:
            break
pinch20 = [round(v, 4) for v in x2]

samples["fist"] = fist20
samples["pinch"] = pinch20
with open(PROFILE, "w", encoding="utf-8") as f:
    json.dump(profile, f, indent=2, ensure_ascii=False)
print("PROFILE SAMPLES UPDATED (playable v4)")
print("FIST20", fist20)
print("PINCH20", pinch20)
print("PLAYABLE V4 DONE")
