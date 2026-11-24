#!/usr/bin/env python3
'''Build a generic MakeHuman head and neck preview in Blender 5.2.

Source geometry: ArtSource/_download/makehuman/base.obj
MakeHuman base mesh attribution: CC0 2020, Data Collection AB.
The OBJ is read only. Only faces in the body group become skin geometry;
joint groups are read as landmark cubes and helper groups are ignored.
'''

import bpy
import bmesh
import math
import os
import statistics
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ART_SOURCE = os.path.normpath(os.path.join(HERE, '..'))
OBJ_PATH = os.path.join(ART_SOURCE, '_download', 'makehuman', 'base.obj')
OUTPUT_DIR = os.path.join(HERE, 'Candidates')
FULL_BODY_HEIGHT_M = 1.70
RENDER_SIZE = 768
ADD_SHORT_HAIR = True
ATTRIBUTION = 'MakeHuman base.obj, CC0 2020, Data Collection AB.'
LANDMARK_NAMES = {
    'joint-l-eye', 'joint-r-eye',
    'joint-l-eye-target', 'joint-r-eye-target',
    'joint-head', 'joint-head-2', 'joint-neck',
}


def log(*items):
    print('GenericHead:', *items, flush=True)


def parse_obj(path):
    '''Read body faces and exact joint-group vertex sets from raw OBJ data.'''
    vertices = []
    body_faces = []
    landmark_ids = {name: set() for name in LANDMARK_NAMES}
    active_groups = set()

    with open(path, 'r', encoding='utf-8', errors='replace') as source:
        for line_number, line in enumerate(source, 1):
            if line.startswith('v '):
                fields = line.split()
                if len(fields) < 4:
                    continue
                vertices.append((float(fields[1]), float(fields[2]), float(fields[3])))
            elif line.startswith('g '):
                active_groups = set(line.split()[1:])
            elif line.startswith('f '):
                face = []
                for token in line.split()[1:]:
                    raw_index = int(token.split('/')[0])
                    index = raw_index - 1 if raw_index > 0 else len(vertices) + raw_index
                    if index < 0 or index >= len(vertices):
                        raise ValueError('Invalid OBJ face index at line %d' % line_number)
                    face.append(index)
                if len(face) < 3:
                    continue
                if 'body' in active_groups:
                    body_faces.append(tuple(face))
                for name in active_groups.intersection(LANDMARK_NAMES):
                    landmark_ids[name].update(face)

    if not vertices or not body_faces:
        raise RuntimeError('The OBJ has no usable vertices or g body faces: ' + path)
    return vertices, body_faces, landmark_ids


def median_center(points):
    '''A joint group is a small landmark cube; coordinate medians reject stray vertices.'''
    return Vector((
        statistics.median(point[0] for point in points),
        statistics.median(point[1] for point in points),
        statistics.median(point[2] for point in points),
    ))


def landmark_centers(vertices, landmark_ids):
    centers = {}
    for name, indices in landmark_ids.items():
        if len(indices) >= 4:
            centers[name] = median_center([vertices[index] for index in indices])
    return centers


def require_landmarks(centers):
    required = (
        'joint-l-eye', 'joint-r-eye',
        'joint-l-eye-target', 'joint-r-eye-target',
        'joint-neck',
    )
    missing = [name for name in required if name not in centers]
    if missing:
        raise RuntimeError('Missing measured OBJ landmark group(s): ' + ', '.join(missing))
    if 'joint-head' not in centers and 'joint-head-2' not in centers:
        raise RuntimeError('Missing joint-head and joint-head-2 landmark groups')


def raw_to_world(point, scale):
    '''Convert MakeHuman raw Y-up coordinates to Blender Z-up coordinates.'''
    return Vector((point[0] * scale, -point[2] * scale, point[1] * scale))


def raw_direction_to_world(direction):
    return Vector((direction[0], -direction[2], direction[1]))


def unit(vector, fallback):
    if vector.length < 1.0e-9:
        return fallback.copy()
    return vector.normalized()


