# Playable 20-control solve (00:06 resumption).
# Read-only on the canonical FBX (loads it, never saves mesh/blend).
# Solves fist/pinch with coordinate descent over the 20 normalized controls,
# evaluating through the exact profile channel mapping (same as Unity
# ProfileDelta bone-local euler semantics). All candidates stay in [0,1].
# Writes back ONLY the samples section of the profile JSON.
import bpy, os, math, json
import numpy as np
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

# fixed rest references
for pb in arm.pose.bones:
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()

deps0 = bpy.context.evaluated_depsgraph_get()


def mesh_world():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    evm = ev.to_mesh()
    W = np.array([list(ev.matrix_world @ v.co) for v in evm.vertices])
    ev.to_mesh_clear()
    return W


REST_W = mesh_world()

# geometric palm normal from the FBX skeleton (baked space), R side
iw = (np.array((arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].tail_local))[:])
      - np.array((arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))[:]))
lw = (np.array((arm.matrix_world @ Vector(arm.data.bones["LittleMeta.R"].tail_local))[:])
      - np.array((arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))[:]))
palm_n = np.cross(iw, lw)
palm_n /= np.linalg.norm(palm_n)
palm_c = np.array((arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].head_local))[:])
# palm-side sign: thumb CMC must be on +side
th1 = np.array((arm.matrix_world @ Vector(arm.data.bones["Thumb1.R"].head_local))[:])
if np.dot(th1 - palm_c, palm_n) < 0:
    palm_n = -palm_n
print("palm normal", np.round(palm_n, 3))

wrist_w = np.array((arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))[:])
HAND_LEN = float(np.linalg.norm(
    np.array((arm.matrix_world @ Vector(arm.data.bones["Middle3.R"].tail_local))[:]) - wrist_w))
print("hand len", round(HAND_LEN, 3))

# ---- tip tracking IDs (rest), per fingertip ----
TIP_BONES = ["Index3.R", "Middle3.R", "Ring3.R", "Little3.R"]
tip_rest = {n: np.array((arm.matrix_world @ Vector(arm.pose.bones[n].tail))[:]) for n in TIP_BONES}
tracked = {n: np.argsort(np.linalg.norm(REST_W - tip_rest[n], axis=1))[:25] for n in TIP_BONES}

# thumb pad set: verts owned by Thumb2/Thumb3 near thumb tip
t_tail = np.array((arm.matrix_world @ Vector(arm.pose.bones["Thumb3.R"].tail))[:])
vg = {g.index: g.name for g in o.vertex_groups}
thumb_set = []
index_set = []
for vi, v in enumerate(o.data.vertices):
    best_w, best_g = -1.0, ""
    for g in v.groups:
        nm = vg.get(g.group, "")
        if g.weight > best_w:
            best_w, best_g = g.weight, nm
    if best_g in ("Thumb3.R", "Thumb2.R") and np.linalg.norm(REST_W[vi] - t_tail) < 0.16 * HAND_LEN:
        thumb_set.append(vi)
    if best_g == "Index3.R":
        index_set.append(vi)
print("pad sets", len(thumb_set), len(index_set))

# in-palm targets: fingertip pulled to just above the palm plane, near MCP line
i_mcp = np.array((arm.matrix_world @ Vector(arm.data.bones["Index1.R"].head_local))[:])
l_mcp = np.array((arm.matrix_world @ Vector(arm.data.bones["Little1.R"].head_local))[:])
targets = {}
for n in TIP_BONES:
    p = tip_rest[n]
    d = np.dot(p - palm_c, palm_n)
    # fingertip should end 0.5-1.2cm above the palm plane, near MCP line
    lateral = (p - palm_c) - np.dot(p - palm_c, palm_n) * palm_n
    targets[n] = palm_c + lateral * 0.55 + palm_n * (0.012)


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
    bpy.context.view_layer.update()


def cost_fist(c20):
    apply20(c20)
    W = mesh_world()
    cost = 0.0
    for n in TIP_BONES:
        ids = tracked[n]
        cur = W[ids].mean(axis=0)
        cost += float(np.linalg.norm(cur - targets[n]))
    # thumb pad toward the index PIP region (above palm, palm-side)
    if thumb_set:
        tc = W[thumb_set].mean(axis=0)
        cost += float(np.linalg.norm(tc - targets["Index3.R"])) * 1.2
    return cost


def gap_pinch(c20):
    apply20(c20)
    W = mesh_world()
    T = W[thumb_set]
    I = W[index_set]
    d = np.linalg.norm(T[:, None, :] - I[None, :, :], axis=2)
    return float(d.min())


# ---- fist solve ----
x = [0.0] * 20  # start from open
cost = cost_fist(x)
print("FIST start", round(cost, 4))
step = 0.15
for it in range(12):
    improved = False
    for ci in range(20):
        if ci in (3, 7, 11, 15, 19):  # twist/spread centered channels
            candidates = (0.5,)
        else:
            candidates = (max(0.0, x[ci] - step), min(1.0, x[ci] + step), 0.0, 1.0)
        for v in candidates:
            t = x[:]
            t[ci] = v
            c = cost_fist(t)
            if c < cost - 1e-5:
                x = t
                cost = c
                improved = True
    print("fist iter", it, round(cost, 4))
    if not improved:
        step /= 2
        if step < 0.01:
            break
fist20 = [round(v, 4) for v in x]

# ---- pinch solve (start from fist: relaxed by opening index slightly) ----
x2 = list(fist20)
x2[4] = 0.55
x2[5] = 0.85
x2[6] = 0.5
x2[3] = 0.0   # full opposition toward index
x2[0] = 0.35
cost = gap_pinch(x2)
print("PINCH start", round(cost, 5))
step = 0.15
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

profile["samples"]["fist"] = fist20
profile["samples"]["pinch"] = pinch20
with open(PROFILE, "w", encoding="utf-8") as f:
    json.dump(profile, f, indent=2, ensure_ascii=False)
print("PROFILE SAMPLES UPDATED")
print("FIST20", fist20)
print("PINCH20", pinch20)
print("PLAYABLE SOLVE DONE")
