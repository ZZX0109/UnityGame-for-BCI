# -*- coding: utf-8 -*-
"""Read-only inspection of the ElenaFF rigged-hand GLB (Blender 5.2).
Prints object hierarchy, armature bones, mesh/material/UV layout, action
timing, and the wrist->middle-tip length so deployment decisions are made
on measured facts. Writes nothing."""
import bpy
from mathutils import Vector


def log(*a):
    print("VH-GLB:", *a, flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(
        filepath=r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\ThirdParty\ElenaFF_RiggedHand\hand.glb")
    sc = bpy.context.scene
    log("scene units: scale", sc.unit_settings.scale_length,
        "system", sc.unit_settings.system)
    log("render fps", sc.render.fps, "/", sc.render.fps_base)

    for ob in sc.objects:
        pt = ob.parent.name if ob.parent else None
        extra = ""
        if ob.type == "ARMATURE":
            extra = f" bones={len(ob.data.bones)}"
        if ob.type == "MESH":
            extra = (f" verts={len(ob.data.vertices)} "
                     f"polys={len(ob.data.polygons)} "
                     f"uv={len(ob.data.uv_layers)} "
                     f"vgroups={len(ob.vertex_groups)}")
        log(f"OBJ {ob.type:8s} {ob.name!r} parent={pt} "
            f"matrix_world.scale={[round(s,4) for s in ob.matrix_world.to_scale().to_tuple()]}{extra}")

    for ob in sc.objects:
        if ob.type != "ARMATURE":
            continue
        log("ARMATURE", ob.name)
        for b in ob.data.bones:
            pw = ob.pose.bones[b.name]
            log(f"  BONE {b.name:24s} head_local={tuple(round(v,4) for v in b.head_local)} "
                f"tail_local={tuple(round(v,4) for v in b.tail_local)} "
                f"use_inherit_rotation={b.use_inherit_rotation}")
        log("  action:", ob.animation_data.action.name if
            (ob.animation_data and ob.animation_data.action) else None)

    for ob in sc.objects:
        if ob.type != "MESH":
            continue
        for mat in ob.data.materials:
            if not mat or not mat.use_nodes:
                continue
            for n in mat.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    img = n.image
                    log("TEX", mat.name, img.name, img.source,
                        tuple(img.size), "packed" if img.packed_file else
                        "unpacked", img.filepath if not img.packed_file else "")
        deps = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(deps)
        ws = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
        mn = Vector((min(c.x for c in ws), min(c.y for c in ws),
                     min(c.z for c in ws)))
        mx = Vector((max(c.x for c in ws), max(c.y for c in ws),
                     max(c.z for c in ws)))
        log("MESH world bbox min", tuple(round(v, 4) for v in mn),
            "max", tuple(round(v, 4) for v in mx),
            "size", tuple(round(v, 4) for v in (mx - mn)))

    for a in bpy.data.actions:
        log("ACTION", a.name, "frame_range",
            tuple(round(f, 4) for f in a.frame_range),
            "fps", a.render_fps if hasattr(a, "render_fps") else "?")
        # sample keyframe times per top-level fcurve group
        for grp in a.groups[:30]:
            ch = [fc.data_path + "±" + fc.array_index for fc in grp.channels]
            ks = grp.channels[0].keyframe_points if grp.channels else []
            ktimes = [round(k.co[0], 3) for k in ks[:3]]
            log("  FCURVE_GRP", grp.name, len(ch), "keys_head", ktimes)

    # wrist -> middle tip length from bone geometry (first armature found)
    for ob in sc.objects:
        if ob.type != "ARMATURE":
            continue
        names = [b.name for b in ob.data.bones]
        wr = [n for n in names if "wrist" in n.lower() or "hand" in n.lower()]
        tips = [n for n in names if "middle" in n.lower() or "index" in n.lower()]
        log("candidate wrist bones", wr[:6], "finger bones", tips[:6])


if __name__ == "__main__":
    main()
