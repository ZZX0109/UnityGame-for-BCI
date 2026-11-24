"""Generate a refined near-field street building with real recessed openings.

The previous Building Tools output had genuine cut-outs but read as a beige box
with two glass stripes.  This version authors the facade manually in Blender:
partitioned wall panels, window/door frames with mullions, shop glazing,
cornice bands, a balcony, awning, roof parapet and service details.  Openings
are real gaps in the front wall, not decals.

Run:
  blender.exe -b --python generate_building_refined.py -- <output_dir>
"""
import os
import sys

import bpy
import bmesh
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)

W = 10.3
D = 4.0
H = 8.1
T = 0.18
FRONT = -D / 2
BACK = D / 2

MAT_SPEC = [
    ("FacadeMasonry", (0.74, 0.68, 0.59), 0.88),
    ("FacadeTrim", (0.92, 0.89, 0.82), 0.75),
    ("FacadeGlass", (0.17, 0.31, 0.36), 0.18),
    ("FacadeDoorWood", (0.34, 0.21, 0.13), 0.72),
    ("FacadeMetal", (0.16, 0.17, 0.18), 0.55),
    ("FacadeAwning", (0.22, 0.39, 0.41), 0.80),
    ("FacadeRoof", (0.32, 0.32, 0.31), 0.90),
]
MASONRY, TRIM, GLASS, WOOD, METAL, AWNING, ROOF = range(len(MAT_SPEC))


def make_material(name, rgb, roughness):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Roughness"].default_value = roughness
    return m


def add_box(bm, centre, size, mat_index):
    res = bmesh.ops.create_cube(bm, size=1.0)
    verts = res["verts"]
    faces = set()
    for v in verts:
        for f in v.link_faces:
            faces.add(f)
    centre = Vector(centre)
    matrix = Matrix.Translation(centre) @ Matrix.Diagonal(
        (size[0], size[1], size[2], 1.0)
    )
    bmesh.ops.transform(bm, matrix=matrix, verts=verts)
    for f in faces:
        f.material_index = mat_index


def front_wall(bm, x0, x1, z0, z1, mat_index=MASONRY):
    if x1 - x0 < 1e-4 or z1 - z0 < 1e-4:
        return
    add_box(bm, ((x0 + x1) / 2, FRONT + T / 2, (z0 + z1) / 2),
            (x1 - x0, T, z1 - z0), mat_index)


def build_facade_panels(bm, z0, z1, openings):
    """Build front wall as solid panels around real openings."""
    cursor = -W / 2
    for x0, x1, oz0, oz1 in sorted(openings):
        if x0 > cursor:
            front_wall(bm, cursor, x0, z0, z1)
        if oz0 > z0:
            front_wall(bm, x0, x1, z0, oz0)
        if oz1 < z1:
            front_wall(bm, x0, x1, oz1, z1)
        cursor = x1
    if cursor < W / 2:
        front_wall(bm, cursor, W / 2, z0, z1)


def add_window(bm, x0, x1, z0, z1, mullions=True):
    f = 0.075
    y_frame = FRONT - 0.025
    y_glass = FRONT + 0.075
    w = x1 - x0
    h = z1 - z0
    # Frame: four rails.
    add_box(bm, (x0 + f / 2, y_frame, (z0 + z1) / 2), (f, 0.10, h), TRIM)
    add_box(bm, (x1 - f / 2, y_frame, (z0 + z1) / 2), (f, 0.10, h), TRIM)
    add_box(bm, ((x0 + x1) / 2, y_frame, z0 + f / 2), (w, 0.10, f), TRIM)
    add_box(bm, ((x0 + x1) / 2, y_frame, z1 - f / 2), (w, 0.10, f), TRIM)
    # Recessed glazing.
    add_box(bm, ((x0 + x1) / 2, y_glass, (z0 + z1) / 2),
            (w - 2 * f, 0.025, h - 2 * f), GLASS)
    if mullions:
        add_box(bm, ((x0 + x1) / 2, y_frame - 0.005, (z0 + z1) / 2),
                (0.035, 0.09, h - 2 * f), TRIM)
        add_box(bm, ((x0 + x1) / 2, y_frame - 0.005, z0 + h * 0.42),
                (w - 2 * f, 0.09, 0.035), TRIM)
    # Sill projects a little further than the frame.
    add_box(bm, ((x0 + x1) / 2, FRONT - 0.055, z0 - 0.035),
            (w + 0.16, 0.20, 0.07), TRIM)


