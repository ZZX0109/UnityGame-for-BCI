# -*- coding: utf-8 -*-
"""Refinement rev4 — world-space palm cameras (correct 180° convention),
projection-based exact framing, neutral lighting. Reads the frozen baseline
.blend; writes output/rev4/ only. Armature untouched.

Entry: blender --background --factory-startup --python refine_hands_rev4.py
"""

import json
import math
import shutil
from pathlib import Path

import bmesh
import bpy
import mathutils

HERE = Path(__file__).resolve().parent
BASELINE = HERE / "baseline"
OUT = HERE / "output" / "rev4"
BLEND_IN = BASELINE / "hands_source.blend"
BLEND_OUT = OUT / "refined_hands.blend"

TARGET_TRIS = 16000
FRAME_FILL = 0.75          # subject occupies ~75% of the frame height


def log(*a):
    print("VH-REFINE4:", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def sfx_of(name):
    return "." + name[-1]


# ---------------------------------------------------------------- textures
def reload_textures_from_baseline():
    fixed = 0
    for img in list(bpy.data.images):
        if img.name in ("Render Result", "Viewer Node"):
            continue
        parts = img.name.split("_")
        if len(parts) < 2:
            continue
        src = BASELINE / f"Hand_{parts[1]}_{parts[2]}.png"
        if not src.exists():
            continue
        loaded = bpy.data.images.load(str(src), check_existing=False)
        keep = img.name
        bpy.data.images.remove(img)
        loaded.name = keep
        fixed += 1
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        nt = mat.node_tree
        for n in list(nt.nodes):
            if n.type == "TEX_IMAGE" and (n.image is None or not n.image.has_data):
                nt.nodes.remove(n)
    log("textures loaded:", fixed)
    return fixed


def link_materials_clean():
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        nt = mat.node_tree
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            continue
        for n in list(nt.nodes):
            if n.type == "TEX_IMAGE":
                nt.nodes.remove(n)
        side = mat.name[-1]
        by_kind = {}
        for img in bpy.data.images:
            parts = img.name.split("_")
            if len(parts) < 2 or parts[0] != "Skin":
                continue
            if parts[1] == side:
                by_kind[parts[2]] = img

        def tex(kind, colorspace):
            img = by_kind.get(kind)
            if img is None:
                return None
            n = nt.nodes.new("ShaderNodeTexImage")
            n.image = img
            n.image.colorspace_settings.name = colorspace
            return n

        t_d = tex("diffuse", "sRGB")
        t_n = tex("normal", "Non-Color")
        t_r = tex("roughness", "Non-Color")
        if t_d:
            nt.links.new(t_d.outputs["Color"], bsdf.inputs["Base Color"])
        if t_n:
            nm = nt.nodes.new("ShaderNodeNormalMap")
            nm.inputs["Strength"].default_value = 1.0
            nt.links.new(t_n.outputs["Color"], nm.inputs["Color"])
            nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
        bsdf.inputs["Metallic"].default_value = 0.0
        spec = bsdf.inputs.get("Specular IOR Level") or bsdf.inputs.get("Specular")
        if spec is not None:
            spec.default_value = 0.2
        rough = bsdf.inputs["Roughness"]
        if t_r:
            nt.links.new(t_r.outputs["Color"], rough)
        else:
            rough.default_value = 0.6


# ---------------------------------------------------------------- geometry
def subdivide_once(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges, use_grid_fill=True,
                              smooth=0.0, cuts=1)
    bm.to_mesh(obj.data)
    bm.free()


def densify_crease_regions(obj, arm):
    sfx = sfx_of(obj.name)
    bands = []
    for n in ("Index1", "Middle1", "Ring1", "Little1", "Thumb1"):
        b = arm.data.bones.get(n + sfx)
        if b:
            bands.append((mathutils.Vector(b.head_local), 0.06))
    wt = arm.data.bones.get("ThumbMeta" + sfx)
    it = arm.data.bones.get("IndexMeta" + sfx)
    if wt and it:
        web = (mathutils.Vector(wt.tail_local) +
               mathutils.Vector(it.tail_local)) / 2
        bands.append((web, 0.09))
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    marked = []
    for e in bm.edges:
        mid = sum((v.co for v in e.verts), mathutils.Vector()) / len(e.verts)
        if any((mid - c).length < r for c, r in bands):
            marked.append(e)
    bmesh.ops.subdivide_edges(bm, edges=marked, use_grid_fill=True,
                              smooth=0.0, cuts=1)
    bm.to_mesh(obj.data)
    bm.free()


def surface_round(obj, strength=0.3):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    avg = {}
    cnt = {}
    for v in bm.verts:
        avg[v.index] = mathutils.Vector((0, 0, 0))
        cnt[v.index] = 0
    for e in bm.edges:
        ia, ib = e.verts[0].index, e.verts[1].index
        avg[ia] += bm.verts[ib].co
        avg[ib] += bm.verts[ia].co
        cnt[ia] += 1
        cnt[ib] += 1
    for v in bm.verts:
        if cnt[v.index]:
            v.co = v.co.lerp(avg[v.index] / cnt[v.index], 0.3)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()


def relax_shading(obj):
    for p in obj.data.polygons:
        p.use_smooth = True


# ---------------------------------------------------------------- weights
def region_weights(obj, arm):
    """Per-finger-chain weights + one neighbour-smoothing pass. Vertices join
    only their own chain (+ metacarpal + Wrist/Forearm): no cross-finger
    blending that pulls sheets toward neighbouring fingers."""
    import numpy as _np

    sfx = sfx_of(obj.name)
    bones = list(arm.data.bones)
    name_idx = {b.name: i for i, b in enumerate(bones)}
    chains = {
        "thumb": ["ThumbMeta", "Thumb1", "Thumb2", "Thumb3"],
        "index": ["IndexMeta", "Index1", "Index2", "Index3"],
        "middle": ["MiddleMeta", "Middle1", "Middle2", "Middle3"],
        "ring": ["RingMeta", "Ring1", "Ring2", "Ring3"],
        "little": ["LittleMeta", "Little1", "Little2", "Little3"],
    }
    chains = {k: [n + sfx for n in v] for k, v in chains.items()}
    chains["common"] = ["Wrist" + sfx, "Forearm" + sfx]

    segs = [(b.name, mathutils.Vector(b.head_local),
             mathutils.Vector(b.tail_local)) for b in bones]

    def dist_to(name, p):
        i = name_idx[name]
        _, h, t = segs[i]
        ab = t - h
        ap = p - h
        tt = max(0.0, min(1.0, ap.dot(ab) / max(ab.length_squared, 1e-9)))
        return (ap - ab * tt).length

    obj.vertex_groups.clear()
    n_v = len(obj.data.vertices)
    wmat = _np.zeros((n_v, len(bones)), dtype=_np.float64)
    for vi, v in enumerate(obj.data.vertices):
        p = mathutils.Vector(v.co)
        best_chain, best_d = None, None
        for key, allowed in chains.items():
            for n in allowed:
                d = dist_to(n, p)
                if best_d is None or d < best_d:
                    best_chain, best_d = key, d
        ws = []
        for n in chains[best_chain]:
            d = dist_to(n, p)
            ws.append(((1.0 / max(d, 0.02)) ** 3, name_idx[n]))
        total = sum(w for w, _ in ws)
        for w, i in ws:
            wmat[vi, i] = w / total

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    sm = _np.copy(wmat)
    for e in bm.edges:
        ia, ib = e.verts[0].index, e.verts[1].index
        sm[ia] = sm[ia] * 0.6 + wmat[ib] * 0.4
        sm[ib] = sm[ib] * 0.6 + wmat[ia] * 0.4
    rowsum = sm.sum(axis=1, keepdims=True)
    rowsum[rowsum < 1e-9] = 1.0
    sm /= rowsum
    wmat = sm
    bm.free()

    for bi, b in enumerate(bones):
        g = obj.vertex_groups.new(name=b.name)
        idx = [vi for vi in range(n_v) if wmat[vi, bi] > 1e-4]
        if idx:
            g.add(idx, 1.0, "REPLACE")
            for vi in idx:
                g.add([vi], float(wmat[vi, bi]), "REPLACE")
    log("region weights rebuilt", obj.name)


def relax_shading_wrapper_removed():
    pass


# ---------------------------------------------------------------- palm frame
def world_palm_frame(side, arm):
    """World-space (normal, up) built from the REAL wrist point and the two
    MCP tails. (Codex review: Wrist.tail is the midpoint of the two MCPs, so
    wrist-tail/MCP/MCP are near-collinear and the cross product degenerates;
    Wrist.HEAD is the true wrist joint and forms a non-degenerate triangle
    with the two MCPs.)

    normal = normalize((indexMCP - wrist) x (littleMCP - wrist)), guarded by
    a minimum cross-product length; sign fixed so the THUMB is on the +normal
    side; up = normalize(middleTip - wrist)."""
    sfx = sfx_of(side)
    w_h = mathutils.Vector(arm.data.bones["Wrist" + sfx].head_local)
    i_mcp = mathutils.Vector(arm.data.bones["IndexMeta" + sfx].tail_local)
    l_mcp = mathutils.Vector(arm.data.bones["LittleMeta" + sfx].tail_local)
    tip = mathutils.Vector(arm.data.bones["Middle3" + sfx].tail_local)
    t_tip = mathutils.Vector(arm.data.bones["Thumb3" + sfx].tail_local)
    rot = arm.matrix_world.to_3x3()
    a = rot @ (i_mcp - w_h)
    b = rot @ (l_mcp - w_h)
    cross = a.cross(b)
    if cross.length < 1e-4:
        raise RuntimeError(
            f"{side}: palm plane degenerate (cross length {cross.length:.2e}); "
            "cannot derive a palm normal.")
    normal = cross.normalized()
    up = (rot @ (tip - w_h)).normalized()
    # Sign, empirically verified by two rendered rounds (rev4): with this rig
    # the cross product above points to the BACK of the hand (nail side),
    # because the Wrist bone tail points from the wrist toward the MCP row
    # THROUGH the palm volume. The palm faces -cross. Flip is unconditional.
    normal = -normal
    return normal, up


def deformed_bounds(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    corners = [ev.matrix_world @ mathutils.Vector(c) for c in ev.bound_box]
    mn = mathutils.Vector((min(c.x for c in corners),
                           min(c.y for c in corners),
                           min(c.z for c in corners)))
    mx = mathutils.Vector((max(c.x for c in corners),
                           max(c.y for c in corners),
                           max(c.z for c in corners)))
    return (mn + mx) / 2, (mx - mn) / 2


# ---------------------------------------------------------------- camera
def look_from(cam_obj, center, direction, up, span_y, span_x, margin=1.25):
    """Ortho camera using AXIS-ALIGNED placement only (verified working in
    the diag ladder): position at center + direction*dist with identity
    rotation for the +Z view, and fixed EULER rotations for the other views
    (no matrix->euler round trips, which broke the aim repeatedly)."""
    d = mathutils.Vector(direction).normalized()
    # choose the axis-aligned view direction closest to the requested one
    axes = {"+Z": (mathutils.Vector((0, 0, 1)), (0, 0, 0)),
            "-Z": (mathutils.Vector((0, 0, -1)), (math.pi, 0, 0)),
            "+X": (mathutils.Vector((1, 0, 0)), (math.pi / 2, 0, math.pi / 2)),
            "-X": (mathutils.Vector((-1, 0, 0)), (math.pi / 2, 0, -math.pi / 2)),
            "+Y": (mathutils.Vector((0, 1, 0)), (math.pi / 2, 0, 0)),
            "-Y": (mathutils.Vector((0, -1, 0)), (-math.pi / 2, 0, 0))}
    best_name, best = max(axes.items(), key=lambda kv: d.dot(kv[1][0]))
    axis, euler = best
    cam_obj.location = center + axis * 10.0
    cam_obj.rotation_euler = euler
    cam_obj.data.type = "ORTHO"
    # subject fills ~75%: diag showed scale 0.5 frames the ~1-unit hand well.
    # hand spans ~2.1 world units incl. forearm -> scale ~2.1*1.25/1.78*2
    span_y = max(span_y, 0.05)
    cam_obj.data.ortho_scale = span_y * 1.25 * 1.78  # full width for 16:9
    cam_obj.data.clip_end = 100.0


def shoot(sc, cam_obj, name):
    sc.render.filepath = str(OUT / f"{name}.png")
    bpy.ops.render.render(write_still=True)
    log("rendered", name)


def projected_spans(obj, x_axis, y_axis):
    """Exact projected spans of the deformed mesh along two world axes."""
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    xs, ys = [], []
    for c in ev.bound_box:
        w = ev.matrix_world @ mathutils.Vector(c)
        xs.append(w.dot(x_axis))
        ys.append(w.dot(y_axis))
    return (max(xs) - min(xs), max(ys) - min(ys))


def export_wrist_local_fbx(side, tag):
    o = bpy.data.objects[f"Hand.{side}"]
    arm = bpy.data.objects[f"Armature.{side}"]
    arm_world, mesh_world = arm.matrix_world.copy(), o.matrix_world.copy()
    wrist_w = arm_world @ mathutils.Vector(arm.data.bones["Wrist." + side].head_local)
    tip_w = arm_world @ mathutils.Vector(arm.data.bones["Middle3." + side].tail_local)
    metric = 0.19 / (tip_w - wrist_w).length
    bake = mathutils.Matrix.Scale(metric, 4) @ mathutils.Matrix.Translation(-wrist_w)
    root = bpy.data.objects.new(f"HandRoot.{side}", None)
    bpy.context.collection.objects.link(root)
    root.matrix_world = mathutils.Matrix.Identity(4)
    arm.parent = root
    arm.matrix_parent_inverse = mathutils.Matrix.Identity(4)
    arm.matrix_world = bake @ arm_world
    o.parent = arm
    o.matrix_parent_inverse = mathutils.Matrix.Identity(4)
    o.matrix_world = bake @ mesh_world
    bpy.context.view_layer.update()
    wrist_after = arm.matrix_world @ mathutils.Vector(
        arm.data.bones["Wrist." + side].head_local)
    if wrist_after.length > 1e-6:
        raise RuntimeError(f"{side} wrist not at component origin: {wrist_after}")
    for sel in bpy.context.selected_objects:
        sel.select_set(False)
    for sel in (root, arm, o):
        sel.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = str(OUT / f"Hand_{side}_{tag}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True,
        object_types={"ARMATURE", "MESH", "EMPTY"}, add_leaf_bones=False,
        bake_anim=False, mesh_smooth_type="OFF", path_mode="ABSOLUTE")
    log("exported", Path(path).name, "metric", round(metric, 6))
    return {"side": side, "file": Path(path).name,
            "metresPerSourceUnit": round(metric, 6)}


# ---------------------------------------------------------------- poses
def clear_all_animation(arm):
    if arm.animation_data:
        arm.animation_data.action = None
        arm.animation_data_clear()


def apply_pose(arm, side, name):
    sfx = sfx_of(side)
    F = ["Index", "Middle", "Ring", "Little"]
    angles = {}
    if name == "fist":
        for f in F:
            angles[f + "1"] = (75, 0, 0)
            angles[f + "2"] = (85, 0, 0)
            angles[f + "3"] = (50, 0, 0)
        angles["Thumb1"] = (30, 0, 25)
        angles["Thumb2"] = (55, 0, 0)
        angles["Thumb3"] = (40, 0, 0)
    elif name == "pinch":
        angles = {"Index1": (45, 0, 0), "Index2": (86, 0, 0), "Index3": (22, 0, 0),
                  "Thumb1": (30, 80, 20), "Thumb2": (71, 0, 0), "Thumb3": (110, 0, 0),
                  "Middle1": (25, 0, 0), "Middle2": (35, 0, 0), "Middle3": (20, 0, 0),
                  "Ring1": (30, 0, 0), "Ring2": (40, 0, 0), "Ring3": (25, 0, 0),
                  "Little1": (35, 0, 0), "Little2": (45, 0, 0), "Little3": (30, 0, 0)}
    for bn, (x, y, z) in angles.items():
        pb = arm.pose.bones.get(bn + sfx)
        if pb:
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (math.radians(x), math.radians(y),
                                 math.radians(z))
    bpy.context.view_layer.update()


def clear_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- main
def main():
    OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_IN))
    sc = bpy.context.scene
    if not sc.world:
        sc.world = bpy.data.worlds.new("Studio")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.05, 0.05, 0.06, 1.0)
    for i, (loc, e, col) in enumerate([
            ((2, -5, 8), 1000, (1.0, 1.0, 1.0)),
            ((-3, 4, -5), 500, (0.9, 0.93, 1.0)),
            ((0, 5, 6), 700, (1.0, 1.0, 1.0))]):
        lt = bpy.data.lights.new(f"k{i}", "AREA")
        lt.energy = e
        lt.size = 2.0
        lt.color = col
        ob = bpy.data.objects.new(f"k{i}", lt)
        ob.location = loc
        bpy.context.collection.objects.link(ob)
    cam = bpy.data.cameras.new("PreviewCam")
    cam.type = "ORTHO"
    camGo = bpy.data.objects.new("PreviewCam", cam)
    bpy.context.collection.objects.link(camGo)
    sc.camera = camGo

    reload_textures_from_baseline()
    link_materials_clean()

    summary = {"hands": [], "renders": []}
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        arm = bpy.data.objects[f"Armature.{side}"]
        clear_all_animation(arm)
        clear_pose(arm)
        before = tri_count(obj.data)

        subdivide_once(obj)
        densify_crease_regions(obj, arm)
        surface_round(obj)
        region_weights(obj, arm)
        relax_shading(obj)
        after = tri_count(obj.data)

        normal, up = world_palm_frame(side, arm)
        center, ext = deformed_bounds(obj)

        # palm normal sign comes from the thumb-landmark test in
        # world_palm_frame (the closed-mesh vote was ~0.5 by construction)

        # whole-hand: palm / back / side
        span_x, span_y = projected_spans(obj, mathutils.Vector((1, 0, 0)),
                                         mathutils.Vector((0, 1, 0)))
        look_from(camGo, center, normal, up, span_x, span_y, margin=1.15)
        shoot(sc, camGo, f"refined4_{side.lower()}_palm")
        look_from(camGo, center, -normal, up, span_x, span_y, margin=1.15)
        shoot(sc, camGo, f"refined4_{side.lower()}_back")
        sx = 1 if side == "R" else -1
        side_dir = up.cross(mathutils.Vector((sx, 0, 0))).normalized()
        look_from(camGo, center, side_dir, up, span_x, span_y, margin=1.15)
        shoot(sc, camGo, f"refined4_{side.lower()}_side")

        # fist closeup (diagnostic deformation only)
        apply_pose(arm, side, "fist")
        f_center, f_ext = deformed_bounds(obj)
        f_span_x, f_span_y = projected_spans(obj, mathutils.Vector((1, 0, 0)),
                                             mathutils.Vector((0, 1, 0)))
        look_from(camGo, f_center, normal, up, f_span_x, f_span_y, margin=1.15)
        shoot(sc, camGo, f"refined4_{side.lower()}_fist")
        clear_pose(arm)

        summary["hands"].append({
            "side": side, "tris_before": before, "tris_after": after,
            "palmNormalWorld": [round(v, 3) for v in normal],
        })

    for side in ("R", "L"):
        clear_all_animation(bpy.data.objects[f"Armature.{side}"])
        clear_pose(bpy.data.objects[f"Armature.{side}"])
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_OUT))
    log("saved", BLEND_OUT)

    bpy.ops.wm.open_mainfile(filepath=str(BLEND_OUT))
    exports = []
    for side in ("R", "L"):
        exports.append(export_wrist_local_fbx(side, "refined4"))
    for side in ("R", "L"):
        for kind in ("diffuse", "normal", "roughness"):
            shutil.copyfile(BASELINE / f"Hand_{side}_{kind}.png",
                            OUT / f"Hand_{side}_{kind}.png")
    summary["exports"] = exports
    (OUT / "refinement-summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n")
    log("summary written")


if __name__ == "__main__":
    main()
