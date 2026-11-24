# Rev3: geometry-constrained contact solve for fist & pinch on the rev7 mesh.
#
# Skin-surface metrics (not bone tails): fingertip pad = mesh verts near a
# bone tail; palm skin = palm-region vertex cloud (rest pose selection);
# gaps via KDTree; penetration signed by closest palm-vertex normal.
#
# Stages:
#   A) fist fingers: pad-to-palm contact band + palm-plane penetration guard
#   B) fist thumb  : thumb pad skin onto index middle-phalanx region skin
#   C) pinch       : thumb pad <-> index pad contact with opposing normals,
#                    multi-start pattern search; middle/ring/little relaxed
# Channels stay untouched (bone/axis/sign/rest/scale). Only the candidate
# JSON in output/rev3 is written; renders are driven by the re-read file.
import bpy, os, math, json
from mathutils import Vector
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
PROFILE_IN = os.path.join(HERE, "input_rev7", "hand_rig_profile.json")
FBX = os.path.join(HERE, "input_rev7", "Hand_R.fbx")
OUTDIR = os.path.join(HERE, "output", "rev3")
os.makedirs(OUTDIR, exist_ok=True)

with open(PROFILE_IN, encoding="utf-8") as f:
    profile_in = json.load(f)
channels_in = profile_in["channels"]
CH = {c["index"]: c for c in channels_in}

FIST_INIT = {   # rev2 finals, degrees
    0: 58.0, 1: 66.0, 2: 35.0, 3: -36.0,
    4: 78.0, 5: 85.0, 6: 60.0, 7: -8.0,
    8: 82.0, 9: 88.0, 10: 62.0, 11: -5.0,
    12: 82.0, 13: 85.0, 14: 60.0, 15: -5.0,
    16: 78.0, 17: 90.0, 18: 55.0, 19: -8.0,
}
PINCH_INIT = {
    0: 38.0, 1: 72.0, 2: 36.0, 3: -62.0,
    4: 52.0, 5: 64.0, 6: 32.0, 7: -3.0,
    8: 22.0, 9: 28.0, 10: 12.0, 11: 0.0,
    12: 25.0, 13: 30.0, 14: 14.0, 15: 0.0,
    16: 28.0, 17: 32.0, 18: 16.0, 19: 0.0,
}

def joint_limit(idx):
    ch = CH[idx]
    return (1.0 - ch["rest"]) * ch["sign"] * ch["scale"]

def clamp_deg(idx, deg):
    lo, hi = -joint_limit(idx), joint_limit(idx)
    if idx == 7:          # index spread: keep mostly adducted in candidates
        lo, hi = -15.0, 8.0
    return max(lo, min(hi, deg))

# ---------------- scene ----------------
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
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
B = arm.pose.bones
FINGERS = ("Index", "Middle", "Ring", "Little")

def apply20(values):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for ci, ch in enumerate(channels_in):
        pb = B.get(ch["bone"] + ".R")
        if pb is None:
            continue
        deg = (values[ci] - ch.get("rest", 0.0)) * ch["scale"] * ch["sign"]
        e = pb.rotation_euler
        e[{"X": 0, "Y": 1, "Z": 2}[ch["axis"]]] += math.radians(deg)
    bpy.context.view_layer.update()

def ctrl_from_deg(idx, deg):
    ch = CH[idx]
    return round(ch["rest"] + ch["sign"] * deg / ch["scale"], 4)

def eval_mesh():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    me = ev.to_mesh()
    mw = ev.matrix_world
    nmat = mw.to_3x3()
    verts = [mw @ v.co for v in me.vertices]
    norms = [(nmat @ v.normal).normalized() for v in me.vertices]
    ev.to_mesh_clear()
    return verts, norms