def add_door(bm, x0, x1, z0, z1):
    w = x1 - x0
    h = z1 - z0
    y = FRONT + 0.055
    add_box(bm, ((x0 + x1) / 2, y, (z0 + z1) / 2), (w - 0.10, 0.07, h - 0.06), WOOD)
    add_box(bm, ((x0 + x1) / 2, y - 0.045, z0 + h * 0.64),
            (w - 0.34, 0.03, h * 0.27), GLASS)
    add_box(bm, (x1 - 0.22, FRONT - 0.03, z0 + h * 0.46),
            (0.055, 0.09, 0.16), METAL)
    add_box(bm, ((x0 + x1) / 2, FRONT - 0.10, z0 + 0.04),
            (w + 0.20, 0.34, 0.08), TRIM)


def build():
    bm = bmesh.new()

    # Solid rear and side walls.
    add_box(bm, (0, BACK - T / 2, H / 2), (W, T, H), MASONRY)
    add_box(bm, (-W / 2 + T / 2, 0, H / 2), (T, D, H), MASONRY)
    add_box(bm, (W / 2 - T / 2, 0, H / 2), (T, D, H), MASONRY)

    # Ground floor: door + two shop windows.
    ground_openings = [
        (-2.70, -1.50, 0.0, 2.20),
        (0.00, 1.90, 0.55, 2.05),
        (2.50, 4.40, 0.55, 2.05),
    ]
    build_facade_panels(bm, 0.0, 2.70, ground_openings)
    add_door(bm, *ground_openings[0])
    for opening in ground_openings[1:]:
        add_window(bm, *opening, mullions=True)

    # Upper floors: three recessed windows per floor.
    upper_centres = [-3.20, 0.0, 3.20]
    for floor in (1, 2):
        z0 = floor * 2.70
        openings = []
        for cx in upper_centres:
            openings.append((cx - 0.675, cx + 0.675, z0 + 0.75, z0 + 2.15))
        build_facade_panels(bm, z0, z0 + 2.70, openings)
        for opening in openings:
            add_window(bm, *opening, mullions=True)

    # Cornice / floor bands.
    for z in (2.66, 5.36, 8.02):
        add_box(bm, (0, FRONT - 0.075, z), (W + 0.22, 0.24, 0.13), TRIM)

    # Shop awning over the ground-floor glazing.
    add_box(bm, (1.85, FRONT - 0.38, 2.34), (7.10, 0.78, 0.08), AWNING)
    for i in range(8):
        stripe_x = -1.55 + i * 0.90
        add_box(bm, (stripe_x, FRONT - 0.78, 2.31), (0.10, 0.06, 0.13), TRIM)

    # Balcony on the middle floor, centred on the middle window.
    add_box(bm, (0, FRONT - 0.46, 2.78), (2.45, 0.92, 0.12), TRIM)
    add_box(bm, (0, FRONT - 0.90, 3.52), (2.45, 0.06, 0.06), METAL)
    for i in range(9):
        bx = -1.10 + i * 0.275
        add_box(bm, (bx, FRONT - 0.90, 3.16), (0.035, 0.05, 0.68), METAL)

    # Roof slab + parapet.
    add_box(bm, (0, 0, H + 0.08), (W + 0.18, D + 0.18, 0.16), ROOF)
    add_box(bm, (0, FRONT - 0.02, H + 0.36), (W + 0.22, 0.16, 0.48), MASONRY)
    add_box(bm, (0, BACK + 0.02, H + 0.36), (W + 0.22, 0.16, 0.48), MASONRY)
    add_box(bm, (-W / 2 - 0.02, 0, H + 0.36), (0.16, D + 0.22, 0.48), MASONRY)
    add_box(bm, (W / 2 + 0.02, 0, H + 0.36), (0.16, D + 0.22, 0.48), MASONRY)

    # Downspout and a small service meter for visual scale.
    add_box(bm, (W / 2 - 0.14, FRONT - 0.07, H / 2), (0.09, 0.09, H), METAL)
    add_box(bm, (-4.55, FRONT - 0.10, 1.05), (0.36, 0.14, 0.50), METAL)

    mesh = bpy.data.meshes.new("NearFieldResidence_Refined")
    bm.to_mesh(mesh)
    bm.free()
    for name, rgb, rough in MAT_SPEC:
        mesh.materials.append(make_material(name, rgb, rough))

    obj = bpy.data.objects.new("NearFieldResidence_Refined", mesh)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    # Create a real UV layout so Unity can apply the refined stucco/plaster texture.
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(island_margin=0.02)
    bpy.ops.object.mode_set(mode="OBJECT")

    path = os.path.join(OUT, "NearFieldResidence_Refined.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True,
        bake_space_transform=False, add_leaf_bones=False, bake_anim=False,
        mesh_smooth_type="FACE",
    )
    print("REFINED_BUILDING_OK", path, "verts", len(mesh.vertices),
          "faces", len(mesh.polygons), "bytes", os.path.getsize(path))


bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
build()
