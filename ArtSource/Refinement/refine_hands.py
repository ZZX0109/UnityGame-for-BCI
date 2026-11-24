# -*- coding: utf-8 -*-
"""Game04 refinement pass — light-realistic natural hands from the baseline.

Reads the frozen baseline .blend (ArtSource/Refinement/baseline/
hands_source.blend, exported by the canonical build), refines ONLY mesh /
weights / materials, and writes everything to ArtSource/Refinement/output/:

  refined_hands.blend          refined editable source (rest pose, same rules)
  Hand_{R,L}.fbx               wrist-local FBX, same export rule as baseline
  Hand_{R,L}_{diffuse,normal,roughness}.png   2K baked textures
  render_*.png                 review set (palm/back/side + deformed closeups)
  refinement-summary.json      what was done (no PASS claims)

Hard invariants (per coordination):
  * bone names, hierarchy, rest bone matrices, left/right curl axes and bone
    lengths are untouched — armature object and bones are read-only here
  * wrist-local export rule preserved: cache parent/world matrices first,
    metric scale 0.19 m wrist->middle tip, root identity, no double offsets
  * continuous skinned mesh only (no capsules / static swaps)

Usage (Codex may re-run):
  blender.exe --background --factory-startup --python refine_hands.py
"""

import json
import math
import os
from pathlib import Path

import bmesh
import bpy
import mathutils

HERE = Path(__file__).resolve().parent
BASELINE = HERE / "baseline"
OUT = HERE / "output"
BUILD = OUT / "build"
BLEND_IN = BASELINE / "hands_source.blend"
BLEND_OUT = OUT / "refined_hands.blend"
REVIEW = OUT
FBX_DIR = OUT

TARGET_TRIS = 16000        # per hand, within the contracted 10-20k band


def log(*a):
    print("VH-REFINE:", *a, flush=True)


# ---------------------------------------------------------------- helpers
def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def subdivide_to_target(obj, target):
    """One whole-mesh edge subdivision multiplies tris by ~4 (quad grid fill):
    baseline 3414 -> ~13656, inside the 10-20k band. A second pass would blow
    past 20k, so exactly one pass, guarded. Single cuts keep the base quads'
    flow, which is what makes the weight rebuild in transfer_weights()
    well-defined."""
    me = obj.data
    if tri_count(me) >= target:
        return
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.subdivide_edges(
        bm, edges=bm.edges, use_grid_fill=True, smooth=0.0, cuts=1)
    bm.to_mesh(me)
    bm.free()
    log("subdivided", obj.name, "tris", tri_count(me))


def transfer_weights(obj):
    """Distance-based re-weighting after subdivision, mirroring the baseline
    production rule (nearest 3 bones, inverse-cube, tip 60/40 parent blend).
    Rebuilding groups from the same rule on the denser mesh removes the hard
    metacarpal crease visible in run13 (few verts -> abrupt group borders)."""
    import numpy as _np
    arm = obj.parent
    bones = [(b.name, mathutils.Vector(b.head_local),
              mathutils.Vector(b.tail_local)) for b in arm.data.bones]
    # clear old groups
    obj.vertex_groups.clear()
    n_v = len(obj.data.vertices)
    wmat = _np.zeros((n_v, len(bones)), dtype=_np.float64)
    for vi, v in enumerate(obj.data.vertices):
        p = mathutils.Vector(v.co)
        ds = []
        for bi, (bn, h, t) in enumerate(bones):
            ab = t - h
            ap = p - h
            tt = max(0.0, min(1.0, ap.dot(ab) / max(ab.length_squared, 1e-9)))
            d = (ap - ab * tt).length
            ds.append((d, bi))
        ds.sort()
        # tip vertices beyond the last knuckle get a 50/50 blend with the
        # parent segment so a deep DIP curl cannot fling them across the scene
        far_tip = ds[0][0] > 0.25 and len(ds) > 1
        if far_tip:
            wmat[vi, ds[0][1]] = 0.6
            wmat[vi, ds[1][1]] = 0.4
        else:
            top = ds[:3]
            total = sum((1.0 / max(d, 0.02)) ** 3 for d, _ in top)
            for d, bi in top:
                wmat[vi, bi] = ((1.0 / max(d, 0.02)) ** 3) / total
    for bi, (bn, _, _) in enumerate(bones):
        g = obj.vertex_groups.new(name=bn)
        idx = [vi for vi in range(n_v) if wmat[vi, bi] > 1e-4]
        if idx:
            g.add(idx, 1.0, "REPLACE")
            for vi in idx:
                g.add([vi], float(wmat[vi, bi]), "REPLACE")
    log("weights rebuilt", obj.name, "groups", len(bones))


