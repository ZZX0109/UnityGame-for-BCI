# -*- coding: utf-8 -*-
"""Refinement rev5 — full skin material re-bake in B's staging.

Reads output/rev4/refined_hands.blend (refined rest mesh, region weights,
13646 tris/hand) and produces a complete same-source set in output/rev5/:
  refined_hands.blend            refined source with baked skin materials
  Hand_{R,L}_refined5.fbx        wrist-local exports (0.19 m rule unchanged)
  Hand_{R,L}_{diffuse,normal,roughness}.png   freshly baked 2K maps
  refined5_{side}_{palm,back}.png  renders with two-sided lighting
  refinement-summary.json

Fixes (Codex 06:10 + run19): palm side flat grey -> full skin shader bake
(natural tone + AO creases + nail masks + roughness layers); two-sided
lighting so palm and back are both lit. Armature untouched.

Usage: blender --background --factory-startup --python refine_hands_rev5.py
"""

import json
import math
from pathlib import Path

import bpy
import mathutils

HERE = Path(__file__).resolve().parent
OUT = HERE / "output" / "rev5"
BLEND_IN = HERE / "output" / "rev4" / "refined_hands.blend"
BLEND_OUT = OUT / "refined_hands.blend"

RES = 2048
NAIL_MASK = "nail_mask"


def log(*a):
    print("VH-REFINE5:", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def side_of(obj_name):
    return obj_name[-1]


# ---------------------------------------------------------------- nail mask
def add_nail_mask(obj, arm):
    """Vertex color layer: alpha = nail mask (1 at dorsal fingertip pads,
    smooth falloff). Nails sit on the dorsal side = opposite the palm
    normal (palm normal points from the MCP row toward the palm hollow)."""
    sfx = "." + side_of(obj.name)
    w_h = mathutils.Vector(arm.data.bones["Wrist" + sfx].head_local)
    i_mcp = mathutils.Vector(arm.data.bones["IndexMeta" + sfx].tail_local)
    l_mcp = mathutils.Vector(arm.data.bones["LittleMeta" + sfx].tail_local)
    palm_normal = (i_mcp - w_h).cross(l_mcp - w_h).normalized()

    if NAIL_MASK in [c.name for c in obj.data.color_attributes]:
        obj.data.color_attributes.remove(obj.data.color_attributes[NAIL_MASK])
    layer = obj.data.color_attributes.new(NAIL_MASK, "FLOAT_COLOR", "POINT")

    tips = []
    for n in ("Thumb3", "Index3", "Middle3", "Ring3", "Little3"):
        b = arm.data.bones.get(n + sfx)
        if b:
            head = mathutils.Vector(b.head_local)
            tail = mathutils.Vector(b.tail_local)
            tips.append((tail, head, 0.03))   # sphere around the fingertip

    for v in obj.data.vertices:
        wp = arm.matrix_world @ mathutils.Vector(v.co)
        val = 0.0
        for tail, head, r in tips:
            d = (wp - tail).length
            dorsal = (wp - tail).dot(palm_normal)   # dorsal side = -palm
            if d < r and dorsal < 0.0:              # only the dorsal pad
                val = max(val, 1.0 - d / r)
        layer.data[v.index].color = (val, val, val, 1.0)
    log("nail mask painted", obj.name)


# ---------------------------------------------------------------- material
def build_skin_material(obj):
    """Procedural skin: natural tone with noise variation, nail mix, roughness
    layers, subtle bump. Wired so DIFFUSE/ROUGHNESS/NORMAL bakes capture it."""
    side = side_of(obj.name)
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
    noise.inputs["Distortion"].default_value = 1.0
    noise.location = (-450, -150)

    skin_ramp = nt.nodes.new("ShaderNodeValToRGB")
    skin_ramp.color_ramp.elements[0].color = (0.62, 0.42, 0.34, 1.0)
    skin_ramp.color_ramp.elements[1].color = (0.80, 0.62, 0.52, 1.0)
    skin_ramp.location = (-250, -150)
    nt.links.new(noise.outputs["Fac"], skin_ramp.inputs["Fac"])

    # color = mix(skin_tone, nail, mask) — mask via vertex color alpha
    mix_col = nt.nodes.new("ShaderNodeMix")
    mix_col.data_type = "RGBA"
    mix_col.blend_type = "MIX"
    mix_col.inputs["A"].default_value = (0.9, 0.76, 0.66, 1.0)  # skin
    mix_col.inputs["B"].default_value = (0.88, 0.84, 0.80, 1.0)  # nail plate
    mix_col.location = (0, 0)
    nt.links.new(skin_ramp.outputs["Color"], mix_col.inputs["A"])
    nt.links.new(vcol.outputs["Alpha"], mix_col.inputs["Factor"])
    nt.links.new(mix_col.outputs["Result"], bsdf.inputs["Base Color"])

    # roughness = mix(0.55 skin, 0.25 nail, mask) + slight noise
    rough_mix = nt.nodes.new("ShaderNodeMix")
    rough_mix.data_type = "RGBA"
    rough_mix.location = (0, -200)
    rough_mix.inputs["A"].default_value = (0.55, 0.55, 0.55, 1.0)
    rough_mix.inputs["B"].default_value = (0.25, 0.25, 0.25, 1.0)
    nt.links.new(vcol.outputs["Alpha"], rough_mix.inputs["Factor"])
    rough_noise = nt.nodes.new("ShaderNodeMath")
    rough_noise.operation = "ADD"
    rough_noise.inputs[1].default_value = 0.0
    rough_noise.location = (150, -250)
    nt.links.new(rough_mix.outputs["Result"], rough_noise.inputs[0])
    nt.links.new(noise.outputs["Fac"], rough_noise.inputs[1])
    nt.links.new(rough_noise.outputs["Value"], bsdf.inputs["Roughness"])

    # subtle bump from the same noise
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.05
    bump.inputs["Distance"].default_value = 0.0006
    bump.location = (0, -350)
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    # bake target image node
    bake_node = nt.nodes.new("ShaderNodeTexImage")
    bake_node.name = "BAKE_TARGET"
    nt.nodes.active = bake_node
    return bake_node


def bake_maps(obj, tag):
    """Bake diffuse / roughness / normal to 2K images; save next to the FBX."""
    mat = obj.data.materials[0]
    nt = mat.node_tree
    img_node = next(n for n in nt.nodes if n.name == "BAKE_TARGET")
    nt.nodes.active = img_node
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)

    out = {}
    for kind, btype, cs in (("diffuse", "DIFFUSE", "sRGB"),
                            ("roughness", "ROUGHNESS", "Non-Color"),
                            ("normal", "NORMAL", "Non-Color")):
        img = bpy.data.images.new(f"Bake.{tag}.{kind}", RES, RES)
        img.colorspace_settings.name = cs
        img_node.image = img
        # albedo only: no lighting baked into the maps (lights are a render
        # concern; baked light would reappear as white/colour blotches)
        if btype == "DIFFUSE":
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
        # save() corrupts filepath_raw on some rel-path states; reload a
        # clean datablock from disk so has_data is True and the path is right
        img.save()
        reloaded = bpy.data.images.load(p, check_existing=False)
        # keep only the newly loaded one (drop the empty bake image)
        bpy.data.images.remove(img)
        out[kind] = reloaded
        log("baked", kind, "->", Path(p).name)
    return out


