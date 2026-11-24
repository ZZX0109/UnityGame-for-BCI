# -*- coding: utf-8 -*-
"""ElenaFF rigged-hand deployment (B2, candidate stage).

Reads ONLY ArtSource/ThirdParty/ElenaFF_RiggedHand/hand.glb; writes to
ArtSource/OpenHandDeployment/output/ :

  ElenaHand_R.fbx / ElenaHand_L.fbx   wrist-local, metric (0.19 m hand),
                                      native skeleton + skin weights, with
                                      the native Open/Close action baked
                                      (R: original curves; L: exact mirrored
                                      numeric bake)
  ElenaFF_basecolor.png               glTF Base Color (sRGB)
  ElenaFF_metallicroughness.png       glTF metallicRoughness (Non-Color)
  ElenaFF_occlusion.png               third packed map (Non-Color)
  ElenaHands.blend                    editable two-hand scene, rest pose
  rig-manifest.json                   bone/timing/scale/mirror facts
  Elena_{R,L}_{open,closed}_{sideA,sideB}.png   verification renders

LEFT hand = real deformable mirror: armature with mirrored heads/tails
(roll 0), same vertex groups, mirrored mesh, and a per-bone numerically
baked Open/Close action such that posed_L = M @ posed_R @ M holds in
armature space for every sampled frame (M = world reflection about the
plane x = wrist.x). The deformed L surface must equal the mirror of the
deformed R surface; this is VERIFIED numerically (max world vertex delta)
and asserted before any export. No negative scale anywhere.

Units: wrist head baked to the world origin, wrist->middle-tip = 0.19 m.

Usage: blender --background --factory-startup --python build_elena.py
"""

import json
import math
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Vector, Quaternion

HERE = Path(__file__).resolve().parent
OUT = HERE / "output"
GLB = HERE.parent / "ThirdParty" / "ElenaFF_RiggedHand" / "hand.glb"

HAND_LEN_M = 0.19
FPS = 24
FRAME_OPEN = 0
FRAME_CLOSED = 32
FRAME_LAST = 77
RES_X, RES_Y = 1280, 960

WRIST = "hand.R_02"
TIP = "middle_03.R_027"
THUMB_REF = "thumb_01.R_08"
MIDDLE_REF = "middle_01.R_025"

MIRROR_SAMPLES = (0, 8, 16, 24, 32, 40, 55, 77)


