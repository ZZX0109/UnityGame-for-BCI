"""Generate refined, game-light street trees with clean branch hierarchy and fine leaf clusters.

This script intentionally replaces the earlier MTree + 14-large-ico-canopy approach:
the previous silhouette read as an exploding shrub.  Here trunk/limbs are authored
procedurally with a clear crown, and foliage is built from many small clusters so
the canopy has a finer, more natural edge while staying under ~2 MB per FBX.

Run:
  blender.exe -b --python generate_trees_refined.py -- <output_dir>
"""
import os
import sys
import math
import random

import bpy
import bmesh
from mathutils import Vector, Matrix, Euler

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)

TREE_SPECS = [
    {"name": "StreetPlane_Oak_Refined", "species": "oak", "seed": 11,
     "height": 7.2, "crown": (2.25, 2.05, 1.65), "clusters": 320},
    {"name": "StreetGinkgo_Willow_Refined", "species": "willow", "seed": 27,
     "height": 6.8, "crown": (2.05, 1.95, 1.75), "clusters": 270},
    {"name": "StreetConifer_Pine_Refined", "species": "pine", "seed": 43,
     "height": 7.6, "crown": (1.45, 1.45, 2.30), "clusters": 620},
]


def make_material(name, rgb, roughness=0.85):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Roughness"].default_value = roughness
    return m


def materials_for(species):
    bark = make_material(f"{species}_Bark", (0.31, 0.24, 0.17), 0.94)
    if species == "pine":
        leaf_rgb = [(0.16, 0.29, 0.15), (0.19, 0.33, 0.17),
                    (0.14, 0.26, 0.14), (0.22, 0.36, 0.19)]
    elif species == "willow":
        leaf_rgb = [(0.50, 0.58, 0.24), (0.56, 0.63, 0.28),
                    (0.45, 0.54, 0.22), (0.61, 0.66, 0.31)]
    else:
        leaf_rgb = [(0.31, 0.47, 0.21), (0.36, 0.52, 0.24),
                    (0.27, 0.43, 0.19), (0.40, 0.55, 0.27)]
    leaves = [make_material(f"{species}_Leaf_{i}", c, 0.82) for i, c in enumerate(leaf_rgb)]
    return [bark] + leaves, list(range(1, 1 + len(leaves)))


def add_tube(bm, path, radii, sides, mat_index):
    """Append a tapered tube along path; returns nothing."""
    if len(path) < 2:
        return
    rings = []
    for i, p in enumerate(path):
        p = Vector(p)
        if i == 0:
            tangent = Vector(path[1]) - p
        elif i == len(path) - 1:
            tangent = p - Vector(path[i - 1])
        else:
            tangent = Vector(path[i + 1]) - Vector(path[i - 1])
        if tangent.length < 1e-6:
            tangent = Vector((0, 0, 1))
        tangent.normalize()
        ref = Vector((0, 0, 1)) if abs(tangent.z) < 0.9 else Vector((1, 0, 0))
        u = tangent.cross(ref).normalized()
        v = tangent.cross(u).normalized()
        ring = []
        for j in range(sides):
            a = 2.0 * math.pi * j / sides
            ring.append(bm.verts.new(p + radii[i] * (math.cos(a) * u + math.sin(a) * v)))
        rings.append(ring)
    for r0, r1 in zip(rings[:-1], rings[1:]):
        for j in range(sides):
            f = bm.faces.new((r0[j], r0[(j + 1) % sides], r1[(j + 1) % sides], r1[j]))
            f.material_index = mat_index
    bm.faces.new(tuple(reversed(rings[0]))).material_index = mat_index
    bm.faces.new(tuple(rings[-1])).material_index = mat_index


