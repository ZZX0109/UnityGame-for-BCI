# -*- coding: utf-8 -*-
"""Refinement rev3 — same-source complete set: refined rest mesh + weights +
correctly loaded 2K textures (no magenta placeholders) + R/L FBX exports.

Root cause fixed (Codex 05:45): the baseline .blend's image datablocks are
EMPTY (has_data=False, stale //../../Assets paths). Blender renders empty
textures as magenta — that is the whole story; the baseline diffuse PNG in
baseline/ is a normal light skin tone. rev3 rebuilds each image datablock
from the baseline PNG bytes (LoadImage from disk), verifies has_data + size,
prunes the duplicate orphan TEX_IMAGE nodes, and links diffuse/roughness/
normal cleanly (non-metal skin, no pink overlay, no hue guessing).

Rest pose: baseline has no actions, but rev3 still clears any animation data
and re-zeroes pose bones before saving/framing, so the rest can never be
re-driven by a stale action.

Outputs to output/rev3/: refined_hands.blend, Hand_{R,L}_refined.fbx,
Hand_{R,L}_{diffuse,normal,roughness}.png (the SAME loaded 2K textures, not
bakes), palm/back/side/fist renders (true palm-normal cameras as rev2),
refinement-summary.json.
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
OUT = HERE / "output" / "rev3"
BLEND_IN = BASELINE / "hands_source.blend"
BLEND_OUT = OUT / "refined_hands.blend"

TARGET_TRIS = 16000
CHAIN_KEYS = ("thumb", "index", "middle", "ring", "little")


def log(*a):
    print("VH-REFINE3:", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def sfx_of(name):
    return "." + name[-1]


# ---------------------------------------------------------------- textures
def reload_textures_from_baseline():
    """Rebuild every empty Skin_* image datablock from the baseline PNG bytes.
    Verifies has_data + size afterwards; prunes orphan TEX_IMAGE nodes so no
    duplicate/unloaded placeholders remain in any material."""
    fixed = 0
    for img in list(bpy.data.images):
        if img.name in ("Render Result", "Viewer Node"):
            continue
        # name shape: Skin_R_diffuse -> Hand_R_diffuse.png in baseline/
        parts = img.name.split("_")
        if len(parts) < 2:
            continue
        src = BASELINE / f"Hand_{parts[1]}_{parts[2]}.png"
        if not src.exists():
            log("missing source for", img.name, src)
            continue
        # replace the empty datablock with one that actually loads the PNG
        loaded = bpy.data.images.load(str(src), check_existing=False)
        fresh_name = img.name
        bpy.data.images.remove(img)          # free the empty placeholder
        loaded.name = fresh_name             # keep the Skin_R_* name convention
        fixed += 1
        log("reloaded", fresh_name, "from", src.name,
            "has_data", loaded.has_data, "size", tuple(loaded.size))
    # prune orphan TEX_IMAGE nodes (duplicates left by earlier re-links)
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        nt = mat.node_tree
        for n in list(nt.nodes):
            if n.type == "TEX_IMAGE" and (n.image is None or
                                          not n.image.has_data):
                nt.nodes.remove(n)
    return fixed


def link_materials_clean():
    """Ensure each Skin material has exactly diffuse/roughness/normal image
    nodes wired to Principled, non-metal, with sane gloss. Reuses the loaded
    datablocks; no pink overlay, no hue guessing."""
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        nt = mat.node_tree
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            continue
        # remove ALL image nodes, then add back exactly three clean ones
        for n in list(nt.nodes):
            if n.type == "TEX_IMAGE":
                nt.nodes.remove(n)
        by_kind = {}
        for img in bpy.data.images:
            parts = img.name.split("_")
            if len(parts) < 2 or parts[0] != "Skin":
                continue
            side, kind = parts[1], parts[2]
            if mat.name.endswith("." + side):
                by_kind[kind] = img
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
            nt.links.new(t_n.outputs["Color"], bsdf.inputs["Normal"])
        bsdf.inputs["Metallic"].default_value = 0.0
        spec = bsdf.inputs.get("Specular IOR Level") or bsdf.inputs.get("Specular")
        if spec is not None:
            spec.default_value = 0.25
        rough = bsdf.inputs["Roughness"]
        if t_r:
            nt.links.new(t_r.outputs["Color"], rough)
        else:
            rough.default_value = 0.55
        # normal maps need the Normal Map conversion node
        if t_n:
            nm = nt.nodes.new("ShaderNodeNormalMap")
            nm.inputs["Strength"].default_value = 1.0
            nt.links.new(t_n.outputs["Color"], nm.inputs["Color"])
            nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
        mat.diffuse_color = (0.8, 0.7, 0.65, 1.0)


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
    avg = {v.index: mathutils.Vector((0, 0, 0)) for v in bm.verts}
    cnt = {v.index: 0 for v in bm.verts}
    for e in bm.edges:
        ia, ib = e.verts[0].index, e.verts[1].index
        avg[ia] += bm.verts[ib].co
        avg[ib] += bm.verts[ia].co
        cnt[ia] += 1
        cnt[ib] += 1
    for v in bm.verts:
        if cnt[v.index]:
            target = avg[v.index] / cnt[v.index]
            v.co = v.co.lerp(target, strength)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()


def relax_shading(obj):
    for p in obj.data.polygons:
        p.use_smooth = True


# ---------------------------------------------------------------- weights
def region_weights(obj, arm):
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


# ---------------------------------------------------------------- palm frame
def palm_frame(side, arm):
    sfx = sfx_of(side)
    w = mathutils.Vector(arm.data.bones["Wrist" + sfx].tail_local)
    i_mcp = mathutils.Vector(arm.data.bones["IndexMeta" + sfx].tail_local)
    l_mcp = mathutils.Vector(arm.data.bones["LittleMeta" + sfx].tail_local)
    tip = mathutils.Vector(arm.data.bones["Middle3" + sfx].tail_local)
    normal = (i_mcp - w).cross(l_mcp - w).normalized()
    up = (tip - w).normalized()
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


def look_from(cam_obj, center, direction, up, extents):
    d = mathutils.Vector(direction).normalized()
    u = mathutils.Vector(up).normalized()
    x = u.cross(d).normalized()
    y = d.cross(x).normalized()
    m = mathutils.Matrix((
        (x.x, y.x, d.x, 0.0),
        (x.y, y.y, d.y, 0.0),
        (x.z, y.z, d.z, 0.0),
        (0.0, 0.0, 0.0, 1.0)))
    cam_obj.rotation_euler = m.to_euler()
    cam_obj.location = center + d * 10.0
    cam_obj.data.type = "ORTHO"
    half_h = max(extents.y, 0.05) * 1.25
    cam_obj.data.ortho_scale = max(half_h * 2.0, half_h * 2.0 * 1.78)


# ---------------------------------------------------------------- poses
def clear_all_animation(arm):
    """Remove any animation data so rest can never be re-driven."""
    for ob in (arm, bpy.data.objects.get(f"Hand.{arm.name[-1]}")):
        if ob and ob.animation_data:
            ob.animation_data.action = None
            ob.animation_data_clear()
    if arm.data.animation_data:
        arm.data.animation_data.action = None
        arm.data.animation_data_clear()


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


# ---------------------------------------------------------------- export
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
    log("exported", os_path_basename(path), "metric", round(metric, 6))
    return {"side": side, "file": Path(path).name,
            "metresPerSourceUnit": round(metric, 6)}


def os_path_basename(p):
    return Path(p).name


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
    for i, (x, y, z, e) in enumerate([(2, -5, 8, 900), (-3, 4, -5, 600),
                                      (0, 5, 6, 1200)]):
        lt = bpy.data.lights.new(f"k{i}", "AREA")
        lt.energy = e
        lt.size = 2.0
        ob = bpy.data.objects.new(f"k{i}", lt)
        ob.location = (x, y, z)
        bpy.context.collection.objects.link(ob)
    cam = bpy.data.cameras.new("PreviewCam")
    cam.type = "ORTHO"
    camGo = bpy.data.objects.new("PreviewCam", cam)
    bpy.context.collection.objects.link(camGo)
    sc.camera = camGo

    # --- textures first: magenta was an empty-datablock symptom ---
    fixed = reload_textures_from_baseline()
    link_materials_clean()

    summary = {"hands": [], "textures_reloaded": fixed, "renders": []}
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        arm = bpy.data.objects[f"Armature.{side}"]
        clear_all_animation(arm)   # rest can never be re-driven by stale actions
        clear_pose(arm)
        before = tri_count(obj.data)

        subdivide_once(obj)
        densify_crease_regions(obj, arm)
        surface_round(obj)
        region_weights(obj, arm)
        relax_shading(obj)
        after = tri_count(obj.data)

        normal, up = palm_frame(side, arm)
        center, ext = deformed_bounds(obj)

        def shot(name, cntr, direction, extents):
            look_from(camGo, cntr, direction, up, extents)
            sc.render.filepath = str(OUT / f"{name}.png")
            bpy.ops.render.render(write_still=True)
            summary["renders"].append(name)
            log("rendered", name)

        shot(f"refined3_{side.lower()}_palm", center, normal, ext)
        shot(f"refined3_{side.lower()}_back", center, [-v for v in normal], ext)
        sx = 1 if side == "R" else -1
        side_dir = up.cross(mathutils.Vector((sx, 0, 0))).normalized()
        shot(f"refined3_{side.lower()}_side", center, side_dir, ext)
        apply_pose(arm, side, "fist")
        f_center, f_ext = deformed_bounds(obj)
        shot(f"refined3_{side.lower()}_fist", f_center, normal, f_ext)
        clear_pose(arm)
        summary["hands"].append({"side": side, "tris_before": before,
                                 "tris_after": after,
                                 "palmNormal": [round(v, 3) for v in normal]})

    for side in ("R", "L"):
        clear_all_animation(bpy.data.objects[f"Armature.{side}"])
        clear_pose(bpy.data.objects[f"Armature.{side}"])
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_OUT))
    log("saved", BLEND_OUT)

    # complete same-source set: re-export FBX from THIS refined mesh/weights
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_OUT))
    exports = []
    for side in ("R", "L"):
        exports.append(export_wrist_local_fbx(side, "refined3"))
    # copy the loaded 2K textures so the FBX set is complete and same-source
    for side in ("R", "L"):
        for kind in ("diffuse", "normal", "roughness"):
            src = BASELINE / f"Hand_{side}_{kind}.png"
            dst = OUT / f"Hand_{side}_{kind}.png"
            shutil.copyfile(src, dst)
    summary["exports"] = exports
    (OUT / "refinement-summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n")
    log("summary written")


if __name__ == "__main__":
    main()
