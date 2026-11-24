# -*- coding: utf-8 -*-
"""Does the ORIGINAL R action itself blow up the mesh when played?"""
import bpy
from mathutils import Vector

GLB = r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\ThirdParty\ElenaFF_RiggedHand\hand.glb"


def log(*a):
    print("VH-RAW:", *a, flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=GLB)
    sc = bpy.context.scene
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    mesh = next(o for o in sc.objects
                if o.type == "MESH" and o.name != "Icosphere")
    for f in (0, 32, 77):
        sc.frame_set(f)
        bpy.context.view_layer.update()
        deps = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(deps)
        evm = ev.to_mesh()
        ws = [ev.matrix_world @ Vector(v.co) for v in evm.vertices][::4]
        ev.to_mesh_clear()
        xs = [p.x for p in ws]; ys = [p.y for p in ws]; zs = [p.z for p in ws]
        log("frame", f, "world span", [round(max(xs) - min(xs), 3),
                                       round(max(ys) - min(ys), 3),
                                       round(max(zs) - min(zs), 3)],
            "center", tuple(round((max(a) + min(a)) / 2, 3)
                            for a in (xs, ys, zs)))
    # location key ranges of the biggest location-driven bones
    act = arm.animation_data.action
    for layer in act.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in cb.fcurves:
                    if fc.data_path.endswith(".location"):
                        vals = [k.co[1] for k in fc.keyframe_points]
                        rng = max(vals) - min(vals)
                        if rng > 0.5:
                            bone = fc.data_path.split('"')[1]
                            log("BIGLOC", bone, "idx", fc.array_index,
                                "range", round(rng, 3))


if __name__ == "__main__":
    main()
