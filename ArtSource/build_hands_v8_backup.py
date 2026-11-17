# Game04 VirtualHand — hand build pipeline (Blender 5.2 batch).
# Base mesh: MakeHuman base.obj (CC0 2020, Data Collection AB).
# Joints: MakeHuman official default.mhskel joint-vertex table (authoritative).
# Output: Assets/VirtualHand/Models/Hand_*.fbx + 2K textures, ArtSource/Hands source .blend,
#         ArtSource/Review/*.png review renders.
import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
DOWNLOAD = os.path.join(HERE, "_download", "makehuman")
ASSETS = os.path.normpath(os.path.join(HERE, "..", "Assets", "VirtualHand", "Models"))
REVIEW = os.path.join(HERE, "Review")
BUILD = os.path.join(HERE, "Hands")
for d in (ASSETS, REVIEW, BUILD):
    os.makedirs(d, exist_ok=True)

def log(*a): print("VH:", *a, flush=True)

# ---------------------------------------------------------------- joints
# From default.mhskel "joints" (vertex-weighted joint positions), right side.
# finger1 = thumb; metacarpal1..4 = index..little metacarpals (their tails = finger2..5 MCP).
JOINTS = {
    "wrist":    [-4.321, 2.402, 1.773],
    "thumb_cmc":[-4.181, 2.303, 2.164],
    "thumb_mcp":[-4.081, 2.237, 2.412],
    "thumb_ip": [-3.976, 2.095, 2.707],
    "index_mcp   ": None,
}
FINGERS = {  # name: (mcp, pip, dip)
    "index":  ([-4.510, 2.011, 2.624], [-4.531, 1.876, 2.875], [-4.561, 1.712, 3.003]),
    "middle": ([-4.620, 1.878, 2.458], [-4.689, 1.702, 2.711], [-4.761, 1.480, 2.823]),
    "ring":   ([-4.702, 1.815, 2.286], [-4.798, 1.625, 2.490], [-4.856, 1.428, 2.553]),
    "little": ([-4.753, 1.741, 2.136], [-4.808, 1.587, 2.251], [-4.833, 1.434, 2.290]),
}
ELBOW = [-3.276, 3.364, 0.334]
WRIST_TAIL = [-4.279, 2.236, 1.989]

def obj_vertices(path):
    vs = []
    with open(path, encoding="utf-8", errors="ignore") as f:
        for line in f:
            if line.startswith("v "):
                p = line.split()
                vs.append((float(p[1]), float(p[2]), float(p[3])))
    return np.array(vs)

def arm_axis(V):
    """PCA axis of right-arm cloud (x<=-3, y>=0.5), pointing elbow->hand (+t toward fingertips)."""
    M = V[(V[:, 0] <= -3.0) & (V[:, 1] >= 0.5)]
    c = M.mean(axis=0)
    u, s, vt = np.linalg.svd(M - c, full_matrices=False)
    axis = vt[0]
    if axis[2] < 0:  # fingertips are +z
        axis = -axis
    return c, axis

