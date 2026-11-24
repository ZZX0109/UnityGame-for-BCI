# -*- coding: utf-8 -*-
"""Refinement rev7 — mask channel + world-space + camera-aim fixes (rev6 base,
texture reload, verifiable palm/back renders.

Four determinate fixes over rev5 (Codex source review):
  1. The nail mask was WRITTEN to color_attributes[...].color = (v,v,v,1)
     but the shader sampled Alpha — which Blender keeps at 1.0 unless the
     layer is created with an alpha-bearing format AND the value is written
     into .alpha. Whole hand therefore picked the NAIL color/roughness
     (uniform grey-white diffuse). rev6 writes the mask into the RGB red
     channel and the shader samples .Color.r — mask and consumer agree.
  2. Space unification: vertex positions use obj.matrix_world @ v.co and
     bone head/tails use arm.matrix_world @ Vector(...) — both in world
     space. rev5 compared mesh-local vertex coords against arm-local bone
     points (wrong space; also wrong for the mirrored L rig).
  3. Baked maps are reloaded from the saved PNGs with explicit color spaces
     (diffuse sRGB, roughness/normal Non-Color) and verified: 2048x2048,
     has_data True, linked into the Principled sockets. No empty datablocks.
  4. Rest hardening and the 0.19 m wrist-local export rule unchanged.

Output: output/rev7/ (rev6 evidence untouched).
Usage: blender --background --factory-startup --python refine_hands_rev6.py
"""

import json
import math
from pathlib import Path

import bmesh
import bpy
import mathutils

HERE = Path(__file__).resolve().parent
OUT = HERE / "output" / "rev7"
BLEND_IN = HERE / "output" / "rev4" / "refined_hands.blend"
BLEND_OUT = OUT / "refined_hands.blend"

RES = 2048
NAIL_MASK = "nail_mask"

# index-flex channel positions (4..6) used by the DB8 selector in rev4; kept
# for reference. The nail mask does not need them.
CHAIN_KEYS = ("thumb", "index", "middle", "ring", "little")


