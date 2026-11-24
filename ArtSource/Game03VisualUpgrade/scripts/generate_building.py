"""Generate one near-field residential building with REAL through-openings.

Uses ranjian0/building_tools v1.0.13 (MIT). Building Tools cuts door/window
apertures into wall faces via bmesh; the result is exported to FBX + OBJ so
Unity 2022.3 imports it natively (no glTFast required).

Building Tools' headless API mirrors its operators, so the active object must be
in Edit Mode with the correct faces selected at each step:
  1. create_floorplan -> object mode result (a single vertical face)
  2. enter EDIT, select the floor face, create_floors -> extrudes storeys
  3. enter EDIT, select front wall faces, create_multigroup -> cuts real openings

Run:
  blender.exe -b --python generate_building.py -- <output_dir>
"""
import os
import sys

import bpy
import bmesh
import addon_utils

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
os.makedirs(OUT, exist_ok=True)

# Register the addon so its custom properties (bt_materials, matgroups, etc.)
# and bpy.types extensions exist. The API functions assume operator registration.
addon_utils.enable("building_tools", default_set=False)
from building_tools.btools.api import api
from building_tools.btools.api.options import (
    FloorplanOptions, FloorPlanType, FloorOptions,
    MultigroupOptions, SizeOffsetOptions, ArrayOptions,
    WindowFillType,
)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

# 10.3 m wide x 4 m deep near-field residence, matching StreetWorld's bay width.
api.create_floorplan(FloorplanOptions(
    type=FloorPlanType.RECTANGULAR, width=10.3, length=4.0,
))
floorplan = bpy.context.active_object
print("BT_FLOORPLAN", floorplan.name, len(floorplan.data.polygons))

# Extrude three storeys. Building Tools requires Edit Mode + selected face.
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
api.create_floors(FloorOptions(
    floor_count=3, floor_height=2.7, add_slab=True, add_columns=False,
    slab_thickness=0.18, slab_outsset=0.06,
))
bpy.ops.object.mode_set(mode="OBJECT")

walls = [o for o in bpy.context.scene.objects if o.type == "MESH"]
print("BT_WALLS", [(w.name, len(w.data.polygons)) for w in walls])

# Cut real door/window apertures into the street-facing (-Y) wall faces.
# Edit Mode + bmesh face selection is mandatory for the btools build functions.
def cut_front_face(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(obj.data)
    bpy.ops.mesh.select_all(action="DESELECT")
    bm.select_flush(False)
    best = None
    for f in bm.faces:
        n = f.normal
        if n.y > -0.8:  # only street-facing -Y faces
            continue
        xs = [v.co.x for v in f.verts]
        span = max(xs) - min(xs)
        if span < 2.0:
            continue
        if best is None or f.calc_area() > best.calc_area():
            best = f
    if best is None:
        bpy.ops.object.mode_set(mode="OBJECT")
        return False
    best.select_set(True)
    bm.select_flush(True)
    bmesh.update_edit_mesh(obj.data, loop_triangles=True)
    try:
        api.create_multigroup(MultigroupOptions(
            components="dw",
            size_offset=SizeOffsetOptions(size=(1.3, 2.0), offset=(0.0, 0.0)),
            array=ArrayOptions(count=3, spread=2.6),
            frame_thickness=0.09, frame_depth=0.12, dw_depth=0.10,
            show_window_fill=True, fill_type_window=WindowFillType.GLASS_PANES,
        ))
        bpy.ops.object.mode_set(mode="OBJECT")
        return True
    except Exception as exc:  # pragma: no cover
        print("BT_CUT_FAIL", obj.name, repr(exc))
        bpy.ops.object.mode_set(mode="OBJECT")
        return False

cut_count = sum(1 for w in walls if cut_front_face(w))
print("BT_CUT_FACE_COUNT", cut_count)
if cut_count == 0:
    raise RuntimeError("Building Tools produced no real door/window cut; refusing to write a decal-only facade.")

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.join()
root = bpy.context.active_object
root.name = "NearFieldResidence_RealOpenings"

# Building Tools leaves faces with no material -> white in Unity. Assign a
# two-slot facade/glass setup so the real openings read as recessed glazing.
def make_mat(name, colour, roughness=0.8):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*colour, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
        bsdf.inputs["Roughness"].default_value = roughness
    return m

masonry = make_mat("NearFieldMasonry", (0.72, 0.63, 0.50))
glass = make_mat("NearFieldGlass", (0.18, 0.30, 0.30), 0.25)
root.data.materials.clear()
root.data.materials.append(masonry)
root.data.materials.append(glass)
# The window/door fill faces are the smaller polygons with near-flat -Y normals.
# Assign glass to faces whose area is small relative to the largest wall face.
areas = sorted((p.area for p in root.data.polygons), reverse=True)
large = areas[0] if areas else 0
for p in root.data.polygons:
    if p.area < large * 0.25:
        p.material_index = 1

fbx = os.path.join(OUT, "NearFieldResidence_RealOpenings.fbx")
obj_path = os.path.join(OUT, "NearFieldResidence_RealOpenings.obj")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, apply_unit_scale=True,
                         bake_space_transform=False, add_leaf_bones=False)
bpy.ops.wm.obj_export(filepath=obj_path, export_selected_objects=True, apply_modifiers=True)

mesh = root.data
print("EXPORT_OK", fbx, "verts", len(mesh.vertices), "faces", len(mesh.polygons))
