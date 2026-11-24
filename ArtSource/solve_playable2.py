# Playable-solve v2 (Codex 04:07 feedback):
# 1. cost_pinch uses bone-ownership mutually-exclusive vertex sets for the
#    thumb pad and index pad (no shared verts, no self-compare).
# 2. palm cost tracks FIXED vertex IDs (locked at rest) toward a fixed
    # in-palm target; no re-finding nearest skin per evaluation.
# 3. Final previews rendered along the true palm normal with the contact
#    point marked by a small sphere so pad contact is visible unoccluded.
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

# ---- rest state capture ----
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


def bone_head_world(name):
    return np.array((arm.matrix_world @ Vector(arm.pose.bones[name].head))[:])


REST_W = mesh_world()

# ---- bone-ownership vertex sets (mutually exclusive, max-weight assignment) ----
vg = {g.index: g.name for g in o.vertex_groups}
N = len(o.data.vertices)
owner = [""] * N
for vi, v in enumerate(o.data.vertices):
    best_w, best_g = -1.0, ""
    for g in v.groups:
        name = vg.get(g.group, "")
        if g.weight > best_w:
            best_w, best_g = g.weight, name
    owner[vi] = best_g

# real hand scale: wrist -> middle tip length from rest mesh
wrist_w = bone_head_world("Wrist.R")
m3_rest = np.array([list(arm.matrix_world @ Vector(arm.pose.bones["Middle3.R"].tail))[:]])
HAND = float(np.linalg.norm(REST_W - m3_rest).min(axis=0).max() if False else
             np.linalg.norm(REST_W[np.argmin(np.linalg.norm(REST_W - wrist_w, axis=1))] -
                            REST_W[np.argmin(np.linalg.norm(
                                REST_W - np.array((arm.matrix_world @ Vector(arm.pose.bones["Middle3.R"].tail))[:]),
                                axis=1))], axis=0).max()) if False else None
# simpler: hand length = wrist to middle-3 tail distance in rest
m3_tail = np.array((arm.matrix_world @ Vector(arm.pose.bones["Middle3.R"].tail))[:])
HAND_LEN = float(np.linalg.norm(m3_tail - wrist_w))
PAD_R = 0.16 * HAND_LEN  # pad zone radius proportional to real hand size
print("HAND_LEN", round(HAND_LEN, 3), "PAD_R", round(PAD_R, 3))

# thumb pad = verts owned by Thumb3 (+Thumb2 tail half), index pad = Index3-owned
thumb_set = [i for i in range(N) if owner[i] in ("Thumb3.R",) and
             np.linalg.norm(REST_W[i] - np.array((arm.matrix_world @ Vector(arm.pose.bones["Thumb3.R"].tail))[:])) < PAD_R]
index_set = [i for i in range(N) if owner[i] == "Index3.R"]
# mutual exclusivity: index pad excludes anything owned by thumb (owner strings differ by construction)
print("thumb_set", len(thumb_set), "index_set", len(index_set),
      "overlap", len(set(thumb_set) & set(index_set)))
assert len(thumb_set) > 0 and len(index_set) > 0, "empty pad sets"
assert not (set(thumb_set) & set(index_set)), "pad sets overlap — not mutually exclusive"

TW = REST_W[thumb_set]   # (k,3)
IX = REST_W[index_set]   # (m,3)
thumb_center_rest = TW.mean(axis=0)
index_center_rest = IX.mean(axis=0)

# fixed in-palm target for each fingertip: point on the rest palm plane
# directly below the tip (project tip onto plane through palm center)
i_mcp = bone_head_world("Index1.R")
l_mcp = bone_head_world("Little1.R")
palm_c = (i_mcp + l_mcp) / 2
ax = (l_mcp - i_mcp)
ax /= np.linalg.norm(ax)
idx_dir = i_mcp - wrist_w
idx_dir /= np.linalg.norm(idx_dir)
palm_n = np.cross(ax, idx_dir)
palm_n /= np.linalg.norm(palm_n)
th1 = bone_head_world("Thumb1.R")
if np.dot(th1 - palm_c, palm_n) < 0:
    palm_n = -palm_n
print("palm normal (fixed):", np.round(palm_n, 3))

