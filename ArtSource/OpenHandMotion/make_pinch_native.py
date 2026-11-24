# Pinch candidate on the NATIVE Elena FF rig (no rebake, no re-rig).
#
# v2 approach (robust to the nested glTF spaces):
#   - pose = artist's Open/Close animation evaluated by Blender at frame
#     open/closed; pinch = fractional LOCAL-rotation blend applied ONLY to
#     listed driving bones; all other bones keep the artist's open pose.
#     No pb.matrix writes (they exploded local scales), no mesh matrix reset.
#   - contact metric via VERTEX GROUPS: evaluated positions of verts weighted
#     to thumb_03.R_010 vs index_03.R_019 = true tip skin clouds.
#   - iterative fraction search until real skin-skin contact (<= 4 mm) or
#     honest failure report.
# Outputs: pinch_candidate.json + previews (palm/side/contact).
# Derivative animation data: CC-BY-SA-4.0, Elena FF.
import bpy, os, math, json
from mathutils import Vector, Quaternion, Matrix
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
GLB = os.path.join(HERE, "..", "ThirdParty", "ElenaFF_RiggedHand", "hand.glb")
REP = json.load(open(os.path.join(HERE, "extract_glb_report.json")))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
arm = next(ob for ob in bpy.data.objects if ob.type == 'ARMATURE')
o = next(ob for ob in bpy.data.objects if ob.type == 'MESH' and len(ob.data.vertices) > 1000)
sc = bpy.context.scene
fps = sc.render.fps
F_OPEN, F_CLOSED = 1.0, 1.0 + 1.333333 * fps

w = bpy.data.worlds.new("W"); w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.85, 0.85, 0.87, 1)
sc.world = w
lt = bpy.data.lights.new("sun", 'SUN'); lt.energy = 2.2
lo = bpy.data.objects.new("sun", lt); bpy.context.collection.objects.link(lo)
lo.rotation_euler = (math.radians(55), 0, math.radians(35))
lt2 = bpy.data.lights.new("fill", 'SUN'); lt2.energy = 1.0
lo2 = bpy.data.objects.new("fill", lt2); bpy.context.collection.objects.link(lo2)
lo2.rotation_euler = (math.radians(120), 0, math.radians(-140))

B = arm.pose.bones

def local_rot(frame):
    sc.frame_set(int(round(frame)))
    bpy.context.view_layer.update()
    out = {}
    for pb in B:
        if pb.parent:
            out[pb.name] = (pb.parent.matrix.inverted() @ pb.matrix).to_quaternion()
        else:
            out[pb.name] = pb.matrix.to_quaternion()
    return out

OPEN_R = local_rot(F_OPEN)
CLOSED_R = local_rot(F_CLOSED)
print("CAPTURED local rotations", len(OPEN_R))

DELTA = {}
for n in OPEN_R:
    DELTA[n] = OPEN_R[n].rotation_difference(CLOSED_R[n])

# vertex-group-based skin clouds (evaluated), anchored at the BIND-pose bone
# HEAD in mesh-local space: these end bones have head at the fingertip and
# tail extending beyond it. Weight groups also cover artist sleeve geometry,
# so the spatial anchor is what selects true fingertip skin.
VG = {}
for name in ("thumb_03.R_010", "index_03.R_019"):
    gi = o.vertex_groups[name].index
    mesh_from_arm = o.matrix_world.inverted() @ arm.matrix_world
    anchor = mesh_from_arm @ arm.data.bones[name].head_local
    VG[name] = (gi, anchor)
print("VERTEX_GROUPS_FOUND", list(VG))

def weighted_cloud(bone):
    gi, anchor = VG[bone]
    sel = []
    for v in o.data.vertices:
        for g in v.groups:
            if g.group == gi and g.weight > 0.1:
                if (v.co - anchor).length < 0.12:
                    sel.append(v.index)
                break
    deps = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(deps)
    me = ev.to_mesh()
    mw = ev.matrix_world
    pts = [mw @ me.vertices[i].co for i in sel]
    ev.to_mesh_clear()
    return pts

def apply_pinch(fracs):
    """start from artist open pose; rotate listed bones by f*delta locally"""
    sc.frame_set(int(round(F_OPEN)))
    bpy.context.view_layer.update()
    for name, f in fracs.items():
        pb = B.get(name)
        if pb is None or f == 0.0:
            continue
        qd = DELTA[name]
        axis, angle = qd.to_axis_angle()
        qf = Quaternion((math.cos(angle * f / 2), *(c * math.sin(angle * f / 2) for c in axis)))
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = OPEN_R[name] @ qf
    bpy.context.view_layer.update()

def gap_for(fracs):
    apply_pinch(fracs)
    tc = weighted_cloud("thumb_03.R_010")
    ic = weighted_cloud("index_03.R_019")
    if not tc or not ic:
        return 1e9
    kd = KDTree(len(ic))
    for k, p in enumerate(ic):
        kd.insert(p, k)
    kd.balance()
    best = 1e9
    for p in tc:
        _, _, d = kd.find(p)
        best = min(best, d)
    return best

