# Game04 VirtualHand — hand build pipeline (Blender 5.2 batch), clean rewrite.
# Base mesh: MakeHuman base.obj (CC0 2020, Data Collection AB).
# Joints: MakeHuman official default.mhskel joint table (authoritative coordinates).
# R arm occupies mesh x<=-2.6 with official R coords; L arm x>=+2.6 with mirrored coords.
# Outputs: Assets/VirtualHand/Models/Hand_{R,L}.fbx + 2K baked PNGs,
#          ArtSource/Hands/hands_source.blend, ArtSource/Review/*.png.
import bpy, os, math, json
import numpy as np
import bmesh
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
DOWNLOAD = os.path.join(HERE, "_download", "makehuman")
ASSETS = os.path.normpath(os.path.join(HERE, "..", "Assets", "VirtualHand", "Models"))
REVIEW = os.path.join(HERE, "Review")
BUILD = os.path.join(HERE, "Hands")
for d in (ASSETS, REVIEW, BUILD):
    os.makedirs(d, exist_ok=True)

def log(*a): print("VH:", *a, flush=True)

# ---------- official joint coordinates (right side, from default.mhskel) ----------
JOINTS_R = {
    "wrist":    [-4.321, 2.402, 1.773],
    "thumb_cmc":[-4.181, 2.303, 2.164],
    "thumb_mcp":[-4.081, 2.237, 2.412],
    "thumb_ip": [-3.976, 2.095, 2.707],
    "index_mcp":  [-4.510, 2.011, 2.624],
    "index_pip":  [-4.531, 1.876, 2.875],
    "index_dip":  [-4.561, 1.712, 3.003],
    "middle_mcp": [-4.620, 1.878, 2.458],
    "middle_pip": [-4.689, 1.702, 2.711],
    "middle_dip": [-4.761, 1.480, 2.823],
    "ring_mcp":   [-4.702, 1.815, 2.286],
    "ring_pip":   [-4.798, 1.625, 2.490],
    "ring_dip":   [-4.856, 1.428, 2.553],
    "little_mcp": [-4.753, 1.741, 2.136],
    "little_pip": [-4.808, 1.587, 2.251],
    "little_dip": [-4.833, 1.434, 2.290],
}
ELBOW_R = [-3.276, 3.364, 0.334]
TIP_SEEDS_R = {
    "thumb":  [-3.95, 2.00, 2.90],
    "index":  [-4.56, 1.60, 3.12],
    "middle": [-4.78, 1.40, 2.90],
    "ring":   [-4.88, 1.30, 2.62],
    "little": [-4.85, 1.36, 2.36],
}

def mirror(p):  # right->left coordinate mirror
    return [-p[0], p[1], p[2]]

def bake_x90(p):
    """OBJ-import object rotation (+90deg X) baked into coordinates:
    (x,y,z) -> (x,-z,y). JOINTS_R/TIP_SEEDS_R are in Y-up OBJ space;
    the mesh data is in the baked Z-up world after unifying binding space."""
    return [p[0], -p[2], p[1]]

# ---------------------------------------------------------------- mesh extraction
def fit_axis(V, side):
    """PCA on the arm cloud of `side`; axis points elbow->fingertips.
    Baked mesh: MakeHuman stands along -Y (arms at |x|>=3, arm band y<=-0.5),
    fingertips toward +Z."""
    sgn = -1.0 if side == "R" else 1.0
    M = V[(V[:, 0] * sgn >= 3.0) & (V[:, 1] >= 0.5)]
    c = M.mean(axis=0)
    _, _, vt = np.linalg.svd(M - c, full_matrices=False)
    axis = vt[0]
    if axis[2] < 0:
        axis = -axis
    return c, axis

