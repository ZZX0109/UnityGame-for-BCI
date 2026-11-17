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

# ---------------------------------------------------------------- mesh extraction
def fit_axis(V):
    """PCA on the right-arm cloud; axis points elbow->fingertips."""
    M = V[(V[:, 0] <= -3.0) & (V[:, 1] >= 0.5)]
    c = M.mean(axis=0)
    _, _, vt = np.linalg.svd(M - c, full_matrices=False)
    axis = vt[0]
    if axis[2] < 0:
        axis = -axis
    return c, axis

def extract_arm(obj):
    me = obj.data
    V = np.array([list(v.co) for v in me.vertices])
    c, axis = fit_axis(V)
    rel = V - c
    d = rel @ axis
    radial = rel - np.outer(d, axis)
    r = np.linalg.norm(radial, axis=1)
    keep = (d >= -1.55) & (d <= 1.35) & (r <= 1.8) & (V[:, 0] <= -2.6)
    bad = np.where(~keep)[0]
    # delete via bmesh directly (mode-independent, reliable in 5.2)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in bad], context="VERTS")
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    ring = [e for e in bm.edges if e.is_boundary]
    bmesh.ops.holes_fill(bm, edges=ring)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth()
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    log(f"extracted verts={len(me.vertices)} tris={tris}")

def fingertip_positions(me):
    V = np.array([list(v.co) for v in me.vertices])
    tips = {}
    for key, seed in TIP_SEEDS_R.items():
        g = np.array(seed)
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
    bone("Forearm", ELBOW_R, JOINTS_R["wrist"])
    palm = [(JOINTS_R["index_mcp"][i] + JOINTS_R["little_mcp"][i]) / 2 for i in range(3)]
    bone("Wrist", JOINTS_R["wrist"], palm, "Forearm")
    chains = {
        "thumb":  [("thumb_cmc", "Thumb1"), ("thumb_mcp", "Thumb2"), ("thumb_ip", "Thumb3")],
        "index":  [("index_mcp", "Index1"), ("index_pip", "Index2"), ("index_dip", "Index3")],
        "middle": [("middle_mcp", "Middle1"), ("middle_pip", "Middle2"), ("middle_dip", "Middle3")],
        "ring":   [("ring_mcp", "Ring1"), ("ring_pip", "Ring2"), ("ring_dip", "Ring3")],
        "little": [("little_mcp", "Little1"), ("little_pip", "Little2"), ("little_dip", "Little3")],
    }
    for key, joints in chains.items():
        pts = [JOINTS_R[jn] for jn, _ in joints] + [tips[key]]
        for i, (_, bn) in enumerate(joints):
            parent = "Wrist" if i == 0 else joints[i - 1][1]
            bone(bn, pts[i], pts[i + 1], parent)
    bpy.ops.object.mode_set(mode="OBJECT")
    return ao

# ---------------------------------------------------------------- skinning
def skin(obj, arm):
    """Deterministic programmatic skinning: object parent + distance-based
    vertex groups + armature modifier. (ops ARMATURE_AUTO is unreliable headless.)"""
    import numpy as _np
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    bones = [(b.name, Vector(b.head_local), Vector(b.tail_local)) for b in arm.data.bones]
    n_v = len(obj.data.vertices)
    names = [b[0] for b in bones]
    wmat = _np.zeros((n_v, len(bones)), dtype=_np.float64)
    for vi, v in enumerate(obj.data.vertices):
        p = Vector(v.co)
        ds = []
        for bi, (bn, h, t) in enumerate(bones):
            ab = t - h; ap = p - h
            tt = max(0.0, min(1.0, ap.dot(ab) / max(ab.length_squared, 1e-9)))
            d = (ap - ab * tt).length
            ds.append((d, bi))
        ds.sort()
        top = ds[:3]
        total = sum((1.0 / max(d, 0.015)) ** 4 for d, _ in top)
        for d, bi in top:
            wmat[vi, bi] = ((1.0 / max(d, 0.015)) ** 4) / total
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