def add_leaf_blade(bm, origin, direction, length, width, mat_index, curl=0.14):
    """One slightly curved, double-sided leaf blade made from 5 vertices."""
    direction = Vector(direction).normalized()
    ref = Vector((0, 0, 1)) if abs(direction.z) < 0.9 else Vector((1, 0, 0))
    side = direction.cross(ref).normalized()
    normal = direction.cross(side).normalized()
    a = Vector(origin)
    b = a + direction * (length * 0.28) + side * (width * 0.42) + normal * (curl * length * 0.25)
    c = a + direction * length + normal * (curl * length)
    d = a + direction * (length * 0.28) - side * (width * 0.42) + normal * (curl * length * 0.25)
    e = a + direction * (length * 0.58) + normal * (curl * length * 0.55)
    front = [bm.verts.new(p) for p in (a, b, c, d, e)]
    back_offset = normal * -0.004
    back = [bm.verts.new(p + back_offset) for p in (a, b, c, d, e)]
    for face in ((0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)):
        bm.faces.new(tuple(front[i] for i in face)).material_index = mat_index
    for face in ((4, 1, 0), (4, 2, 1), (4, 3, 2), (4, 0, 3)):
        bm.faces.new(tuple(back[i] for i in face)).material_index = mat_index


def add_leaf_spray(bm, centre, scale, mat_index, rotation, rng, species):
    """A small fan of individual leaves replacing a single faceted canopy blob."""
    count = 18 if species == "pine" else 11 if species == "oak" else 9
    rot3 = rotation.to_3x3() if rotation is not None else None
    centre = Vector(centre)
    for _ in range(count):
        if species == "willow":
            angle = rng.uniform(0, 2 * math.pi)
            direction = Vector((math.cos(angle) * 0.35, math.sin(angle) * 0.35, -1.0)).normalized()
        elif species == "pine":
            angle = rng.uniform(0, 2 * math.pi)
            direction = Vector((math.cos(angle), math.sin(angle), rng.uniform(-0.25, 0.35))).normalized()
        else:
            angle = rng.uniform(0, 2 * math.pi)
            z = rng.uniform(-0.55, 0.45)
            r = math.sqrt(max(0.0, 1.0 - z * z))
            direction = Vector((math.cos(angle) * r, math.sin(angle) * r, z)).normalized()
        if rot3 is not None:
            direction = rot3 @ direction
        origin = centre + Vector((rng.uniform(-0.35, 0.35) * scale[0],
                                  rng.uniform(-0.35, 0.35) * scale[1],
                                  rng.uniform(-0.25, 0.25) * scale[2]))
        length = scale[0] * (rng.uniform(1.30, 1.90) if species == "pine" else rng.uniform(0.75, 1.25))
        if species == "pine":
            width = length * rng.uniform(0.45, 0.65)
        elif species == "willow":
            width = length * rng.uniform(0.38, 0.58)
        else:
            width = length * rng.uniform(0.48, 0.72)
        add_leaf_blade(bm, origin, direction, length, width, mat_index,
                       curl=rng.uniform(0.08, 0.20))


def sample_ellipsoid(rng, centre, radii, count, min_z):
    pts = []
    guard = 0
    while len(pts) < count and guard < count * 40:
        guard += 1
        u = rng.uniform(-1, 1)
        theta = rng.uniform(0, 2 * math.pi)
        # Bias toward the outer shell so the canopy has an irregular but solid edge.
        r = (1.0 - 0.45 * rng.random()) ** (1.0 / 3.0)
        s = math.sqrt(max(0.0, 1.0 - u * u))
        x = centre[0] + radii[0] * r * s * math.cos(theta)
        y = centre[1] + radii[1] * r * s * math.sin(theta)
        z = centre[2] + radii[2] * r * u
        if z < min_z:
            continue
        pts.append((x, y, z))
    return pts


