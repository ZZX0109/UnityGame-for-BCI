#!/usr/bin/env python3
"""Apply rev2 edits to build_mpfb_avatar_rev2.py (one-shot, verified replacements)."""
from pathlib import Path
import sys

TARGET = Path(__file__).resolve().parent / 'build_mpfb_avatar_rev2.py'
src = TARGET.read_text(encoding='utf-8')
applied = []


def replace(old, new, label):
    global src
    if old not in src:
        sys.exit(f'PATCH FAILED, pattern not found: {label}')
    if src.count(old) != 1:
        sys.exit(f'PATCH FAILED, pattern not unique: {label}')
    src = src.replace(old, new)
    applied.append(label)


# 1. CLI args for body-shape and fabric fixes.
replace("""    for role in REQUIRED_ROLES:
        parser.add_argument(ROLE_CLI[role], type=Path, dest=role,
                            help='Explicit asset file for ' + role + '.')""",
"""    for role in REQUIRED_ROLES:
        parser.add_argument(ROLE_CLI[role], type=Path, dest=role,
                            help='Explicit asset file for ' + role + '.')
    parser.add_argument('--cupsize', type=float, default=0.25,
                        help='Macro cupsize override so the body stays inside CC0 clothes.')
    parser.add_argument('--firmness', type=float, default=0.35,
                        help='Macro firmness override (chest clearance).')
    parser.add_argument('--weight', type=float, default=0.55,
                        help='Macro weight override (fills sweater/hip volume).')
    parser.add_argument('--shirt-offset-mm', dest='shirt_offset_mm', type=float, default=3.0,
                        help='Outward normal offset (mm) applied to the sweater mesh.')
    parser.add_argument('--trousers-offset-mm', dest='trousers_offset_mm', type=float, default=2.0,
                        help='Outward normal offset (mm) applied to the trousers mesh.')
    parser.add_argument('--bump-strength', dest='bump_strength', type=float, default=0.2,
                        help='Bump node strength for knit fabric (anti shimmer at distance).')
    parser.add_argument('--normal-strength', dest='normal_strength', type=float, default=0.35,
                        help='Normal-map node strength for trousers (anti shimmer at distance).')""",
    'cli-args')

# 2. Body macro overrides in create_human_and_assets.
replace("""def create_human_and_assets(human_service, target_service, selected):
    macro = target_service.get_default_macro_info_dict()
    # Adult, average build, neutral gender macro values where MPFB exposes the fields.
    for key in ('gender', 'age', 'weight', 'muscle', 'height'):
        if key in macro:
            macro[key] = 0.5""",
"""def create_human_and_assets(human_service, target_service, selected, fabric=None, macro_overrides=None):
    macro = target_service.get_default_macro_info_dict()
    # Adult, average build, neutral gender macro values where MPFB exposes the fields.
    for key in ('gender', 'age', 'weight', 'muscle', 'height'):
        if key in macro:
            macro[key] = 0.5
    if macro_overrides:
        for key, value in macro_overrides.items():
            if key in macro:
                macro[key] = value""",
    'macro-overrides')

# 3. Fabric clearance + material calming after each asset is equipped.
replace("""        if asset_object is None or asset_object.type != 'MESH':
            raise RuntimeError(f'MPFB did not produce a mesh for required {role} asset: {selected[role]["path"]}')
        asset_objects[role] = asset_object""",
"""        if asset_object is None or asset_object.type != 'MESH':
            raise RuntimeError(f'MPFB did not produce a mesh for required {role} asset: {selected[role]["path"]}')
        asset_objects[role] = asset_object
        if fabric is not None and role in ('shirt', 'trousers'):
            calm_fabric_material(asset_object, fabric['bump_strength'], fabric['normal_strength'])
            offset_key = role + '_offset_m'
            if offset_key in fabric and fabric[offset_key] > 0.0:
                add_fabric_clearance(asset_object, fabric[offset_key])""",
    'fabric-call')

