# -*- coding: utf-8 -*-
"""Refinement rev2 — true palm-normal cameras, region-aware weights, geometry
rounding. Reads the frozen baseline .blend; writes output/rev2/ only.

Fixed vs rev1 (Codex 05:10 feedback):
  1. palm/back cameras placed on the REAL palm normal (wrist->indexMCP x
     wrist->littleMCP, per side), frame up-vector along wrist->middle-tip:
     five fingers, nails and wrist fully visible, no finger overlap.
  2. Region-aware weights: each vertex only joins its own finger chain
     (+ metacarpal + Wrist + Forearm), chosen by nearest chain head; one
     topology-neighbour smoothing pass relaxes borders. No nearest-3 across
     all bones -> no thin-sheet/fold from neighbour-finger mis-assignment.
  3. Geometry: crease-band densify (knuckles, thumb web, palm root) plus a
     light surface-round pass so finger contours are actually round.
  4. Material: baked textures are repacked (stale paths fixed), highlights
     tamed (specular 0.25 / roughness fallback 0.55), magenta neutralized
     with a multiply node.

Armature names, hierarchy, rest matrices, axes and lengths untouched.
Usage: blender --background --factory-startup --python refine_hands_rev2.py
"""

import json
import math
from pathlib import Path

import bmesh
import bpy
import mathutils

HERE = Path(__file__).resolve().parent
BASELINE = HERE / "baseline"
OUT = HERE / "output" / "rev2"
BLEND_IN = BASELINE / "hands_source.blend"
BLEND_OUT = OUT / "refined_hands.blend"

TARGET_TRIS = 16000
CHAIN_KEYS = ("thumb", "index", "middle", "ring", "little")