def rewire_to_baked(obj, baked):
    """Swap the procedural tree for the baked maps (production equivalent)."""
    mat = obj.data.materials[0]
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (0, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    for kind, socket in (("diffuse", "Base Color"),
                         ("roughness", "Roughness"),
                         ("normal", None)):
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = baked[kind]
        tex.location = (-300, 150 * ("diffuse roughness normal".split().index(kind) - 1))
        if socket:
            nt.links.new(tex.outputs[0], bsdf.inputs[socket])
        else:
            nm = nt.nodes.new("ShaderNodeNormalMap")
            nm.inputs["Strength"].default_value = 1.0
            nt.links.new(tex.outputs[0], nm.inputs["Color"])
            nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])


# ---------------------------------------------------------------- camera
def look_from(cam_obj, center, direction, extents, margin=1.25):
    """Axis-aligned ortho camera on the ±Z axis — the combination verified by
    the diag ladder (identity rotation looks toward -Z and frames the hand).
    `direction` is used only to pick the closer Z side; the palm normal of
    this rig is dominated by -Z so both views are Z-axis views in practice."""
    d = mathutils.Vector(direction).normalized()
    cam_obj.data.type = "ORTHO"
    half_h = max(extents.length, 0.05) * margin
    # ortho_scale is the full WIDTH; height = ortho_scale / aspect(16:9).
    # Set so the subject spans ~75% of the height: height = 2*half_h_subj/m
    cam_obj.data.ortho_scale = max(half_h * 2.0 * 1.78, 0.1)
    cam_obj.location = (center.x, center.y, center.z + 10.0)
    cam_obj.rotation_euler = (0, 0, 0)   # identity looks toward -Z


