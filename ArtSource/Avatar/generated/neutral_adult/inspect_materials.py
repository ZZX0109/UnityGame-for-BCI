# Phase-3 isolated material inspection: open the built avatar .blend and dump
# runtime evidence for the bronze skin and cortu pants materials.
# Run: blender.exe -b <blend> -P inspect_materials.py
import bpy
import json

OUT = "material_inspection.json"
report = {"blender": bpy.app.version_string, "materials": {}, "images": {}, "missing_images": []}

for img in bpy.data.images:
    entry = {"filepath": img.filepath, "size": list(img.size), "packed": img.packed_file is not None}
    if img.size[0] > 0:
        entry["decoded_pixels"] = img.size[0] * img.size[1]
    else:
        entry["decoded_pixels"] = 0
        if img.filepath and not img.packed_file:
            report["missing_images"].append(img.filepath)
    report["images"][img.name] = entry

for mat in bpy.data.materials:
    entry = {"users": mat.users, "nodes": [], "principled": {}}
    if mat.use_nodes:
        for n in mat.node_tree.nodes:
            node_info = {"type": n.type, "name": n.name}
            if n.type == 'BSDF_PRINCIPLED':
                for sock in ("Roughness", "Specular IOR Level", "Metallic", "Alpha"):
                    if sock in n.inputs:
                        try:
                            entry["principled"][sock] = round(n.inputs[sock].default_value, 4)
                        except (TypeError, ValueError):
                            pass
            if n.type in ('TEX_IMAGE', 'NORMAL_MAP', 'BUMP'):
                if n.type == 'TEX_IMAGE' and n.image:
                    node_info["image"] = n.image.name
                if n.type == 'BUMP' and "Strength" in n.inputs:
                    node_info["strength"] = round(n.inputs["Strength"].default_value, 4)
                entry["nodes"].append(node_info)
            else:
                entry["nodes"].append(node_info)
    else:
        entry["nodes"] = "use_nodes=False"
    report["materials"][mat.name] = entry

with open(OUT, "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=2)
print("PHASE3_REPORT_WRITTEN", OUT, "materials=", len(report["materials"]),
      "images=", len(report["images"]), "missing=", len(report["missing_images"]))