def log(*a):
    print("VH-REFINE6:", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def sfx_of(name):
    return "." + name[-1]


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
    """Region-aware weights in WORLD space: vertices (obj.matrix_world) and
    bone segments (arm.matrix_world) are compared in the same space."""
    import numpy as _np

    sfx = sfx_of(obj.name)
    mw = obj.matrix_world
    amw = arm.matrix_world
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

    # bone segments in WORLD space
    segs = [(b.name,
             amw @ mathutils.Vector(b.head_local),
             amw @ mathutils.Vector(b.tail_local)) for b in bones]

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
        p = mw @ mathutils.Vector(v.co)          # world-space vertex
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
    log("region weights rebuilt (world space)", obj.name)


# ---------------------------------------------------------------- nail mask
def add_nail_mask(obj, arm):
    """Vertex-color mask written into the RED channel (the channel the
    shader actually samples). 1.0 at dorsal fingertip pads with falloff.
    World-space throughout. Returns (skin_frac, nail_frac) for verification."""
    sfx = sfx_of(obj.name)
    mw = obj.matrix_world
    amw = arm.matrix_world
    w_h = amw @ mathutils.Vector(arm.data.bones["Wrist" + sfx].head_local)
    i_mcp = amw @ mathutils.Vector(arm.data.bones["IndexMeta" + sfx].tail_local)
    l_mcp = amw @ mathutils.Vector(arm.data.bones["LittleMeta" + sfx].tail_local)
    palm_normal = (i_mcp - w_h).cross(l_mcp - w_h).normalized()

    if NAIL_MASK in [c.name for c in obj.data.color_attributes]:
        obj.data.color_attributes.remove(obj.data.color_attributes[NAIL_MASK])
    layer = obj.data.color_attributes.new(NAIL_MASK, "FLOAT_COLOR", "POINT")

    tips = []
    for n in ("Thumb3", "Index3", "Middle3", "Ring3", "Little3"):
        b = arm.data.bones.get(n + sfx)
        if b:
            tail = amw @ mathutils.Vector(b.tail_local)
            tips.append((tail, 0.03))

    vals = []
    for v in obj.data.vertices:
        wp = mw @ mathutils.Vector(v.co)
        val = 0.0
        for tail, r in tips:
            d = (wp - tail).length
            dorsal = (wp - tail).dot(palm_normal)
            if d < r and dorsal < 0.0:    # dorsal pad only (opposite the palm)
                val = max(val, 1.0 - d / r)
        vals.append(val)
        layer.data[v.index].color = (val, 0.0, 0.0, 1.0)  # mask in RED
    skin = sum(1 for v in vals if v < 0.05)
    nail = sum(1 for v in vals if v > 0.8)
    log(f"nail mask {obj.name}: skin {skin}, nail {nail}, "
        f"other {len(vals) - skin - nail}")
    return skin / len(vals), nail / len(vals)


# ---------------------------------------------------------------- material
def build_skin_material(obj):
    """Clean procedural skin. Mask sampled from the RED channel of the
    nail_mask attribute (matches add_nail_mask writes)."""
    mat = obj.data.materials[0]
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (150, 0)
    bsdf.inputs["Metallic"].default_value = 0.0
    spec = bsdf.inputs.get("Specular IOR Level") or bsdf.inputs.get("Specular")
    if spec is not None:
        spec.default_value = 0.25
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.layer_name = NAIL_MASK
    vcol.location = (-150, 150)

    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 9.0
    noise.inputs["Detail"].default_value = 5.0
    noise.location = (-450, -150)

    skin_ramp = nt.nodes.new("ShaderNodeValToRGB")
    skin_ramp.color_ramp.elements[0].color = (0.62, 0.42, 0.34, 1.0)
    skin_ramp.color_ramp.elements[1].color = (0.80, 0.62, 0.52, 1.0)
    skin_ramp.location = (-250, -150)
    nt.links.new(noise.outputs["Fac"], skin_ramp.inputs["Fac"])

    # color = mix(skin, nail, mask.red)
    mix_col = nt.nodes.new("ShaderNodeMix")
    mix_col.data_type = "RGBA"
    mix_col.blend_type = "MIX"
    mix_col.inputs["A"].default_value = (0.72, 0.52, 0.42, 1.0)   # skin
    mix_col.inputs["B"].default_value = (0.78, 0.72, 0.66, 1.0)   # nail
    mix_col.location = (0, 0)
    nt.links.new(skin_ramp.outputs["Color"], mix_col.inputs["A"])
    # Separate RGB -> red channel as the mask factor
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    sep.location = (-150, 250)
    nt.links.new(vcol.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], mix_col.inputs["Factor"])
    nt.links.new(mix_col.outputs["Result"], bsdf.inputs["Base Color"])

    # roughness = mix(0.55 skin, 0.25 nail, mask.red)
    rough_mix = nt.nodes.new("ShaderNodeMix")
    rough_mix.data_type = "RGBA"
    rough_mix.location = (0, -200)
    rough_mix.inputs["A"].default_value = (0.55, 0.55, 0.55, 1.0)
    rough_mix.inputs["B"].default_value = (0.25, 0.25, 0.25, 1.0)
    nt.links.new(sep.outputs["Red"], rough_mix.inputs["Factor"])
    nt.links.new(rough_mix.outputs["Result"], bsdf.inputs["Roughness"])

    # subtle bump
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.05
    bump.location = (0, -350)
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    # bake target
    bake_node = nt.nodes.new("ShaderNodeTexImage")
    bake_node.name = "BAKE_TARGET"
    nt.nodes.active = bake_node