def load_obj_body_faces(path):
    """Parse the OBJ and return (verts, faces, group_names) where faces carries
    the group of each face. base.obj contains the skin under `g body` plus many
    `joint-*` cube boxes and `helper-*` groups that must never reach render/skin."""
    verts = []
    faces = []  # (tuple of 1-based vert indices, group)
    group = "body"
    with open(path, encoding="utf-8", errors="ignore") as f:
        for line in f:
            if line.startswith("v "):
                p = line.split()
                verts.append((float(p[1]), float(p[2]), float(p[3])))
            elif line.startswith("g "):
                group = line.strip()[2:]
            elif line.startswith("f "):
                idx = [int(tok.split("/")[0]) for tok in line.split()[1:]]
                faces.append((idx, group))
    return verts, faces


def extract_arm(obj, side):
    """Keep only `g body` skin faces inside the arm-region spatial mask.
    Joint/helper boxes are never imported (they are numeric markers only).
    Mask runs in raw Y-up OBJ space; verts are baked to world at the end."""
    sgn = -1.0 if side == "R" else 1.0
    verts, faces = load_obj_body_faces(os.path.join(DOWNLOAD, "base.obj"))
    V = np.array(verts)
    c, axis = fit_axis(V, side)
    rel = V - c
    d = rel @ axis
    radial = rel - np.outer(d, axis)
    r = np.linalg.norm(radial, axis=1)
    # raw OBJ space: Y-up, R arm at x<=-2.6, arm band y>=0.5
    keep_v = (d >= -1.55) & (d <= 1.35) & (r <= 1.8) & (V[:, 0] * sgn >= 2.6)

    bm = bmesh.new()
    vmap = {}
    body_faces = [f for f in faces if f[1] == "body"]
    for idx, grp in body_faces:
        loop = []
        for vi in idx:
            key = vi - 1
            if not keep_v[key]:
                loop = None
                break
            if key not in vmap:
                vmap[key] = bm.verts.new(V[key])
            loop.append(vmap[key])
        if loop and len(set(loop)) == len(loop):
            try:
                bm.faces.new(loop)
            except ValueError:
                pass  # duplicate face in source
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # seal the forearm cut with an n-gon cap (open tube backfaces otherwise
    # show as a flared sleeve from behind) and orient all normals outward
    ring = [e for e in bm.edges if e.is_boundary]
    if ring:
        bmesh.ops.holes_fill(bm, edges=ring)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # bake to world space (same +90degX as the importer object transform)
    for v in bm.verts:
        v.co = Vector(bake_x90(v.co[:]))
    me = obj.data
    bm.to_mesh(me)
    bm.free()
    me.update()

    # UV: fresh smart projection (source OBJ vt indices don't survive remap)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=66.0)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.shade_smooth()
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    log(f"body-only skin: verts={len(me.vertices)} tris={tris}")

def fingertip_positions(me, side):
    V = np.array([list(v.co) for v in me.vertices])
    tips = {}
    for key, seed in TIP_SEEDS_R.items():
        seed_baked = bake_x90(seed)  # same space as baked mesh data
        g = np.array(mirror(seed_baked) if side == "L" else seed_baked)
        dd = np.linalg.norm(V - g, axis=1)
        cand = V[dd < 0.3]
        if len(cand) == 0:
            tips[key] = list(g)
        else:
            tips[key] = list(cand[np.argmin(dd[dd < 0.3])])
    return tips

