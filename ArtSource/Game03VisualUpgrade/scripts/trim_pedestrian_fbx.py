"""Trim the MakeHuman/Ara pedestrian FBX to the four animations used by Game03.

The source FBX ships 496 animation actions (~70 MB). StreetBackgroundPopulation
only needs Idle / Walk / Texting / Phone call. Keep those (both Armature and Key
variants, so either importer convention still works), drop the rest, and export
an animation-bearing FBX with the same mesh/material/rig structure.

Usage:
  blender -b --python trim_pedestrian_fbx.py -- <source_fbx> <output_fbx>
"""
import os
import sys
import bpy

KEEP = ("|Idle|", "|Walk|", "|Texting|", "|Phone call|")


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(args) < 2:
        raise SystemExit("usage: trim_pedestrian_fbx.py <source_fbx> <output_fbx>")
    src, dst = args[0], args[1]
    os.makedirs(os.path.dirname(dst), exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)

    before = len(bpy.data.actions)
    removed = 0
    for action in list(bpy.data.actions):
        if not any(tag in action.name for tag in KEEP):
            bpy.data.actions.remove(action, do_unlink=True)
            removed += 1
    kept = [a.name for a in bpy.data.actions]

    # Remove Blender's default placeholder camera/cube/light left by some imports.
    for obj in list(bpy.data.objects):
        if obj.type in {"CAMERA", "LIGHT"} or (obj.type == "MESH" and len(obj.data.vertices) <= 8):
            bpy.data.objects.remove(obj, do_unlink=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=1.0,
        add_leaf_bones=False,
        use_mesh_modifiers=True,
        use_armature_deform_only=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
    )
    size = os.path.getsize(dst)
    print(f"PED_TRIM_OK actions={before}->before removed={removed} kept={kept}")
    print(f"PED_TRIM_FILE {dst} {size} bytes ({size/1048576:.2f} MB)")


if __name__ == "__main__":
    main()