def make_material(name, srgb, roughness, subsurface=0.0, metallic=0.0):
    def linear(component):
        if component <= 0.04045:
            return component / 12.92
        return ((component + 0.055) / 1.055) ** 2.4

    color = tuple(linear(channel) for channel in srgb)
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    shader = material.node_tree.nodes.get('Principled BSDF')
    if shader:
        shader.inputs['Base Color'].default_value = (*color, 1.0)
        shader.inputs['Roughness'].default_value = roughness
        shader.inputs['Metallic'].default_value = metallic
        if 'Subsurface Weight' in shader.inputs:
            shader.inputs['Subsurface Weight'].default_value = subsurface
        if 'Subsurface Scale' in shader.inputs:
            shader.inputs['Subsurface Scale'].default_value = 0.12
        if 'Specular IOR Level' in shader.inputs:
            shader.inputs['Specular IOR Level'].default_value = 0.24
    return material


def build_body_mesh(vertices, body_faces, scale, cut_raw_y):
    used_indices = sorted({index for face in body_faces for index in face})
    index_map = {old: new for new, old in enumerate(used_indices)}
    world_vertices = [raw_to_world(vertices[index], scale) for index in used_indices]
    faces = [tuple(index_map[index] for index in face) for face in body_faces]

    mesh = bpy.data.meshes.new('GenericHead_BodyMesh')
    mesh.from_pydata(world_vertices, [], faces)
    mesh.validate(verbose=False)
    mesh.update(calc_edges=True)

    cut_z = cut_raw_y * scale
    tolerance = max(1.0e-7, scale * 1.0e-5)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    geometry = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(
        bm,
        geom=geometry,
        plane_co=Vector((0.0, 0.0, cut_z)),
        plane_no=Vector((0.0, 0.0, 1.0)),
        dist=tolerance,
        clear_inner=False,
        clear_outer=False,
    )

    below = [face for face in bm.faces if face.calc_center_median().z < cut_z - tolerance]
    if below:
        bmesh.ops.delete(bm, geom=below, context='FACES')

    cut_edges = [
        edge for edge in bm.edges
        if edge.is_valid and edge.is_boundary
        and all(abs(vertex.co.z - cut_z) <= tolerance * 8.0 for vertex in edge.verts)
    ]
    cap_faces = []
    if cut_edges:
        fill_result = bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
        cap_faces = fill_result.get('faces', [])
    else:
        log('No horizontal cut loop was found; leaving the crop open')

    loose_edges = [edge for edge in bm.edges if edge.is_valid and not edge.link_faces]
    if loose_edges:
        bmesh.ops.delete(bm, geom=loose_edges, context='EDGES')
    loose_verts = [vertex for vertex in bm.verts if vertex.is_valid and not vertex.link_edges]
    if loose_verts:
        bmesh.ops.delete(bm, geom=loose_verts, context='VERTS')
    if bm.faces:
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update(calc_edges=True)

    for polygon in mesh.polygons:
        polygon.use_smooth = True
        if all(abs(mesh.vertices[index].co.z - cut_z) <= tolerance * 8.0 for index in polygon.vertices):
            polygon.use_smooth = False
    return mesh


def create_uv_sphere(name, location, rotation, scale, material, segments=48, rings=32):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=segments,
        ring_count=rings,
        radius=1.0,
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = rotation.to_euler()
    obj.scale = scale
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    obj['generic_preview_component'] = True
    obj['source_attribution'] = ATTRIBUTION
    return obj


def add_eye(label, center, gaze, eye_radius, materials):
    normal = unit(gaze, Vector((0.0, -1.0, 0.0)))
    rotation = normal.to_track_quat('Z', 'Y')
    sclera, limbal, iris, pupil = materials

    globe = create_uv_sphere(
        'Eye_' + label + '_Sclera', center, rotation,
        (eye_radius, eye_radius * 0.82, eye_radius * 0.88), sclera,
    )
    globe['generic_preview_component'] = True

    iris_center = center + normal * (eye_radius * 0.84)
    create_uv_sphere(
        'Eye_' + label + '_LimbalRing', iris_center, rotation,
        (eye_radius * 0.385, eye_radius * 0.385, eye_radius * 0.030), limbal,
        segments=40, rings=24,
    )
    create_uv_sphere(
        'Eye_' + label + '_Iris', iris_center + normal * (eye_radius * 0.018), rotation,
        (eye_radius * 0.335, eye_radius * 0.335, eye_radius * 0.034), iris,
        segments=40, rings=24,
    )
    create_uv_sphere(
        'Eye_' + label + '_Pupil', iris_center + normal * (eye_radius * 0.042), rotation,
        (eye_radius * 0.145, eye_radius * 0.145, eye_radius * 0.025), pupil,
        segments=32, rings=20,
    )
    return globe


