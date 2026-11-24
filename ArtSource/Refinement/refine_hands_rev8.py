# -*- coding: utf-8 -*-
"""Refinement rev8 — targeted SKIN-WEIGHT surgery for the two run23 fist
defects (dorsal knuckle hard fold, thumb-web bulk/tear). Geometry, skeleton,
rest matrices, UVs and the rev7 textures are untouched.

Input : output/rev7/refined_hands.blend (read-only)
Output: output/rev8/ (blend, 2 FBX, copied 2K textures, 4 same-camera
        before/after comparison renders of the A2-rev2 fist sample,
        refinement-summary.json)

Diagnosis (probe_rev8.py / probe_rev8b.py + render inspection, read-only):
rest geometry is clean (13724 tris/hand); under the A2 fist sample the
1/d^3 weights create DISPLACEMENT CLIFFS — adjacent vertices deformed by
very different amounts tear the mesh (black sliver at the thumb web in
the web_before render) and read as hard dorsal folds at the knuckles.

Fix, weight-space only, self-locating:
  1. classic parent/child relaxation at each flexion joint head (50/50
     pull with linear falloff);
  2. web-centre vertices hand part of their thumb/index-meta weight to
     Wrist (web is hand-back skin);
  3. DISPLACEMENT-DRIVEN HARMONISATION, 3 passes: while the fist sample
     is applied, find edges whose endpoints' posed displacements differ
     by more than a threshold and mix the two endpoints' weight dicts
     50/50. Thresholds 0.15/0.10/0.07 shrink the cliff into a smooth
     gradient wherever it actually is — no radius tuning.
  4. rest-space checksum before/after proves geometry untouched.

Usage: blender --background --factory-startup --python refine_hands_rev8.py
"""

import json
import math
import shutil
from pathlib import Path

import bpy
import mathutils

HERE = Path(__file__).resolve().parent
OUT = HERE / "output" / "rev8"
BLEND_IN = HERE / "output" / "rev7" / "refined_hands.blend"
TEX_DIR_IN = HERE / "output" / "rev7"
PROFILE = (HERE.parent / "ActionCalibration" / "input_rev7" /
           "hand_rig_profile.json")

RES_X, RES_Y = 1280, 960


