# -*- coding: utf-8 -*-
"""Focused probe: under the A2 fist sample, which bones OWN the web verts
that displace most? Read-only, rev7 input."""
import json
import math
from pathlib import Path

import bpy
import mathutils

HERE = Path(__file__).resolve().parent
BLEND_IN = HERE / "output" / "rev7" / "refined_hands.blend"
PROFILE = (HERE.parent / "ActionCalibration" / "input_rev7" /
           "hand_rig_profile.json")


def main():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_IN))
    prof = json.load(open(str(PROFILE), encoding="utf-8"))
    chans, fist = prof["channels"], prof["samples"]["fist"]
    side = "R"
    sfx = "." + side
    obj = bpy.data.objects[f"Hand.{side}"]
    arm = bpy.data.objects[f"Armature.{side}"]

    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
    for i, c in enumerate(chans):
        pb = arm.pose.bones[c["bone"] + sfx]
        axis = {"X": 0, "Y": 1, "Z": 2}[c["axis"]]
        deg = (fist[i] - c["rest"]) * c["scale"] * c["sign"]
        pb.rotation_euler[axis] += math.radians(deg)
    bpy.context.view_layer.update()

    amw, mw = arm.matrix_world, obj.matrix_world
    anchors = []
    for n in ("ThumbMeta", "IndexMeta"):
        anchors.append(amw @ mathutils.Vector(
            arm.data.bones[n + sfx].tail_local))
    for n in ("Thumb1", "Index1"):
        anchors.append(amw @ mathutils.Vector(
            arm.data.bones[n + sfx].head_local))
    web_c = sum(anchors, mathutils.Vector()) / len(anchors)
    hand_len = (amw @ mathutils.Vector(
        arm.data.bones["Middle3" + sfx].tail_local) -
        amw @ mathutils.Vector(
        arm.data.bones["Wrist" + sfx].head_local)).length
    print("VH-P8B: hand_len", round(hand_len, 4),
          "web_c", tuple(round(x, 3) for x in web_c))

    rw = [mw @ mathutils.Vector(v.co) for v in obj.data.vertices]
    deps = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(deps)
    evm = ev.to_mesh()
    pw = [mw @ mathutils.Vector(v.co) for v in evm.vertices]
    ev.to_mesh_clear()

    rows = []
    for vi, p in enumerate(rw):
        d = (p - web_c).length
        if d < 0.20 * hand_len:
            rows.append(((pw[vi] - p).length, vi, d))
    rows.sort(reverse=True)
    for disp, vi, d in rows[:15]:
        gs = sorted(obj.data.vertices[vi].groups,
                    key=lambda g: -g.weight)[:3]
        wtxt = " ".join(
            f"{obj.vertex_groups[g.group].name}:{g.weight:.2f}" for g in gs)
        print(f"VH-P8B: vi {vi} disp {disp:.3f} d_web {d:.3f} | {wtxt}")


if __name__ == "__main__":
    main()