# ---------------------------------------------------------------- step 1: extract arm region
def extract_arm(side):  # mesh is pre-mirrored so both sides share the x>=0.9 code path
    sign = -1.0
    obj = bpy.context.selected_objects[0]
    me = obj.data
    V = np.array([list(v.co) for v in me.vertices])
    c, axis = arm_axis(V)
    d = (V * np.array([sign, 1, 1]) - c) @ (axis * np.array([sign, 1, 1]))
    # keep: within tube around axis, t range covers forearm stub + hand
    radial = (V * np.array([sign, 1, 1]) - c) - np.outer(d, axis * np.array([sign, 1, 1]))
    r = np.linalg.norm(radial, axis=1)
    keep = (d >= -1.9) & (d <= 1.35) & (r <= 1.7) & (V[:, 0] * sign <= -2.6)
    # delete rest
    bm = me  # use bmesh-free path via select
    for v in me.vertices:
        v.select = False
    bad = np.where(~keep)[0]
    import bmesh
    bmv = bmesh.new(); bmv.from_mesh(me)
    bpy.ops.object.mode_set(mode="OBJECT")
    sel = [False] * len(me.vertices)
    for i in bad: sel[i] = True
    for i, v in enumerate(me.vertices): v.select = sel[i]
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.delete(type="VERT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    if side == "L":
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.flip_normals()
    bpy.ops.mesh.select_all(action="SELECT")
    # fill the wrist boundary ring robustly via bmesh hole fill
    import bmesh as _bm
    bmh = _bm.from_edit_mesh(me)
    _mesh_holes = [f for f in bmh.faces if False]
    edges = [e for e in bmh.edges if e.is_boundary or e.is_wire]
    bmesh.ops.holes_fill(bmh, edges=edges)
    bmesh.ops.triangulate(bmh, faces=[f for f in bmh.faces if len(f.verts) > 4])
    _bm.update_edit_mesh(me)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.name = f"Hand.{side}"
    log(f"arm {side} extracted, verts={len(me.vertices)} tris={sum(len(p.vertices)-2 for p in me.polygons)}")

# ---------------------------------------------------------------- step 2: armature
def make_bone(arm_ed, name, head, tail, parent=None):
    b = arm_ed.new(name)
    b.head = Vector(head)
    b.tail = Vector(tail)
    if parent:
        pb = arm_ed.get(parent)
        if pb is not None:
            b.parent = pb
            b.use_connect = False
    return b

def build_armature(side, tip_pos):
    sfx = "." + side
    s = -1.0  # both meshes are normalized onto the x>=0.9 side
    def m(p): return [p[0] * s, p[1], p[2]]
    name = f"Armature.{side}"
    arm = bpy.data.armatures.new(name)
    ao = bpy.data.objects.new(name, arm)
    bpy.context.collection.objects.link(ao)
    bpy.context.view_layer.objects.active = ao
    bpy.ops.object.mode_set(mode="EDIT")
    ed = arm.edit_bones
    make_bone(ed, "Forearm" + sfx, m(ELBOW), m(JOINTS["wrist"]))
    make_bone(ed, "Wrist" + sfx, m(JOINTS["wrist"]), m(WRIST_TAIL), "Forearm" + sfx)
    chains = {
        "thumb":  ([m(JOINTS["thumb_cmc"]), m(JOINTS["thumb_mcp"]), m(JOINTS["thumb_ip"])], tip_pos[side]["thumb"], ["Thumb1", "Thumb2", "Thumb3"]),
        "index":  ([m(p) for p in FINGERS["index"]], tip_pos[side]["index"], ["Index1", "Index2", "Index3"]),
        "middle": ([m(p) for p in FINGERS["middle"]], tip_pos[side]["middle"], ["Middle1", "Middle2", "Middle3"]),
        "ring":   ([m(p) for p in FINGERS["ring"]], tip_pos[side]["ring"], ["Ring1", "Ring2", "Ring3"]),
        "little": ([m(p) for p in FINGERS["little"]], tip_pos[side]["little"], ["Little1", "Little2", "Little3"]),
    }
    for key, (js, tip, names) in chains.items():
        pts = js + [tip]
        for i, nm in enumerate(names):
            parent = ("Wrist" + sfx) if i == 0 else (names[i - 1] + sfx)
            make_bone(ed, nm + sfx, pts[i], pts[i + 1], parent)
    bpy.ops.object.mode_set(mode="OBJECT")
    return ao

def fingertip_positions(side, me):
    """Geometric fingertip: cluster of vertices most distal along each finger direction."""
    s = -1.0  # both meshes are normalized onto the x>=0.9 side
    V = np.array([list(v.co) for v in me.vertices])
    tips = {}
    chains = {
        "thumb":  ([JOINTS["thumb_cmc"], JOINTS["thumb_mcp"], JOINTS["thumb_ip"]], [-3.991, 2.005, 2.846]),
        "index":  (FINGERS["index"], [-4.58, 1.62, 3.15]),
        "middle": (FINGERS["middle"], [-4.80, 1.35, 2.95]),
        "ring":   (FINGERS["ring"], [-4.90, 1.30, 2.70]),
        "little": (FINGERS["little"], [-4.86, 1.33, 2.42]),
    }
    for key, (js, guess) in chains.items():
        g = np.array(guess) * np.array([s, 1, 1])
        # tip = vertex with max travel beyond last joint, within 0.6u radius of seed guess
        dd = np.linalg.norm(V - g, axis=1)
        cand = V[dd < 0.45]
        tip = cand[np.argmax(cand[:, 2] if key != "thumb" else cand[:, 2])] if len(cand) else g
        tips[key] = list(tip)
    tips["thumb"] = [tips["thumb"][0], tips["thumb"][1], tips["thumb"][2]]
    return tips

# ---------------------------------------------------------------- step 3: skinning
def skin(obj, arm):
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = obj
    sel = bpy.context.selected_objects[:]
    for o in sel: o.select_set(True)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    # smooth weights
    bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
    bpy.ops.object.vertex_group_smooth(group_select_mode="ALL", factor=0.5, repeat=4)
    bpy.ops.object.mode_set(mode="OBJECT")

# ---------------------------------------------------------------- step 4: material + bake
def skin_material(side):
    mat = bpy.data.materials.new(f"Skin.{side}")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    # procedural skin albedo: warm base + noise mottling
    base = nt.nodes.new("ShaderNodeRGB"); base.outputs[0].default_value = (0.86, 0.62, 0.50, 1.0)
    n1 = nt.nodes.new("ShaderNodeTexNoise"); n1.inputs["Scale"].default_value = 6.0; n1.inputs["Detail"].default_value = 8.0
    n2 = nt.nodes.new("ShaderNodeTexNoise"); n2.inputs["Scale"].default_value = 28.0; n2.inputs["Detail"].default_value = 10.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.78, 0.55, 0.44, 1.0)
    ramp.color_ramp.elements[1].color = (0.93, 0.72, 0.62, 1.0)
    mix = nt.nodes.new("ShaderNodeMixRGB"); mix.blend_type = "MIX"; mix.inputs["Fac"].default_value = 0.45
    nt.links.new(n1.outputs["Fac"], mix.inputs["Color1"])
    nt.links.new(n2.outputs["Fac"], mix.inputs["Color2"])
    nt.links.new(base.outputs[0], mix.inputs["Color1"])
    nt.links.new(ramp.outputs["Color"], mix.inputs["Color2"])
    n1a = nt.nodes.new("ShaderNodeTexNoise"); n1a.outputs[0].default_value
    nt.links.new(n1.outputs["Fac"], mix.inputs["Fac"])
    nt.links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.45
    bsdf.inputs["Specular IOR Level"].default_value = 0.3 if "Specular IOR Level" in bsdf.inputs else 0.3
    return mat, nt, bsdf, mix, n1, n2