# ---------------------------------------------------------------- armature
def build_armature(side, tips):
    flip = side == "L"
    def m(p):
        return mirror(p) if flip else list(p)
    # all joint constants are authored in Y-up OBJ space; the mesh data lives in
    # the baked Z-up world, so bring the joints into the same space exactly once.
    J = {k: bake_x90(v) for k, v in JOINTS_R.items()}
    ELBOW = bake_x90(ELBOW_R)
    name = f"Armature.{side}"
    arm = bpy.data.armatures.new(name)
    ao = bpy.data.objects.new(name, arm)
    bpy.context.collection.objects.link(ao)
    bpy.context.view_layer.objects.active = ao
    bpy.ops.object.mode_set(mode="EDIT")
    ed = arm.edit_bones
    def bone(nm, head, tail, parent=None):
        b = ed.new(nm + "." + side)
        b.head = Vector(m(head)); b.tail = Vector(m(tail))
        if parent:
            b.parent = ed[parent + "." + side]
            b.use_connect = False
        return b
    bone("Forearm", ELBOW, J["wrist"])
    palm = [(J["index_mcp"][i] + J["little_mcp"][i]) / 2 for i in range(3)]
    bone("Wrist", J["wrist"], palm, "Forearm")
    # metacarpal segments (official mhskel geometry): wrist-tail -> MCP, covering the palm
    palm_root = [(J["index_mcp"][i] + J["little_mcp"][i]) / 2 for i in range(3)]
    palm_root = [(palm_root[0] + J["wrist"][0]) / 2, palm_root[1], palm_root[2]]
    metas = {
        "IndexMeta":  (J["wrist"], J["index_mcp"]),
        "MiddleMeta": (J["wrist"], J["middle_mcp"]),
        "RingMeta":   (palm_root, J["ring_mcp"]),
        "LittleMeta": (palm_root, J["little_mcp"]),
        "ThumbMeta":  (J["wrist"], J["thumb_cmc"]),
    }
    for nm, (h, t) in metas.items():
        bone(nm, h, t, "Wrist")
    chains = {
        "thumb":  [("thumb_cmc", "Thumb1"), ("thumb_mcp", "Thumb2"), ("thumb_ip", "Thumb3")],
        "index":  [("index_mcp", "Index1"), ("index_pip", "Index2"), ("index_dip", "Index3")],
        "middle": [("middle_mcp", "Middle1"), ("middle_pip", "Middle2"), ("middle_dip", "Middle3")],
        "ring":   [("ring_mcp", "Ring1"), ("ring_pip", "Ring2"), ("ring_dip", "Ring3")],
        "little": [("little_mcp", "Little1"), ("little_pip", "Little2"), ("little_dip", "Little3")],
    }
    meta_of = {"thumb": "ThumbMeta", "index": "IndexMeta", "middle": "MiddleMeta",
               "ring": "RingMeta", "little": "LittleMeta"}
    for key, joints in chains.items():
        # tips come in side-local baked coords; joints are baked R coords, and
        # m() applies the single side mirror on both consistently
        tip_side = mirror(tips[key]) if flip else tips[key]
        pts = [J[jn] for jn, _ in joints] + [tip_side]
        for i, (_, bn) in enumerate(joints):
            parent = meta_of[key] if i == 0 else joints[i - 1][1]
            bone(bn, pts[i], pts[i + 1], parent)
    # Align each finger/thumb bone with EditBone.align_roll so its local Z
    # points along the TRUE palm normal, derived from geometry IN THE SAME
    # armature space as the bones: normal = normalize((wrist->indexMCP) x
    # (wrist->littleMCP)) using the baked+joint-mirrored coordinates (J/m),
    # computed per side — never reuse the raw R vector for L.
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.mode_set(mode="EDIT")
    iw = Vector(m(J["index_mcp"])) - Vector(m(J["wrist"]))
    lw = Vector(m(J["little_mcp"])) - Vector(m(J["wrist"]))
    palm_normal = (iw.cross(lw)).normalized()
    if flip:
        # Mirror algebra (Codex): with X mirrored by M, same-order cross product
        # yields N_L = -M*N_R, but a mirrored rotation keeping the same signed
        # angle must use axis -M*X_R. Without this extra flip the L index curls
        # opposite to R for the same positive control. Flip once here (skeleton
        # side), NOT in the shared profile values.
        palm_normal = -palm_normal
    for b in arm.edit_bones:
        if b.name.startswith(("Forearm", "Wrist")):
            continue
        b.align_roll(palm_normal)
    bpy.ops.object.mode_set(mode="OBJECT")
    return ao