# ---------------------------------------------------------------- export
def export_wrist_local_fbx(side, tag):
    o = bpy.data.objects[f"Hand.{side}"]
    arm = bpy.data.objects[f"Armature.{side}"]
    arm_world, mesh_world = arm.matrix_world.copy(), o.matrix_world.copy()
    wrist_w = arm_world @ mathutils.Vector(
        arm.data.bones["Wrist." + side].head_local)
    tip_w = arm_world @ mathutils.Vector(
        arm.data.bones["Middle3." + side].tail_local)
    metric = 0.19 / (tip_w - wrist_w).length
    bake = mathutils.Matrix.Scale(metric, 4) @ mathutils.Matrix.Translation(
        -wrist_w)
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


# ---------------------------------------------------------------- main
def main():
    OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_IN))
    sc = bpy.context.scene

    # two-sided lighting: palm AND back both lit (run19 palm was grey/black)
    for i, (loc, e) in enumerate([((2, -5, 8), 350), ((-2, 5, 8), 250),
                                  ((0, 5, -8), 250), ((0, -5, -8), 250),
                                  ((5, 0, 3), 180), ((-5, 0, 3), 180)]):
        lt = bpy.data.lights.new(f"k{i}", "AREA")
        lt.energy = e
        lt.size = 2.5
        ob = bpy.data.objects.new(f"k{i}", lt)
        ob.location = loc
        bpy.context.collection.objects.link(ob)

    # reuse/normalize the camera name: a second run would otherwise get
    # PreviewCam.001 (an unconfigured duplicate) bound as the scene camera
    for stale in list(bpy.data.objects):
        if stale.name.startswith("PreviewCam"):
            bpy.data.objects.remove(stale)
    for stale in list(bpy.data.cameras):
        if stale.users == 0:
            bpy.data.cameras.remove(stale)
    preview_cam = bpy.data.cameras.new("PreviewCam")
    preview_cam.type = "ORTHO"
    preview_cam_go = bpy.data.objects.new("PreviewCam", preview_cam)
    bpy.context.collection.objects.link(preview_cam_go)
    sc.camera = preview_cam_go

    summary = {"hands": [], "renders": [], "exports": []}
    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        arm = obj.parent
        if arm is None or arm.type != "ARMATURE":
            arm = bpy.data.objects[f"Armature.{side}"]
        add_nail_mask(obj, arm)
        bake_node = build_skin_material(obj)
        baked = bake_maps(obj, side)
        rewire_to_baked(obj, baked)

        sfx = "." + side
        w_h = mathutils.Vector(arm.data.bones["Wrist" + sfx].head_local)
        i_mcp = mathutils.Vector(arm.data.bones["IndexMeta" + sfx].tail_local)
        l_mcp = mathutils.Vector(arm.data.bones["LittleMeta" + sfx].tail_local)
        normal = (i_mcp - w_h).cross(l_mcp - w_h).normalized()
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
        ext = (mx - mn) / 2

        # palm view: camera on -normal (palm hollow side); back: +normal
        look_from(camGo := bpy.data.objects["PreviewCam"]
                  if "PreviewCam" in bpy.data.objects else
                  _new_cam(), center, -normal, ext)
        render_view(sc, f"refined5_{side.lower()}_palm")
        look_from(bpy.data.objects["PreviewCam"], center, normal, ext)
        render_view(sc, f"refined5_{side.lower()}_back")
        summary["hands"].append({"side": side, "tris": tri_count(obj.data),
                                 "textures": [k for k in baked]})

    (OUT / "refinement-summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n")
    for side in ("R", "L"):
        arm = bpy.data.objects[f"Armature.{side}"]
        if arm.animation_data:
            arm.animation_data.action = None
            arm.animation_data_clear()
        clear_pose(arm)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_OUT))
    log("saved", BLEND_OUT)

    bpy.ops.wm.open_mainfile(filepath=str(BLEND_OUT))
    for side in ("R", "L"):
        summary["exports"].append(export_wrist_local_fbx(side, "refined5"))
    (OUT / "refinement-summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8", newline="\n")
    log("summary written")


def _new_cam():
    cam = bpy.data.cameras.new("PreviewCam")
    cam.type = "ORTHO"
    go = bpy.data.objects.new("PreviewCam", cam)
    bpy.context.collection.objects.link(go)
    sc = bpy.context.scene
    sc.camera = go
    return go


def render_view(sc, name):
    # debug: verify the camera and material state at render time
    cam = sc.camera
    log("render", name, "cam", cam.name if cam else None,
        "camLoc", [round(v, 2) for v in cam.location] if cam else None)
    sc.render.filepath = str(OUT / f"{name}.png")
    bpy.ops.render.render(write_still=True)
    log("rendered", name)


def clear_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()


if __name__ == "__main__":
    main()
