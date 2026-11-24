# -*- coding: utf-8 -*-
"""Probe #2 (read-only): material socket wiring, deform-bone set, action
channel modes, world-space wrist->tip measurement, reflection check."""
import bpy
from mathutils import Vector, Matrix

GLB = r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\ThirdParty\ElenaFF_RiggedHand\hand.glb"


def log(*a):
    print("VH-P2:", *a, flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=GLB)
    sc = bpy.context.scene

    # material wiring
    for ob in sc.objects:
        if ob.type != "MESH" or ob.name == "Icosphere":
            continue
        for mat in ob.data.materials:
            log("MAT", mat.name, "nodes:",
                [(n.type, n.name) for n in mat.node_tree.nodes
                 if n.type in ("TEX_IMAGE", "BSDF_PRINCIPLED",
                               "NORMAL_MAP", "SEPARATE_COLOR")])
            for n in mat.node_tree.nodes:
                if n.type == "BSDF_PRINCIPLED":
                    for inp in n.inputs:
                        for lk in inp.links:
                            log("  LINK ->", inp.name, "<-", lk.from_node.type,
                                lk.from_node.name)
            for n in mat.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    for lk in mat.node_tree.links:
                        if lk.from_node == n:
                            log("  TEXOUT", n.image.name, "->",
                                lk.to_node.type, lk.to_socket.name)

    # armature measures
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    mesh = next(o for o in sc.objects
                if o.type == "MESH" and o.name != "Icosphere")
    log("ARM det", round(arm.matrix_world.determinant(), 6),
        "scale", [round(s, 6) for s in arm.matrix_world.to_scale()])
    log("MESH det", round(mesh.matrix_world.determinant(), 6),
        "scale", [round(s, 6) for s in mesh.matrix_world.to_scale()])
    log("MESH parent", mesh.parent.name,
        "parent_type", mesh.parent_type)

    names = [b.name for b in arm.data.bones]
    wrist = next(n for n in names if n.startswith("hand."))
    tip = next(n for n in names if n.startswith("middle_03"))
    bw = arm.data.bones[wrist]
    bt = arm.data.bones[tip]
    wpos = arm.matrix_world @ Vector(bw.head_local)
    tpos = arm.matrix_world @ Vector(bt.tail_local)
    log("WRIST", wrist, "world", tuple(round(v, 4) for v in wpos))
    log("TIP", tip, "world", tuple(round(v, 4) for v in tpos))
    log("HANDLEN wrist->tip world", round((tpos - wpos).length, 4))

    # deform bones = vgroups with any weight > 0
    used = set()
    for vg in mesh.vertex_groups:
        pass
    for v in mesh.data.vertices:
        for g in v.groups:
            if g.weight > 0.0:
                used.add(mesh.vertex_groups[g.group].name)
    log("DEFORM BONES", len(sorted(used)), sorted(used))

    # action channels
    act = arm.animation_data.action
    log("ACTION", act.name, "range", tuple(round(f, 3) for f in act.frame_range))
    paths = {}
    for fc in act.fcurves:
        key = fc.data_path.split(".")[-1]
        paths.setdefault(key, set()).add(
            fc.data_path.split('"')[1] if '"' in fc.data_path else fc.data_path)
    log("CHANNEL KINDS", {k: len(v) for k, v in paths.items()})
    rot_bones = sorted(paths.get("rotation_quaternion", []))
    loc_bones = sorted(paths.get("location", []))
    log("QUAT BONES", len(rot_bones), rot_bones[:25])
    log("LOC BONES", len(loc_bones), loc_bones[:5])
    # sample first bone's keys to see mode (quaternion = 4 channels)
    for fc in act.fcurves[:4]:
        log("FC", fc.data_path, fc.array_index,
            "keys", len(fc.keyframe_points))


if __name__ == "__main__":
    main()