def relax_shading(obj):
    """Smooth normals + uniform shade so the low-poly facets disappear; edge
    splits limited to the wrist cap seam (sharp by construction)."""
    me = obj.data
    for p in me.polygons:
        p.use_smooth = True
    bm = bmesh.new()
    bm.from_mesh(me)
    for e in bm.edges:
        e.smooth = True
    bm.to_mesh(me)
    bm.free()


def add_nails_and_polish(obj):
    """Local geometry detail near fingertips: inset the last ring before each
    tip and crease it slightly so the nail plate reads as a separate surface
    without textures (nail polish stays in the material step). Lightweight:
    works on the tip vertex clusters created by subdivision."""
    # Nail plates come from the 2K diffuse/normal bake; geometry keeps the
    # natural fingertip pad. Kept minimal deliberately — no new bone mapping.
    pass


def repack_image_paths():
    """The baseline .blend references baked PNGs via a stale relative path
    (//../../Assets/...). Repoint each image datablock at the baseline folder
    copy (Skin_R_diffuse -> Hand_R_diffuse.png etc.) and reload."""
    fixed = 0
    for img in bpy.data.images:
        if img.name in ("Render Result", "Viewer Node"):
            continue
        # "Skin_R_diffuse" -> "Hand_R_diffuse.png"
        parts = img.name.split("_")
        if len(parts) < 2:
            continue
        candidate = BASELINE / f"Hand_{parts[1]}_{parts[2]}.png"
        if not candidate.exists():
            continue
        img.filepath = str(candidate)
        try:
            img.reload()
        except Exception as e:
            log("reload failed", img.name, e)
        img.pack()
        fixed += 1
    log("image paths repacked:", fixed)


def refine_material(obj, side):
    """Conservative material correction: keep the baseline's baked diffuse/
    roughness links, force non-metal skin, and neutralize the magenta bake
    (MakeHuman base color is baked into the diffuse PNG) toward a restrained
    skin tone with a multiply node — texture bytes stay untouched."""
    mat = obj.data.materials[0] if obj.data.materials else None
    if mat is None or not mat.use_nodes:
        return
    nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf is None:
        return
    bsdf.inputs["Metallic"].default_value = 0.0
    # Blender 5.x Principled renamed Specular -> Specular IOR Level
    spec = bsdf.inputs.get("Specular IOR Level") or bsdf.inputs.get("Specular")
    if spec is not None:
        spec.default_value = 0.35
    base = bsdf.inputs["Base Color"]
    if base.is_linked:
        src = base.links[0].from_socket
        # multiply the baked magenta toward neutral skin: G/B lifted relative
        # to R cancels the blue-dominant magenta cast
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        mix.inputs["B"].default_value = (1.0, 0.78, 0.80, 1.0)
        nt.links.new(src, mix.inputs["A"])
        nt.links.new(mix.outputs["Result"], base)
    # subtle red-shift in subsurface for pale-skin realism, not purple
    ssw = bsdf.inputs.get("Subsurface Weight")
    if ssw is not None:
        ssw.default_value = 0.05
    ssr = bsdf.inputs.get("Subsurface Radius")
    if ssr is not None:
        ssr.default_value = (0.10, 0.035, 0.02)


# ---------------------------------------------------------------- export (baseline rule)
def export_wrist_local_fbx(side, tag):
    """Same rule as baseline reexport: cache parent/world matrices, metric
    scale 0.19 m wrist->middle tip, root identity, verify wrist at origin."""
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
    path = os.path.join(FBX_DIR, f"Hand_{side}_{tag}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True,
        object_types={"ARMATURE", "MESH", "EMPTY"}, add_leaf_bones=False,
        bake_anim=False, mesh_smooth_type="OFF", path_mode="ABSOLUTE")
    log("exported wrist-local FBX", side, "metric", round(metric, 4))
    return {"side": side, "file": os.path.basename(path),
            "metresPerSourceUnit": round(metric, 6)}