def log(*a):
    print("VH-REFINE8:", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def sfx_of(side):
    return "." + side


# ---------------------------------------------------------------- weights
JOINT_BANDS = [  # (parent, child, radius as fraction of hand length)
    ("IndexMeta", "Index1", 0.055),
    ("Index1", "Index2", 0.030),
    ("Index2", "Index3", 0.026),
    ("MiddleMeta", "Middle1", 0.055),
    ("Middle1", "Middle2", 0.030),
    ("Middle2", "Middle3", 0.026),
    ("RingMeta", "Ring1", 0.055),
    ("Ring1", "Ring2", 0.030),
    ("Ring2", "Ring3", 0.026),
    ("LittleMeta", "Little1", 0.055),
    ("Little1", "Little2", 0.030),
    ("Little2", "Little3", 0.026),
    ("ThumbMeta", "Thumb1", 0.055),
    ("Thumb1", "Thumb2", 0.030),
    ("Thumb2", "Thumb3", 0.026),
]
WEB_OWNERS = ("ThumbMeta", "Thumb1", "IndexMeta", "Index1")
BLEND_AT_JOINT = 0.75   # parent/child ratio pulled this far toward 50/50
WEB_TO_WRIST = 0.60     # weight moved to Wrist at the web centre
HARM_THRESHOLDS = (0.15, 0.10, 0.07, 0.05, 0.035, 0.025, 0.02)  # diffuse to convergence
HARM_ZONE = 0.45   # harmonise only edges near web/joint zones (×hand_len)


def read_weights(obj):
    w = {}
    for v in obj.data.vertices:
        d = {}
        for g in v.groups:
            name = obj.vertex_groups[g.group].name
            if g.weight > 1e-4:
                d[name] = g.weight
        w[v.index] = d
    return w


def write_weights(obj, w):
    obj.vertex_groups.clear()
    names = sorted({n for d in w.values() for n in d})
    gidx = {}
    for n in names:
        gidx[n] = obj.vertex_groups.new(name=n).index
    for vi, d in w.items():
        for n, wt in d.items():
            if wt > 1e-4:
                obj.vertex_groups[gidx[n]].add([vi], wt, "REPLACE")


def posed_world(obj):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    evm = ev.to_mesh()
    pw = [obj.matrix_world @ mathutils.Vector(v.co) for v in evm.vertices]
    ev.to_mesh_clear()
    return pw


def rest_world(obj):
    mw = obj.matrix_world
    return [mw @ mathutils.Vector(v.co) for v in obj.data.vertices]


def displacement_field(obj, rw):
    pw = posed_world(obj)
    return [ (pw[i] - p).length for i, p in enumerate(rw) ]


def harmonise_cliffs(obj, w, rw, thresholds, zone_centers, hand_len):
    """While the fist pose is applied: mix the weight dicts of vertices
    joined by an edge whose endpoint displacements differ too much.
    Restricted to edges near the zone centers (web + joint bands) so the
    diffusion cannot wash out global skin detail. Iterates until the max
    cliff stops shrinking. Returns deduplicated mixed verts."""
    mw = obj.matrix_world
    zone_r = HARM_ZONE * hand_len
    edges = [(e.vertices[0], e.vertices[1]) for e in obj.data.edges
             if any((rw[e.vertices[0]] - c).length < zone_r or
                    (rw[e.vertices[1]] - c).length < zone_r
                    for c in zone_centers)]
    log(f"  harmonise zone edges {len(edges)} of {len(obj.data.edges)}")
    touched = set()
    for thr in thresholds:
        bpy.context.view_layer.update()
        disp = displacement_field(obj, rw)
        mixed = set()
        for a, b in edges:
            if abs(disp[a] - disp[b]) <= thr:
                continue
            if disp[a] < 0.02 and disp[b] < 0.02:
                continue          # static area, ignore float noise
            wa, wb = w[a], w[b]
            allk = set(wa) | set(wb)
            for k in allk:
                va, vb = wa.get(k, 0.0), wb.get(k, 0.0)
                wa[k] = va * 0.5 + vb * 0.5
                wb[k] = vb * 0.5 + va * 0.5
            mixed.add(a)
            mixed.add(b)
        # renormalise mixed verts, drop negligible tail
        for vi in mixed:
            d = w[vi]
            s = sum(d.values())
            if s > 1e-9:
                w[vi] = {k: v / s for k, v in d.items() if v / s > 5e-3}
        write_weights(obj, w)
        touched |= mixed
        cliff = max_edge_cliff(obj, rw)
        log(f"  harmonise pass thr {thr}: mixed {len(mixed)} "
            f"(cumulative {len(touched)}), max cliff {cliff:.3f}")
    return touched


def max_edge_cliff(obj, rw):
    disp = displacement_field(obj, rw)
    m = 0.0
    for e in obj.data.edges:
        a, b = e.vertices
        if disp[a] < 0.02 and disp[b] < 0.02:
            continue
        m = max(m, abs(disp[a] - disp[b]))
    return m


def joint_head_world(arm, side, parent, child):
    sfx = sfx_of(side)
    return arm.matrix_world @ mathutils.Vector(
        arm.data.bones[child + sfx].head_local)  # == parent tail


def fix_weights(obj, arm, side):
    """Weight-space surgery. Returns a dict of counters for the summary."""
    mw = obj.matrix_world
    sfx = sfx_of(side)
    hand_len = (arm.matrix_world @ mathutils.Vector(
        arm.data.bones["Middle3" + sfx].tail_local) -
        arm.matrix_world @ mathutils.Vector(
        arm.data.bones["Wrist" + sfx].head_local)).length

    w = read_weights(obj)
    edited = set()

    # ---- 1. joint-band parent/child relaxation ----
    # NOTE: vertex-group names carry the side suffix (Index1.R), so every
    # group lookup needs sfx appended.
    band_hits = 0
    for parent, child, rf in JOINT_BANDS:
        parent_g, child_g = parent + sfx, child + sfx
        head = joint_head_world(arm, side, parent, child)
        r = rf * hand_len
        for vi, d in w.items():
            wc, wp = d.get(child_g, 0.0), d.get(parent_g, 0.0)
            if wc <= 1e-4 and wp <= 1e-4:
                continue
            p = mw @ mathutils.Vector(obj.data.vertices[vi].co)
            dist = (p - head).length
            if dist >= r:
                continue
            t = BLEND_AT_JOINT * (1.0 - dist / r)
            m = wc + wp
            d[child_g] = wc + t * (m / 2 - wc)
            d[parent_g] = wp + t * (m / 2 - wp)
            edited.add(vi)
            band_hits += 1
    write_weights(obj, w)

    # ---- 2. web region: OUTER rim follows the thumb column (bridges the
    # opening as the thumb rotates away), INNER web anchors to Wrist.
    # Moving web skin to Wrist alone leaves the outer rim behind -> the
    # thumb opening shows through (black wedge), so rim verts get their
    # thumb-column share boosted instead. ----
    anchors = []
    for n in ("ThumbMeta", "IndexMeta"):
        anchors.append(arm.matrix_world @ mathutils.Vector(
            arm.data.bones[n + sfx].tail_local))
    for n in ("Thumb1", "Index1"):
        anchors.append(arm.matrix_world @ mathutils.Vector(
            arm.data.bones[n + sfx].head_local))
    web_c = sum(anchors, mathutils.Vector()) / len(anchors)
    r_web = 0.16 * hand_len
    thumb_col = {n + sfx for n in ("ThumbMeta", "Thumb1", "Thumb2")}
    thumb_out = (anchors[0] - web_c).normalized()   # towards thumb rim
    wrist_g = "Wrist" + sfx
    web_hits = 0
    for vi, d in w.items():
        p = mw @ mathutils.Vector(obj.data.vertices[vi].co)
        off = p - web_c
        dist = off.length
        if dist >= r_web:
            continue
        t = 1.0 - dist / r_web                     # 1 at web centre
        # outer rim: positive along the thumb direction -> follow thumb
        toward_thumb = off.normalized().dot(thumb_out) if dist > 1e-9 else 0.0
        if toward_thumb > 0.05:
            cur = sum(d.get(n, 0.0) for n in thumb_col)
            boost = 0.95 * t * min(toward_thumb / 0.6, 1.0)
            if cur < boost:
                # scale thumb column up, shrink everything else proportionally
                add = boost - cur
                others = {k: v for k, v in d.items() if k not in thumb_col}
                s_others = sum(others.values())
                if s_others > 1e-6:
                    take = min(add, s_others)
                    for k in others:
                        d[k] = d[k] * (1.0 - take / s_others)
                    per = take / len(thumb_col)
                    for n in thumb_col:
                        d[n] = d.get(n, 0.0) + per
                    edited.add(vi)
                    web_hits += 1
        else:
            # inner web: gently anchor to Wrist (damp the bulk)
            move = 0.0
            take_t = WEB_TO_WRIST * t * 0.5
            for n in thumb_col:
                k = d.get(n, 0.0) * take_t
                d[n] = d.get(n, 0.0) - k
                move += k
            if move > 0:
                d[wrist_g] = d.get(wrist_g, 0.0) + move
                edited.add(vi)
                web_hits += 1
    write_weights(obj, w)

    # ---- 3. displacement-driven harmonisation (pose is applied) ----
    rw = rest_world(obj)
    zone_centers = [web_c]
    for parent, child, rf in JOINT_BANDS:
        zone_centers.append(joint_head_world(arm, side, parent, child))
    harmonise_cliffs(obj, w, rw, HARM_THRESHOLDS, zone_centers, hand_len)
    edited |= set()

    cliff = max_edge_cliff(obj, rw)
    log(f"{side}: band verts {band_hits}, web verts {web_hits}, "
        f"final max edge cliff {cliff:.3f} (hand_len {hand_len:.3f})")
    return {"band_verts": band_hits, "web_verts": web_hits,
            "max_edge_cliff": round(cliff, 4),
            "hand_len": round(hand_len, 4)}


# ---------------------------------------------------------------- metrics
def fist_metrics(obj, arm, side):
    """Band edge ratios + web displacement under the CURRENT pose."""
    sfx = sfx_of(side)
    amw, mw = arm.matrix_world, obj.matrix_world
    knuckles = ["Index1", "Middle1", "Ring1", "Little1", "Thumb1"]
    rest_pts = [amw @ mathutils.Vector(arm.data.bones[n + sfx].head_local)
                for n in knuckles]
    rw = [mw @ mathutils.Vector(v.co) for v in obj.data.vertices]
    pw = posed_world(obj)

    ratios = []
    for e in obj.data.edges:
        a, b = e.vertices
        for c in rest_pts:
            if (rw[a] - c).length < 0.055 and (rw[b] - c).length < 0.055:
                lr = (rw[a] - rw[b]).length
                if lr > 1e-7:
                    ratios.append((pw[a] - pw[b]).length / lr)
                break
    ratios.sort()
    mn = ratios[0] if ratios else None

    anchors = []
    for n in ("ThumbMeta", "IndexMeta"):
        anchors.append(amw @ mathutils.Vector(
            arm.data.bones[n + sfx].tail_local))
    for n in ("Thumb1", "Index1"):
        anchors.append(amw @ mathutils.Vector(
            arm.data.bones[n + sfx].head_local))
    web_c = sum(anchors, mathutils.Vector()) / len(anchors)
    hand_len = (amw @ mathutils.Vector(
        arm.data.bones["Middle3" + sfx].tail_local) -
        amw @ mathutils.Vector(
        arm.data.bones["Wrist" + sfx].head_local)).length
    disp = []
    for vi, p in enumerate(rw):
        if (p - web_c).length < 0.12 * hand_len:
            disp.append((pw[vi] - p).length)
    disp.sort(reverse=True)

    disp_all = [(pw[i] - p).length for i, p in enumerate(rw)]
    cliff = 0.0
    for e in obj.data.edges:
        a, b = e.vertices
        if disp_all[a] < 0.02 and disp_all[b] < 0.02:
            continue
        cliff = max(cliff, abs(disp_all[a] - disp_all[b]))
    return {"band_edges": len(ratios), "min_ratio": mn,
            "web_n": len(disp),
            "web_max_disp": disp[0] if disp else 0.0,
            "web_mean_disp": (sum(disp) / len(disp)) if disp else 0.0,
            "max_edge_cliff": round(cliff, 4)}


# ---------------------------------------------------------------- pose
def apply_sample(arm, chans, sample, side):
    sfx = sfx_of(side)
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for i, c in enumerate(chans):
        pb = arm.pose.bones[c["bone"] + sfx]
        axis = {"X": 0, "Y": 1, "Z": 2}[c["axis"]]
        deg = (sample[i] - c["rest"]) * c["scale"] * c["sign"]
        pb.rotation_euler[axis] += math.radians(deg)
    bpy.context.view_layer.update()


def clear_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- render
def look_from(cam_obj, center, direction, extent):
    d = mathutils.Vector(direction).normalized()
    cam_obj.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam_obj.location = center + d * 10.0
    cam_obj.data.ortho_scale = extent * 2.6   # subject ~75% of frame


def setup_scene():
    sc = bpy.context.scene
    if not sc.world:
        sc.world = bpy.data.worlds.new("Studio")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.35, 0.35, 0.36, 1.0)
    lights = [((2, -5, 8), 300), ((-2, 5, 8), 220), ((0, 5, -8), 220),
              ((5, 0, 3), 160), ((-5, 0, 3), 160)]
    for i, (loc, e) in enumerate(lights):
        lt = bpy.data.lights.new(f"r8k{i}", "AREA")
        lt.energy = e
        lt.size = 2.5
        ob = bpy.data.objects.new(f"r8k{i}", lt)
        ob.location = loc
        bpy.context.collection.objects.link(ob)
    cam = bpy.data.cameras.new("Rev8Cam")
    cam.type = "ORTHO"
    cam_go = bpy.data.objects.new("Rev8Cam", cam)
    bpy.context.collection.objects.link(cam_go)
    sc.camera = cam_go
    sc.render.resolution_x = RES_X
    sc.render.resolution_y = RES_Y
    return cam_go