def bake_textures(obj, side, size=2048):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 16
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    mat, nt, bsdf, mix, n1, n2 = skin_material(side)
    obj.data.materials.clear(); obj.data.materials.append(mat)
    imgs = {}
    for kind in ("diffuse", "normal", "roughness"):
        img = bpy.data.images.new(f"Skin_{side}_{kind}", size, size)
        imgs[kind] = img
    def bake_to(kind, bake_type, pass_filter=None):
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = imgs[kind]
        nt.nodes.active = node
        bpy.context.view_layer.objects.active = obj
        kw = dict(type=bake_type, margin=16, use_selected_to_active=False)
        if pass_filter: kw["pass_filter"] = pass_filter
        bpy.ops.object.bake(**kw)
        imgs[kind].filepath_raw = imgs[kind].filepath = os.path.join(ASSETS, f"Hand_{side}_{kind}.png")
        imgs[kind].file_format = "PNG"
        imgs[kind].save()
        log(f"baked {kind} {side}")
    try:
        bake_to("diffuse", "DIFFUSE", {"COLOR"})
    except Exception as e:
        log("diffuse bake failed, fallback:", e)
    try:
        bake_to("roughness", "ROUGHNESS")
    except Exception as e:
        log("rough bake failed:", e)
    try:
        bake_to("normal", "NORMAL")
    except Exception as e:
        log("normal bake failed:", e)
    # relink baked images as the exported material
    nt.links.new(nt.nodes.new("ShaderNodeTexImage"), bsdf.inputs["Base Color"]) if False else None
    for k, sock in (("diffuse", "Base Color"), ("roughness", "Roughness")):
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = imgs[k]
        nt.links.new(tex.outputs["Color"], bsdf.inputs[sock])
    nrm = nt.nodes.new("ShaderNodeTexImage"); nrm.image = imgs["normal"]
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    return imgs