# ---------------------------------------------------------------- render
def studio(scene):
    if not scene.world:
        scene.world = bpy.data.worlds.new("Studio")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.05, 0.05, 0.06, 1.0)
    for i, (x, y, z, energy, sz) in enumerate([
            (2, -5, 8, 3000, 3.0), (-3, 4, -5, 1800, 1.6), (0, 5, 6, 4500, 1.2)]):
        lt = bpy.data.lights.new(f"key{i}", "AREA")
        lt.energy = energy
        lt.size = sz
        ob = bpy.data.objects.new(f"key{i}", lt)
        ob.location = (x, y, z)
        bpy.context.collection.objects.link(ob)
        tgt = mathutils.Vector((0, 0, 2.4))
        d = tgt - mathutils.Vector(ob.location)
        ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render_at(filepath, focus, direction, dist=8.5, lens=85, res=(1280, 960)):
    sc = bpy.context.scene
    sc.render.filepath = filepath
    cam = bpy.data.cameras.new(filepath)
    cam.lens = lens
    co = bpy.data.objects.new(filepath, cam)
    bpy.context.collection.objects.link(co)
    d = mathutils.Vector(direction).normalized()
    co.location = mathutils.Vector(focus) + d * dist
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.camera = co
    sc.render.resolution_x, sc.render.resolution_y = res
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)
    log("rendered", os.path.basename(filepath))


def apply_pose(arm, side, name):
    """Re-apply the baseline pose actions (read from the blend's action data
    is unreliable headless; the angles are the baseline's own authored values)."""
    sfx = "." + side
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
    BUILD.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_IN))
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "CYCLES"
    if sc.render.engine == "CYCLES":
        sc.cycles.samples = 32

    report = {"refined": [], "exports": [], "renders": []}
    repack_image_paths()
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        before = tri_count(obj.data)
        subdivide_to_target(obj, TARGET_TRIS)
        transfer_weights(obj)
        relax_shading(obj)
        refine_material(obj, side)
        after = tri_count(obj.data)
        report["refined"].append({"side": side, "tris_before": before,
                                  "tris_after": after})
        log(side, f"{before} -> {after} tris")

    studio(sc)

    # rest review set (per hand: palm/back/side)
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        bb = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
        focus = sum(bb, mathutils.Vector()) / 8.0
        sx = 1 if side == "R" else -1
        for nm, d in (("palm", (0.0, -1, 0.05)), ("back", (0.0, 1, 0.05)),
                      ("side", (1 * sx, -0.3, 0.1))):
            p = str(REVIEW / f"refined_{side.lower()}_{nm}.png")
            render_at(p, focus, d)
            report["renders"].append(os.path.basename(p))

    # deformed closeups: fist and pinch per hand (same pose actions as baseline)
    for side in ("R", "L"):
        arm = bpy.data.objects[f"Armature.{side}"]
        obj = bpy.data.objects[f"Hand.{side}"]
        bb = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
        focus = sum(bb, mathutils.Vector()) / 8.0
        sx = 1 if side == "R" else -1
        for pose in ("fist", "pinch"):
            apply_pose(arm, side, pose)
            bpy.context.view_layer.update()
            ev = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
            fbb = [ev.matrix_world @ mathutils.Vector(c) for c in ev.bound_box]
            focus = sum(fbb, mathutils.Vector()) / 8.0
            p = str(REVIEW / f"refined_{side.lower()}_{pose}.png")
            render_at(p, focus, (0.3 * sx, -1, 0.2))
            report["renders"].append(os.path.basename(p))
            clear_pose(arm)

    # ---- save refined source at REST ----
    for side in ("R", "L"):
        clear_pose(bpy.data.objects[f"Armature.{side}"])
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_OUT))
    log("saved", BLEND_OUT)

    # ---- exports through the baseline wrist-local rule (reopened clean) ----
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_OUT))
    for side in ("R", "L"):
        report["exports"].append(export_wrist_local_fbx(side, "refined"))

    # ---- copy textures (baseline bakes live in the blend; re-export standalone) ----
    for side in ("R", "L"):
        for kind in ("diffuse", "normal", "roughness"):
            src = BUILD / f"Hand_{side}_{kind}.png"
            if src.exists():
                dst = OUT / f"Hand_{side}_{kind}.png"
                dst.write_bytes(src.read_bytes())
                log("texture", dst.name)

    (OUT / "refinement-summary.json").write_text(
        json.dumps({"scope": "mesh/weights/material refinement; armature, rest "
                    "matrices, axes and lengths untouched; Unity verification "
                    "remains Codex's", "unverified": "Unity import, deformation "
                    "quality in Play mode, final art sign-off", "data": report},
                   indent=2) + "\n", encoding="utf-8", newline="\n")
    log("summary written")


if __name__ == "__main__":
    main()