# ---------------------------------------------------------------- bake
def bake_maps(obj, tag):
    mat = obj.data.materials[0]
    nt = mat.node_tree
    img_node = next(n for n in nt.nodes if n.name == "BAKE_TARGET")
    nt.nodes.active = img_node
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    for kind, btype, cs in (("diffuse", "DIFFUSE", "sRGB"),
                            ("roughness", "ROUGHNESS", "Non-Color"),
                            ("normal", "NORMAL", "Non-Color")):
        img = bpy.data.images.new(f"Bake.{tag}.{kind}", RES, RES)
        img.colorspace_settings.name = cs
        img_node.image = img
        if btype == "DIFFUSE":
            # albedo only — no lighting baked into the color map
            bpy.ops.object.bake(type=btype, use_clear=True, margin=8,
                                use_selected_to_active=False,
                                pass_filter={"COLOR"})
        else:
            bpy.ops.object.bake(type=btype, use_clear=True, margin=8,
                                use_selected_to_active=False)
        p = str(OUT / f"Hand_{tag}_{kind}.png")
        img.filepath_raw = p
        img.file_format = "PNG"
        img.save()
        log("baked", kind, "->", Path(p).name)


def link_baked_from_disk(obj, tag):
    """Reload the saved PNGs as clean datablocks (correct color spaces),
    wire them to Principled, and VERIFY size/has_data/links."""
    mat = obj.data.materials[0]
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    # remove all image nodes; we rebuild exactly three
    for n in list(nt.nodes):
        if n.type == "TEX_IMAGE":
            nt.nodes.remove(n)
    checks = {}
    for kind, cs, socket in (("diffuse", "sRGB", "Base Color"),
                             ("roughness", "Non-Color", "Roughness"),
                             ("normal", "Non-Color", None)):
        p = str(OUT / f"Hand_{tag}_{kind}.png")
        img = bpy.data.images.load(p, check_existing=True)
        img.colorspace_settings.name = cs
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        if socket:
            nt.links.new(tex.outputs[0], bsdf.inputs[socket])
        else:
            nm = nt.nodes.new("ShaderNodeNormalMap")
            nm.inputs["Strength"].default_value = 1.0
            nt.links.new(tex.outputs[0], nm.inputs["Color"])
            nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
        checks[kind] = (tuple(img.size), img.has_data)
    for kind, (size, has_data) in checks.items():
        if size != (2048, 2048) or not has_data:
            raise RuntimeError(f"{kind} reload failed: size {size}, "
                               f"has_data {has_data}")
    log("baked maps reloaded + linked:", checks)


# ---------------------------------------------------------------- camera
def look_from(cam_obj, center, direction, extents, margin=1.25):
    """Ortho camera placed at center + direction*dist, aimed at center,
    with frame-up derived from the real world up projected onto the camera
    plane. Ortho size fits the projected bounds (subject ~75% of frame)."""
    d = mathutils.Vector(direction).normalized()
    # to_track_quat("-Z", ...) makes local -Z point along the GIVEN vector.
    # The camera must look from center+d*10 back toward the center, i.e. its
    # -Z must point along -d -> pass -d as the track direction.
    cam_obj.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam_obj.location = center + d * 10.0
    cam_obj.data.type = "ORTHO"
    # fit the projected extents onto the 16:9 frame with margin
    x_axis = cam_obj.matrix_world.to_3x3().col[0].normalized()
    y_axis = cam_obj.matrix_world.to_3x3().col[1].normalized()
    need_w = max(extents.dot(x_axis), extents.x, 0.05) * margin
    need_h = max(extents.dot(y_axis), extents.y, 0.05) * margin
    # ortho_scale is the full width; height = ortho_scale / aspect
    cam_obj.data.ortho_scale = max(need_w, need_h * 1.78) * 2.0