# ---------------------------------------------------------------- step 5: pose actions
def pose_actions(arm, side):
    sfx = "." + side
    def act(name, angles):
        a = bpy.data.actions.new(f"pose_{name}_{side}")
        arm.animation_data_create()
        arm.animation_data.action = a
        for bone, (x, y, z) in angles.items():
            pb = arm.pose.bones.get(bone + sfx)
            if pb is None: continue
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
            pb.keyframe_insert("rotation_euler", frame=1)
    F = ["Index", "Middle", "Ring", "Little"]
    # fist: strong flexion, thumb wraps over
    a = {}
    for f in F:
        a[f + "1"] = (85, 0, 0); a[f + "2"] = (105, 0, 0); a[f + "3"] = (60, 0, 0)
    a["Thumb1"] = (30, 0, 25); a["Thumb2"] = (55, 0, 0); a["Thumb3"] = (40, 0, 0)
    act("fist", a)
    # pinch: thumb pad meets index pad
    a = {"Index1": (55, 0, 0), "Index2": (35, 0, 0), "Index3": (20, 0, 0)}
    a["Thumb1"] = (35, 20, 40); a["Thumb2"] = (45, 0, 0); a["Thumb3"] = (25, 0, 0)
    a["Middle1"] = (25, 0, 0); a["Middle2"] = (35, 0, 0); a["Middle3"] = (20, 0, 0)
    a["Ring1"] = (30, 0, 0); a["Ring2"] = (40, 0, 0); a["Ring3"] = (25, 0, 0)
    a["Little1"] = (35, 0, 0); a["Little2"] = (45, 0, 0); a["Little3"] = (30, 0, 0)
    act("pinch", a)
    # point: index straight, others curled
    a = {}
    for i, f in enumerate(F):
        v = 0 if f == "Index" else 85
        a[f + "1"] = (v, 0, 0); a[f + "2"] = (0 if v == 0 else 105, 0, 0); a[f + "3"] = (0 if v == 0 else 60, 0, 0)
    a["Thumb1"] = (15, 0, 10); a["Thumb2"] = (25, 0, 0); a["Thumb3"] = (15, 0, 0)
    act("point", a)
    # relax: gentle curl
    a = {}
    for i, f in enumerate(F):
        k = 8 + i * 4
        a[f + "1"] = (k, 0, 0); a[f + "2"] = (k + 6, 0, 0); a[f + "3"] = (k, 0, 0)
    a["Thumb1"] = (8, 0, 8); a["Thumb2"] = (12, 0, 0); a["Thumb3"] = (8, 0, 0)
    act("relax", a)
    log("pose actions authored for", side)

# ---------------------------------------------------------------- step 6: render helpers
def studio(scene):
    world = bpy.data.worlds.new("Studio") if not scene.world else scene.world
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg: bg.inputs[0].default_value = (0.05, 0.05, 0.06, 1.0)
    keys = [(4, 6, 8, 3000, 3.0), (-6, 4, -5, 2500, 1.6), (0, -6, 6, 5000, 1.2)]
    for i, (x, y, z, energy, sz) in enumerate(keys):
        lt = bpy.data.lights.new(f"key{i}", "AREA")
        lt.energy = energy; lt.size = sz
        ob = bpy.data.objects.new(f"key{i}", lt)
        ob.location = (x, z, y)
        bpy.context.collection.objects.link(ob)
        # aim at wrist area
        tgt = Vector((4.6, 2.0, 2.6))
        d = tgt - Vector(ob.location)
        ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

def render_view(obj, arm, filepath, quat=None, res=(1280, 960)):
    sc = bpy.context.scene
    cam = bpy.data.cameras.new("cam"); cam.lens = 85
    co = bpy.data.objects.new("cam", cam)
    bpy.context.collection.objects.link(co)
    sc.camera = co
    focus = Vector((-4.5, 2.0, 2.5)) if obj.name.endswith(".R") else Vector((4.5, 2.0, 2.5))
    co.location = focus + Vector((0, -16, 0))
    if quat: co.rotation_euler = quat
    else:
        d = Vector((0.4, -8.5, 0.8))
        co.location = focus + d.normalized() * 10
        co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.render.resolution_x, sc.render.resolution_y = res
    try:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    except Exception:
        try: sc.render.engine = "BLENDER_EEVEE"
        except Exception: sc.render.engine = "CYCLES"; sc.cycles.samples = 32
    sc.render.filepath = filepath
    bpy.ops.render.render(write_still=True)
    log("rendered", filepath)

