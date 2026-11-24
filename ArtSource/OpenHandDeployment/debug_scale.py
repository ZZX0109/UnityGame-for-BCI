# -*- coding: utf-8 -*-
"""Print mesh matrix_world + evaluated world vert ranges at each stage of
the build to find where the scale disappears."""
import bpy
from mathutils import Matrix, Vector

GLB = r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand\ArtSource\ThirdParty\ElenaFF_RiggedHand\hand.glb"
WRIST = "hand.R_02"


def log(*a):
    print("VH-S:", *a, flush=True)


def world_span(obj, tag):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    evm = ev.to_mesh()
    ws = [ev.matrix_world @ Vector(v.co) for v in evm.vertices][::4]
    ev.to_mesh_clear()
    xs = [p.x for p in ws]; ys = [p.y for p in ws]; zs = [p.z for p in ws]
    log(tag, "MW scale", [round(s, 5) for s in obj.matrix_world.to_scale()],
        "world span", [round(max(xs) - min(xs), 4),
                       round(max(ys) - min(ys), 4),
                       round(max(zs) - min(zs), 4)])


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=GLB)
    sc = bpy.context.scene
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    mesh = next(o for o in sc.objects
                if o.type == "MESH" and o.name != "Icosphere")
    world_span(mesh, "S0 import")

    arm_w, mesh_w = arm.matrix_world.copy(), mesh.matrix_world.copy()
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    corners = [ev.matrix_world @ Vector(c) for c in ev.bound_box]
    ev.to_mesh_clear()
    mn = Vector((min(c.x for c in corners), min(c.y for c in corners),
                 min(c.z for c in corners)))
    mx = Vector((max(c.x for c in corners), max(c.y for c in corners),
                 max(c.z for c in corners)))
    span = max(mx - mn)
    metric = 0.19 / span
    wrist_w = arm_w @ Vector(arm.data.bones[WRIST].head_local)
    bake = Matrix.Scale(metric, 4) @ Matrix.Translation(-wrist_w)

    root = bpy.data.objects.new("R", None)
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
    world_span(mesh, "S1 baked")
    log("S1 mesh MW scale", [round(s, 5)
                             for s in mesh.matrix_world.to_scale()],
        "arm MW scale", [round(s, 5) for s in arm.matrix_world.to_scale()])

    # add an L copy + mirror edit, then re-measure
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

    # bake a trivial action on L via keyframe_insert across a frame sweep
    pb_l = arm_l.pose.bones["hand.R_02"]
    pb_l.rotation_mode = "QUATERNION"
    act = bpy.data.actions.new("T")
    arm_l.animation_data_create()
    arm_l.animation_data.action = act
    sc.frame_set(0)
    pb_l.rotation_quaternion = (1, 0, 0, 0)
    pb_l.keyframe_insert("rotation_quaternion", frame=0)
    sc.frame_set(32)
    pb_l.rotation_quaternion = (0.7, 0.1, 0.2, 0.3)
    pb_l.keyframe_insert("rotation_quaternion", frame=32)
    sc.frame_set(0)
    bpy.context.view_layer.update()

    for f in (0, 32):
        sc.frame_set(f)
        bpy.context.view_layer.update()
        world_span(mesh, f"S4 afterLbake frame{f}")


if __name__ == "__main__":
    main()
