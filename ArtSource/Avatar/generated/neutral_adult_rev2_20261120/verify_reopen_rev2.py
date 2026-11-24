"""Fresh-process re-open checks for the three exported avatar formats.

Runs three separate Blender processes (one per format) and writes one JSON
report per format into reopen_results/. Every check records the file hash it
opened, the scene structure it saw, and material/image inspection results.
"""
import bpy
import hashlib
import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT_DIR = HERE / 'reopen_results'
OUT_DIR.mkdir(exist_ok=True)
TARGET = sys.argv[sys.argv.index('--') + 1]
FORMAT_TAG = sys.argv[sys.argv.index('--') + 2]


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


report = {'format': FORMAT_TAG, 'file': TARGET, 'file_sha256': sha256_file(TARGET)}
try:
    if FORMAT_TAG == 'blend':
        bpy.ops.wm.open_mainfile(filepath=TARGET)
        objects = bpy.data.objects
        report['object_count'] = len(objects)
        report['mesh_objects'] = sorted(o.name for o in objects if o.type == 'MESH')
        report['armatures'] = sorted(o.name for o in objects if o.type == 'ARMATURE')
        report['armature_bone_count'] = sum(len(o.data.bones) for o in objects if o.type == 'ARMATURE')
        report['material_count'] = len(bpy.data.materials)
        report['image_count'] = len(bpy.data.images)
        missing_images = []
        for image in bpy.data.images:
            if image.source == 'FILE' and not image.packed_file:
                path = bpy.path.abspath(image.filepath)
                if not os.path.exists(path):
                    missing_images.append(image.name)
        report['missing_unpacked_images'] = missing_images
        report['images_with_pixels'] = sum(
            1 for image in bpy.data.images if image.has_data)
    else:  # FBX or GLB import into an empty scene
        bpy.ops.wm.read_factory_settings(use_empty=True)
        if FORMAT_TAG == 'fbx':
            bpy.ops.import_scene.fbx(filepath=TARGET)
        else:
            bpy.ops.import_scene.gltf(filepath=TARGET)
        objects = bpy.data.objects
        report['object_count'] = len(objects)
        report['mesh_objects'] = sorted(o.name for o in objects if o.type == 'MESH')
        report['armatures'] = sorted(o.name for o in objects if o.type == 'ARMATURE')
        report['armature_bone_count'] = sum(len(o.data.bones) for o in objects if o.type == 'ARMATURE')
        report['material_count'] = len(bpy.data.materials)
        report['image_count'] = len(bpy.data.images)
        report['images_with_pixels'] = sum(1 for image in bpy.data.images if image.has_data)
        report['vertex_total'] = sum(len(o.data.vertices) for o in objects if o.type == 'MESH')
    report['status'] = 'OK'
except Exception as error:  # noqa: BLE001 - report any failure verbatim
    report['status'] = 'FAIL'
    report['error'] = repr(error)

(OUT_DIR / f'reopen_{FORMAT_TAG}.json').write_text(
    json.dumps(report, indent=2), encoding='utf-8')
print(f'REOPEN_{FORMAT_TAG.upper()} ' + report['status'])