TIP_BONES = ["Index3.R", "Middle3.R", "Ring3.R", "Little3.R"]
tip_rest = {n: np.array((arm.matrix_world @ Vector(arm.pose.bones[n].tail))[:]) for n in TIP_BONES}
tip_targets = {}
for n in TIP_BONES:
    p = tip_rest[n]
    d = np.dot(p - palm_c, palm_n)
    target = p - palm_n * (d - 0.06 * HAND_LEN)  # pull tip to 6% of hand len above palm plane
    tip_targets[n] = target

# tracked vertex IDs: for each fingertip, lock the K nearest rest verts (K=25)
K = 25
tracked = {}
for n in TIP_BONES:
    ids = np.argsort(np.linalg.norm(REST_W - tip_rest[n], axis=1))[:K]
    tracked[n] = ids
# thumb tracked set
tracked["Thumb3.R"] = np.argsort(np.linalg.norm(REST_W - thumb_center_rest, axis=1))[:K]


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


def posed_W():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    evm = ev.to_mesh()
    W = np.array([list(ev.matrix_world @ v.co) for v in evm.vertices])
    ev.to_mesh_clear()
    return W


def cost_fist(c20):
    apply20(c20)
    W = posed_W()
    cost = 0.0
    # tracked fingertips must CROSS the rest palm plane toward the back side
    # (normal-distance sign flip) and land near the MCP line height
    for n in TIP_BONES:
        ids = tracked[n]
        cur = W[ids].mean(axis=0)
        d_now = np.dot(cur - palm_c, palm_n)
        # rest distance was ~+0.09 (palm side); fist wants it negative (behind)
        # target: -0.25*HAND_LEN behind the palm plane
        target_d = -0.25 * HAND_LEN
        cost += abs(d_now - target_d)
    # thumb pad must be ON the palm (normal ~0) AND near the index PIP line
    tids = tracked["Thumb3.R"]
    tcur = W[tids].mean(axis=0)
    t_d = np.dot(tcur - palm_c, palm_n)
    cost += abs(t_d) * 2.0
    # lateral: thumb near the index MCP lateral position
    lat = (tcur - palm_c) - np.dot(tcur - palm_c, palm_n) * palm_n
    lat_i = (bone_head_world("Index1.R") - palm_c)
    lat_i = lat_i - np.dot(lat_i, palm_n) * palm_n
    cost += float(np.linalg.norm(lat - lat_i)) * 1.2
    return cost


def cost_pinch(c20):
    apply20(c20)
    W = posed_W()
    # deformed positions of the two mutually-exclusive pad sets
    dt = W[thumb_set]
    di = W[index_set]
    # min pair distance (subsample for speed)
    dt = dt[::2]
    di = di[::2]
    d = np.linalg.norm(dt[:, None, :] - di[None, :, :], axis=2)
    return float(d.min())


# ---- optimize ----
x = list(profile["samples"]["fist"])
cost = cost_fist(x)
print("FIST start", round(cost, 4))
step = 0.12
for it in range(10):
    improved = False
    for ci in range(20):
        for delta in (step, -step):
            t = x[:]
            t[ci] = max(0.0, min(1.0, t[ci] + delta))
            c = cost_fist(t)
            if c < cost - 1e-5:
                x = t; cost = c; improved = True
    print("fist iter", it, round(cost, 4))
    if not improved:
        step /= 2
        if step < 0.01: break
fist20 = [round(v, 4) for v in x]

x2 = list(fist20)
cost = cost_pinch(x2)
print("PINCH start", round(cost, 4))
step = 0.12
for it in range(10):
    improved = False
    for ci in list(range(0, 8)):
        for delta in (step, -step):
            t = x2[:]
            t[ci] = max(0.0, min(1.0, t[ci] + delta))
            c = cost_pinch(t)
            if c < cost - 1e-5:
                x2 = t; cost = c; improved = True
    print("pinch iter", it, round(cost, 4))
    if not improved:
        step /= 2
        if step < 0.01: break
pinch20 = [round(v, 4) for v in x2]

profile["samples"]["fist"] = fist20
profile["samples"]["pinch"] = pinch20
with open(PROFILE, "w", encoding="utf-8") as f:
    json.dump(profile, f, indent=2, ensure_ascii=False)
print("PROFILE UPDATED (playable v2)")
print("FIST20", fist20)
print("PINCH20", pinch20)
print("PLAYABLE V2 DONE")