PINCH_FRACS = {
    "thumb_base.R_03": 0.50,
    "thumb_01.R_08": 0.55,
    "thumb_02.R_09": 0.35,
    "thumb_03.R_010": 0.30,
    "index_01.R_017": 0.42,
    "index_02.R_018": 0.55,
    "index_03.R_019": 0.42,
    "middle_01.R_025": 0.25, "middle_02.R_026": 0.30, "middle_03.R_027": 0.25,
    "ring_01.R_033": 0.25, "ring_02.R_034": 0.30, "ring_03.R_035": 0.25,
    "pinky_01.R_041": 0.25, "pinky_02.R_042": 0.30, "pinky_03.R_043": 0.25,
    "index_base.R_012": 0.3, "middle_base.R_020": 0.3,
    "ring_base.R_028": 0.3, "pinky_base.R_036": 0.3,
}

g0 = gap_for(PINCH_FRACS)
print("PINCH_INITIAL_GAP", round(g0, 5))

TARGET = 0.004
best_fracs = dict(PINCH_FRACS)
g = g0
KNOBS = [("thumb_02.R_09", 0.1), ("thumb_03.R_010", 0.1),
         ("thumb_01.R_08", 0.1), ("index_02.R_018", 0.1),
         ("index_03.R_019", 0.1), ("index_01.R_017", 0.1),
         ("thumb_base.R_03", 0.1)]
for it in range(60):
    if g <= TARGET:
        break
    improved = False
    for bone, step in KNOBS:
        for sgn in (+1, -1):
            trial = dict(best_fracs)
            trial[bone] = round(min(1.0, max(0.0, trial[bone] + sgn * step)), 4)
            gt = gap_for(trial)
            if gt < g - 1e-4:
                best_fracs, g, improved = trial, gt, True
                break
        if g <= TARGET:
            break
    if not improved:
        KNOBS = [(b, s / 2) for b, s in KNOBS]
        if max(s for _, s in KNOBS) < 0.005:
            break
print("PINCH_FINAL_GAP", round(g, 5))
apply_pinch(best_fracs)

cand = {
    "source": REP["source"],
    "basis": "per listed bone: local rotation = artist-open * (open->closed delta)^f; "
             "all other bones keep the artist's open pose. Fractions are of the "
             "artist's own Open/Close rotation.",
    "pinch_fractions": best_fracs,
    "measured_skin_gap_m_native_units": round(g, 5),
    "contact_claim": bool(g <= 0.004),
    "license": "Derivative animation data - CC-BY-SA-4.0, Elena FF",
}
with open(os.path.join(HERE, "pinch_candidate.json"), "w", encoding="utf-8") as f:
    json.dump(cand, f, indent=1, ensure_ascii=False)
print("WROTE pinch_candidate.json CONTACT_CLAIM", cand["contact_claim"])

# --- renders ---
def palm_normal():
    a = arm.matrix_world @ arm.data.bones["index_base.R_012"].head_local
    b = arm.matrix_world @ arm.data.bones["middle_base.R_020"].head_local
    c = arm.matrix_world @ arm.data.bones["pinky_base.R_036"].head_local
    return ((b - a).cross(c - b)).normalized()

def render(tag, direction, zoom=1.25, focus_at=None):
    # frame on the HAND region only: the model includes artist sleeve/base
    # geometry (pulse.R_01 / hand.R_02 driven) that dwarfs the hand itself
    hand_bones = ("index_base.R_012", "middle_base.R_020", "pinky_base.R_036",
                  "thumb_base.R_03", "index_03.R_019", "thumb_03.R_010")
    pts = []
    for bn in hand_bones:
        pb = B[bn]
        pts.append(arm.matrix_world @ pb.head)
        pts.append(arm.matrix_world @ pb.tail)
    c = sum(pts, Vector()) / len(pts)
    span = max(max(p[i] for p in pts) - min(p[i] for p in pts) for i in range(3))
    focus = c
    if focus_at is not None:
        focus = focus_at
        zoom = 0.12
    cam = bpy.data.cameras.new("c" + tag); cam.type = 'ORTHO'
    cam.ortho_scale = span * zoom
    co = bpy.data.objects.new("c" + tag, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(direction).normalized()
    co.location = focus + d * 10
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.filepath = os.path.join(HERE, tag + ".png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    print("RENDERED", tag)

UP = Vector((0, 1, 0))
n = palm_normal()
tc = weighted_cloud("thumb_03.R_010")
ic = weighted_cloud("index_03.R_019")
contact = (sum(tc, Vector()) / len(tc) + sum(ic, Vector()) / len(ic)) / 2
render("elena_pinch_palm", n)
render("elena_pinch_side", n.cross(UP))
render("elena_pinch_contact", n, focus_at=contact)

sc.frame_set(int(round(F_OPEN)))
bpy.context.view_layer.update()
render("elena_open_palm", n)
print("ELENA_PINCH_DONE")