def palm_normal():
    iw = (arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    lw = (arm.matrix_world @ Vector(arm.data.bones["LittleMeta.R"].tail_local)
          - arm.matrix_world @ Vector(arm.data.bones["Wrist.R"].head_local))
    return (iw.cross(lw)).normalized()   # PALMSIGN=+1 confirmed (probe_v2)

# rest-pose palm region + KD
apply20([CH[i]["rest"] for i in range(20)])
rv, rn = eval_mesh()
n_palm = palm_normal()
palm_center = (arm.matrix_world @ Vector(arm.data.bones["IndexMeta.R"].head_local)
               + arm.matrix_world @ Vector(arm.data.bones["LittleMeta.R"].head_local)) / 2
PALM = [i for i, (p, n) in enumerate(zip(rv, rn))
        if (p - palm_center).length < 0.10 and n.dot(n_palm) > 0.25]
PALM_KD = KDTree(len(PALM))
for k, vi in enumerate(PALM):
    PALM_KD.insert(rv[vi], k)
PALM_KD.balance()
PALM_N = {k: rn[vi] for k, vi in enumerate(PALM)}
print("PALM_REGION_VERTS", len(PALM))

def pad(bone, verts, norms, r=0.022):
    """skin pad around a bone tail: (point, normal, count)"""
    tail = arm.matrix_world @ Vector(B[bone + ".R"].tail)
    pts, nrms = [], []
    for vi, p in enumerate(verts):
        if (p - tail).length < r:
            pts.append(p); nrms.append(norms[vi])
    if not pts:
        return tail, None, 0
    m = sum(pts, Vector()) / len(pts)
    n = sum(nrms, Vector((0, 0, 0)))
    return m, (n.normalized() if n.length > 1e-6 else None), len(pts)

def region_pts(center, verts, r):
    return [p for p in verts if (p - center).length < r]

def palm_gap_with_penetration(p):
    co, idx, dist = PALM_KD.find(p)
    side = PALM_N[idx].dot(p - co)
    return dist, side

def region_gap(pts_a, pts_b):
    kd = KDTree(len(pts_b))
    for k, p in enumerate(pts_b):
        kd.insert(p, k)
    kd.balance()
    best = 1e9
    for p in pts_a:
        _, _, d = kd.find(p)
        best = min(best, d)
    return best

# ---------------- objectives ----------------
def fist_finger_loss(deg_tab):
    """finger pads touch palm skin; no through-penetration"""
    s = [ctrl_from_deg(i, deg_tab.get(i, 0.0)) for i in range(20)]
    apply20(s)
    verts, norms = eval_mesh()
    loss, rep = 0.0, {}
    for f in FINGERS:
        p, n, cnt = pad(f + "3", verts, norms)
        if cnt == 0:
            p, n, cnt = pad(f + "2", verts, norms)
        gap, side = palm_gap_with_penetration(p)
        rep[f] = (round(gap, 4), round(side, 4))
        if gap > 0.010:
            loss += 400.0 * (gap - 0.010) ** 2
        if side < -0.0025:
            loss += 2500.0 * (-0.0025 - side) ** 2
    return loss, rep

def tip_cloud(bone, verts, r=0.024):
    c = arm.matrix_world @ Vector(B[bone + ".R"].tail)
    return [p for p in verts if (p - c).length < r]

def cloud_gap(a, b):
    if not a or not b:
        return 1e9
    return region_gap(a, b)

def fist_thumb_loss(deg_tab):
    """thumb tip skin cloud onto index middle-phalanx skin cloud (wrap)"""
    s = [ctrl_from_deg(i, deg_tab.get(i, 0.0)) for i in range(20)]
    apply20(s)
    verts, norms = eval_mesh()
    tcloud = tip_cloud("Thumb3", verts)
    i2c = arm.matrix_world @ Vector(B["Index2.R"].tail)
    reg = region_pts(i2c, verts, 0.028)
    d = cloud_gap(tcloud, reg)
    loss = 1200.0 * max(0.0, d - 0.003) ** 2
    loss += 0.008 * sum((deg_tab[i] - FIST_INIT[i]) ** 2 for i in deg_tab) / 100.0
    return loss, {"thumb_idx_gap": round(d, 4)}

def pinch_loss(deg_tab):
    """thumb tip skin cloud <-> index tip skin cloud contact; others fixed"""
    s = [ctrl_from_deg(i, deg_tab.get(i, 0.0)) for i in range(20)]
    apply20(s)
    verts, norms = eval_mesh()
    tcloud = tip_cloud("Thumb3", verts)
    icloud = tip_cloud("Index3", verts)
    d = cloud_gap(tcloud, icloud)
    tp, tn, tc = pad("Thumb3", verts, norms, r=0.02)
    ip, in_, ic = pad("Index3", verts, norms, r=0.02)
    ndot = tn.dot(in_) if (tn and in_) else 0.0
    loss = 1400.0 * max(0.0, d - 0.002) ** 2
    # normal shaping only once contact is near; otherwise it fights contact
    if d < 0.030:
        loss += 700.0 * max(0.0, ndot + 0.6) ** 2
        ip_deg = deg_tab[2]
        loss += 40.0 * max(0.0, ip_deg - 55.0) ** 2 / 100.0
    # opposition must stay toward the index (negative delta), never splay out
    if deg_tab[3] > -10.0:
        loss += 800.0 * (deg_tab[3] + 10.0) ** 2 / 100.0
    loss += 0.008 * sum((deg_tab[i] - PINCH_INIT[i]) ** 2 for i in deg_tab) / 100.0
    return loss, {"gap": round(d, 4), "ndot": round(ndot, 3)}

def pattern_search(tab, var_idx, loss_fn, steps=(6.0, 2.5, 1.0, 0.4), sweeps=6):
    tab = dict(tab)
    best, rep = loss_fn(tab)
    evals = 1
    for _ in range(sweeps):
        improved = False
        for i in var_idx:
            for st in steps:
                moved = True
                while moved:
                    moved = False
                    for sgn in (+1, -1):
                        cand = dict(tab)
                        cand[i] = clamp_deg(i, tab[i] + sgn * st)
                        l2, r2 = loss_fn(cand)
                        evals += 1
                        if l2 < best - 1e-9:
                            tab, best, rep = cand, l2, r2
                            moved = improved = True
                            break
        if not improved:
            break
    print("  evals", evals)
    return tab, best, rep

# ---- Stage A: fist fingers (from rev2 start) ----
fist_tab = dict(FIST_INIT)
fist_vars = [4, 5, 6, 8, 9, 10, 12, 13, 14, 16, 17, 18]
print("STAGE A fist fingers")
fist_tab, fl, frep = pattern_search(fist_tab, fist_vars, fist_finger_loss)
print("FIST_FINGERS", round(fl, 5), frep)

# ---- Stage B: fist thumb wrap (fingers fixed) ----
print("STAGE B fist thumb")
fist_tab, tl, trep = pattern_search(fist_tab, [0, 1, 2, 3], fist_thumb_loss,
                                    steps=(8.0, 3.0, 1.0, 0.4))
print("FIST_THUMB", round(tl, 5), trep)
print("FIST_THUMB_TABLE", {i: round(fist_tab[i], 1) for i in (0, 1, 2, 3)})
print("FIST_FINGER_TABLE", {i: round(fist_tab[i], 1) for i in (4, 5, 6, 8, 9, 10, 12, 13, 14, 16, 17, 18)})
print("FIST_SATURATED", [i for i in (0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 13, 14, 16, 17, 18)
                         if fist_tab[i] >= joint_limit(i) - 1e-3])

# ---- Stage C: pinch, multi-start ----
pinch_vars = [0, 1, 2, 3, 4, 5, 6, 7]
starts = {
    "rev2": dict(PINCH_INIT),
    "tight": {**PINCH_INIT, 0: 60.0, 1: 85.0, 2: 45.0, 3: -80.0,
              4: 60.0, 5: 75.0, 6: 40.0, 7: -6.0},
    "mcptop": {**PINCH_INIT, 0: 30.0, 1: 85.0, 2: 40.0, 3: -55.0,
               4: 55.0, 5: 65.0, 6: 30.0},
    "reach": {**PINCH_INIT, 0: 30.0, 1: 55.0, 2: 25.0, 3: -78.0, 4: 45.0, 5: 55.0, 6: 25.0},
}
best_tab, best_loss, best_rep, best_name = None, 1e9, None, None
for name, st_tab in starts.items():
    print("STAGE C pinch start", name)
    t, l, r = pattern_search(st_tab, pinch_vars, pinch_loss,
                             steps=(5.0, 2.0, 0.8), sweeps=6)
    print("  ->", name, round(l, 5), r)
    if l < best_loss:
        best_tab, best_loss, best_rep, best_name = t, l, r, name
pinch_tab, prep = best_tab, best_rep
print("PINCH_BEST", best_name, round(best_loss, 5), prep)

# ---- Stage C2: fine continuation + hand variants on the best solution ----
fine_tab, fine_l, fine_r = pattern_search(dict(pinch_tab), pinch_vars, pinch_loss,
                                          steps=(1.2, 0.4), sweeps=8)
print("C2_FINE", round(fine_l, 5), fine_r)
if fine_l < best_loss:
    pinch_tab, best_loss, best_rep = fine_tab, fine_l, fine_r
# hand variants: push opposition/flex while keeping the found pattern
for name, deltas in {
    "opp+": {3: -12.0, 0: -6.0},
    "curlidx+": {4: +10.0, 5: +10.0, 3: -8.0},
    "thumbflex+": {1: +12.0, 2: +10.0, 3: -10.0},
    "ipless": {2: -25.0, 1: +8.0, 3: -14.0},
    "straightover": {2: -35.0, 1: +18.0, 0: +6.0, 3: -16.0},
}.items():
    v = dict(pinch_tab)
    for k, dv in deltas.items():
        v[k] = clamp_deg(k, v[k] + dv)
    v, l2, r2 = pattern_search(v, pinch_vars, pinch_loss,
                               steps=(2.0, 0.7), sweeps=4)
    print("C2_VAR", name, round(l2, 5), r2)
    if l2 < best_loss:
        pinch_tab, best_loss, best_rep = v, l2, r2
print("PINCH_FINAL", round(best_loss, 5), best_rep)
print("PINCH_TABLE", {i: round(pinch_tab[i], 1) for i in pinch_vars})
print("PINCH_SATURATED", [i for i in pinch_vars
                          if abs(pinch_tab[i]) >= joint_limit(i) - 1e-3 and pinch_tab[i] > 0])

# ---------------- write candidate JSON ----------------
FIST_FIXED = {7: -8.0, 11: -5.0, 15: -5.0, 19: -8.0}
PINCH_FIXED = {8: 22.0, 9: 28.0, 10: 12.0, 11: 0.0, 12: 25.0, 13: 30.0,
               14: 14.0, 15: 0.0, 16: 28.0, 17: 32.0, 18: 16.0, 19: 0.0}
# clamp solved tables to channel limits (candidates beyond limit, e.g. the
# rev2-inherited 78-82 deg MCP starts, are capped with an honest log)
for i in range(20):
    if i in fist_tab:
        c = clamp_deg(i, fist_tab[i])
        if c != fist_tab[i]:
            print("RANGE_CLAMP fist ch%02d %.1f -> %.1f deg" % (i, fist_tab[i], c))
            fist_tab[i] = c
    if i in pinch_tab:
        c = clamp_deg(i, pinch_tab[i])
        if c != pinch_tab[i]:
            print("RANGE_CLAMP pinch ch%02d %.1f -> %.1f deg" % (i, pinch_tab[i], c))
            pinch_tab[i] = c
fist20 = [ctrl_from_deg(i, fist_tab.get(i, FIST_FIXED.get(i, 0.0))) for i in range(20)]
pinch20 = [ctrl_from_deg(i, pinch_tab.get(i, PINCH_FIXED.get(i, 0.0))) for i in range(20)]
open20 = [0.0] * 20
open20[3] = open20[7] = open20[11] = open20[15] = open20[19] = 0.5

profile_out = {
    "version": profile_in["version"],
    "rig": profile_in["rig"],
    "space": profile_in["space"],
    "channels": channels_in,
    "samples": {"open": open20, "fist": fist20, "pinch": pinch20},
    "notes": list(profile_in.get("notes", [])),
    "source_note": (
        "Author presets v3 (ActionCalibration): rev2 pose refined by staged "
        "pattern search against skin-surface contact metrics on the rev7 mesh "
        "(fingertip pads to palm skin; thumb pad to index middle-phalanx skin "
        "in fist; thumb-index pad contact with opposing normals in pinch). "
        "Channels unchanged from input_rev7. No Unity validation here (Codex)."
    ),
}
with open(os.path.join(OUTDIR, "hand_rig_profile.json"), "w", encoding="utf-8") as f:
    json.dump(profile_out, f, indent=2, ensure_ascii=False)
print("CHANNELS_IDENTICAL",
      json.dumps(profile_out["channels"]) == json.dumps(channels_in))
print("FIST20", fist20)
print("PINCH20", pinch20)

# ---------------- re-read written file; render from it only ----------------
with open(os.path.join(OUTDIR, "hand_rig_profile.json"), encoding="utf-8") as f:
    prof_rt = json.load(f)
samples_rt = prof_rt["samples"]

def render(tag, direction, zoom=1.25, focus_at=None):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    pts = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    focus = sum(pts, Vector()) / 8.0
    if focus_at is not None:
        focus = focus_at
        zoom = 0.5
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

UP = Vector((0, 1, 0))
n = palm_normal()

apply20(samples_rt["fist"])
verts, norms = eval_mesh()
for f in FINGERS:
    p, _, _ = pad(f + "3", verts, norms)
    g, s = palm_gap_with_penetration(p)
    print("FIST_SKIN_GAP", f, round(g, 4), "side", round(s, 4))
tcloud = tip_cloud("Thumb3", verts)
i2c = arm.matrix_world @ Vector(B["Index2.R"].tail)
reg = region_pts(i2c, verts, 0.028)
print("FIST_SKIN_GAP_ThumbIndexPIP", round(cloud_gap(tcloud, reg), 4))
render("rev3_fist_palm", n)
render("rev3_fist_side_thumb", n.cross(UP))
render("rev3_fist_side_pinky", (-n).cross(UP))

apply20(samples_rt["pinch"])
verts, norms = eval_mesh()
tcloud = tip_cloud("Thumb3", verts)
icloud = tip_cloud("Index3", verts)
print("PINCH_SKIN_GAP", round(cloud_gap(tcloud, icloud), 4))
tp, tn, tc = pad("Thumb3", verts, norms, r=0.02)
ip_, in_, ic = pad("Index3", verts, norms, r=0.02)
print("PINCH_NDOT", round(tn.dot(in_), 3))
render("rev3_pinch_palm", n)
render("rev3_pinch_contact", n, focus_at=(tp + ip_) / 2)

print("REV3_DONE")
