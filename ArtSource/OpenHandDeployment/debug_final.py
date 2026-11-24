# -*- coding: utf-8 -*-
"""Reopen the saved ElenaHands.blend and measure R mesh spans per frame,
with and without the action bound."""
import bpy
from mathutils import Vector


def log(*a):
    print("VH-F:", *a, flush=True)


def main():
    bpy.ops.wm.open_mainfile(filepath=(
        r"C:\Users\Ait\Desktop\Neuralife\Unity\Game04-VirtualHand"
        r"\ArtSource\OpenHandDeployment\output\ElenaHands.blend"))
    sc = bpy.context.scene
    mesh = next(o for o in sc.objects
                if o.type == "MESH" and o.name != "Icosphere")
    arm = next(o for o in sc.objects if o.type == "ARMATURE" and
               o.name == "Object_11")

    def span(tag):
        deps = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(deps)
        evm = ev.to_mesh()
        ws = [ev.matrix_world @ Vector(v.co) for v in evm.vertices][::4]
        ev.to_mesh_clear()
        xs = [p.x for p in ws]; ys = [p.y for p in ws]; zs = [p.z for p in ws]
        log(tag, "span", [round(max(xs) - min(xs), 4),
                          round(max(ys) - min(ys), 4),
                          round(max(zs) - min(zs), 4)])

    for f in (0, 16, 32, 77):
        sc.frame_set(f)
        bpy.context.view_layer.update()
        span(f"f{f} with-action")
    # unbind R action
    arm.animation_data.action = None
    for f in (0, 32):
        sc.frame_set(f)
        bpy.context.view_layer.update()
        span(f"f{f} no-action")
    # is the REST itself fine?
    log("rest check done")


if __name__ == "__main__":
    main()