# ---------------------------------------------------------------- textures
def copy_and_relink(side):
    """Copy the rev7 2K PNGs into output/rev8 and point the material's
    image nodes at the copies (rev7 stays untouched)."""
    mat = bpy.data.objects[f"Hand.{side}"].data.materials[0]
    nt = mat.node_tree
    kinds = ("diffuse", "roughness", "normal")
    for kind in kinds:
        src = TEX_DIR_IN / f"Hand_{side}_{kind}.png"
        dst = OUT / f"Hand_{side}_{kind}.png"
        shutil.copyfile(src, dst)
        img = bpy.data.images.load(str(dst), check_existing=False)
        img.colorspace_settings.name = ("sRGB" if kind == "diffuse"
                                        else "Non-Color")
        swapped = False
        for n in nt.nodes:
            if n.type == "TEX_IMAGE" and n.image and \
                    Path(n.image.filepath).name == src.name:
                n.image = img
                swapped = True
        log(f"{side} relink {kind}: swapped={swapped}, size={tuple(img.size)}")


# ---------------------------------------------------------------- export
def export_wrist_local_fbx(side, tag):
    """rev7's proven wrist-local rule, byte-identical behaviour."""
    o = bpy.data.objects[f"Hand.{side}"]
    arm = bpy.data.objects[f"Armature.{side}"]
    arm_world, mesh_world = arm.matrix_world.copy(), o.matrix_world.copy()
    wrist_w = arm_world @ mathutils.Vector(
        arm.data.bones["Wrist." + side].head_local)
    tip_w = arm_world @ mathutils.Vector(
        arm.data.bones["Middle3." + side].tail_local)
    metric = 0.19 / (tip_w - wrist_w).length
    bake = (mathutils.Matrix.Scale(metric, 4) @
            mathutils.Matrix.Translation(-wrist_w))
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
    # restore rest matrices so the saved blend keeps the canonical rig space
    arm.parent = None
    arm.matrix_world = arm_world
    o.matrix_world = mesh_world
    bpy.context.view_layer.update()
    log("exported", Path(path).name, "metric", round(metric, 6))
    return {"side": side, "file": Path(path).name,
            "metresPerSourceUnit": round(metric, 6)}