# ---------------------------------------------------------------- skinning
def skin(obj, arm, side):
    """Deterministic programmatic skinning: object parent + distance-based
    vertex groups + armature modifier. (ops ARMATURE_AUTO is unreliable headless.)"""
    import numpy as _np
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    bones = [(b.name, Vector(b.head_local), Vector(b.tail_local)) for b in arm.data.bones]
    n_v = len(obj.data.vertices)
    names = [b[0] for b in bones]
    wmat = _np.zeros((n_v, len(bones)), dtype=_np.float64)
    # pure distance weights: metacarpal bones now cover the palm, so nearest-bone
    # assignment is continuous across the wrist->MCP span (no hard partition needed)
    for vi, v in enumerate(obj.data.vertices):
        p = Vector(v.co)
        ds = []
        for bi, (bn, h, t) in enumerate(bones):
            ab = t - h; ap = p - h
            tt = max(0.0, min(1.0, ap.dot(ab) / max(ab.length_squared, 1e-9)))
            d = (ap - ab * tt).length
            ds.append((d, bi))
        ds.sort()
        # tip vertices beyond the last knuckle get a 50/50 blend with the parent
        # segment so a 200-degree DIP curl cannot fling them across the scene.
        far_tip = ds[0][0] > 0.25 and len(ds) > 1
        if far_tip:
            d0, b0 = ds[0]
            b1 = ds[1][1]
            wmat[vi, b0] = 0.6
            wmat[vi, b1] = 0.4
        else:
            top = ds[:3]
            total = sum((1.0 / max(d, 0.02)) ** 3 for d, _ in top)
            for d, bi in top:
                wmat[vi, bi] = ((1.0 / max(d, 0.02)) ** 3) / total
    for bi, bn in enumerate(names):
        g = obj.vertex_groups.new(name=bn)
        idx = [vi for vi in range(n_v) if wmat[vi, bi] > 1e-4]
        if idx:
            g.add(idx, 1.0, "REPLACE")  # temp 1.0 then set real values
            for vi in idx:
                g.add([vi], float(wmat[vi, bi]), "REPLACE")
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mod.use_vertex_groups = True
    log("skinned", obj.name, "bones", len(bones))

# ---------------------------------------------------------------- material + bake
SKIN_BASE = (0.86, 0.62, 0.50)
def skin_material(side):
    mat = bpy.data.materials.new(f"Skin.{side}")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = 0.45
    return mat, nt, bsdf

def bake_textures(obj, side, nt, bsdf, size=2048):
    sc = bpy.context.scene
    bpy.context.view_layer.objects.active = obj  # ensure mesh is active for baking
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 16
    me = obj.data
    if not me.uv_layers:
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=66.0)
        bpy.ops.object.mode_set(mode="OBJECT")
    imgs = {}
    for kind in ("diffuse", "normal", "roughness"):
        img = bpy.data.images.new(f"Skin_{side}_{kind}", size, size)
        img.filepath_raw = img.filepath = os.path.join(ASSETS, f"Hand_{side}_{kind}.png")
        imgs[kind] = img
    bsdf.inputs["Base Color"].default_value = (*SKIN_BASE, 1.0)
    def bake(kind, bake_type, pass_filter=None):
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = imgs[kind]
        nt.nodes.active = node
        bpy.context.view_layer.objects.active = obj
        for oo in bpy.context.selected_objects: oo.select_set(False)  # bake applies to selection
        obj.select_set(True)
        kw = dict(type=bake_type, margin=16, use_selected_to_active=False)
        if pass_filter: kw["pass_filter"] = pass_filter
        bpy.ops.object.bake(**kw)
        imgs[kind].file_format = "PNG"
        imgs[kind].save()
        log("baked", kind, side)
    bake("diffuse", "DIFFUSE", {"COLOR"})
    try: bake("roughness", "ROUGHNESS")
    except Exception as e: log("rough bake skip", e)
    try: bake("normal", "NORMAL")
    except Exception as e: log("normal bake skip", e)
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = imgs["diffuse"]
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if "roughness" in imgs:
        rtex = nt.nodes.new("ShaderNodeTexImage"); rtex.image = imgs["roughness"]
        nt.links.new(rtex.outputs["Color"], bsdf.inputs["Roughness"])

