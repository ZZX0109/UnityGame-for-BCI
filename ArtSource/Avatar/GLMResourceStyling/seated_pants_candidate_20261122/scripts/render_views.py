"""Render source-vs-candidate comparison views at two seated poses.

Loads a .blend (source or candidate), applies the seated pose in memory,
positions the existing studio camera, and renders four fixed views:
front, side, three-quarter front, and a first-person eye point of view.
Body stays visible; no material/lighting changes; EEVEE matches scene engine.
"""
from __future__ import annotations

import json
import math
import sys
import traceback
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

ROOT = Path(r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\Avatar\GLMResourceStyling\seated_pants_candidate_20261122")
OUT_DIR = ROOT / "renders"
ROOT_DROP_M = 0.35
THIGH, CALF = -80.0, 70.0
RES = (960, 1080)
EYE_LOC = Vector((0.0, -0.55, 1.10))    # seated eye point pushed forward of torso
EYE_TARGET = Vector((0.0, 0.05, 0.40))  # knees/lap area of seated pose


def set_pose(armature, thigh_deg, calf_deg):
    sagittal = Vector((1.0, 0.0, 0.0))
    for side in ("l", "r"):
        for name, degrees in ((f"thigh_{side}", thigh_deg), (f"calf_{side}", calf_deg)):
            pose = armature.pose.bones[name]
            local = pose.bone.matrix_local.to_3x3().transposed() @ sagittal
            local.normalize()
            pose.rotation_mode = "QUATERNION"
            pose.rotation_quaternion = Quaternion(local, math.radians(degrees))


def aim_camera(cam_obj, target):
    direction = Vector(target) - cam_obj.location
    cam_obj.rotation_mode = "QUATERNION"
    cam_obj.rotation_quaternion = direction.to_track_quat("-Z", "Y")


def main():
    argv = sys.argv
    tag = argv[argv.index("--") + 1] if "--" in argv else "candidate"
    out_dir = OUT_DIR / tag
    out_dir.mkdir(parents=True, exist_ok=True)

    scene = bpy.context.scene
    cam = bpy.data.objects["AvatarRenderCamera"]
    rig = bpy.data.objects["Human.rig"]
    baseline_loc = rig.location.copy()
    names = [f"{kind}_{side}" for kind in ("thigh", "calf") for side in ("l", "r")]
    saved = {n: rig.pose.bones[n].rotation_quaternion.copy() for n in names}

    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 64
    scene.render.resolution_x, scene.render.resolution_y = RES
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(out_dir) + "\\"
    cam_data = cam.data
    saved_lens = cam_data.lens

    rig.location.z -= ROOT_DROP_M
    set_pose(rig, THIGH, CALF)
    bpy.context.view_layer.update()

    hip_world = rig.matrix_world @ Vector((0.0, 0.0, 0.55))
    views = {
        "front": (Vector((0.0, -1.6, 0.62)), hip_world),
        "side": (Vector((1.6, -0.05, 0.62)), hip_world),
        "threequarter": (Vector((1.05, -1.15, 0.72)), hip_world),
        "firstperson": (EYE_LOC.copy(), EYE_TARGET.copy()),
    }
    for name, (loc, target) in views.items():
        cam.location = loc
        cam_data.lens = 50 if name != "firstperson" else 60
        aim_camera(cam, target)
        scene.render.filepath = str(out_dir / f"{tag}_{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"GLM_RENDER {tag} {name} done")

    cam_data.lens = saved_lens
    rig.location = baseline_loc
    for n, q in saved.items():
        rig.pose.bones[n].rotation_quaternion = q


try:
    main()
except Exception:
    traceback.print_exc()
    sys.exit(1)
