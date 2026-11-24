# -*- coding: utf-8 -*-
"""Read-only probe of output/rev7/refined_hands.blend for the rev8 weight fix.
Prints object/bone/vertex-group structure, tri counts, and fist-pose
deformation diagnostics (knuckle edge collapse + thumb-web displacement).
Writes nothing."""
import json
import math
from pathlib import Path

import bpy
import mathutils

HERE = Path(__file__).resolve().parent
BLEND_IN = HERE / "output" / "rev7" / "refined_hands.blend"
PROFILE = HERE.parent / "ActionCalibration" / "input_rev7" / "hand_rig_profile.json"


def log(*a):
    print("VH-PROBE8:", *a, flush=True)


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def main():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND_IN))
    prof = json.load(open(str(PROFILE), encoding="utf-8"))
    chans = prof["channels"]
    fist = prof["samples"]["fist"]

    for side in ("R", "L"):
        obj = bpy.data.objects[f"Hand.{side}"]
        arm = bpy.data.objects[f"Armature.{side}"]
        log(side, "mesh tris", tri_count(obj.data),
            "verts", len(obj.data.vertices),
            "vgroups", len(obj.vertex_groups),
            "parent", obj.parent.name if obj.parent else None)
        log(side, "armature bones", [b.name for b in arm.data.bones])
        # any leftover pose?
        nonzero = [pb.name for pb in arm.pose.bones
                   if mathutils.Vector(pb.rotation_euler).length > 1e-9]
        log(side, "leftover posed bones", nonzero)
        if side == "R":
            log("arm matrix_world", [list(r) for r in arm.matrix_world])
            log("mesh matrix_world", [list(r) for r in obj.matrix_world])

        # ---- apply fist sample through the shared channel mapping ----
        for pb in arm.pose.bones:
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (0, 0, 0)
        sfx = "." + side
        for i, c in enumerate(chans):
            bone = c["bone"] + sfx
            axis = {"X": 0, "Y": 1, "Z": 2}[c["axis"]]
            deg = (fist[i] - c["rest"]) * c["scale"] * c["sign"]
            pb = arm.pose.bones.get(bone)
            if pb is None:
                raise RuntimeError("missing pose bone " + bone)
            pb.rotation_euler[axis] += math.radians(deg)
        bpy.context.view_layer.update()

        # ---- diagnostics in world space ----
        amw = arm.matrix_world
        mw = obj.matrix_world

        def world_of(bone_name, local):
            return amw @ mathutils.Vector(
                arm.data.bones[bone_name + sfx].local)

        # rest positions of MCP heads (rest matrices, independent of pose)
        knuckle_bones = ["Index1", "Middle1", "Ring1", "Little1", "Thumb1"]
        rest_pts = {}
        for n in knuckle_bones:
            rest_pts[n] = amw @ mathutils.Vector(
                arm.data.bones[n + sfx].head_local)

        # deformed vertex positions
        deps = bpy.context.evaluated_depsgraph_get()
        ev = obj.evaluated_get(deps)
        evm = ev.to_mesh()
        pw = [mw @ mathutils.Vector(v.co) for v in evm.vertices]
        ev.to_mesh_clear()

        # rest vertex positions
        rw = [mw @ mathutils.Vector(v.co) for v in obj.data.vertices]

        # 1) knuckle-band edge collapse: edges with both ends within 0.055
        #    of a rest MCP head; compare posed vs rest edge length
        import itertools
        band_edges = []
        for e in obj.data.edges:
            a, b = e.vertices
            pa, pb_ = rw[a], rw[b]
            for n, c in rest_pts.items():
                if (pa - c).length < 0.055 and (pb_ - c).length < 0.055:
                    band_edges.append((a, b))
                    break
        log(side, "knuckle band edges", len(band_edges))
        ratios = []
        for a, b in band_edges:
            lr = (rw[a] - rw[b]).length
            lp = (pw[a] - pw[b]).length
            if lr > 1e-7:
                ratios.append(lp / lr)
        ratios.sort()
        if ratios:
            q = lambda f: ratios[int(f * (len(ratios) - 1))]
            log(side, f"edge len ratio posed/rest: min {ratios[0]:.3f} "
                f"q05 {q(0.05):.3f} median {q(0.5):.3f} max {ratios[-1]:.3f}")

        # 2) thumb-web displacement: verts near the web center
        wt = amw @ mathutils.Vector(arm.data.bones["ThumbMeta" + sfx].tail_local)
        it = amw @ mathutils.Vector(arm.data.bones["IndexMeta" + sfx].tail_local)
        web = (wt + it) / 2
        disp = []
        for vi, p in enumerate(rw):
            if (p - web).length < 0.09:
                disp.append(((pw[vi] - p).length, vi))
        disp.sort(reverse=True)
        top = [f"{vi}:{d:.3f}" for d, vi in disp[:8]]
        log(side, "web verts", len(disp), "max displacements", top)
        # which bones own the web verts?
        own = {}
        for d, vi in disp[:30]:
            best, bw = None, -1
            for g in obj.vertex_groups:
                for gd in g.element[vi].data if False else []:
                    pass
            # simpler: read via vertex_groups API below
        # weight readout for the worst 12 web verts
        worst = [vi for _, vi in disp[:12]]
        for vi in worst:
            ws = []
            for g in obj.data.vertices[vi].groups:
                name = obj.vertex_groups[g.group].name
                if g.weight > 0.05:
                    ws.append(f"{name}:{g.weight:.2f}")
            log(side, f"web vert {vi} rest-w", ws)

        # 3) knuckle weight ownership for worst-collapsed band verts
        if ratios:
            # find verts in the worst quartile edges
            worst_v = set()
            for a, b in band_edges:
                lr = (rw[a] - rw[b]).length
                lp = (pw[a] - pw[b]).length
                if lr > 1e-7 and lp / lr < 0.35:
                    worst_v.add(a)
                    worst_v.add(b)
            from collections import Counter
            cnt = Counter()
            for vi in worst_v:
                best, bw = None, -1
                for g in obj.data.vertices[vi].groups:
                    name = obj.vertex_groups[g.group].name
                    if g.weight > bw:
                        best, bw = name, g.weight
                cnt[best] += 1
            log(side, "worst-collapse verts max-weight owner", dict(cnt))

        if side == "R":
            # palm frame for cameras (rev7 non-degenerate derivation)
            w_h = amw @ mathutils.Vector(arm.data.bones["Wrist" + sfx].head_local)
            i_mcp = amw @ mathutils.Vector(
                arm.data.bones["IndexMeta" + sfx].tail_local)
            l_mcp = amw @ mathutils.Vector(
                arm.data.bones["LittleMeta" + sfx].tail_local)
            normal = (i_mcp - w_h).cross(l_mcp - w_h).normalized()
            log("R palm normal (rest frame)", tuple(round(x, 3) for x in normal))
            # posed MCP cluster center for closeups
            posed_heads = []
            for n in knuckle_bones:
                pb = arm.pose.bones[n + sfx]
                posed_heads.append(pb.head)
            c = sum(posed_heads, mathutils.Vector()) / len(posed_heads)
            log("posed knuckle cluster center (world)",
                tuple(round(x, 3) for x in c))


if __name__ == "__main__":
    main()