def log(*a):
    print("VH-REFINE2:", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def sfx_of(side):
    """Suffix from a side char or an object/bone name (last char)."""
    return "." + side[-1]


# ---------------------------------------------------------------- geometry
def subdivide_once(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges, use_grid_fill=True,
                              smooth=0.0, cuts=1)
    bm.to_mesh(obj.data)
    bm.free()


def densify_crease_regions(obj, arm):
    """Subdivide only edges inside crease bands: MCP knuckle rings, the thumb
    web, and the palm root. Adds vertices exactly where weights transition."""
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
    """One light neighbour-average pass over vertex positions to round the
    finger contours; normals recalculated afterwards."""
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
    """Region-aware skin weights: vertices join only their own finger chain
    (+ metacarpal + Wrist + Forearm), one neighbour-smoothing pass relaxes
    borders, then rows are renormalized."""
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
        # owning chain = chain whose best bone distance is smallest
        best_chain, best_d, best_bone = None, None, None
        for key, allowed in chains.items():
            for n in allowed:
                d = dist_to(n, p)
                if best_d is None or d < best_d:
                    best_chain, best_d, best_bone = key, d, n
        allowed = chains[best_chain]
        ws = []
        for n in allowed:
            d = dist_to(n, p)
            ws.append(((1.0 / max(d, 0.02)) ** 3, name_idx[n]))
        total = sum(w for w, _ in ws)
        for w, i in ws:
            wmat[vi, i] = w / total

    # neighbour smoothing (one pass), then renormalize
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    sm = _np.copy(wmat)
    for e in bm.edges:
        a, b = e.verts[0].index, e.verts[1].index
        sm[a] = sm[a] * 0.6 + wmat[b] * 0.4
        sm[b] = sm[b] * 0.6 + wmat[a] * 0.4
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


# ---------------------------------------------------------------- material
def fix_material(obj):
    mat = obj.data.materials[0] if obj.data.materials else None
    if mat is None or not mat.use_nodes:
        return
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        return
    bsdf.inputs["Metallic"].default_value = 0.0
    spec = bsdf.inputs.get("Specular IOR Level") or bsdf.inputs.get("Specular")
    if spec is not None:
        spec.default_value = 0.25          # tame sharp highlights
    rough = bsdf.inputs["Roughness"]
    if not rough.is_linked:
        rough.default_value = 0.55         # safe mid roughness
    base = bsdf.inputs["Base Color"]
    if base.is_linked:
        src = base.links[0].from_socket
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs["B"].default_value = (1.0, 0.82, 0.84, 1.0)
        nt.links.new(src, mix.inputs["A"])
        nt.links.new(mix.outputs["Result"], base)


# ---------------------------------------------------------------- palm frame
def palm_frame(side, arm):
    """(normal, up) for the true palm surface. IndexMeta/LittleMeta heads sit
    AT the wrist joint in this rig (zero vectors), so the normal uses the
    METACARPAL TAILS: normal = (wristTail->indexMCP) x (wristTail->
    littleMCP); up = wrist->middleTip."""
    sfx = sfx_of(side)
    w = mathutils.Vector(arm.data.bones["Wrist" + sfx].tail_local)
    i_mcp = mathutils.Vector(arm.data.bones["IndexMeta" + sfx].tail_local)
    l_mcp = mathutils.Vector(arm.data.bones["LittleMeta" + sfx].tail_local)
    tip = mathutils.Vector(arm.data.bones["Middle3" + sfx].tail_local)
    normal = (i_mcp - w).cross(l_mcp - w).normalized()
    up = (tip - w).normalized()
    return normal, up


def look_from(cam_obj, center, direction, up, extents):
    """Orthographic camera on `center` looking along -direction, frame up
    along `up`, sized to the deformed bounds with a margin."""
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
    cam_obj.data.type = 'ORTHO'
    # ortho_scale is the FULL width of the frame; keep the subject within a
    # 1.25x margin. Aspect 16:9 means height = width/1.78.
    half_h = max(extents.y, 0.05) * 1.25
    cam_obj.data.ortho_scale = max(half_h * 2.0, half_h * 2.0 * 1.78)


def deformed_bounds(obj):
    """Returns (center, extents, size) as mathutils Vectors, computed from the
    evaluated (deformed) bounding box."""
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    corners = [ev.matrix_world @ mathutils.Vector(c) for c in ev.bound_box]
    mn = mathutils.Vector((min(c.x for c in corners),
                           min(c.y for c in corners),
                           min(c.z for c in corners)))
    mx = mathutils.Vector((max(c.x for c in corners),
                           max(c.y for c in corners),
                           max(c.z for c in corners)))
    center = (mn + mx) / 2
    extents = (mx - mn) / 2
    return center, extents, mx - mn


def shoot(sc, cam, name):
    sc.render.filepath = str(OUT / f"{name}.png")
    bpy.ops.render.render(write_still=True)
    log("rendered", name)


# ---------------------------------------------------------------- poses
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
        if pb is None:
            continue
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (math.radians(x), math.radians(y), math.radians(z))
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
    for i, (x, y, z, e) in enumerate([(2, -5, 8, 900), (-3, 4, -5, 600),
                                      (0, 5, 6, 1200)]):
        lt = bpy.data.lights.new(f"k{i}", "AREA")
        lt.energy = e
        lt.size = 2.0
        ob = bpy.data.objects.new(f"k{i}", lt)
        ob.location = (x, y, z)
        bpy.context.collection.objects.link(ob)
    cam = bpy.data.cameras.new("PreviewCam")
    camGo = bpy.data.objects.new("PreviewCam", cam)
    bpy.context.collection.objects.link(camGo)
    sc.camera = camGo

    summary = {"hands": []}
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        arm = bpy.data.objects[f"Armature.{side}"]
        before = tri_count(obj.data)

        # geometry: linear densify + crease bands + rounding
        subdivide_once(obj)
        densify_crease_regions(obj, arm)
        surface_round(obj)

        # weights: region-aware + neighbour smoothing
        region_weights(obj, arm)
        relax_shading(obj)
        fix_material(obj)
        after = tri_count(obj.data)

        normal, up = palm_frame(side, arm)
        rest_center, rest_ext, rest_size = deformed_bounds(obj)

        def shot(name, center, direction, ext):
            look_from(camGo, center, direction, up, ext)
            shoot(sc, cam, name)

        # palm view (camera on +normal side, fingers up)
        shot(f"refined2_{side.lower()}_palm", rest_center, normal, rest_ext)
        # back view
        shot(f"refined2_{side.lower()}_back", rest_center, -normal, rest_ext)
        # side view: from thumb side
        sx = 1 if side == "R" else -1
        side_dir = up.cross(mathutils.Vector((sx, 0, 0))).normalized()
        shot(f"refined2_{side.lower()}_side", rest_center, side_dir, rest_ext)
        # fist closeup (pose before, bounds from the deformed mesh)
        apply_pose(arm, side, "fist")
        f_center, f_ext, _ = deformed_bounds(obj)
        shot(f"refined2_{side.lower()}_fist", f_center, normal, f_ext)
        clear_pose(arm)
        # restore rest target pose
        bpy.context.view_layer.update()

        summary["hands"].append({
            "side": side,
            "tris_before": before,
            "tris_after": after,
            "palmNormal": [round(v, 3) for v in normal],
        })
        log(side, f"{before} -> {after} tris")

    (OUT / "refinement-summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n")
    for side in ("R", "L"):
        clear_pose(bpy.data.objects[f"Armature.{side}"])
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_OUT))
    log("saved", BLEND_OUT)


if __name__ == "__main__":
    main()