# 4. Helper functions before create_human_and_assets.
replace("""def create_human_and_assets(human_service, target_service, selected,""",
"""def add_fabric_clearance(asset_object, offset_m):
    # Small constant displacement along vertex normals so the cloth shell clears
    # the body and neighbouring garments. Millimetre scale, not a cover-up.
    modifier = asset_object.modifiers.new(name='FabricClearance', type='DISPLACE')
    modifier.strength = offset_m
    modifier.midlevel = 0.5


def calm_fabric_material(asset_object, bump_strength, normal_strength):
    # Reduce bump/normal intensities that alias into heavy noise at full-body
    # distances; the diffuse textures themselves stay untouched.
    for material in asset_object.data.materials:
        if material is None or not material.use_nodes:
            continue
        for node in material.node_tree.nodes:
            if node.type == 'BUMP' and 'Strength' in node.inputs:
                node.inputs['Strength'].default_value = bump_strength
            elif node.type == 'NORMAL_MAP' and 'Strength' in node.inputs:
                node.inputs['Strength'].default_value = normal_strength


def create_human_and_assets(human_service, target_service, selected,""",
    'helpers')

# 5. Pass fabric/overrides at the call site.
replace("""    basemesh, rig, asset_objects, macro = create_human_and_assets(human_service, target_service, selected)""",
"""    fabric = {
        'bump_strength': args.bump_strength,
        'normal_strength': args.normal_strength,
        'shirt_offset_m': args.shirt_offset_mm / 1000.0,
        'trousers_offset_m': args.trousers_offset_mm / 1000.0,
    }
    macro_overrides = {'cupsize': args.cupsize, 'firmness': args.firmness, 'weight': args.weight}
    basemesh, rig, asset_objects, macro = create_human_and_assets(
        human_service, target_service, selected, fabric=fabric, macro_overrides=macro_overrides)""",
    'call-site')

# 6. New multi-view renderer with poses and metadata.
replace("""def render_views(output_dir, camera, center):
    scene = bpy.context.scene
    distance = 4.15
    views = {
        'front.png': (center[0], center[1] - distance, center[2]),
        'three_quarter.png': (
            center[0] + distance * 0.5,
            center[1] - distance * 0.8660254,
            center[2] + 0.03,
        ),
    }
    for filename, location in views.items():
        camera.location = location
        direction = Vector(center) - camera.location
        camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(output_dir / filename)
        bpy.ops.render.render(write_still=True)""",
"""def find_upper_arm(rig, side):
    for bone in rig.pose.bones:
        name = bone.name.lower()
        if 'upperarm' in name and name.endswith('_' + side):
            return bone
    return None


def raise_arm(rig, side, angle_rad):
    # Rotate the upper-arm pose bone about the world Y (forward) axis so the
    # arm lifts sideways in the frontal plane, exposing the underarm seam.
    pose_bone = find_upper_arm(rig, side)
    if pose_bone is None:
        raise RuntimeError(f'No upperarm bone found for side {side!r}.')
    rest_matrix = pose_bone.matrix.copy()
    rotation = Matrix.Rotation(angle_rad if side == 'l' else -angle_rad, 4, 'Y')
    pose_bone.matrix = rotation @ rest_matrix
    return rest_matrix


def lower_arm(rig, side, rest_matrix):
    pose_bone = find_upper_arm(rig, side)
    if pose_bone is not None:
        pose_bone.matrix = rest_matrix


def render_views(output_dir, camera, center, rig=None):
    import json as _json
    scene = bpy.context.scene
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1500
    distance = 4.15
    cx, cy, cz = center[0], center[1], center[2]
    simple_views = {
        'front.png': ((cx, cy - distance, cz), 50.0, 'front full body, baseline camera'),
        'three_quarter.png': ((cx + distance * 0.5, cy - distance * 0.8660254, cz + 0.03), 50.0,
                              'three-quarter front, baseline camera'),
        'side.png': ((cx - distance, cy, 0.85), 50.0, 'left profile full body, baseline camera'),
        'eye_level_chest_waist.png': ((cx + 0.15, cy - 0.75, 1.62), 35.0,
                                      'eye-height camera looking down at chest and waist'),
        'feet_oblique_side.png': ((cx + 0.9, cy - 1.1, 0.45), 50.0,
                                  'oblique side view of both feet and trouser hems'),
    }
    metadata = {'camera': 'AvatarRenderCamera', 'lights': ['AvatarKey', 'AvatarFill', 'AvatarRim'],
                'resolution': [1000, 1500], 'view_transform': 'AgX', 'views': []}

    def shoot(filename, location, lens, note):
        camera_data = camera.data
        camera_data.lens = lens
        camera.location = location
        direction = Vector(center) - camera.location
        camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(output_dir / filename)
        bpy.ops.render.render(write_still=True)
        metadata['views'].append({'file': filename, 'camera_position': list(location),
                                  'target': list(center), 'focal_length_mm': lens, 'note': note})
        print('WROTE', filename, flush=True)

    for filename, (location, lens, note) in simple_views.items():
        shoot(filename, location, lens, note)

    posed_views = []
    if rig is not None:
        for side, filename, cam_pos, lens in (
            ('l', 'oblique_front_left_armraised.png', (cx - distance * 0.5, cy - distance * 0.8660254, cz + 0.03), 50.0),
            ('r', 'oblique_front_right_armraised.png', (cx + distance * 0.5, cy - distance * 0.8660254, cz + 0.03), 50.0),
        ):
            rest = raise_arm(rig, side, 0.9)
            bpy.context.view_layer.update()
            shoot(filename, list(cam_pos), lens, 'oblique front with ' + side + ' arm raised ~52 deg')
            lower_arm(rig, side, rest)
            posed_views.append({'side': side, 'file': filename,
                                'pose': 'upperarm rotated 0.9 rad about world Y (arm raised)'})
        metadata['posed_views'] = posed_views
    else:
        metadata['posed_views'] = []

    with (output_dir / 'views_metadata.json').open('w', encoding='utf-8') as meta_file:
        _json.dump(metadata, meta_file, indent=2)
        meta_file.write('\\n')""",
    'render-views')