# ---------------------------------------------------------------- poses
POSE_ANGLES = {}  # (side, name) -> {bone: (x,y,z) degrees}

def _bend_axis(pb, arm):
    """Curl axis for a finger bone. In the rest A-pose, fingers extend along
    +Z and the palm faces -Y, so curling = rotating about world X. Mirror-safe
    because world-X rotation of an L-side bone chain curls toward the palm too."""
    return Vector((1.0, 0.0, 0.0))

def apply_pose(arm, side, name):
    """Local-euler curl: Blender bone local X is the curl axis; negative = flexion
    toward the palm (verified by render probe)."""
    sfx = "." + side
    angles = POSE_ANGLES[(side, name)]
    for bn, (x, y, z) in angles.items():
        pb = arm.pose.bones.get(bn + sfx)
        if pb is None: continue
        pb.rotation_mode = "XYZ"
        # sign is authored per pose (positive X curls palm-ward in this roll state)
        pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
    bpy.context.view_layer.update()

def clear_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()

# ---------------------------------------------------------------- poses
def pose_actions(arm, side):
    sfx = "." + side
    F = ["Index", "Middle", "Ring", "Little"]
    def act(name, angles):
        POSE_ANGLES[(side, name)] = dict(angles)
    a = {}
    for f in F:
        a[f+"1"] = (75,0,0); a[f+"2"] = (85,0,0); a[f+"3"] = (50,0,0)
    a["Thumb1"] = (30,0,25); a["Thumb2"] = (55,0,0); a["Thumb3"] = (40,0,0)
    act("fist", a)
    # pinch: thumb pad meets index pad. Thumb1 y-twist (about bone axis) swings
    # the thumb pad to face the palm; index curls to meet it.
    # pinch: IK-solved contact re-expressed in the Unity-FBX sign convention
    a = {"Index1": (45,0,0), "Index2": (86,0,0), "Index3": (22,0,0),
         "Thumb1": (30,80,20), "Thumb2": (71,0,0), "Thumb3": (110,0,0),
         "Middle1": (25,0,0), "Middle2": (35,0,0), "Middle3": (20,0,0),
         "Ring1": (30,0,0), "Ring2": (40,0,0), "Ring3": (25,0,0),
         "Little1": (35,0,0), "Little2": (45,0,0), "Little3": (30,0,0)}
    act("pinch", a)
    a = {}
    for i, f in enumerate(F):
        k = 8 + i * 4
        a[f+"1"] = (k,0,0); a[f+"2"] = (k+6,0,0); a[f+"3"] = (k,0,0)
    a["Thumb1"] = (8,0,8); a["Thumb2"] = (12,0,0); a["Thumb3"] = (8,0,0)
    act("relax", a)
    log("pose actions authored", side)

# ---------------------------------------------------------------- lighting & render
def studio(scene):
    if not scene.world:
        scene.world = bpy.data.worlds.new("Studio")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get("Background")
    if bg: bg.inputs[0].default_value = (0.05, 0.05, 0.06, 1.0)
    for i, (x, y, z, energy, sz) in enumerate([
            (2, -5, 8, 3000, 3.0), (-3, 4, -5, 1800, 1.6), (0, 5, 6, 4500, 1.2)]):
        lt = bpy.data.lights.new(f"key{i}", "AREA")
        lt.energy = energy; lt.size = sz
        ob = bpy.data.objects.new(f"key{i}", lt)
        ob.location = (x, y, z)
        bpy.context.collection.objects.link(ob)
        tgt = Vector((0, 0, 2.4))
        d = tgt - Vector(ob.location)
        ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

def render_at(filepath, focus, direction, dist=8.5, lens=85, res=(1280, 960)):
    sc = bpy.context.scene
    sc.render.filepath = os.path.join(REVIEW, os.path.basename(filepath) + ".png") \
        if not os.path.isabs(filepath) else filepath
    cam = bpy.data.cameras.new(filepath); cam.lens = lens
    co = bpy.data.objects.new(filepath, cam)
    bpy.context.collection.objects.link(co)
    d = Vector(direction).normalized()
    co.location = Vector(focus) + d * dist
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = res
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    log("rendered", os.path.basename(filepath))