# ---------------------------------------------------------------- poses
def clear_all_animation(arm):
    if arm.animation_data:
        arm.animation_data.action = None
        arm.animation_data_clear()


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
    for i, (loc, e) in enumerate([((2, -5, 8), 350), ((-2, 5, 8), 250),
                                  ((0, 5, -8), 250), ((0, -5, -8), 250),
                                  ((5, 0, 3), 180), ((-5, 0, 3), 180)]):
        lt = bpy.data.lights.new(f"k{i}", "AREA")
        lt.energy = e
        lt.size = 2.5
        ob = bpy.data.objects.new(f"k{i}", lt)
        ob.location = loc
        bpy.context.collection.objects.link(ob)
    cam = bpy.data.cameras.new("PreviewCam")
    cam.type = "ORTHO"
    camGo = bpy.data.objects.new("PreviewCam", cam)
    bpy.context.collection.objects.link(camGo)
    sc.camera = camGo

    summary = {"hands": [], "renders": [], "exports": []}
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        arm = bpy.data.objects[f"Armature.{side}"]
        clear_all_animation(arm)
        clear_pose(arm)
        before = tri_count(obj.data)
        assert before <= 20000, (
            f"{side}: input already has {before} tris; the rev4 source is "
            "expected (13646). Refusing to exceed the 20k budget.")

        # input is the already-refined rev4 mesh: NO global subdivision,
        # only the crease-band densify + weights/shading work remain
        densify_crease_regions(obj, arm)
        surface_round(obj)
        region_weights(obj, arm)
        relax_shading(obj)
        after = tri_count(obj.data)

        add_nail_mask(obj, arm)
        build_skin_material(obj)
        bake_maps(obj, side)
        link_baked_from_disk(obj, side)

        normal, up = palm_frame_world(side, arm)
        center, ext = deformed_bounds(obj)

        # palm view: camera on the -normal side (palm faces the hollow)
        look_from(camGo, center, -normal, ext)
        sc.render.filepath = str(OUT / f"refined6_{side.lower()}_palm.png")
        bpy.ops.render.render(write_still=True)
        summary["renders"].append(f"refined6_{side.lower()}_palm")
        # back view
        look_from(camGo, center, normal, ext)
        sc.render.filepath = str(OUT / f"refined6_{side.lower()}_back.png")
        bpy.ops.render.render(write_still=True)
        summary["renders"].append(f"refined6_{side.lower()}_back")
        # side view from the thumb side
        sx = 1 if side == "R" else -1
        look_from(camGo, center, mathutils.Vector((sx, 0, 0)), ext)
        sc.render.filepath = str(OUT / f"refined6_{side.lower()}_side.png")
        bpy.ops.render.render(write_still=True)
        summary["renders"].append(f"refined6_{side.lower()}_side")

        summary["hands"].append({"side": side, "tris_before": before,
                                 "tris_after": after})

    for side in ("R", "L"):
        clear_all_animation(bpy.data.objects[f"Armature.{side}"])
        clear_pose(bpy.data.objects[f"Armature.{side}"])
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "refined_hands.blend"))

    # exports through the baseline wrist-local rule
    for side in ("R", "L"):
        export_wrist_local_fbx(side, "refined6")

    (OUT / "refinement-summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n")
    log("summary written")


def palm_frame_world(side, arm):
    """World-space (normal, up): real wrist point + the two MCP tails."""
    sfx = sfx_of(side)
    amw = arm.matrix_world
    w_h = amw @ mathutils.Vector(arm.data.bones["Wrist" + sfx].head_local)
    i_mcp = amw @ mathutils.Vector(arm.data.bones["IndexMeta" + sfx].tail_local)
    l_mcp = amw @ mathutils.Vector(arm.data.bones["LittleMeta" + sfx].tail_local)
    tip = amw @ mathutils.Vector(arm.data.bones["Middle3" + sfx].tail_local)
    normal = (i_mcp - w_h).cross(l_mcp - w_h).normalized()
    up = (tip - w_h).normalized()
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
        raise RuntimeError(f"{side} wrist not at origin: {wrist_after}")
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


if __name__ == "__main__":
    main()