# ---------------------------------------------------------------- main
def main():
    OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_IN))
    prof = json.load(open(str(PROFILE), encoding="utf-8"))
    chans, fist = prof["channels"], prof["samples"]["fist"]

    cam_go = setup_scene()
    summary = {"input": str(BLEND_IN), "hands": {}, "renders": [],
               "exports": []}

    # rest-state structural snapshot + checksum (must not change)
    rest_sum = {}
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        rest_sum[side] = (tri_count(obj.data),
                          round(sum(v.co.length for v in obj.data.vertices), 6))

    # ---- apply fist to both hands (pose comparison only, per brief) ----
    for side in ("R", "L"):
        apply_sample(bpy.data.objects[f"Armature.{side}"], chans, fist, side)

    # ---- BEFORE: numeric + renders ----
    before = {}
    for side in ("R", "L"):
        before[side] = fist_metrics(
            bpy.data.objects[f"Hand.{side}"],
            bpy.data.objects[f"Armature.{side}"], side)
        log(f"{side} BEFORE", before[side])

    arm_r = bpy.data.objects["Armature.R"]
    amw = arm_r.matrix_world
    w_h = amw @ mathutils.Vector(arm_r.data.bones["Wrist.R"].head_local)
    i_mcp = amw @ mathutils.Vector(
        arm_r.data.bones["IndexMeta.R"].tail_local)
    l_mcp = amw @ mathutils.Vector(
        arm_r.data.bones["LittleMeta.R"].tail_local)
    normal = (i_mcp - w_h).cross(l_mcp - w_h).normalized()
    knuckles = ["Index1", "Middle1", "Ring1", "Little1", "Thumb1"]
    kc = sum((amw @ mathutils.Vector(arm_r.data.bones[n + ".R"].head_local)
              for n in knuckles), mathutils.Vector()) / len(knuckles)
    thumb_dir = (amw @ mathutils.Vector(
        arm_r.data.bones["ThumbMeta.R"].tail_local) - w_h).normalized()

    look_from(cam_go, kc, normal, 0.42)
    bpy.context.scene.render.filepath = str(OUT / "rev8_cmp_dorsal_before.png")
    bpy.ops.render.render(write_still=True)
    summary["renders"].append("rev8_cmp_dorsal_before")

    web_dir = (normal * 0.45 + thumb_dir * 0.89).normalized()
    look_from(cam_go, kc, web_dir, 0.40)
    bpy.context.scene.render.filepath = str(OUT / "rev8_cmp_web_before.png")
    bpy.ops.render.render(write_still=True)
    summary["renders"].append("rev8_cmp_web_before")

    # ---- FIX ----
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        arm = bpy.data.objects[f"Armature.{side}"]
        info = fix_weights(obj, arm, side)
        after_m = fist_metrics(obj, arm, side)
        log(f"{side} AFTER", after_m)
        tc, chk = tri_count(obj.data), sum(
            v.co.length for v in obj.data.vertices)
        chk = round(chk, 6)
        assert (tc, chk) == rest_sum[side], (
            f"{side}: rest geometry changed! {(tc, chk)} vs {rest_sum[side]}")
        assert after_m["max_edge_cliff"] < before[side]["max_edge_cliff"], (
            f"{side}: displacement cliff did not shrink")
        assert after_m["min_ratio"] > before[side]["min_ratio"] - 1e-6, (
            f"{side}: knuckle band did not improve")
        summary["hands"][side] = {
            "before": before[side], "after": after_m, "fix": info,
            "tris": tc, "verts": len(obj.data.vertices),
            "bones": len(arm.data.bones)}

    # ---- AFTER renders (also proves the relink) ----
    copy_and_relink("R")
    copy_and_relink("L")
    look_from(cam_go, kc, normal, 0.42)
    bpy.context.scene.render.filepath = str(OUT / "rev8_cmp_dorsal_after.png")
    bpy.ops.render.render(write_still=True)
    summary["renders"].append("rev8_cmp_dorsal_after")

    look_from(cam_go, kc, web_dir, 0.40)
    bpy.context.scene.render.filepath = str(OUT / "rev8_cmp_web_after.png")
    bpy.ops.render.render(write_still=True)
    summary["renders"].append("rev8_cmp_web_after")

    # ---- save rest blend + exports ----
    for side in ("R", "L"):
        clear_pose(bpy.data.objects[f"Armature.{side}"])
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "refined_hands.blend"))
    for side in ("R", "L"):
        summary["exports"].append(export_wrist_local_fbx(side, "rev8"))

    (OUT / "refinement-summary.json").write_text(
        json.dumps(summary, indent=2, default=str) + "\n",
        encoding="utf-8", newline="\n")
    log("summary written")


if __name__ == "__main__":
    main()