def build_oak(bm, rng, spec, leaf_indices):
    h = spec["height"]
    clear = 2.35
    lean_x = 0.18
    trunk_pts = []
    trunk_radii = []
    for i in range(7):
        t = i / 6
        trunk_pts.append(Vector((lean_x * t * t, 0.04 * math.sin(t * 3), clear + 1.55 * t)))
        trunk_radii.append(0.245 - 0.115 * t)
    add_tube(bm, trunk_pts, trunk_radii, 12, 0)

    limb_ends = []
    for i in range(7):
        az = i * (2 * math.pi / 7) + rng.uniform(-0.22, 0.22)
        z0 = rng.uniform(2.55, 4.05)
        start = Vector((lean_x * ((z0 - clear) / 1.55) ** 2, 0, z0))
        elev = math.radians(rng.uniform(30, 48))
        length = rng.uniform(1.55, 2.35)
        direction = Vector((math.cos(az) * math.cos(elev), math.sin(az) * math.cos(elev), math.sin(elev)))
        mid = start + direction * length * 0.52 + Vector((0, 0, 0.12))
        end = start + direction * length
        add_tube(bm, [start, mid, end], [0.095, 0.061, 0.031], 8, 0)
        limb_ends.append(end)
        # Two short upward forks, kept sparse so the trunk reads cleanly.
        for k in range(2):
            fork_az = az + rng.choice([-0.55, 0.55])
            fork_dir = Vector((math.cos(fork_az) * 0.8, math.sin(fork_az) * 0.8, 0.65)).normalized()
            fend = mid + fork_dir * rng.uniform(0.65, 1.05)
            add_tube(bm, [mid, mid.lerp(fend, 0.55), fend], [0.042, 0.028, 0.014], 6, 0)
            limb_ends.append(fend)

    centre = (lean_x * 0.6, 0.0, h - 1.65)
    radii = spec["crown"]
    pts = sample_ellipsoid(rng, centre, radii, spec["clusters"], min_z=3.25)
    for e in limb_ends[:10]:
        for _ in range(3):
            pts.append(tuple(e + Vector((rng.uniform(-0.28, 0.28),
                                         rng.uniform(-0.28, 0.28),
                                         rng.uniform(-0.18, 0.25)))))
    for p in pts[:spec["clusters"] + 24]:
        s = rng.uniform(0.13, 0.23)
        rot = Euler((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5),
                     rng.uniform(0, 2 * math.pi)), "XYZ").to_matrix().to_4x4()
        add_leaf_spray(bm, p,
                       (s * rng.uniform(1.25, 1.75), s * rng.uniform(0.80, 1.15),
                        s * rng.uniform(0.42, 0.70)),
                       rng.choice(leaf_indices), rot, rng, "oak")


def build_willow(bm, rng, spec, leaf_indices):
    h = spec["height"]
    clear = 2.25
    trunk_pts = []
    trunk_radii = []
    for i in range(7):
        t = i / 6
        trunk_pts.append(Vector((0.12 * t, 0.05 * t * t, clear + 1.75 * t)))
        trunk_radii.append(0.235 - 0.105 * t)
    add_tube(bm, trunk_pts, trunk_radii, 12, 0)

    whips = []
    for i in range(7):
        az = i * (2 * math.pi / 7) + rng.uniform(-0.18, 0.18)
        z0 = rng.uniform(2.7, 4.0)
        start = Vector((0.1, 0, z0))
        elev = math.radians(rng.uniform(34, 52))
        length = rng.uniform(1.5, 2.2)
        direction = Vector((math.cos(az) * math.cos(elev), math.sin(az) * math.cos(elev), math.sin(elev)))
        mid = start + direction * length * 0.6
        end = start + direction * length
        add_tube(bm, [start, mid, end], [0.09, 0.055, 0.028], 8, 0)
        # Drooping whip: gentle arc down from the limb tip, not a hairball.
        drop = rng.uniform(1.1, 1.9)
        p0 = end
        p1 = end + Vector((math.cos(az) * 0.22, math.sin(az) * 0.22, -drop * 0.35))
        p2 = end + Vector((math.cos(az) * 0.42, math.sin(az) * 0.42, -drop))
        add_tube(bm, [p0, p1, p2], [0.026, 0.018, 0.008], 6, 0)
        whips.append((p0, p1, p2))

    used = 0
    for p0, p1, p2 in whips:
        for _ in range(6):
            if used >= spec["clusters"]:
                break
            t = rng.uniform(0.05, 1.0)
            if t < 0.5:
                base = p0.lerp(p1, t * 2)
            else:
                base = p1.lerp(p2, (t - 0.5) * 2)
            base = base + Vector((rng.uniform(-0.16, 0.16), rng.uniform(-0.16, 0.16), 0))
            s = rng.uniform(0.11, 0.19)
            rot = Euler((0, 0, rng.uniform(0, 2 * math.pi)), "XYZ").to_matrix().to_4x4()
            add_leaf_spray(bm, base,
                           (s * 0.85, s * 0.85, s * rng.uniform(1.8, 2.6)),
                           rng.choice(leaf_indices), rot, rng, "willow")
            used += 1
    # Fill the upper crown so it does not read as bare sticks.
    centre = (0.06, 0.0, h - 1.55)
    for p in sample_ellipsoid(rng, centre, spec["crown"], spec["clusters"] - used, min_z=4.2):
        s = rng.uniform(0.12, 0.21)
        rot = Euler((rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4),
                     rng.uniform(0, 2 * math.pi)), "XYZ").to_matrix().to_4x4()
        add_leaf_spray(bm, p,
                       (s * rng.uniform(1.1, 1.5), s * rng.uniform(0.8, 1.1),
                        s * rng.uniform(0.45, 0.75)),
                       rng.choice(leaf_indices), rot, rng, "willow")