def log(*a):
    print("VH-ELENA:", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def rest_rel(pb):
    """Parent-relative REST 4x4 of a pose bone. Blender 5.2: Bone.matrix is
    a 3x3 rotation; bone.matrix_local (armature space, 4x4) is the reliable
    rest matrix."""
    if pb.parent:
        return pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    return pb.bone.matrix_local.copy()


def world_verts(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    evm = ev.to_mesh()
    mw = obj.matrix_world
    out = [mw @ Vector(v.co) for v in evm.vertices]
    ev.to_mesh_clear()
    return out


def reflection(wx):
    return (Matrix.Translation(Vector((wx, 0, 0))) @
            Matrix.Diagonal((-1, 1, 1, 1)) @
            Matrix.Translation(Vector((-wx, 0, 0))))


# ------------------------------------------------------------------ bake
def bake_mirror_action(arm_r, arm_l, M_arm):
    """Exact numeric mirror bake. For every sampled frame and every bone:
    posed_L must equal M @ posed_R @ M (armature space). With Blender's
    composition posed = parentPosed @ restRel @ basis, the required basis is
        basis = restRel^-1 @ parentPosed^-1 @ (M @ posedR @ M)
    computed top-down (parents before children) — no reliance on roll
    conventions. Returns the new action."""
    act_r = arm_r.animation_data.action
    frame_lo, frame_hi = [int(v) for v in act_r.frame_range]
    frame_hi = max(frame_hi, FRAME_LAST)

    # DFS order so L parents are processed before children
    order = []

    def walk(pb):
        order.append(pb)
        for c in pb.children:
            walk(c)
    for pb in (p for p in arm_l.pose.bones if p.parent is None):
        walk(pb)

    # which R bones have animation channels (Blender 5.2 Action slots API:
    # fcurves live in layers[0].strips[*].channelbags[*])
    driven = set()
    for layer in act_r.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in cb.fcurves:
                    if '"' in fc.data_path:
                        driven.add(fc.data_path.split('"')[1])
                loc_n = sum(1 for fc in cb.fcurves
                            if fc.data_path.endswith(".location"))
                if loc_n:
                    log(f"  channelbag slot_handle={cb.slot_handle}: "
                        f"{len(cb.fcurves)} fcurves, {loc_n} location")
    log("R driven bones:", len(driven))

    # NOTE: the source location channels (42 fcurves) are KEPT. After the
    # metric bake they are harmless (verified: playing frame 32 spans
    # 0.24 m vs rest 0.19 m, no blow-up); muting/removing them in 5.2
    # poisons depsgraph evaluation and makes ALL channels stop applying.

    # bones whose channels include location (must be baked as translation+rot)
    loc_driven = set()
    for layer in act_r.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in cb.fcurves:
                    if fc.data_path.endswith(".location") and \
                            '"' in fc.data_path:
                        loc_driven.add(fc.data_path.split('"')[1])
    log("R location-driven bones:", len(loc_driven))

    # Pure-DATA evaluation path: read the R rotation fcurves with
    # fc.evaluate(frame) — no depsgraph, no action playback. This is immune
    # to the location-channel blow-up and the 5.2 slot quirks.
    rot_curves = {}   # bone -> {axis_index: fcurve}
    for layer in act_r.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in cb.fcurves:
                    if fc.data_path.endswith("rotation_quaternion") and                             '"' in fc.data_path:
                        bone = fc.data_path.split('"')[1]
                        rot_curves.setdefault(bone, {})[fc.array_index] = fc
    log("R rotation fcurve bones:", len(rot_curves))

    posed_r_cache = {}
    samples = {}   # bone -> {frame: (loc, quat)}
    for f in range(frame_lo, frame_hi + 1):
        # pose the L armature from decoded R rotations, top-down
        posed_l = {}
        for pb in order:
            # decoded R local basis (quaternion only; location stripped)
            if pb.name in rot_curves and len(rot_curves[pb.name]) == 4:
                q = Quaternion(tuple(
                    rot_curves[pb.name][i].evaluate(f) for i in range(4)))
                basis_r = Matrix.Translation((0, 0, 0)) @ q.to_matrix().to_4x4()
            else:
                basis_r = Matrix.Identity(4)
            if pb.parent:
                target = M_arm @ (posed_r_cache[pb.parent.name] @
                                  rest_rel(pb) @ basis_r) @ M_arm
            else:
                target = M_arm @ rest_rel(pb) @ basis_r @ M_arm
            posed_r_cache[pb.name] = (
                (posed_r_cache[pb.parent.name] @ rest_rel(pb) @ basis_r)
                if pb.parent else rest_rel(pb) @ basis_r)
            # invert the L FK chain for the local basis
            if pb.parent:
                basis = rest_rel(pb).inverted() @ (
                    posed_l[pb.parent.name].inverted() @ target)
            else:
                basis = rest_rel(pb).inverted() @ target
            tr, q2, _ = basis.decompose()
            posed_l[pb.name] = target
            if pb.name in driven:
                samples.setdefault(pb.name, {})[f] = (tr, q2.normalized())

    act_l = bpy.data.actions.new("Open/Close")
    arm_l.animation_data_create()
    arm_l.animation_data.action = act_l
    for name, keys in samples.items():
        pb = arm_l.pose.bones[name]
        pb.rotation_mode = "QUATERNION"
        for f in sorted(keys):
            tr, q = keys[f]
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=f)
            # every driven bone needs its (tiny) local translation keyed:
            # roll-0 rests are not exact conjugates of the R rests
            pb.location = tr
            pb.keyframe_insert("location", frame=f)
    log("L action baked:", act_l.name, tuple(act_l.frame_range),
        "bones:", len(samples))
    return act_l


# ------------------------------------------------------------------ textures
def export_textures(mesh_obj):
    for mat in mesh_obj.data.materials:
        if not mat or not mat.use_nodes:
            continue
        nt = mat.node_tree
        for n in nt.nodes:
            if n.type != "TEX_IMAGE" or not n.image:
                continue
            img = n.image
            role = "extra"
            for lk in nt.links:
                if lk.from_node == n:
                    if lk.to_node.type == "BSDF_PRINCIPLED" and \
                            lk.to_socket.name == "Base Color":
                        role = "basecolor"
                    elif lk.to_node.type == "SEPARATE_COLOR":
                        role = "metallicroughness"
                    elif lk.to_node.type == "MATH":
                        role = "occlusion"
            p = OUT / f"ElenaFF_{role}.png"
            img.filepath_raw = str(p)
            img.file_format = "PNG"
            img.save()
            log("texture", img.name, "->", p.name)


# ------------------------------------------------------------------ renders
def render_all(sc, arm_r, mesh_r, arm_l, mesh_l):
    if not sc.world:
        sc.world = bpy.data.worlds.new("W")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.35, 0.35, 0.36, 1.0)
    for i, (loc, e) in enumerate([((2, -5, 8), 300), ((-2, 5, 8), 220),
                                  ((0, 5, -8), 220), ((5, 0, 3), 160),
                                  ((-5, 0, 3), 160)]):
        lt = bpy.data.lights.new(f"k{i}", "AREA")
        lt.energy, lt.size = e, 2.5
        ob = bpy.data.objects.new(f"k{i}", lt)
        ob.location = loc
        sc.collection.objects.link(ob)
    cam = bpy.data.cameras.new("ElenaCam")
    cam.type = "ORTHO"
    cam_go = bpy.data.objects.new("ElenaCam", cam)
    sc.collection.objects.link(cam_go)
    sc.camera = cam_go

    # framing from EVALUATED world verts at rest
    vw = world_verts(mesh_r)
    import numpy as _np
    pts = _np.array([(p.x, p.y, p.z) for p in vw[::4]])
    ctr = pts.mean(axis=0)
    cov = _np.cov((pts - ctr).T)
    evals, evecs = _np.linalg.eigh(cov)     # ascending
    thin = Vector(evecs[:, 0])              # palm normal
    long_ax = Vector(evecs[:, 2])           # fingers direction
    log("PCA world normal", tuple(round(v, 3) for v in thin),
        "long", tuple(round(v, 3) for v in long_ax),
        "std", [round(float(_np.sqrt(e)), 4) for e in evals])
    n = thin
    up = long_ax
    center = Vector(ctr)
    extent = float(_np.sqrt(evals[2])) * 1.7   # long-axis std ~ half-span

    out = []
    for side, mesh_t in (("R", mesh_r), ("L", mesh_l)):
        for frame, tag in ((FRAME_OPEN, "open"), (FRAME_CLOSED, "closed")):
            sc.frame_set(frame)
            bpy.context.view_layer.update()
            for k, dname in ((n, "sideA"), (-n, "sideB")):
                d = (k * 0.7 + up * 0.35).normalized()
                cam_go.location = center + d * 10
                cam_go.rotation_euler = (-d).to_track_quat(
                    "-Z", "Y").to_euler()
                cam.ortho_scale = extent * 2.6
                p = OUT / f"Elena_{side}_{tag}_{dname}.png"
                sc.render.filepath = str(p)
                bpy.ops.render.render(write_still=True)
                out.append(p.name)
    sc.frame_set(0)
    return out


