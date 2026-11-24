# SPDX-License-Identifier: GPL-3.0-or-later
# GLMResourceStyling style_preview.py
#
# Non-destructive preview renderer for the MPFB2 full-body character produced by
# ArtSource/Avatar/build_mpfb_avatar.py (Luna AG's exporter).
#
# Usage (Blender 4.x/5.x, headless):
#   blender.exe -b -P style_preview.py -- \
#       --blend  <path to AG's .blend> \
#       --config <path to appearance_presets.json> \
#       --preset <preset id, e.g. casual_female_A> \
#       [--out   <output dir, default: ./Results/<preset>_<timestamp> inside this directory>]
#
# Behaviour:
#   * Opens the original .blend read-only and saves an adjusted COPY into the output
#     directory. The original .blend is never modified.
#   * Adjusts existing materials only (Principled BSDF roughness / base color tint)
#     according to the preset. No procedurally generated textures: a missing object
#     or a missing material is a hard failure (exit code 2).
#   * Adds a neutral studio light rig (three-point area lights) and renders a head
#     close-up plus an upper-body shot.
#   * Writes only inside the requested output directory (default: this script's dir).
#
# Exit codes: 0 ok, 2 missing assets/materials, 3 bad usage/inputs.

import argparse
import json
import os
import re
import sys
import time

import bpy
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))

# MPFB object_type custom-property values (lowercase) -> preset JSON role.
ROLE_OBJECT_TYPES = {
    "skin": ("basemesh",),
    "eyes": ("eyes",),
    "eyebrows": ("eyebrows",),
    "eyelashes": ("eyelashes",),
    "teeth": ("teeth",),
    "tongue": ("tongue",),
    "hair": ("hair",),
    "clothes": ("clothes",),
}


def log(msg):
    # ASCII-only so headless Blender on Windows consoles never chokes on cp936.
    print("[style_preview] " + msg)


def fail(code, msg):
    log("FAIL(%d): %s" % (code, msg))
    sys.stderr.write("[style_preview] FAIL(%d): %s\n" % (code, msg))
    sys.exit(code)


def parse_args():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    p = argparse.ArgumentParser()
    p.add_argument("--blend", required=True)
    p.add_argument("--config", required=True)
    p.add_argument("--preset", required=True)
    p.add_argument("--out", default=None)
    p.add_argument("--samples", type=int, default=64)
    args = p.parse_args(argv)
    for k in ("blend", "config"):
        v = getattr(args, k)
        if not os.path.isfile(v):
            fail(3, "%s not found: %s" % (k, v))
    return args


def flatten_preset(preset):
    """Yield (role, entry) for every asset entry in the preset."""
    for role, sub in ROLE_OBJECT_TYPES.items():
        if role not in preset:
            continue
        item = preset[role]
        if role == "clothes":
            for entry in item:
                yield role, entry
        elif isinstance(item, dict):
            yield role, item


def get_prop(obj, name):
    try:
        v = obj.get(name)
        return str(v) if v is not None else ""
    except Exception:
        return ""


def find_character_object(objs, role, entry):
    """Locate the mesh object that carries this preset asset.

    Primary key: MPFB's asset_source custom property ('hair/short02.mhclo').
    Fallback: object name suffix from add_mhclo_asset naming
    (<basemesh>.<asset_name>).
    """
    fname = entry.get("filename", "")
    aname = entry.get("asset_name", "")
    want_tail = (entry.get("relative_path", "").replace("\\", "/") or
                 fname).lower()
    base = fname.lower()
    named = aname.lower()

    for obj in objs:
        if obj.type != 'MESH':
            continue
        src = get_prop(obj, "asset_source").replace("\\", "/").lower()
        if src and (src.endswith(want_tail) or src.endswith(base)):
            return obj
        otype = get_prop(obj, "object_type").lower()
        if otype in ROLE_OBJECT_TYPES.get(role, ()) and named in obj.name.lower():
            return obj
        # add_mhclo_asset names objects <human>.<asset_name>
        if named and obj.name.lower().endswith("." + named):
            return obj
    return None


def find_basemesh(objs):
    for obj in objs:
        if obj.type == 'MESH' and get_prop(obj, "object_type").lower() == "basemesh":
            return obj
    # MPFB save fallback: "<name>.body"
    for obj in objs:
        if obj.type == 'MESH' and obj.name.lower().endswith(".body"):
            return obj
    return None


def apply_adjustments(obj, entry, errors):
    """Roughness + tint on existing Principled BSDF. Uses existing textures only."""
    mat_slots = [s for s in obj.material_slots if s.material]
    if not mat_slots:
        errors.append("object '%s' has no material" % obj.name)
        return
    rough = entry.get("roughness")
    tint = entry.get("tint_rgb")
    for slot in mat_slots:
        mat = slot.material
        if not mat.use_nodes:
            errors.append("material '%s' on '%s' is not node-based" % (mat.name, obj.name))
            continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None:
            errors.append("material '%s' on '%s' has no Principled BSDF" % (mat.name, obj.name))
            continue
        if rough:
            r = rough if isinstance(rough, (int, float)) else None
            rng = None
            if r is None and isinstance(rough, str):
                m = re.match(r"^\s*([0-9.]+)\s*-\s*([0-9.]+)", rough)
                if m:
                    rng = (float(m.group(1)), float(m.group(2)))
            if r is not None:
                bsdf.inputs["Roughness"].default_value = float(r)
            elif rng:
                bsdf.inputs["Roughness"].default_value = sum(rng) / 2.0
        if tint and isinstance(tint, (list, tuple)) and len(tint) == 3:
            bsdf.inputs["Base Color"].default_value = (
                float(tint[0]), float(tint[1]), float(tint[2]), 1.0)