def add_lips_material(mesh, eye_mid, side_axis, front_axis, eye_spacing, lip_material_index):
    up_axis = Vector((0.0, 0.0, 1.0))
    mouth_vertical = -1.02 * eye_spacing
    candidates = []
    for polygon in mesh.polygons:
        center = polygon.center
        lateral = (center - eye_mid).dot(side_axis)
        vertical = (center - eye_mid).dot(up_axis)
        if abs(lateral) <= 0.45 * eye_spacing and abs(vertical - mouth_vertical) <= 0.19 * eye_spacing:
            depth = (center - eye_mid).dot(front_axis)
            candidates.append((polygon, depth))
    if not candidates:
        return
    front_depth = max(depth for _, depth in candidates)
    for polygon, depth in candidates:
        if depth >= front_depth - 0.035 * eye_spacing:
            polygon.material_index = lip_material_index


def add_eyebrow(label, eye_center, side_axis, front_axis, eye_spacing, material, skin_bvh):
    up_axis = Vector((0.0, 0.0, 1.0))
    outward = -side_axis if label == 'Left' else side_axis
    vertices = []
    faces = []

    def projected_surface_point(nominal):
        origin = nominal + front_axis * (eye_spacing * 1.4)
        point, normal, _, _ = skin_bvh.ray_cast(origin, -front_axis, eye_spacing * 4.0)
        if point is None:
            point, normal, _, _ = skin_bvh.find_nearest(nominal)
        if point is None:
            return nominal
        normal = unit(normal, front_axis)
        return point + normal * (eye_spacing * 0.006)

    for station in range(9):
        t = station / 4.0 - 1.0
        arch = 0.040 * math.exp(-((t - 0.18) / 0.76) ** 2)
        vertical = eye_spacing * (0.255 + arch + 0.012 * t)
        half_width = eye_spacing * (0.012 + 0.018 * (1.0 - abs(t)))
        lateral = outward * (t * eye_spacing * 0.30)
        for edge_sign in (-1.0, 1.0):
            nominal = eye_center + lateral + up_axis * (vertical + edge_sign * half_width)
            vertices.append(projected_surface_point(nominal))

    for station in range(8):
        first = station * 2
        faces.append((first, first + 2, first + 3, first + 1))

    mesh = bpy.data.meshes.new('Eyebrow_' + label + '_Mesh')
    mesh.from_pydata(vertices, [], faces)
    mesh.validate(verbose=False)
    mesh.update(calc_edges=True)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    if bm.faces:
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update(calc_edges=True)

    obj = bpy.data.objects.new('Eyebrow_' + label, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    obj['generic_preview_component'] = True
    obj['source_attribution'] = ATTRIBUTION
    return obj


def add_short_hair_shell(body_obj, eye_mid, front_axis, eye_spacing, material):
    source_mesh = body_obj.data
    up_axis = Vector((0.0, 0.0, 1.0))
    selected_faces = []
    for polygon in source_mesh.polygons:
        relative = polygon.center - eye_mid
        height = relative.dot(up_axis)
        depth = relative.dot(front_axis)
        if height < -0.20 * eye_spacing:
            continue
        if depth > 0.14 * eye_spacing and height < 0.58 * eye_spacing:
            continue
        selected_faces.append(polygon)
    if not selected_faces:
        return None

    used_indices = sorted({index for polygon in selected_faces for index in polygon.vertices})
    index_map = {old: new for new, old in enumerate(used_indices)}
    vertices = [
        source_mesh.vertices[index].co + source_mesh.vertices[index].normal * (eye_spacing * 0.003)
        for index in used_indices
    ]
    faces = [tuple(index_map[index] for index in polygon.vertices) for polygon in selected_faces]
    hair_mesh = bpy.data.meshes.new('ShortHairScalpShell_Mesh')
    hair_mesh.from_pydata(vertices, [], faces)
    hair_mesh.validate(verbose=False)
    hair_mesh.update(calc_edges=True)
    bm = bmesh.new()
    bm.from_mesh(hair_mesh)
    if bm.faces:
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(hair_mesh)
    bm.free()
    hair_mesh.update(calc_edges=True)

    hair_obj = bpy.data.objects.new('ShortHairScalpShell', hair_mesh)
    bpy.context.collection.objects.link(hair_obj)
    hair_obj.data.materials.append(material)
    for polygon in hair_obj.data.polygons:
        polygon.use_smooth = True
    smooth = hair_obj.modifiers.new('Scalp shell smoothing', type='SUBSURF')
    smooth.subdivision_type = 'CATMULL_CLARK'
    smooth.levels = 1
    smooth.render_levels = 1
    shell = hair_obj.modifiers.new('Clipped scalp shell', type='SOLIDIFY')
    shell.thickness = max(0.0005, eye_spacing * 0.012)
    shell.offset = -1.0
    shell.use_rim = True
    hair_obj['generic_preview_component'] = True
    hair_obj['source_attribution'] = ATTRIBUTION
    return hair_obj


def add_area_light(name, target, direction, power, size, color):
    direction = unit(direction, Vector((0.0, -1.0, 0.0)))
    data = bpy.data.lights.new(name, type='AREA')
    data.energy = power
    data.shape = 'DISK'
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = target + direction * max(0.75, size * 1.4)
    obj.rotation_euler = (target - obj.location).to_track_quat('-Z', 'Y').to_euler()
    return obj


def evaluated_world_points(objects):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        evaluated_mesh = evaluated.to_mesh()
        if evaluated_mesh:
            points.extend(evaluated.matrix_world @ vertex.co for vertex in evaluated_mesh.vertices)
            evaluated.to_mesh_clear()
    if not points:
        raise RuntimeError('No preview geometry is available for camera framing')
    return points


def configure_camera(camera, front, points):
    world_up = Vector((0.0, 0.0, 1.0))
    front = front - world_up * front.dot(world_up)
    front = unit(front, Vector((0.0, -1.0, 0.0)))
    right = unit(front.cross(world_up), Vector((-1.0, 0.0, 0.0)))
    up = unit(right.cross(front), world_up)

    projected = [(point.dot(right), point.dot(up), point.dot(front)) for point in points]
    low_x, high_x = min(p[0] for p in projected), max(p[0] for p in projected)
    low_y, high_y = min(p[1] for p in projected), max(p[1] for p in projected)
    low_d, high_d = min(p[2] for p in projected), max(p[2] for p in projected)
    center = right * ((low_x + high_x) * 0.5) + up * ((low_y + high_y) * 0.5) + front * ((low_d + high_d) * 0.5)
    frame_width = max(high_x - low_x, high_y - low_y)

    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = max(frame_width / 0.70, 0.05)
    camera.location = center + front * max(1.0, frame_width * 3.0)
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
    return center, front, right, up


def setup_scene():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 64
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.render.resolution_x = RENDER_SIZE
    scene.render.resolution_y = RENDER_SIZE
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.film_transparent = False
    try:
        scene.view_settings.view_transform = 'AgX'
        scene.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass

    world = bpy.data.worlds.new('GenericHead_StudioWorld') if not bpy.data.worlds else bpy.data.worlds[0]
    scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes.get('Background')
    if background:
        background.inputs['Color'].default_value = (0.18, 0.18, 0.18, 1.0)
        background.inputs['Strength'].default_value = 0.42
    scene['asset_description'] = 'Neutral generic adult head-and-neck preview; no patient likeness.'
    scene['base_mesh_attribution'] = ATTRIBUTION
    return scene


def main():
    if not os.path.isfile(OBJ_PATH):
        raise FileNotFoundError('Expected local MakeHuman source mesh at: ' + OBJ_PATH)
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    vertices, body_faces, landmark_ids = parse_obj(OBJ_PATH)
    centers = landmark_centers(vertices, landmark_ids)
    require_landmarks(centers)

    body_vertex_ids = {index for face in body_faces for index in face}
    body_raw = [vertices[index] for index in body_vertex_ids]
    raw_min_y = min(point[1] for point in body_raw)
    raw_max_y = max(point[1] for point in body_raw)
    body_height = raw_max_y - raw_min_y
    if body_height <= 1.0e-6:
        raise RuntimeError('Could not measure body height from g body vertices')
    scale = FULL_BODY_HEIGHT_M / body_height

    left_eye_raw = centers['joint-l-eye']
    right_eye_raw = centers['joint-r-eye']
    left_target_raw = centers['joint-l-eye-target']
    right_target_raw = centers['joint-r-eye-target']
    raw_eye_mid = (left_eye_raw + right_eye_raw) * 0.5
    eye_spacing_raw = (right_eye_raw - left_eye_raw).length
    if eye_spacing_raw <= body_height * 0.008 or eye_spacing_raw >= body_height * 0.12:
        raise RuntimeError('Measured eye landmark spacing is outside a plausible head range')

    head_names = [name for name in ('joint-head', 'joint-head-2') if name in centers]
    head_raw = min(head_names, key=lambda name: (centers[name] - raw_eye_mid).length)
    head_to_neck = abs(centers[head_raw].y - centers['joint-neck'].y)
    neck_extension = max(0.22 * eye_spacing_raw, min(0.34 * head_to_neck, 0.72 * eye_spacing_raw))
    cut_raw_y = centers['joint-neck'].y - neck_extension
    if cut_raw_y <= raw_min_y or cut_raw_y >= raw_max_y:
        raise RuntimeError('Measured head/neck landmarks produced an invalid crop plane')

    # Remove any previous scene content so the preview and exports are deterministic.
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)

    skin = make_material('Skin_MediumBrown', (0.42, 0.33, 0.27), 0.60, subsurface=0.055)
    lips = make_material('Lips_NaturalWarmBrown', (0.40, 0.285, 0.24), 0.62, subsurface=0.02)
    sclera = make_material('Eye_SoftIvory', (0.84, 0.81, 0.76), 0.34)
    limbal = make_material('Eye_LimbalBrown', (0.105, 0.055, 0.031), 0.40)
    iris = make_material('Eye_IrisWarmBrown', (0.30, 0.145, 0.065), 0.37)
    pupil = make_material('Eye_Pupil', (0.012, 0.009, 0.007), 0.24)
    eyebrows = make_material('Brows_SoftDarkBrown', (0.12, 0.075, 0.05), 0.82)
    short_hair = make_material('Hair_ShortClippedDarkBrown', (0.075, 0.048, 0.034), 0.82)

    body_mesh = build_body_mesh(vertices, body_faces, scale, cut_raw_y)
    body_mesh.materials.append(skin)
    body_mesh.materials.append(lips)
    body_obj = bpy.data.objects.new('GenericAdultHeadAndNeck', body_mesh)
    bpy.context.collection.objects.link(body_obj)
    body_obj['generic_preview_component'] = True
    body_obj['source_attribution'] = ATTRIBUTION
    body_obj['source_group'] = 'body'
    body_obj['full_body_scale_m'] = FULL_BODY_HEIGHT_M
    body_obj['crop_landmarks'] = 'joint-head / joint-head-2 and joint-neck'

    left_eye = raw_to_world(left_eye_raw, scale)
    right_eye = raw_to_world(right_eye_raw, scale)
    left_target = raw_to_world(left_target_raw, scale)
    right_target = raw_to_world(right_target_raw, scale)
    eye_mid = (left_eye + right_eye) * 0.5
    eye_spacing = (right_eye - left_eye).length
    left_gaze = unit(left_target - left_eye, Vector((0.0, -1.0, 0.0)))
    right_gaze = unit(right_target - right_eye, left_gaze)
    front = unit(left_gaze + right_gaze, left_gaze)
    up_axis = Vector((0.0, 0.0, 1.0))
    relaxed_gaze = unit(front - up_axis * 0.035, front)
    left_gaze = unit(left_gaze * 0.82 + relaxed_gaze * 0.18, relaxed_gaze)
    right_gaze = unit(right_gaze * 0.82 + relaxed_gaze * 0.18, relaxed_gaze)
    eye_radius = eye_spacing * 0.166
    eye_center_offset = front * (eye_spacing * 0.02)
    eye_materials = (sclera, limbal, iris, pupil)
    eye_objects = [
        add_eye('Left', left_eye - eye_center_offset, left_gaze, eye_radius, eye_materials),
        add_eye('Right', right_eye - eye_center_offset, right_gaze, eye_radius, eye_materials),
    ]

    side_axis = unit(right_eye - left_eye, Vector((1.0, 0.0, 0.0)))
    add_lips_material(body_mesh, eye_mid, side_axis, front, eye_spacing, 1)

    smooth = body_obj.modifiers.new('Smooth anatomical surface', type='SUBSURF')
    smooth.subdivision_type = 'CATMULL_CLARK'
    smooth.levels = 1
    smooth.render_levels = 1
    smooth.show_only_control_edges = True

    depsgraph = bpy.context.evaluated_depsgraph_get()
    skin_bvh = BVHTree.FromObject(body_obj, depsgraph)
    eyebrow_objects = [
        add_eyebrow('Left', left_eye, side_axis, front, eye_spacing, eyebrows, skin_bvh),
        add_eyebrow('Right', right_eye, side_axis, front, eye_spacing, eyebrows, skin_bvh),
    ]
    hair_obj = add_short_hair_shell(body_obj, eye_mid, front, eye_spacing, short_hair) if ADD_SHORT_HAIR else None

    scene = setup_scene()
    camera_data = bpy.data.cameras.new('GenericHead_PreviewCamera')
    camera = bpy.data.objects.new('GenericHead_PreviewCamera', camera_data)
    bpy.context.collection.objects.link(camera)
    camera.data.lens = 55.0
    scene.camera = camera

    # Use measured eye targets for the front direction and measured landmarks
    # for the crop plane. Camera framing uses the resulting actual mesh bounds.
    body_objects = [body_obj] + eye_objects + eyebrow_objects
    if hair_obj is not None:
        body_objects.append(hair_obj)
    points = evaluated_world_points(body_objects)
    front_h = unit(Vector((front.x, front.y, 0.0)), Vector((0.0, -1.0, 0.0)))
    center, front_h, right_h, up_h = configure_camera(camera, front_h, points)

    add_area_light('Studio_Key', center, front_h - right_h * 0.65 + up_h * 0.55, 35.0, 0.85, (1.0, 0.97, 0.93))
    add_area_light('Studio_Fill', center, front_h + right_h * 0.85 + up_h * 0.12, 16.0, 1.05, (0.94, 0.97, 1.0))
    add_area_light('Studio_Rim', center, -front_h + right_h * 0.30 + up_h * 0.45, 22.0, 0.70, (0.98, 0.98, 1.0))

    front_path = os.path.join(OUTPUT_DIR, 'GenericHead_front.png')
    three_quarter_path = os.path.join(OUTPUT_DIR, 'GenericHead_three_quarter.png')
    blend_path = os.path.join(OUTPUT_DIR, 'GenericHead.blend')
    glb_path = os.path.join(OUTPUT_DIR, 'GenericHead.glb')

    scene.render.filepath = front_path
    configure_camera(camera, front_h, points)
    bpy.ops.render.render(write_still=True)

    quarter = Quaternion(Vector((0.0, 0.0, 1.0)), math.radians(25.0)) @ front_h
    scene.render.filepath = three_quarter_path
    configure_camera(camera, quarter, points)
    bpy.ops.render.render(write_still=True)

    # Keep the saved project opening on the frontal studio view.
    scene.render.filepath = front_path
    configure_camera(camera, front_h, points)
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    export_objects = [body_obj] + [obj for obj in bpy.context.scene.objects if obj.type == 'MESH' and obj.get('generic_preview_component') and obj not in eye_objects]
    export_objects.extend([obj for obj in bpy.context.scene.objects if obj.type == 'MESH' and obj.get('generic_preview_component') and obj not in export_objects])
    bpy.ops.object.select_all(action='DESELECT')
    for obj in export_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = body_obj
    try:
        bpy.ops.export_scene.gltf(
            filepath=glb_path,
            export_format='GLB',
            use_selection=True,
            export_apply=True,
        )
    except TypeError:
        bpy.ops.export_scene.gltf(filepath=glb_path, export_format='GLB', use_selection=True)

    log('Built generic head-and-neck preview from', OBJ_PATH)
    log('Scale: %.6f m per OBJ unit; body height: %.3f m' % (scale, body_height * scale))
    log('Crop plane uses measured joint-neck and %s landmarks' % head_raw)
    log('Attribution:', ATTRIBUTION)
    log('Outputs:', blend_path, glb_path, front_path, three_quarter_path)


if __name__ == '__main__':
    main()