# ---------------------------------------------------------------- driver
def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    # import base mesh once, split into two arms
    bpy.ops.wm.obj_import(filepath=os.path.join(DOWNLOAD, "base.obj"))
    base = bpy.context.selected_objects[0]
    log("imported", base.name, len(base.data.vertices))
    tips = {}
    for side in ("R", "L"):
        bpy.ops.wm.obj_import(filepath=os.path.join(DOWNLOAD, "base.obj"))
        o = bpy.context.selected_objects[0]
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        if side == "L":
            # mirror the other arm onto the same code path (x->-x), then fix normals
            for v in o.data.vertices:
                v.co.x = -v.co.x
        extract_arm(side)
        tips[side] = fingertip_positions(side, o.data)
        log(side, "tips", {k: [round(v, 2) for v in t] for k, t in tips[side].items()})
        arm = build_armature(side, tips)
        skin(o, arm)
        bake_textures(o, side)
        pose_actions(arm, side)
        o.name = f"Hand.{side}"
    # review renders
    studio(sc)
    r = sc.render
    r.resolution_x, r.resolution_y = 1280, 960
    for side in ("R", "L"):
        arm = bpy.data.objects[f"Armature.{side}"]
        hand = bpy.data.objects[f"Hand.{side}"]
        focus = Vector((4.6, 2.0, 2.6))
        views = {
            "palm":   ((0, -1, 0.05),),
            "back":   ((0, 1, 0.05),),
            "side":   ((1, -0.3, 0.1),),
            "fist":   ((0, -1, 0.15),),
            "pinch":  ((0.3, -1, 0.2),),
        }
        for nm, (dq,) in views.items():
            cam = bpy.data.cameras.new(nm); cam.lens = 85
            co = bpy.data.objects.new(nm, cam)
            bpy.context.collection.objects.link(co)
            d = Vector(dq)
            if side == "L": d = Vector((-d.x, d.y, d.z))
            co.location = focus + d.normalized() * 16.0
            co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
            # apply pose
            for a in arm.animation_data.action_slot if False else []:
                pass
            slots = None
            # set pose from authored actions one at a time
            want = {"fist": "pose_fist", "pinch": "pose_pinch"}.get(nm)
            sc.camera = co
            # stash current
            cur = arm.animation_data.action if arm.animation_data else None
            if want:
                act = bpy.data.actions.get(want + "_" + side)
                if act:
                    arm.animation_data.action = act
                    if act.slots: arm.animation_data.action_slot = act.slots[0]
                    bpy.context.view_layer.update()
            r.filepath = os.path.join(REVIEW, f"hand_{side.lower()}_{nm}.png")
            bpy.ops.render.render(write_still=True)
            arm.animation_data.action = cur
            bpy.data.objects.remove(co)
        # neutral open-pose three-quarter view
        cam = bpy.data.cameras.new("threequarter"); cam.lens = 85
        co = bpy.data.objects.new("threequarter", cam)
        bpy.context.collection.objects.link(co)
        d = Vector((-0.7, -1, 0.35)) if side == "R" else Vector((0.7, -1, 0.35))
        co.location = focus + d.normalized() * 16.0
        co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        sc.camera = co
        r.filepath = os.path.join(REVIEW, f"hand_{side.lower()}_open.png")
        bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(co)
    # two hands together (A-pose forearms entering from top corners)
    cam = bpy.data.cameras.new("twohands"); cam.lens = 50
    co = bpy.data.objects.new("twohands", cam)
    bpy.context.collection.objects.link(co)
    co.location = (0, -8.5, 4.2)
    co.rotation_euler = (math.radians(78), 0, 0)
    sc.camera = co
        # offset each hand rig toward the center, L rotated 180deg so palms face each other
    offs = {"R": (-3.9, 0, 0, 0.0), "L": (3.9, 0, 0, math.pi)}
    for side in ("R", "L"):
        for oname in (f"Armature.{side}", f"Hand.{side}"):
            ob = bpy.data.objects[oname]
            dx, dy, dz, rz = offs[side]
            ob.location.x += dx; ob.location.z += dz
            if side == "L": ob.rotation_euler.z += rz
        arm = bpy.data.objects[f"Armature.{side}"]
        act = bpy.data.actions.get(f"pose_fist_{side}")
        if act:
            arm.animation_data.action = act
            if act.slots: arm.animation_data.action_slot = act.slots[0]
    bpy.context.view_layer.update()
    r.filepath = os.path.join(REVIEW, "hand_two_open.png")
    bpy.ops.render.render(write_still=True)
    # apply fist on both for holding view
    r.filepath = os.path.join(REVIEW, "hand_two_fist.png")
    bpy.ops.render.render(write_still=True)
    # export FBX per hand (armature + skinned mesh + baked textures alongside)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BUILD, "hands_source.blend"))
    for side in ("R", "L"):
        hand = bpy.data.objects[f"Hand.{side}"]
        arm = bpy.data.objects[f"Armature.{side}"]
        for o in bpy.context.selected_objects: o.select_set(False)
        hand.select_set(True); arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(ASSETS, f"Hand_{side}.fbx"),
            use_selection=True, global_scale=0.1, apply_unit_scale=True,
            object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
            bake_anim=False, mesh_smooth_type="OFF", path_mode="RELATIVE")
        log("exported FBX", side)
    log("saved hands_source.blend")

main()