def set_action(arm, pose_name_and_side):
    # "pose_fist_R" -> pose name "fist", side "R"
    parts = pose_name_and_side.split("_")
    side = parts[-1]
    name = parts[-2]
    apply_pose(arm, side, name)

def clear_action(arm):
    clear_pose(arm)

def reset_pose(arm):
    """Zero all pose rotations so saved blend/FBX are at rest."""
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()

# ---------------------------------------------------------------- driver
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "CYCLES"
    if sc.render.engine == "CYCLES": sc.cycles.samples = 32
    hands = {}
    for side in ("R", "L"):
        # body-only skin mesh built directly from the OBJ `g body` group; the
        # imported placeholder object is discarded (joint/helper boxes never load)
        bpy.ops.wm.obj_import(filepath=os.path.join(DOWNLOAD, "base.obj"))
        o = bpy.context.selected_objects[0]
        bpy.context.view_layer.objects.active = o
        # extract_arm rebuilds obj.data as body-only skin in baked world space
        # and the object transform is zeroed there; no extra bake needed here.
        o.matrix_world.identity()
        extract_arm(o, side)
        tips = fingertip_positions(o.data, side)
        arm = build_armature(side, tips)
        skin(o, arm, side)
        mat, nt, bsdf = skin_material(side)
        o.data.materials.clear(); o.data.materials.append(mat)
        bake_textures(o, side, nt, bsdf)
        pose_actions(arm, side)
        o.name = f"Hand.{side}"
        hands[side] = (o, arm)
        log(side, "tips", {k: [round(x, 2) for x in t] for k, t in tips.items()})
    # ---- review renders ----
    studio(sc)
    # rest-state review renders
    for side in ("R", "L"):
        o, arm = hands[side]
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        focus = sum(bb, Vector()) / 8.0
        log(side, "render focus", [round(v, 2) for v in focus])
        sx = 1 if side == "R" else -1
        render_at(f"hand_{side.lower()}_open", focus, (0.35*sx, -1, 0.25))
        render_at(f"hand_{side.lower()}_palm", focus, (0.0, -1, 0.05))
        render_at(f"hand_{side.lower()}_back", focus, (0.0, 1, 0.05))
        render_at(f"hand_{side.lower()}_side", focus, (1*sx, -0.3, 0.1))
    # ---- save at REST ----
    for side in ("R", "L"):
        reset_pose(hands[side][1])
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BUILD, "hands_source.blend"))
    log("saved hands_source.blend")

def export_wrist_local_fbx(side):
    """Export an identity root; capture both worlds before moving the parent."""
    o = bpy.data.objects[f"Hand.{side}"]
    arm = bpy.data.objects[f"Armature.{side}"]
    arm_world, mesh_world = arm.matrix_world.copy(), o.matrix_world.copy()
    wrist_w = arm_world @ Vector(arm.data.bones["Wrist." + side].head_local)
    tip_w = arm_world @ Vector(arm.data.bones["Middle3." + side].tail_local)
    metric = 0.19 / (tip_w - wrist_w).length
    bake = Matrix.Scale(metric, 4) @ Matrix.Translation(-wrist_w)
    root = bpy.data.objects.new(f"HandRoot.{side}", None)
    bpy.context.collection.objects.link(root)
    root.matrix_world = Matrix.Identity(4)
    arm.parent = root
    arm.matrix_parent_inverse = Matrix.Identity(4)
    arm.matrix_world = bake @ arm_world
    o.parent = arm
    o.matrix_parent_inverse = Matrix.Identity(4)
    o.matrix_world = bake @ mesh_world
    bpy.context.view_layer.update()
    wrist_after = arm.matrix_world @ Vector(arm.data.bones["Wrist." + side].head_local)
    if wrist_after.length > 1e-6:
        raise RuntimeError(f"{side} wrist is not at component origin: {wrist_after}")
    for selected in bpy.context.selected_objects:
        selected.select_set(False)
    for selected in (root, arm, o):
        selected.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(ASSETS, f"Hand_{side}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True,
        object_types={"ARMATURE", "MESH", "EMPTY"}, add_leaf_bones=False,
        bake_anim=False, mesh_smooth_type="OFF", path_mode="ABSOLUTE")
    log("exported wrist-local FBX", side, list(wrist_after), "metric", metric)
    return {"side": side, "file": path, "wristAtComponentOrigin": list(wrist_after),
            "metresPerSourceUnit": metric, "wristToMiddleBoneTipMetres": 0.19}


