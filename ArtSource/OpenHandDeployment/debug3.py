# -*- coding: utf-8 -*-
"""Focused: does the L armature actually pose when the action plays?"""
import bpy
from mathutils import Matrix, Vector

GLB = r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\ThirdParty\ElenaFF_RiggedHand\hand.glb"


def log(*a):
    print("VH-D3:", *a, flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=GLB)
    sc = bpy.context.scene
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    mesh = next(o for o in sc.objects
                if o.type == "MESH" and o.name != "Icosphere")

    arm_l = bpy.data.objects.new("ElenaArmature.L", arm.data.copy())
    sc.collection.objects.link(arm_l)
    bpy.context.view_layer.update()

    # key a big rotation on hand.R_02 via keyframe_insert
    pb = arm_l.pose.bones["hand.R_02"]
    pb.rotation_mode = "QUATERNION"
    act = bpy.data.actions.new("T")
    arm_l.animation_data_create()
    arm_l.animation_data.action = act
    sc.frame_set(0)
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.keyframe_insert("rotation_quaternion", frame=0)
    sc.frame_set(32)
    pb.rotation_quaternion = (0.7, 0.1, 0.2, 0.3)
    pb.keyframe_insert("rotation_quaternion", frame=32)
    sc.frame_set(0)
    bpy.context.view_layer.update()
    log("f0 quat", tuple(round(v, 3) for v in pb.rotation_quaternion),
        "head", tuple(round(v, 4) for v in pb.head))
    sc.frame_set(32)
    bpy.context.view_layer.update()
    log("f32 quat", tuple(round(v, 3) for v in pb.rotation_quaternion),
        "head", tuple(round(v, 4) for v in pb.head))
    log("slots", [s.name for s in act.slots])
    log("ad slot", arm_l.animation_data.action_slot,
        "slot_handle", [cb.slot_handle for l in act.layers
                        for st in l.strips for cb in st.channelbags])


if __name__ == "__main__":
    main()