def setup_studio(scene):
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = max(16, SAMPLES)
    scene.cycles.use_denoising = True
    scene.render.film_transparent = False

    world = scene.world or bpy.data.worlds.new("StudioWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.85, 0.85, 0.87, 1.0)
        bg.inputs[1].default_value = 1.0

    # Three-point area-light rig, warm key / cool fill / rim.
    specs = [
        ("Key",  (1.6, -2.4, 1.7), (5, -30, 0),  3200.0, (1.0, 0.96, 0.92)),
        ("Fill", (-2.6, -2.0, 1.0), (-25, -12, 0), 900.0, (0.92, 0.96, 1.0)),
        ("Rim",  (0.6, 2.2, 1.9),  (160, -14, 0), 2200.0, (1.0, 1.0, 1.0)),
    ]
    for name, loc, rot, power, color in specs:
        data = bpy.data.lights.new(name="Studio_" + name, type='AREA')
        data.energy = power
        data.size = 2.2
        data.color = color
        light = bpy.data.objects.new(name="Studio_" + name, object_data=data)
        light.location, light.rotation_euler = loc, [r * 3.14159265 / 180.0 for r in rot]
        scene.collection.objects.link(light)


def add_camera(scene, name, location, target):
    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = 85 if "head" in name else 60
    cam = bpy.data.objects.new(name, cam_data)
    cam.location = location
    scene.collection.objects.link(cam)
    direction = Vector(location) - Vector(target)
    cam.rotation_euler = direction.to_track_quat('Z', 'Y').to_euler()
    return cam


def framed_shot(scene, cam_loc_target, name, resolution, out_dir):
    cam = add_camera(scene, name, cam_loc_target[0], cam_loc_target[1])
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)
    log("rendered " + scene.render.filepath)


def main():
    global SAMPLES
    args = parse_args()
    SAMPLES = args.samples

    with open(args.config, "r", encoding="utf-8") as f:
        config = json.load(f)
    preset = next((p for p in config.get("presets", []) if p.get("id") == args.preset), None)
    if preset is None:
        fail(3, "preset id '%s' not in %s" % (args.preset, [p.get("id") for p in config.get("presets", [])]))

    out_dir = args.out or os.path.join(
        SCRIPT_DIR, "Results", "%s_%s" % (args.preset, time.strftime("%Y%m%d-%H%M%S")))
    os.makedirs(out_dir, exist_ok=True)

    # --- Open the original read-only; we never save over it. ---
    bpy.ops.wm.open_mainfile(filepath=args.blend)
    scene = bpy.context.scene
    objs = list(bpy.context.scene.objects)

    basemesh = find_basemesh(objs)
    if basemesh is None:
        fail(2, "no MPFB basemesh found (object_type 'Basemesh' or '*.body') in %s" % args.blend)

    errors = []
    matched = []
    for role, entry in flatten_preset(preset):
        obj = find_character_object(objs, role, entry)
        if obj is None:
            errors.append("asset not in .blend: role=%s asset=%s (%s)" %
                          (role, entry.get("asset_name"), entry.get("relative_path")))
            continue
        apply_adjustments(obj, entry, errors)
        matched.append((role, entry.get("asset_name"), obj.name))

    if errors:
        fail(2, "; ".join(errors) +
             " -- per TASK.md, missing assets must not be faked. Check appearance_presets.json "
             "statuses and that AG's .blend was built with these assets.")

    # --- Save the adjusted copy, never touching the original path. ---
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "restyled_copy.blend"))

    # --- Framing from the character bounding box. MakeHuman/MPFB face -Y. ---
    bbox_ws = [basemesh.matrix_world @ Vector(c) for c in basemesh.bound_box]
    zmin = min(v.z for v in bbox_ws)
    zmax = max(v.z for v in bbox_ws)
    h = max(1e-6, zmax - zmin)
    cx = sum(v.x for v in bbox_ws) / 8.0
    cy = sum(v.y for v in bbox_ws) / 8.0
    head_z = zmax - 0.09 * h
    head_target = Vector((cx, cy, head_z))
    head_loc = Vector((cx, cy - 0.9 * h, head_z + 0.02 * h))
    upper_target = Vector((cx, cy, zmax - 0.30 * h))
    upper_loc = Vector((cx, cy - 1.4 * h, zmax - 0.22 * h))

    setup_studio(scene)
    framed_shot(scene, (head_loc, head_target), "head_closeup", (1024, 1024), out_dir)
    framed_shot(scene, (upper_loc, upper_target), "upper_body", (1024, 1280), out_dir)

    with open(os.path.join(out_dir, "applied.json"), "w", encoding="utf-8") as f:
        json.dump({"preset": args.preset, "source_blend": os.path.abspath(args.blend),
                   "matched": [{"role": r, "asset": a, "object": o} for r, a, o in matched]},
                  f, ensure_ascii=False, indent=2)
    log("done -> " + out_dir)


SAMPLES = 64

if __name__ == "__main__":
    main()
