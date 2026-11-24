# ActionCalibration solver — runs on the input snapshot FBX (read-only).
# Positive-flexion convention (sign=+1): control 1.0 = full calibrated palmward curl.
# Start from the real open sample; twist/spread pinned at 0.5 during fist;
# thumb opposition optimized over full 0..1 during pinch. All candidates [0,1].
import bpy, os, math, json
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
FBX = os.path.join(HERE, "input", "Hand_R.fbx")
PROFILE_IN = os.path.join(HERE, "input", "hand_rig_profile.json")
PROFILE_OUT = os.path.join(HERE, "output", "hand_rig_profile.json")
os.makedirs(os.path.dirname(PROFILE_OUT), exist_ok=True)

with open(PROFILE_IN, encoding="utf-8") as f:
    profile = json.load(f)
channels = profile["channels"]
samples = profile["samples"]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
arm = bpy.data.objects["Armature.R"]
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH')
sc = bpy.context.scene
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")


def mesh_world():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    evm = ev.to_mesh()
    W = np.array([list(ev.matrix_world @ v.co) for v in evm.vertices])
    ev.to_mesh_clear()
    return W


def bone_head_world(name):
    return np.array((arm.matrix_world @ Vector(arm.pose.bones[name].head))[:])


# ---- fixed rest references ----
for pb in arm.pose.bones:
    pb.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()
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

# ---- pad sets (bone-ownership, mutually exclusive) ----
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

# ---- tip tracking IDs + fixed in-palm targets ----
TIP_BONES = ["Index3.R", "Middle3.R", "Ring3.R", "Little3.R"]
tip_rest = {n: np.array((arm.matrix_world @ Vector(arm.pose.bones[n].tail))[:]) for n in TIP_BONES}
tracked = {n: np.argsort(np.linalg.norm(REST_W - tip_rest[n], axis=1))[:25] for n in TIP_BONES}
THUMB_TRACKED = np.argsort(np.linalg.norm(REST_W - t_tail, axis=1))[:25]

i_mcp = np.array((arm.matrix_world @ Vector(arm.data.bones["Index1.R"].head_local))[:])
targets = {}
stagger = [0.012, 0.004, -0.004, -0.012]
for k, n in enumerate(TIP_BONES):
    p = tip_rest[n]
    lat = (p - palm_c) - np.dot(p - palm_c, palm_n) * palm_n
    # target: pull 55% toward the palm centre, just above the palm plane
    targets[n] = palm_c + lat * 0.45 + palm_n * (0.012 + stagger[k])
i_pip = np.array((arm.matrix_world @ Vector(arm.data.bones["Index2.R"].head_local))[:])
thumb_target = i_pip + palm_n * 0.02 * HAND_LEN


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
    apply20(c20)
    W = posed_W()
    cost = 0.0
    for n in TIP_BONES:
        ids = tracked[n]
        cur = W[ids].mean(axis=0)
        cost += float(np.linalg.norm(cur - targets[n])) / HAND_LEN
    if thumb_set:
        tc = W[thumb_set].mean(axis=0)
        cost += float(np.linalg.norm(tc - thumb_target)) / HAND_LEN * 1.5
    return cost


def gap_pinch(c20):
    apply20(c20)
    W = posed_world = posed_W()
    T = W[thumb_set]
    I = W[index_set]
    return float(np.linalg.norm(T[:, None, :] - I[None, :, :], axis=2).min())


def render_tagged(tag, direction, dist=1.2):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    focus = sum(pts, Vector()) / 8.0
    span = max(max(p.x for p in pts) - min(p.x for p in pts),
               max(p.y for p in pts) - min(p.y for p in pts),
               max(p.z for p in pts) - min(p.z for p in pts))
    cam = bpy.data.cameras.new("c" + tag); cam.type = 'ORTHO'
    cam.ortho_scale = span * 1.25
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(direction).normalized()
    co.location = focus + d * dist * 4
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    out = os.path.join(HERE, "output", f"preview_{tag}.png")
    sc.render.filepath = out
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)


# ================= fist solve (staged: MCP first, then PIP/DIP) =================
x = list(samples["open"])  # real open: flex 0, twist/spread 0.5
cost = cost_fist(x)
print("FIST start", round(cost, 4))
# stage A: only MCP channels (4, 8, 12, 16) + thumb
MCP = [0, 1, 4, 8, 12, 16]
step = 0.15
for it in range(10):
    improved = False
    for ci in MCP:
        for delta in (step, -step):
            t = x[:]
            t[ci] = max(0.0, min(1.0, t[ci] + delta))
            c = cost_fist(t)
            if c < cost - 1e-5:
                x = t
                cost = c
                improved = True
    print("fist MCP iter", it, round(cost, 4))
    if not improved:
        step /= 2
        if step < 0.01:
            break
# stage B: PIP/DIP channels
PIP = [2, 5, 6, 9, 10, 13, 14, 17, 18]
step = 0.15
for it in range(10):
    improved = False
    for ci in PIP:
        for delta in (step, -step):
            t = x[:]
            t[ci] = max(0.0, min(1.0, t[ci] + delta))
            c = cost_fist(t)
            if c < cost - 1e-5:
                x = t
                cost = c
                improved = True
    print("fist PIP iter", it, round(cost, 4))
    if not improved:
        step /= 2
        if step < 0.01:
            break
fist20 = [round(v, 4) for v in x]

# ================= pinch solve =================
x2 = list(fist20)
x2[3] = 0.0    # full opposition
x2[0] = 0.35   # thumb CMC
x2[4] = 0.55   # index MCP relaxed
x2[5] = 0.85
x2[6] = 0.5
cost = gap_pinch(x2)
print("PINCH start", round(cost, 5))
step = 0.15
for it in range(12):
    improved = False
    for ci in (0, 1, 2, 4, 5, 6, 3):
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

# ---- write output profile: input channels + solved samples ----
profile["samples"]["fist"] = fist20
profile["samples"]["pinch"] = pinch20
profile["source_note"] = (
    "Solved by ArtSource/ActionCalibration/solve_playable_v1.py on input/Hand_R.fbx "
    "(read-only); coordinate descent over the 20 controls, all candidates [0,1], "
    "evaluated through the same channel mapping Unity uses. twist/spread pinned "
    "at 0.5 for fist; thumb opposition optimized over full 0..1 for pinch. "
    "Authored preset = none; this is pure solver output.")
with open(PROFILE_OUT, "w", encoding="utf-8") as f:
    json.dump(profile, f, indent=2, ensure_ascii=False)
print("OUTPUT PROFILE WRITTEN", PROFILE_OUT)
print("FIST20", fist20)
print("PINCH20", pinch20)
print("SOLVE DONE")