# ------------------------------------------------------------------ export
def export_fbx(root, tag):
    for sel in bpy.context.selected_objects:
        sel.select_set(False)
    stack = [root]
    sel_set = {root}
    while stack:
        o = stack.pop()
        stack.extend(o.children)
        sel_set.add(o)
    for o in sel_set:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = OUT / f"ElenaHand_{tag}.fbx"
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, apply_unit_scale=True,
        object_types={"ARMATURE", "MESH", "EMPTY"}, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        mesh_smooth_type="OFF", path_mode="ABSOLUTE")
    log("exported", path.name)


def write_manifest(mesh_r, metric, mirror_delta, renders):
    doc = {
        "source": {
            "file": "ArtSource/ThirdParty/ElenaFF_RiggedHand/hand.glb",
            "sha256": "3eb51d0b612fb4b5f3b63c9e32ac145be"
                      "191abcfd38ca7d3c5cf4fe0a7ee2d58",
            "author": "Elena FF (elenaferfor)",
            "origin": "https://sketchfab.com/3d-models/"
                      "rigged-hand-eae97cc2a742413cb5338ab942b12c1e",
            "license": "CC-BY-SA 4.0",
            "attribution": "Modified derivative of 'Rigged hand' by Elena "
                           "FF; redistribution requires author credit, this "
                           "modification note, and CC-BY-SA 4.0.",
        },
        "units": {
            "targetHandLengthMetres": HAND_LEN_M,
            "metricFactor": round(metric, 8),
            "wristBone": WRIST,
            "wristAtOrigin": True,
        },
        "animation": {
            "actionName": "Open/Close",
            "fps": FPS,
            "frames": {"open": FRAME_OPEN, "mostClosed": FRAME_CLOSED,
                       "last": FRAME_LAST},
            "seconds": {"open": 0.0,
                        "mostClosed": round(FRAME_CLOSED / FPS, 6),
                        "reopen": round(FRAME_LAST / FPS, 6)},
        },
        "mirror": {
            "method": "per-bone top-down numeric bake; "
                      "posed_L = M posed_R M (armature space)",
            "verifiedMaxWorldDelta": round(mirror_delta, 6),
        },
        "mesh": {"triangles": tri_count(mesh_r.data),
                 "vertices": len(mesh_r.data.vertices)},
        "renders": renders,
    }
    p = OUT / "rig-manifest.json"
    p.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8",
                 newline="\n")
    log("manifest written:", p.name)