def render_posed_and_composite():
    """Reopen the clean saved .blend and do every posed render + FBX export there.
    (Pose-renders from the freshly-built in-memory scene exhibited artifacts that
    the same operations on a freshly-opened file do not; keep the verified path.)"""
    bpy.ops.wm.open_mainfile(filepath=os.path.join(BUILD, "hands_source.blend"))
    sc = bpy.context.scene
    for side in ("R", "L"):
        o = bpy.data.objects[f"Hand.{side}"]
        arm = bpy.data.objects[f"Armature.{side}"]
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        focus = sum(bb, Vector()) / 8.0
        sx = 1 if side == "R" else -1
        for pose in ("fist", "pinch"):
            set_action(arm, f"pose_{pose}_{side}")
            render_at(f"hand_{side.lower()}_{pose}", focus, (0.3*sx, -1, 0.2))
            clear_pose(arm)
    for side in ("R", "L"):
        export_wrist_local_fbx(side)
    # two-hand composites: rotate/move L about its own wrist pivot
    for side in ("R", "L"):
        arm = bpy.data.objects[f"Armature.{side}"]
        wrist = Vector(mirror(bake_x90(JOINTS_R["wrist"])) if side == "L" else bake_x90(JOINTS_R["wrist"]))
        target = Vector((-1.05, 0, 2.4)) if side == "R" else Vector((1.05, 0, 2.4))
        T = Matrix.Translation(target)
        if side == "L":
            T = T @ Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Translation(-wrist)
        else:
            T = T @ Matrix.Translation(-wrist)
        arm.matrix_world = T @ arm.matrix_world
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    allcoords = []
    for side in ("R", "L"):
        ev = bpy.data.objects[f"Hand.{side}"].evaluated_get(deps)
        for c in ev.bound_box:
            allcoords.append(ev.matrix_world @ Vector(c))
    mn = Vector((min(p.x for p in allcoords), min(p.y for p in allcoords), min(p.z for p in allcoords)))
    mx = Vector((max(p.x for p in allcoords), max(p.y for p in allcoords), max(p.z for p in allcoords)))
    mid = (mn + mx) / 2
    span = max(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z)
    two_dist = max(4.0, span * 1.35)
    render_at("hand_two_open", mid, (0.35, -1, 0.5), dist=two_dist, lens=60)
    for side in ("R", "L"):
        set_action(bpy.data.objects[f"Armature.{side}"], f"pose_fist_{side}")
    bpy.context.view_layer.update()
    allcoords = []
    for side in ("R", "L"):
        ev = bpy.data.objects[f"Hand.{side}"].evaluated_get(deps)
        for c in ev.bound_box:
            allcoords.append(ev.matrix_world @ Vector(c))
    mn = Vector((min(p.x for p in allcoords), min(p.y for p in allcoords), min(p.z for p in allcoords)))
    mx = Vector((max(p.x for p in allcoords), max(p.y for p in allcoords), max(p.z for p in allcoords)))
    mid = (mn + mx) / 2
    span = max(mx.x - mn.x, mx.y - mn.y, mx.z - mn.z)
    two_dist = max(4.0, span * 1.35)
    render_at("hand_two_fist", mid, (0.35, -1, 0.5), dist=two_dist, lens=60)

def main2():
    pass

if __name__ == "__main__":
    main()
    render_posed_and_composite()
