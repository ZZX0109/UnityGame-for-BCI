# -*- coding: utf-8 -*-
"""Debug why the L mirror verification fails: check action evaluation and
per-bone bake math at frame 32."""
import bpy
from mathutils import Matrix, Vector

GLB = r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\ThirdParty\ElenaFF_RiggedHand\hand.glb"


def log(*a):
    print("VH-DBG:", *a, flush=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=GLB)
    sc = bpy.context.scene
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    mesh = next(o for o in sc.objects
                if o.type == "MESH" and o.name != "Icosphere")

    # minimal clean: root + bake to metric (same as build)
    wrist_w = arm.matrix_world @ Vector(arm.data.bones["hand.R_02"].head_local)
    tip_w = arm.matrix_world @ Vector(
        arm.data.bones["middle_03.R_027"].tail_local)
    metric = 0.19 / (tip_w - wrist_w).length
    bake = Matrix.Scale(metric, 4) @ Matrix.Translation(-wrist_w)
    arm_w, mesh_w = arm.matrix_world.copy(), mesh.matrix_world.copy()
    root = bpy.data.objects.new("ElenaHand.R", None)
    sc.collection.objects.link(root)
    root.matrix_world = Matrix.Identity(4)
    mesh.parent = None
    arm.parent = root
    arm.matrix_parent_inverse = Matrix.Identity(4)
    mesh.parent = root
    mesh.matrix_parent_inverse = Matrix.Identity(4)
    arm.matrix_world = bake @ arm_w
    mesh.matrix_world = bake @ mesh_w
    bpy.context.view_layer.update()

    # L armature mirrored
    arm_l = bpy.data.objects.new("ElenaArmature.L", arm.data.copy())
    sc.collection.objects.link(arm_l)
    arm_l.parent = root
    arm_l.matrix_parent_inverse = Matrix.Identity(4)
    arm_l.matrix_world = arm.matrix_world.copy()
    wx = arm.data.bones["hand.R_02"].head_local.x
    M = (Matrix.Translation(Vector((wx, 0, 0))) @
         Matrix.Diagonal((-1, 1, 1, 1)) @
         Matrix.Translation(Vector((-wx, 0, 0))))
    bpy.context.view_layer.objects.active = arm_l
    bpy.ops.object.mode_set(mode="EDIT")
    for eb in arm_l.data.edit_bones:
        eb.head = M @ eb.head
        eb.tail = M @ eb.tail
        eb.roll = 0.0
    bpy.ops.object.mode_set(mode="OBJECT")

    # TEST 1: hand-pose ONE bone on L via direct assignment and see if the
    # posed matrix equals M @ posed_R @ M for that bone (root bone test)
    sc.frame_set(0)
    bpy.context.view_layer.update()
    pb_r = arm.pose.bones["hand.R_02"]
    pb_l = arm_l.pose.bones["hand.R_02"]
    rest_l = pb_l.matrix.copy()
    log("L rest hand.R_02 loc", tuple(round(v, 4) for v in rest_l.to_translation()))
    # mirror target for the ROOT bone at R rest:
    target = M @ pb_r.matrix.copy() @ M
    log("target  loc", tuple(round(v, 4) for v in target.to_translation()))
    # set L basis directly: basis = restRel^-1 @ target (root bone)
    basis = pb_l.bone.matrix_local.inverted() @ target
    tr, q, sc3 = basis.decompose()
    pb_l.rotation_mode = "QUATERNION"
    pb_l.rotation_quaternion = q.normalized()
    pb_l.location = (0, 0, 0)
    bpy.context.view_layer.update()
    log("L posed  loc", tuple(round(v, 4) for v in pb_l.matrix.to_translation()))
    d = (pb_l.matrix.to_translation() - target.to_translation()).length
    log("TEST1 root-bone direct basis delta:", round(d, 6))

    # TEST 2: same for a child bone using the parent chain posed
    pb_r2 = arm.pose.bones["index_01.R_017"]
    pb_l2 = arm_l.pose.bones["index_01.R_017"]
    target2 = M @ pb_r2.matrix.copy() @ M
    rest_rel = pb_l2.bone.matrix_local.copy()
    if pb_l2.parent:
        rest_rel = pb_l2.parent.bone.matrix_local.inverted() @ rest_rel
        basis2 = rest_rel.inverted() @ (pb_l2.parent.matrix.inverted() @ target2)
    else:
        basis2 = rest_rel.inverted() @ target2
    tr2, q2, _ = basis2.decompose()
    pb_l2.rotation_mode = "QUATERNION"
    pb_l2.rotation_quaternion = q2.normalized()
    bpy.context.view_layer.update()
    d2 = (pb_l2.matrix.to_translation() - target2.to_translation()).length
    log("TEST2 child-bone direct basis delta:", round(d2, 6),
        "rot diff deg", round(q2.rotation_difference(
            pb_l2.matrix.to_quaternion()).angle * 57.2958, 3))

    # TEST 3: does a keyed action evaluate on arm_l?
    act = bpy.data.actions.new("T")
    arm_l.animation_data_create()
    arm_l.animation_data.action = act
    sc.frame_set(0)
    pb_l.rotation_quaternion = (1, 0, 0, 0)
    pb_l.keyframe_insert("rotation_quaternion", frame=0)
    pb_l.rotation_quaternion = q  # some other value at frame 10
    pb_l.keyframe_insert("rotation_quaternion", frame=10)
    sc.frame_set(10)
    bpy.context.view_layer.update()
    log("TEST3 action evaluates:", pb_l.rotation_quaternion[:] != (1, 0, 0, 0),
       "slots:", [(s.name) for s in act.slots])


if __name__ == "__main__":
    main()