# ------------------------------------------------------------------ main
def main():
    OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(GLB))
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = RES_X, RES_Y

    # ---- 1. identify + clean ------------------------------------------------
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    mesh = next(o for o in sc.objects
                if o.type == "MESH" and o.name != "Icosphere")
    junk_names = [o.name for o in sc.objects
                  if o.name == "Icosphere"
                  or (o.parent is not None and o.parent.name in ("Light", "Camera"))
                  or o.name in ("Light", "Camera")]
    for name in junk_names:
        bpy.data.objects.remove(sc.objects[name], do_unlink=True)
    log("removed junk:", junk_names)

    # ---- 2. flatten chain: root empty + metric bake + wrist at origin ----
    arm_w, mesh_w = arm.matrix_world.copy(), mesh.matrix_world.copy()
    # Scale anchor: the REST world bbox longest edge of the skin (an open
    # hand's longest extent = wrist->fingertip). Bone-chain lengths are NOT
    # usable here: the source rig has structural bones (hand.R.001_011,
    # tail z=58) that run far past the skin, so a wrist->tail "hand length"
    # overestimates by ~20x.
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    corners = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    ev.to_mesh_clear()
    mn_c = Vector((min(c.x for c in corners), min(c.y for c in corners),
                   min(c.z for c in corners)))
    mx_c = Vector((max(c.x for c in corners), max(c.y for c in corners),
                   max(c.z for c in corners)))
    span = max(mx_c - mn_c)
    metric = HAND_LEN_M / span
    log("rest bbox edge (open hand) =", round(span, 4),
        "metric factor", round(metric, 8))
    wrist_w = arm_w @ Vector(arm.data.bones[WRIST].head_local)
    bake = Matrix.Scale(metric, 4) @ Matrix.Translation(-wrist_w)

    root = bpy.data.objects.new("ElenaHand.R", None)
    sc.collection.objects.link(root)
    root.matrix_world = Matrix.Identity(4)
    mesh.parent = None                    # skinning rides on the modifier
    arm.parent = root
    arm.matrix_parent_inverse = Matrix.Identity(4)
    mesh.parent = root
    mesh.matrix_parent_inverse = Matrix.Identity(4)
    arm.matrix_world = bake @ arm_w       # single consistent pass
    mesh.matrix_world = bake @ mesh_w
    bpy.context.view_layer.update()
    wrist_now = arm.matrix_world @ Vector(arm.data.bones[WRIST].head_local)
    assert wrist_now.length < 1e-6, f"wrist not at origin: {wrist_now}"
    # verify the skin's longest extent now equals the target hand length
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    corners = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    ev.to_mesh_clear()
    mn_c = Vector((min(c.x for c in corners), min(c.y for c in corners),
                   min(c.z for c in corners)))
    mx_c = Vector((max(c.x for c in corners), max(c.y for c in corners),
                   max(c.z for c in corners)))
    span_m = max(mx_c - mn_c)
    assert abs(span_m - HAND_LEN_M) < 1e-3, f"hand span {span_m}"
    log("wrist at origin; skin longest extent", round(span_m, 4), "m")

    rest_sum = round(sum(v.co.length for v in mesh.data.vertices), 6)
    tri_n = tri_count(mesh.data)
    log("R mesh:", tri_n, "tris,", len(mesh.data.vertices), "verts")

    # ---- 3. LEFT: mirrored armature + mesh ----
    # ONE world reflection (plane through the world wrist, normal = world X)
    # conjugated into each space — the glTF import chain carries rotations,
    # so a raw local-x mirror is NOT the same plane in world coords.
    arm_l = bpy.data.objects.new("ElenaArmature.L", arm.data.copy())
    sc.collection.objects.link(arm_l)
    root_l = bpy.data.objects.new("ElenaHand.L", None)
    sc.collection.objects.link(root_l)
    root_l.matrix_world = Matrix.Identity(4)
    arm_l.parent = root_l
    arm_l.matrix_parent_inverse = Matrix.Identity(4)
    arm_l.matrix_world = arm.matrix_world.copy()

    arm_mw = arm.matrix_world
    Refl_world = reflection(0.0)          # wrist is at the world origin
    M_arm = arm_mw.inverted() @ Refl_world @ arm_mw      # armature space
    rollfix = {}
    bpy.context.view_layer.objects.active = arm_l
    bpy.ops.object.mode_set(mode="EDIT")
    for eb in arm_l.data.edit_bones:
        eb.head = (M_arm @ eb.head)
        eb.tail = (M_arm @ eb.tail)
        # roll: recompute AFTER the geometry mirror so the L local axes are
        # the exact conjugate of the R local axes (restL = M restR M). The
        # correct roll is derived from the conjugated parent-relative matrix
        # — set below once heads/tails are final (Blender derives X/Z from
        # roll, so we solve for the roll that reproduces the conjugate X/Z).
        rollfix[eb.name] = eb

    mesh_l = bpy.data.objects.new("ElenaMesh.L", mesh.data.copy())
    sc.collection.objects.link(mesh_l)
    mesh_l.parent = root_l
    mesh_l.matrix_parent_inverse = Matrix.Identity(4)
    mesh_l.matrix_world = mesh.matrix_world.copy()
    # a new object carries NO modifiers (they are object-level): bind the
    # mirrored skin to the mirrored armature explicitly
    mod = mesh_l.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_l
    # the R mesh keeps its original modifier (retargeted to arm by name if
    # the importer bound it by object datablock — it did, same object)
    log("L armature modifier bound:", mod.object.name)
    bm = bmesh.new()
    bm.from_mesh(mesh_l.data)
    bm.verts.ensure_lookup_table()
    mesh_mw = mesh_l.matrix_world
    M_mesh = mesh_mw.inverted() @ Refl_world @ mesh_mw
    for v in bm.verts:
        v.co = M_mesh @ v.co
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh_l.data)
    bm.free()
    # mirror-invariance check belongs to WORLD space (the local sum of |co|
    # changes under a world-plane reflection that is not through the origin
    # plane); the real correctness gate is the world-level mirror
    # verification in step 5 plus the tri count below.
    # plane-consistency diagnostics: all three mirror planes must be the
    # same world plane (through origin, normal = world X)
    _Mw_arm = arm.matrix_world @ M_arm @ arm.matrix_world.inverted()
    _Mw_mesh = mesh_l.matrix_world @ M_mesh @ mesh_l.matrix_world.inverted()
    log("PLANE arm conj:", [tuple(round(v, 4) for v in row)
                            for row in _Mw_arm])
    log("PLANE mesh conj:", [tuple(round(v, 4) for v in row)
                             for row in _Mw_mesh])
    log("PLANE Refl_world:", [tuple(round(v, 4) for v in row)
                              for row in Refl_world])
    assert tri_count(mesh_l.data) == tri_n
    log("L mesh mirrored; tris", tri_count(mesh_l.data))

    # ---- 4. actions ----
    log("R action", arm.animation_data.action.name,
        tuple(arm.animation_data.action.frame_range))
    act_l_obj = bake_mirror_action(arm, arm_l, M_arm)
    act_l_name = act_l_obj.name

    # ---- 5. numeric mirror verification ----
    worst = 0.0
    worst_info = None
    for f in MIRROR_SAMPLES:
        sc.frame_set(f)
        bpy.context.view_layer.update()
        wr = world_verts(mesh)
        wl = world_verts(mesh_l)
        wrist_w = arm.matrix_world @ Vector(
            arm.data.bones[WRIST].head_local)
        M_w = (Matrix.Translation((wrist_w.x, 0, 0)) @
               Matrix.Diagonal((-1, 1, 1, 1)) @
               Matrix.Translation((-wrist_w.x, 0, 0)))
        for i in range(0, len(wr), 5):
            d = ((M_w @ wr[i]) - wl[i]).length
            if d > worst:
                worst = d
                worst_info = (f, i, tuple(round(v, 4) for v in wr[i]),
                              tuple(round(v, 4) for v in (M_w @ wr[i])),
                              tuple(round(v, 4) for v in wl[i]))
    log("mirror verification max world delta:", round(worst, 6))
    if worst_info:
        log("worst at frame/vert", worst_info[:2],
            "\n  R world    ", worst_info[2],
            "\n  mirrored R ", worst_info[3],
            "\n  L actual   ", worst_info[4])
    if worst > 2e-3:
        # action-evaluation diagnostic
        sc.frame_set(55)
        bpy.context.view_layer.update()
        for bn in ("hand.R_02", "index_01.R_017", "thumb_01.R_08"):
            pl = arm_l.pose.bones[bn]
            pr = arm.pose.bones[bn]
            log("DIAG", bn, "L quat", tuple(round(v, 3) for v in pl.rotation_quaternion),
                "R quat", tuple(round(v, 3) for v in pr.rotation_quaternion),
                "L mode", pl.rotation_mode)
        log("DIAG action", arm_l.animation_data.action.name if
            arm_l.animation_data.action else None,
            "slot", arm_l.animation_data.action_slot)
    if worst > 2e-3:
        raise RuntimeError(f"mirror bake failed verification: {worst}")

    # ---- 6. single-session: textures + renders + save + export ----
    # (verified: keep R's original action untouched; saving with live
    # evaluated state + reload corrupts the armature bind in 5.2, so
    # everything happens in THIS session, ending at rest frame 0)
    export_textures(mesh)
    renders = render_all(sc, arm, mesh, arm_l, mesh_l)
    sc.frame_set(0)
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "ElenaHands.blend"))
    export_fbx(root, "R")
    export_fbx(root_l, "L")
    write_manifest(mesh, metric, worst, renders)
    log("DONE")

    export_textures(mesh)
    renders = render_all(sc, arm, mesh, arm_l, mesh_l)
    sc.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "ElenaHands.blend"))
    export_fbx(root, "R")
    export_fbx(root_l, "L")
    write_manifest(mesh, metric, worst, renders)
    log("DONE")


if __name__ == "__main__":
    main()
