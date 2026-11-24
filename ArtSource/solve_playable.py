# Playable-solve: optimize the 20 normalized controls directly (always [0,1]),
# evaluating through the SAME apply20 channel mapping Unity uses. Metrics use
# fixed rest-palm references (not pose-dependent tails). The final JSON samples
# are exactly the solver output — no clamping, no lossy back-conversion.
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

# fixed rest-palm references, captured BEFORE any pose is applied
for pb in arm.pose.bones:
    pb.rotation_euler = (0, 0, 0)
bpy.context.view_layer.update()
deps = bpy.context.evaluated_depsgraph_get()


def world_pts():
    ev = o.evaluated_get(deps)
    return np.array([list(ev.matrix_world @ c) for c in ev.bound_box]), ev


def mesh_verts():
    ev = o.evaluated_get(deps)
    evm = ev.to_mesh()
    W = np.array([list(ev.matrix_world @ v.co) for v in evm.vertices])
    ev.to_mesh_clear()
    return W


REST_W = mesh_verts()  # (N,3) rest world vertices — fixed reference
wrist_w = np.array((arm.matrix_world @ Vector(arm.pose.bones["Wrist.R"].head))[:])
# fixed palm target: centroid of rest wrist + 0.55 * (rest wrist->middle-tip)
mid_rest = REST_W[np.argmax(REST_W[:, 2] - REST_W[:, 1])]  # dummy, replaced below
# index MCP & little MCP rest world for the palm plane
def bone_head_world(name):
    return np.array((arm.matrix_world @ Vector(arm.pose.bones[name].head))[:])

# capture rest anchors BEFORE posing
i_mcp = bone_head_world("Index1.R")
l_mcp = bone_head_world("Little1.R")
palm_center_rest = (i_mcp + l_mcp) / 2
palm_axis = (l_mcp - i_mcp)
palm_axis = palm_axis / np.linalg.norm(palm_axis)
# palm plane normal at rest: cross(index axis dir, palm_axis)
index_dir_rest = bone_head_world("Index2.R") - i_mcp
index_dir_rest /= np.linalg.norm(index_dir_rest)
palm_n_rest = np.cross(palm_axis, index_dir_rest)
palm_n_rest /= np.linalg.norm(palm_n_rest)
# sign check: thumb CMC head should be on +normal side
th_rest = bone_head_world("Thumb1.R")
if np.dot(th_rest - palm_center_rest, palm_n_rest) < 0:
    palm_n_rest = -palm_n_rest
print("REST palm normal (fixed reference):", np.round(palm_n_rest, 3))

# fingertip rest positions (from bone tails at rest)
def tail_world(name):
    return np.array((arm.matrix_world @ Vector(arm.pose.bones[name].tail))[:])

tips_rest = {n: tail_world(n) for n in
             ("Index3.R", "Middle3.R", "Ring3.R", "Little3.R", "Thumb3.R")}


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


def posed_verts():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    evm = ev.to_mesh()
    W = np.array([list(ev.matrix_world @ v.co) for v in evm.vertices])
    ev.to_mesh_clear()
    return W


# palm plane is FIXED (rest reference): points move toward it along -normal
def palm_metrics():
    W = mesh_verts()
    d = (W - palm_center_rest) @ palm_n_rest  # signed distance to rest palm plane
    # metric: how much of the fingertip region has reached the palm
    # (fingertip zone = rest verts near the four fingertips)
    cost = 0.0
    for n, tr in tips_rest.items():
        # nearest deformed vertex to this rest fingertip
        near = np.linalg.norm(W - tr, axis=1)
        k = int(np.argmin(near))
        # tip distance to fixed palm plane
        tip_d = (W[k] - palm_center_rest) @ palm_n_rest
        cost += abs(tip_d) + np.linalg.norm(W[k] - tr) * 0.2
    return cost, d


# channel bounds
LO = [0.0] * 20
HI = [1.0] * 20
# twist/spread channels center at 0.5
for ci, ch in enumerate(channels):
    if abs(ch.get("rest", 0.0) - 0.5) < 0.01:
        LO[ci] = 0.0
        HI[ci] = 1.0

x = list(profile["samples"]["fist"])


def cost_fist(c20):
    apply20(c20)
    cost, _ = palm_metrics()
    return cost


print("=== FIST: optimizing 20 controls directly ===")
cost = cost_fist(x)
print("start cost", round(cost, 4))
step = 0.12
for it in range(10):
    improved = False
    for ci in range(20):
        for delta in (step, -step):
            t = x[:]
            t[ci] = max(LO[ci], min(HI[ci], t[ci] + delta))
            if t == x:
                continue
            c = cost_fist(t)
            if c < cost - 1e-5:
                x = t
                cost = c
                improved = True
    print("fist iter", it, "cost", round(cost, 4))
    if not improved:
        step /= 2
        if step < 0.01:
            break
fist20 = [round(v, 4) for v in x]

print("=== PINCH: thumb-index pad contact, 20 controls ===")
x2 = list(fist20)
# relax thumb/index for pinch start
x2[0] = 0.33; x2[1] = 0.79; x2[2] = 1.0; x2[3] = 0.0
x2[4] = 0.6; x2[5] = 1.0; x2[6] = 0.44


def cost_pinch(c20):
    apply20(c20)
    W = mesh_verts()
    # contact: min distance between thumb-pad zone and index-pad zone (mesh verts)
    # zones defined as rest verts near each tip
    ti = tips_rest["Index3.R"]
    tt = tips_rest["Thumb3.R"]
    zi = np.linalg.norm(REST_W - ti, axis=1) < 0.35
    zt = np.linalg.norm(REST_W - tt, axis=1) < 0.35
    if zi.sum() == 0 or zt.sum() == 0:
        return 10.0
    Wi = W[zi]
    Wt = W[zt]
    # min pair distance between the two zones
    d = np.linalg.norm(Wi[:, None, :] - Wt[None, :, :], axis=2)
    return float(d.min())


cost = cost_pinch(x2)
print("start gap", round(cost, 4))
step = 0.12
for it in range(10):
    improved = False
    for ci in list(range(0, 8)):
        for delta in (step, -step):
            t = x2[:]
            t[ci] = max(LO[ci], min(HI[ci], t[ci] + delta))
            if t == x2:
                continue
            c = cost_pinch(t)
            if c < cost - 1e-5:
                x2 = t
                cost = c
                improved = True
    print("pinch iter", it, "gap", round(cost, 4))
    if not improved:
        step /= 2
        if step < 0.01:
            break
pinch20 = [round(v, 4) for v in x2]

profile["samples"]["fist"] = fist20
profile["samples"]["pinch"] = pinch20
with open(PROFILE, "w", encoding="utf-8") as f:
    json.dump(profile, f, indent=2, ensure_ascii=False)
print("PROFILE UPDATED (playable, no clamp)")
print("FIST20", fist20)
print("PINCH20", pinch20)
print("PLAYABLE SOLVE DONE")