# 7. Import Matrix for pose math.
replace("""import bpy
from mathutils import Vector""",
"""import bpy
from mathutils import Matrix, Vector""",
    'mathutils-import')

# 8. Pass rig into render_views.
replace("""    camera, center = create_lighting_and_camera(scale_record['final_bounds_m'])
    render_views(output_dir, camera, center)""",
"""    camera, center = create_lighting_and_camera(scale_record['final_bounds_m'])
    render_views(output_dir, camera, center, rig=rig)""",
    'render-call')

# 9. Manifest: record revision, fixes, licenses from descriptors, and all files.
replace("""        'assets': provenance,""",
"""        'assets': {role: dict(info, **{
            'license_from_descriptor': parse_descriptor_license(info['file'], 'license'),
            'author_from_descriptor': parse_descriptor_license(info['file'], 'author'),
            'official_asset_page_verified_this_round': 'unknown',
        }) for role, info in provenance.items()},
        'revision': 'rev2_20261120',
        'revision_changes': [
            'macro cupsize/firmness/weight adjusted so the body stays inside the CC0 sweater',
            'millimetre-scale normal displacement on shirt/trousers to clear body and pant-shoe overlap',
            'bump/normal map strength lowered on knit/pants to stop far-field texture noise',
            'five baseline-comparable plus two posed views rendered with recorded camera metadata',
        ],
        'fabric_fixes': {
            'macro_overrides': macro_overrides,
            'shirt_offset_m': fabric['shirt_offset_m'],
            'trousers_offset_m': fabric['trousers_offset_m'],
            'bump_strength': fabric['bump_strength'],
            'normal_strength': fabric['normal_strength'],
        },""",
    'manifest-assets')

# 10. Descriptor header license parser.
replace("""def sha256_file(path):""",
"""def parse_descriptor_license(path, key):
    try:
        with Path(path).open('r', encoding='utf-8', errors='replace') as handle:
            for line in handle:
                stripped = line.strip().lstrip('#').strip()
                if stripped.lower().startswith(key):
                    value = stripped[len(key):].lstrip(':').strip()
                    if value:
                        return value
    except OSError:
        return 'unknown'
    return 'unknown'


def sha256_file(path):""",
    'license-parser')

# 11. generated_files completeness.
replace("""        'generated_files': [
            str(blend_path), str(fbx_path), str(glb_path),
            str(output_dir / 'front.png'), str(output_dir / 'three_quarter.png'),
        ],""",
"""        'generated_files': [str(blend_path), str(fbx_path), str(glb_path)]
            + [str(output_dir / name) for name in (
                'front.png', 'three_quarter.png', 'side.png',
                'oblique_front_left_armraised.png', 'oblique_front_right_armraised.png',
                'eye_level_chest_waist.png', 'feet_oblique_side.png', 'views_metadata.json')],""",
    'generated-files')

TARGET.write_text(src, encoding='utf-8')
print('APPLIED PATCHES:')
for label in applied:
    print(' -', label)