def apply_pose(arm, side, name):
    sfx = "." + side
    for bn, (x, y, z) in POSE_ANGLES[(side, name)].items():
        pb = arm.pose.bones.get(bn + sfx)
        if pb is None: continue
        pb.rotation_mode = "XYZ"
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
        a[f+"1"] = (85,0,0); a[f+"2"] = (105,0,0); a[f+"3"] = (60,0,0)
    a["Thumb1"] = (30,0,25); a["Thumb2"] = (55,0,0); a["Thumb3"] = (40,0,0)
    act("fist", a)
    a = {"Index1": (55,0,0), "Index2": (35,0,0), "Index3": (20,0,0),
         "Thumb1": (35,20,40), "Thumb2": (45,0,0), "Thumb3": (25,0,0),
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

def render_at(filepath, focus, direction, dist=6.5, lens=85, res=(1280, 960)):
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
    side = pose_name_and_side.rsplit("_", 1)[1]
    name = pose_name_and_side.rsplit("_", 1)[0]
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
        bpy.ops.wm.obj_import(filepath=os.path.join(DOWNLOAD, "base.obj"))
        o = bpy.context.selected_objects[0]
        bpy.context.view_layer.objects.active = o
        if side == "L":
            for v in o.data.vertices:
                v.co.x = -v.co.x
        extract_arm(o)
        tips = fingertip_positions(o.data)
        arm = build_armature(side, tips)
        skin(o, arm)
        mat, nt, bsdf = skin_material(side)
        o.data.materials.clear(); o.data.materials.append(mat)
        bake_textures(o, side, nt, bsdf)
        pose_actions(arm, side)
        o.name = f"Hand.{side}"
        hands[side] = (o, arm)
        log(side, "tips", {k: [round(x, 2) for x in t] for k, t in tips.items()})
    # ---- review renders ----
    studio(sc)
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
        for pose in ("fist", "pinch"):
            set_action(arm, f"pose_{pose}_{side}")
            render_at(f"hand_{side.lower()}_{pose}", focus, (0.3*sx, -1, 0.2))
            clear_action(arm)
    # ---- save + export at REST, before any compositing ----
    for side in ("R", "L"):
        reset_pose(hands[side][1])
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BUILD, "hands_source.blend"))
    log("saved hands_source.blend")
    for side in ("R", "L"):
        o, arm = hands[side]
        for oo in bpy.context.selected_objects: oo.select_set(False)
        o.select_set(True); arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(ASSETS, f"Hand_{side}.fbx"),
            use_selection=True, global_scale=0.1, apply_unit_scale=True,
            object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
            bake_anim=False, mesh_smooth_type="OFF", path_mode="ABSOLUTE")
        log("exported FBX", side)
    # ---- two-hand composites (post-export): rotate L about its own wrist pivot ----
    for side in ("R", "L"):
        o, arm = hands[side]
        wrist = Vector(mirror(JOINTS_R["wrist"]) if side == "L" else JOINTS_R["wrist"])
        target = Vector((-1.05, 0, 2.4)) if side == "R" else Vector((1.05, 0, 2.4))
        T = Matrix.Translation(target)
        if side == "L":
            Rz = Matrix.Rotation(math.pi, 4, 'Z')
            T = T @ Rz @ Matrix.Translation(-wrist)
        else:
            T = T @ Matrix.Translation(-wrist)
        arm.matrix_world = T @ arm.matrix_world
    bpy.context.view_layer.update()
    mid = Vector((0, 0, 2.9))
    render_at("hand_two_open", mid, (0.35, -1, 0.5), dist=7.5, lens=60)
    for side in ("R", "L"):
        set_action(hands[side][1], f"pose_fist_{side}")
    render_at("hand_two_fist", mid, (0.35, -1, 0.5), dist=7.5, lens=60)

main()