def build_pine(bm, rng, spec, leaf_indices):
    h = spec["height"]
    trunk_pts = []
    trunk_radii = []
    for i in range(10):
        t = i / 9
        trunk_pts.append(Vector((0.05 * math.sin(t * 2), 0.03 * t, h * t)))
        trunk_radii.append(0.20 - 0.155 * t)
    add_tube(bm, trunk_pts, trunk_radii, 12, 0)

    cluster_budget = spec["clusters"]
    whorls = 17
    for w in range(whorls):
        z = 1.25 + w * (h - 1.15) / (whorls - 1)
        t = (z - 1.25) / max(0.01, h - 2.4)
        length = 1.72 * (1.0 - 0.63 * t) + 0.22
        count = 9 if z < 5.6 else 7
        for i in range(count):
            az = i * (2 * math.pi / count) + w * 0.48 + rng.uniform(-0.08, 0.08)
            start = Vector((0.05 * math.sin(z / h * 2), 0.03 * z / h, z))
            horiz = Vector((math.cos(az), math.sin(az), 0))
            p1 = start + horiz * length * 0.55 + Vector((0, 0, 0.06))
            p2 = start + horiz * length + Vector((0, 0, 0.22))
            add_tube(bm, [start, p1, p2], [0.052 - 0.025 * t, 0.032 - 0.014 * t, 0.012], 6, 0)
            for k in range(5):
                if cluster_budget <= 0:
                    break
                f = rng.uniform(0.22, 1.0)
                base = start.lerp(p2, f) + Vector((rng.uniform(-0.1, 0.1),
                                                    rng.uniform(-0.1, 0.1), 0.03))
                s = rng.uniform(0.10, 0.18) * (1.0 - 0.35 * t)
                rot = Euler((0, 0, rng.uniform(0, 2 * math.pi)), "XYZ").to_matrix().to_4x4()
                add_leaf_spray(bm, base, (s * 1.65, s * 1.65, s * 0.50),
                               rng.choice(leaf_indices), rot, rng, "pine")
                cluster_budget -= 1


def build_tree(spec):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    rng = random.Random(spec["seed"])
    mats, leaf_indices = materials_for(spec["species"])

    bm = bmesh.new()
    if spec["species"] == "oak":
        build_oak(bm, rng, spec, leaf_indices)
    elif spec["species"] == "willow":
        build_willow(bm, rng, spec, leaf_indices)
    else:
        build_pine(bm, rng, spec, leaf_indices)

    mesh = bpy.data.meshes.new(spec["name"])
    bm.to_mesh(mesh)
    bm.free()
    for m in mats:
        mesh.materials.append(m)
    obj = bpy.data.objects.new(spec["name"], mesh)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    path = os.path.join(OUT, spec["name"] + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True,
        bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
        mesh_smooth_type="FACE",
    )
    print("REFINED_TREE_OK", path, "verts", len(mesh.vertices),
          "faces", len(mesh.polygons), "bytes", os.path.getsize(path))


for spec in TREE_SPECS:
    build_tree(spec)
