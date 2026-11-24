# -*- coding: utf-8 -*-
"""After strip, which posed bone walks away from the skin? Compares posed
world heads at frame 77 vs the skin bbox."""
import bpy
from mathutils import Vector

GLB = r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\ThirdParty\ElenaFF_RiggedHand\hand.glb"


def log(*a):
    print("VH-W:", *a, flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=GLB)
    sc = bpy.context.scene
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    mesh = next(o for o in sc.objects
                if o.type == "MESH" and o.name != "Icosphere")
    # strip locations
    act = arm.animation_data.action
    for layer in act.layers:
        for strip in layer.strips:
            for cb in strip.channelbags:
                for fc in list(cb.fcurves):
                    if fc.data_path.endswith(".location"):
                        cb.fcurves.remove(fc)
    sc.frame_set(77)
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    evm = ev.to_mesh()
    ws = [ev.matrix_world @ Vector(v.co) for v in evm.vertices][::4]
    ev.to_mesh_clear()
    xs = [p.x for p in ws]; ys = [p.y for p in ws]; zs = [p.z for p in ws]
    log("f77 span", [round(max(xs) - min(xs), 3),
                     round(max(ys) - min(ys), 3),
                     round(max(zs) - min(zs), 3)])
    for bn in ("_rootJoint", "pulse.R_01", "hand.R_02", "hand.R.001_011",
               "thumb_base.R_03", "index_base.R_012", "index_01.R_017",
               "middle_01.R_025", "ring_01.R_033", "pinky_01.R_041"):
        pb = arm.pose.bones.get(bn)
        if pb is None:
            continue
        w = arm.matrix_world @ pb.matrix.to_translation()
        log("BONE", bn, "world head", tuple(round(v, 3) for v in w))


if __name__ == "__main__":
    main()
