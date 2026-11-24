#!/usr/bin/env python3
"""Build a clothed, rigged prototype avatar from the local MPFB and CC0 asset packs.

Run with Blender 5.2 in background mode. This script never downloads assets and writes
outputs only below ArtSource/Avatar in this Game04 project.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib
import importlib.util
import json
import os
import sys
import traceback
from datetime import datetime, timezone
from pathlib import Path
from types import ModuleType

import bpy

PROJECT_ROOT = Path(__file__).resolve().parents[2]
AVATAR_ROOT = PROJECT_ROOT / 'ArtSource' / 'Avatar'
RESOURCE_ROOT = PROJECT_ROOT / 'ArtSource' / '_download' / 'avatar-resources'
DEFAULT_MPFB_SRC = RESOURCE_ROOT / 'mpfb2_v2.0.18' / 'mpfb2-2.0.18' / 'src'
DEFAULT_OUTPUT = AVATAR_ROOT / 'generated' / 'neutral_adult'
PACK_NAMES = {'makehuman_system_assets_cc0', 'skins02_cc0'}
REQUIRED_ROLES = ('skin', 'eyes', 'eyebrows', 'hair', 'shirt', 'trousers', 'shoes')
ROLE_PACK = {'skin': 'skins02_cc0'}
ROLE_SUFFIX = {'skin': '.mhmat'}
ROLE_CLI = {
    'skin': '--skin', 'eyes': '--eyes', 'eyebrows': '--eyebrows', 'hair': '--hair',
    'shirt': '--shirt', 'trousers': '--trousers', 'shoes': '--shoes',
}
PREFERRED_NAMES = {
    'skin': ('young_caucasian_female.mhmat', 'young_caucasian_male.mhmat',
             'young_asian_female.mhmat', 'young_asian_male.mhmat',
             'young_african_female.mhmat', 'young_african_male.mhmat'),
    'eyes': ('low-poly.mhclo', 'high-poly.mhclo'),
    'eyebrows': ('eyebrow001.mhclo', 'eyebrow01.mhclo'),
    'hair': ('hair01.mhclo', 'hair001.mhclo', 'short01.mhclo', 'long01.mhclo'),
    'shirt': ('shirt01.mhclo', 'shirt.mhclo', 'tshirt01.mhclo', 't-shirt01.mhclo'),
    'trousers': ('trousers01.mhclo', 'trousers.mhclo', 'jeans01.mhclo', 'pants01.mhclo'),
    'shoes': ('shoes01.mhclo', 'shoe01.mhclo', 'shoes.mhclo', 'boots01.mhclo'),
}


class MissingAssetsError(RuntimeError):
    pass


def parse_args():
    parser = argparse.ArgumentParser(description='Build the Game04 MPFB avatar.')
    parser.add_argument('--mpfb-src', type=Path, default=DEFAULT_MPFB_SRC,
                        help='Existing MPFB source directory; no download is performed.')
    parser.add_argument('--assets-root', type=Path, action='append', dest='assets_roots',
                        help='Search root containing the extracted CC0 packs; may be repeated.')
    parser.add_argument('--output-dir', type=Path, default=DEFAULT_OUTPUT,
                        help='Output directory, restricted to ArtSource/Avatar.')
    for role in REQUIRED_ROLES:
        parser.add_argument(ROLE_CLI[role], type=Path, dest=role,
                            help='Explicit asset file for ' + role + '.')
    args_list = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    return parser.parse_args(args_list)


def locate_pack_roots(search_roots):
    found = set()
    for search_root in search_roots:
        if not search_root.is_dir():
            continue
        if search_root.name in PACK_NAMES:
            found.add(search_root.resolve())
            continue
        for current, directories, _files in os.walk(search_root):
            current_path = Path(current)
            if current_path.name in PACK_NAMES:
                found.add(current_path.resolve())
                directories[:] = []
    return sorted(found, key=lambda item: item.as_posix().lower())


def is_candidate(role, path, pack_root):
    relative = path.relative_to(pack_root).as_posix().lower()
    if role == 'skin':
        return path.suffix.lower() == '.mhmat'
    if path.suffix.lower() != '.mhclo':
        return False
    if role == 'hair':
        return 'hair' in relative and not any(term in relative for term in ('eyebrow', 'brow', 'eyelash'))
    if role == 'eyebrows':
        return 'eyebrow' in relative or 'brow' in relative
    if role == 'eyes':
        return 'eye' in relative and not any(term in relative for term in ('eyebrow', 'eyelash'))
    if role == 'shirt':
        return any(term in relative for term in ('shirt', 't-shirt', 'tshirt', 'blouse', 'tank_top'))
    if role == 'trousers':
        return any(term in relative for term in ('trouser', 'pants', 'jean', 'legging'))
    if role == 'shoes':
        return any(term in relative for term in ('shoe', 'sneaker', 'boot', 'footwear', 'sandal'))
    return False


def preferred_sort_key(role, path):
    preferred = PREFERRED_NAMES.get(role, ())
    name = path.name.lower()
    try:
        rank = preferred.index(name)
    except ValueError:
        rank = len(preferred)
    return rank, path.as_posix().lower()


def discover_candidate(role, pack_roots):
    wanted_pack = ROLE_PACK.get(role, 'makehuman_system_assets_cc0')
    suffix = ROLE_SUFFIX.get(role, '.mhclo')
    candidates = []
    for root in pack_roots:
        if root.name != wanted_pack:
            continue
        for path in root.rglob('*'):
            if path.is_file() and path.suffix.lower() == suffix and is_candidate(role, path, root):
                candidates.append(path.resolve())
    return sorted(set(candidates), key=lambda item: preferred_sort_key(role, item))


def resolve_assets(args, pack_roots):
    selected = {}
    missing = []
    for role in REQUIRED_ROLES:
        explicit = getattr(args, role)
        if explicit is not None:
            path = explicit.expanduser().resolve()
            expected_suffix = ROLE_SUFFIX.get(role, '.mhclo')
            if not path.is_file():
                missing.append(f'{role}: explicit file does not exist: {path}')
                continue
            if path.suffix.lower() != expected_suffix:
                missing.append(f'{role}: expected a {expected_suffix} file, got: {path}')
                continue
            selected[role] = {'path': path, 'method': 'explicit CLI path'}
            continue
        candidates = discover_candidate(role, pack_roots)
        if candidates:
            selected[role] = {'path': candidates[0], 'method': 'deterministic CC0 pack discovery'}
            continue
        pack = ROLE_PACK.get(role, 'makehuman_system_assets_cc0')
        missing.append(
            f'{role}: no matching {ROLE_SUFFIX.get(role, ".mhclo")} found under {pack}; '
            f'pass {ROLE_CLI[role]} FILE or add the extracted pack with --assets-root'
        )
    if missing:
        details = '\n'.join('  - ' + item for item in missing)
        raise MissingAssetsError(
            'Required avatar assets are missing or invalid. No primitive substitutes are used.\n'
            + details
        )
    return selected


def pack_for_path(path, pack_roots):
    for root in pack_roots:
        try:
            path.relative_to(root)
            return root
        except ValueError:
            pass
    return None


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open('rb') as source_file:
        for block in iter(lambda: source_file.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def asset_provenance(selected, pack_roots):
    result = {}
    for role, entry in selected.items():
        path = entry['path']
        pack_root = pack_for_path(path, pack_roots)
        if pack_root is not None:
            license_name = 'CC0, as distributed with the named source pack'
            pack_name = pack_root.name
            source_relative = path.relative_to(pack_root).as_posix()
        else:
            license_name = 'Not inferred for this explicit external path'
            pack_name = 'explicit external path'
            source_relative = path.name
        result[role] = {
            'file': str(path),
            'source_pack': pack_name,
            'source_relative_path': source_relative,
            'selection_method': entry['method'],
            'license': license_name,
            'descriptor_sha256': sha256_file(path),
        }
    return result


def bootstrap_mpfb(source_dir, local_runtime_dir):
    source_dir = source_dir.expanduser().resolve()
    init_file = source_dir / 'mpfb' / '__init__.py'
    if not init_file.is_file():
        raise RuntimeError(
            f'MPFB source was not found at {source_dir}. Expected mpfb/__init__.py. '
            'Use --mpfb-src to point to the existing extracted MPFB 2.0.18 source.'
        )

    # Match the supplied smoke-script bootstrap while keeping extension data in this prototype.
    # Preferences are not saved, and the temporary path redirect is restored immediately.
    bpy.context.preferences.use_preferences_save = False
    local_runtime_dir.mkdir(parents=True, exist_ok=True)
    original_extension_path_user = bpy.utils.extension_path_user

    def project_local_extension_path(package_id, *args, **kwargs):
        if 'mpfb' in str(package_id).lower():
            return str(local_runtime_dir)
        return original_extension_path_user(package_id, *args, **kwargs)

    bpy.utils.extension_path_user = project_local_extension_path
    try:
        if 'bl_ext' not in sys.modules:
            package = ModuleType('bl_ext')
            package.__path__ = []
            sys.modules['bl_ext'] = package
        if 'bl_ext.neuralife' not in sys.modules:
            package = ModuleType('bl_ext.neuralife')
            package.__path__ = [str(source_dir)]
            sys.modules['bl_ext.neuralife'] = package
        else:
            package = sys.modules['bl_ext.neuralife']
            package.__path__ = [str(source_dir)]

        module_name = 'bl_ext.neuralife.mpfb'
        if module_name in sys.modules:
            mpfb = sys.modules[module_name]
        else:
            spec = importlib.util.spec_from_file_location(
                module_name,
                init_file,
                submodule_search_locations=[str(source_dir / 'mpfb')],
            )
            if spec is None or spec.loader is None:
                raise RuntimeError(f'Could not load MPFB package from {init_file}')
            mpfb = importlib.util.module_from_spec(spec)
            sys.modules[module_name] = mpfb
            spec.loader.exec_module(mpfb)

        addon = bpy.context.preferences.addons.get(module_name)
        if addon is None:
            addon = bpy.context.preferences.addons.new()
            addon.module = module_name
        mpfb.register()
    finally:
        bpy.utils.extension_path_user = original_extension_path_user

    human_service = importlib.import_module(module_name + '.services.humanservice').HumanService
    target_service = importlib.import_module(module_name + '.services.targetservice').TargetService
    return human_service, target_service


def clear_scene():
    if bpy.context.object is not None and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def create_human_and_assets(human_service, target_service, selected):
    macro = target_service.get_default_macro_info_dict()
    # Adult, average build, neutral gender macro values where MPFB exposes the fields.
    for key in ('gender', 'age', 'weight', 'muscle', 'height'):
        if key in macro:
            macro[key] = 0.5
    basemesh = human_service.create_human(
        scale=0.1,
        feet_on_ground=True,
        macro_detail_dict=macro,
    )
    human_service.set_character_skin(
        str(selected['skin']['path']),
        basemesh,
        skin_type='ENHANCED_SSS',
        material_instances=True,
    )
    rig = human_service.add_builtin_rig(basemesh, 'game_engine', import_weights=True)
    if rig is None or rig.type != 'ARMATURE':
        raise RuntimeError('MPFB did not create the requested game_engine deformation rig.')

    asset_objects = {}
    type_names = {
        'eyes': 'Eyes', 'eyebrows': 'Eyebrows', 'hair': 'Hair',
        'shirt': 'Clothes', 'trousers': 'Clothes', 'shoes': 'Clothes',
    }
    for role in ('eyes', 'eyebrows', 'hair', 'shirt', 'trousers', 'shoes'):
        material_type = 'PROCEDURAL_EYES' if role == 'eyes' else 'MAKESKIN'
        try:
            asset_object = human_service.add_mhclo_asset(
                str(selected[role]['path']),
                basemesh,
                asset_type=type_names[role],
                subdiv_levels=1,
                material_type=material_type,
                set_up_rigging=True,
                interpolate_weights=True,
            )
        except Exception as error:
            raise RuntimeError(f'MPFB failed to load required {role} asset {selected[role]["path"]}: {error}') from error
        if asset_object is None or asset_object.type != 'MESH':
            raise RuntimeError(f'MPFB did not produce a mesh for required {role} asset: {selected[role]["path"]}')
        asset_objects[role] = asset_object

    # Clothes are loaded through MPFB, which applies their own delete-group masks to the body.
    # The script does not remove or rewrite arbitrary body triangles.
    for role in ('shirt', 'trousers', 'shoes'):
        if role not in asset_objects:
            raise RuntimeError(f'Required clothing item {role} was not equipped; export is blocked.')
    return basemesh, rig, asset_objects, macro


def parent_avatar_under_root():
    scene = bpy.context.scene
    root = bpy.data.objects.new('AvatarRoot_1m70', None)
    scene.collection.objects.link(root)
    root.empty_display_type = 'PLAIN_AXES'
    root.empty_display_size = 0.2
    root.location = (0.0, 0.0, 0.0)
    for obj in tuple(scene.objects):
        if obj is root or obj.parent is not None:
            continue
        world_matrix = obj.matrix_world.copy()
        obj.parent = root
        obj.matrix_world = world_matrix
    return root


def mesh_bounds():
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or obj.hide_render:
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            for vertex in mesh.vertices:
                point = evaluated.matrix_world @ vertex.co
                points.append((point.x, point.y, point.z))
        finally:
            evaluated.to_mesh_clear()
    if not points:
        raise RuntimeError('The assembled avatar has no visible mesh vertices.')
    return {
        'min': [min(point[index] for point in points) for index in range(3)],
        'max': [max(point[index] for point in points) for index in range(3)],
    }


def uniform_scale_avatar(root, target_height=1.70):
    bpy.context.view_layer.update()
    initial_bounds = mesh_bounds()
    initial_height = initial_bounds['max'][2] - initial_bounds['min'][2]
    if initial_height <= 0.0:
        raise RuntimeError(f'Avatar height is invalid: {initial_height}')
    factor = target_height / initial_height
    root.scale = (factor, factor, factor)
    root.location.z = -initial_bounds['min'][2] * factor
    bpy.context.view_layer.update()
    final_bounds = mesh_bounds()
    final_height = final_bounds['max'][2] - final_bounds['min'][2]
    if abs(final_height - target_height) > 0.001:
        raise RuntimeError(f'Uniform avatar scaling produced {final_height:.4f} m, expected {target_height:.2f} m.')
    return {
        'target_height_m': target_height,
        'initial_height_m': initial_height,
        'uniform_scale_factor': factor,
        'final_height_m': final_height,
        'final_bounds_m': final_bounds,
    }


def create_lighting_and_camera(bounds):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1500
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.film_transparent = False
    if hasattr(scene, 'eevee') and hasattr(scene.eevee, 'taa_render_samples'):
        scene.eevee.taa_render_samples = 64
    try:
        scene.view_settings.view_transform = 'AgX'
    except TypeError:
        pass

    world = bpy.data.worlds.new('AvatarStudioWorld')
    world.use_nodes = True
    background = world.node_tree.nodes.get('Background')
    if background is not None:
        background.inputs['Color'].default_value = (0.22, 0.24, 0.28, 1.0)
        background.inputs['Strength'].default_value = 0.8
    scene.world = world

    def add_area_light(name, location, energy, size):
        light_data = bpy.data.lights.new(name, type='AREA')
        light_data.energy = energy
        light_data.shape = 'DISK'
        light_data.size = size
        light_obj = bpy.data.objects.new(name, light_data)
        scene.collection.objects.link(light_obj)
        light_obj.location = location
        target = ((bounds['min'][0] + bounds['max'][0]) * 0.5,
                  (bounds['min'][1] + bounds['max'][1]) * 0.5,
                  (bounds['min'][2] + bounds['max'][2]) * 0.5)
        direction = bpy.mathutils.Vector(target) - light_obj.location
        light_obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()

    center = (
        (bounds['min'][0] + bounds['max'][0]) * 0.5,
        (bounds['min'][1] + bounds['max'][1]) * 0.5,
        (bounds['min'][2] + bounds['max'][2]) * 0.5,
    )
    add_area_light('AvatarKey', (center[0] - 2.8, center[1] - 3.0, center[2] + 2.8), 520.0, 3.0)
    add_area_light('AvatarFill', (center[0] + 3.0, center[1] - 1.5, center[2] + 1.6), 240.0, 3.5)
    add_area_light('AvatarRim', (center[0] + 0.5, center[1] + 2.5, center[2] + 2.5), 420.0, 2.5)

    camera_data = bpy.data.cameras.new('AvatarRenderCamera')
    camera = bpy.data.objects.new('AvatarRenderCamera', camera_data)
    scene.collection.objects.link(camera)
    camera_data.lens = 50.0
    camera_data.sensor_fit = 'VERTICAL'
    camera_data.dof.use_dof = False
    scene.camera = camera
    return camera, center


def render_views(output_dir, camera, center):
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
        direction = bpy.mathutils.Vector(center) - camera.location
        camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(output_dir / filename)
        bpy.ops.render.render(write_still=True)


def select_avatar_objects(objects, active_object):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        if obj.name in bpy.context.view_layer.objects:
            obj.select_set(True)
    bpy.context.view_layer.objects.active = active_object


def export_avatar(output_dir, avatar_objects, rig):
    if not hasattr(bpy.ops.export_scene, 'fbx'):
        raise RuntimeError('Blender FBX exporter is unavailable in this Blender installation.')
    if not hasattr(bpy.ops.export_scene, 'gltf'):
        raise RuntimeError('Blender glTF exporter is unavailable in this Blender installation.')

    fbx_path = output_dir / 'neutral_adult_avatar.fbx'
    select_avatar_objects(avatar_objects, rig)
    bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        use_selection=True,
        object_types={'MESH', 'ARMATURE', 'EMPTY'},
        use_mesh_modifiers=True,
        use_mesh_modifiers_render=True,
        use_armature_deform_only=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode='COPY',
        embed_textures=True,
    )

    glb_path = output_dir / 'neutral_adult_avatar.glb'
    select_avatar_objects(avatar_objects, rig)
    bpy.ops.export_scene.gltf(
        filepath=str(glb_path),
        export_format='GLB',
        use_selection=True,
        export_skins=True,
        export_animations=False,
        export_apply=False,
    )
    return fbx_path, glb_path


def collect_mesh_counts(objects):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    counts = {}
    source_total = 0
    evaluated_total = 0
    for obj in objects:
        if obj.type != 'MESH':
            continue
        source_count = len(obj.data.vertices)
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            evaluated_count = len(mesh.vertices)
            face_count = len(mesh.polygons)
        finally:
            evaluated.to_mesh_clear()
        counts[obj.name] = {
            'source_vertices': source_count,
            'evaluated_vertices': evaluated_count,
            'evaluated_faces': face_count,
        }
        source_total += source_count
        evaluated_total += evaluated_count
    return {
        'by_mesh_object': counts,
        'total_source_vertices': source_total,
        'total_evaluated_vertices': evaluated_total,
    }


def find_mpfb_license(source_dir):
    package_root = source_dir.parent
    for name in ('LICENSE', 'LICENSE.txt', 'COPYING'):
        candidate = package_root / name
        if candidate.is_file():
            return str(candidate)
    return None


def main():
    args = parse_args()
    if tuple(bpy.app.version[:2]) < (5, 2):
        raise RuntimeError(f'Blender 5.2 or newer is required; found {bpy.app.version_string}.')

    output_dir = args.output_dir.expanduser().resolve()
    avatar_root = AVATAR_ROOT.resolve()
    try:
        output_dir.relative_to(avatar_root)
    except ValueError as error:
        raise RuntimeError(f'Output must stay under {avatar_root}; refusing to write to {output_dir}.') from error

    search_roots = [root.expanduser().resolve() for root in (args.assets_roots or [RESOURCE_ROOT])]
    pack_roots = locate_pack_roots(search_roots)
    selected = resolve_assets(args, pack_roots)
    provenance = asset_provenance(selected, pack_roots)

    output_dir.mkdir(parents=True, exist_ok=True)
    mpfb_src = args.mpfb_src.expanduser().resolve()
    human_service, target_service = bootstrap_mpfb(mpfb_src, AVATAR_ROOT / '.mpfb-runtime')
    clear_scene()
    basemesh, rig, asset_objects, macro = create_human_and_assets(human_service, target_service, selected)

    root = parent_avatar_under_root()
    scale_record = uniform_scale_avatar(root, 1.70)
    avatar_objects = [obj for obj in bpy.context.scene.objects if obj not in ()]
    # Capture the character before adding render-only cameras and lights.
    avatar_objects = [obj for obj in tuple(bpy.context.scene.objects) if obj not in ()]
    camera, center = create_lighting_and_camera(scale_record['final_bounds_m'])
    render_views(output_dir, camera, center)
    fbx_path, glb_path = export_avatar(output_dir, avatar_objects, rig)

    blend_path = output_dir / 'neutral_adult_avatar.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    manifest_path = output_dir / 'avatar_manifest.json'
    bone_names = sorted(bone.name for bone in rig.data.bones)
    mesh_counts = collect_mesh_counts(avatar_objects)
    manifest = {
        'created_utc': datetime.now(timezone.utc).isoformat(),
        'project': 'Game04-VirtualHand prototype',
        'blender_version': bpy.app.version_string,
        'mpfb': {
            'version': '2.0.18',
            'source_directory': str(mpfb_src),
            'license': 'GPL; source package provenance retained',
            'license_file': find_mpfb_license(mpfb_src),
            'bootstrap': 'Local source imported using the supplied smoke-script extension-module approach.',
        },
        'assets': provenance,
        'body': {
            'macro_values': {key: float(value) for key, value in macro.items()
                             if isinstance(value, (int, float))},
            'rig': 'game_engine',
            'rig_object': rig.name,
            'uniform_scale': scale_record,
        },
        'equipped_objects': {role: obj.name for role, obj in asset_objects.items()},
        'bone_names': bone_names,
        'mesh_vertex_counts': mesh_counts,
        'generated_files': [
            str(blend_path), str(fbx_path), str(glb_path),
            str(output_dir / 'front.png'), str(output_dir / 'three_quarter.png'),
        ],
        'material_note': 'Blender shader nodes remain in the authoring scene. Adapt materials for Unity Built-in Render Pipeline after import.',
        'fbx_note': 'Exports the game_engine deformation rig and skin weights without leaf bones or animation clips.',
    }
    with manifest_path.open('w', encoding='utf-8') as manifest_file:
        json.dump(manifest, manifest_file, indent=2, ensure_ascii=False)
        manifest_file.write('\n')
    print(f'Avatar outputs written to {output_dir}', flush=True)
    print(f'Manifest: {manifest_path}', flush=True)


if __name__ == '__main__':
    try:
        main()
    except MissingAssetsError as error:
        print(str(error), file=sys.stderr, flush=True)
        raise SystemExit(2)
    except Exception:
        traceback.print_exc()
        raise
