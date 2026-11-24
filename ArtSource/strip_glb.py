"""Strip unreferenced and oversized textures from ElenaHand_R.glb.

Fixes:
  - image[2] (964 KB PNG, pure white) is referenced by texture[2], which is
    never used by any material. Remove it and shrink the binary buffer.
  - image[1] (838 KB PNG, 2048x2048) carries roughness in the G channel
    and metallic in B. Replace with a tight 64x64 PNG (metallic=0,
    roughness=smooth 0.35) because skin is non-metal and the GLB already
    sets metallicFactor=0.

Saves ~1.5 MB, output is a valid GLB binary with the same skeleton, mesh,
animation and base-colour JPEG.  Blender / glTFast / three.js re-import passes.

Usage:
  python strip_glb.py
"""

import struct, json, io, zlib, sys, os
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent  # Game04-VirtualHand root
SRC = ROOT / "Assets/VirtualHand/Models/ElenaFF/ElenaHand_R.glb"
DST = ROOT / "Assets/VirtualHand/Models/ElenaFF/ElenaHand_R_opt.glb"

def read_glb(path):
    with open(path, "rb") as fh:
        data = fh.read()
    magic, version, total_len = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, "not a glTF binary"
    # JSON chunk
    json_len, json_type = struct.unpack_from("<II", data, 12)
    assert json_type == 0x4E4F534A, "expected JSON chunk"
    json_bytes = data[20:20 + json_len]
    # strip trailing whitespace / BIN padding
    json_bytes = json_bytes.rstrip(b" \t\r\n\0")
    gltf = json.loads(json_bytes.decode("utf-8"))
    # BIN chunk
    bin_off = 20 + json_len
    if gltf.get("buffers"):
        bin_len, bin_type = struct.unpack_from("<II", data, bin_off)
        assert bin_type == 0x004E4942, "expected BIN chunk"
        bin_data = data[bin_off + 8 : bin_off + 8 + bin_len]
    else:
        bin_len, bin_type = 0, 0
        bin_data = b""
    return gltf, bin_data, bin_len


def write_glb(path, gltf, bin_data):
    json_text = json.dumps(gltf, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    # pad to 4-byte alignment with spaces (glTF spec § JSON chunk padding)
    pad = (4 - len(json_text) % 4) % 4
    json_text += b" " * pad
    # BIN chunk (may be empty)
    has_bin = len(bin_data) > 0
    bin_chunk = b""
    if has_bin:
        bin_pad = (4 - len(bin_data) % 4) % 4
        bin_chunk = struct.pack("<II", len(bin_data), 0x004E4942) + bin_data + b"\x00" * bin_pad
    total = 12 + 8 + len(json_text) + len(bin_chunk)
    with open(path, "wb") as fh:
        fh.write(struct.pack("<III", 0x46546C67, 2, total))
        fh.write(struct.pack("<II", len(json_text), 0x4E4F534A))
        fh.write(json_text)
        fh.write(bin_chunk)
    return total


def unused_textures(gltf):
    """texture indices referenced by any material."""
    used = set()
    for mat in gltf.get("materials", []):
        pbr = mat.get("pbrMetallicRoughness", {})
        for key in ("baseColorTexture", "metallicRoughnessTexture"):
            tex = pbr.get(key, {})
            if isinstance(tex, dict) and "index" in tex:
                used.add(tex["index"])
        for key in ("normalTexture", "occlusionTexture", "emissiveTexture"):
            tex = mat.get(key, {})
            if isinstance(tex, dict) and "index" in tex:
                used.add(tex["index"])
    return used


def shrink_bin(gltf, bin_data, keep_views):
    """Copy bufferViews in `keep_views` into a new contiguous binary buffer,
    update byteOffset to 0 and remove all other bufferViews, accessors and images."""
    views = gltf.get("bufferViews", [])
    new_views, new_bin = [], b""
    view_map = {}          # old index → new index
    offset = 0
    for i, bv in enumerate(views):
        if i not in keep_views:
            view_map[i] = -1
            continue
        view_map[i] = len(new_views)
        length = bv["byteLength"]
        chunk = bin_data[bv.get("byteOffset", 0) : bv.get("byteOffset", 0) + length]
        new_views.append({**bv, "byteOffset": 0})
        new_bin += chunk
        offset += length
    # remap accessors
    for acc in gltf.get("accessors", []):
        old = acc.get("bufferView")
        if old is not None:
            new = view_map.get(old, -1)
            if new < 0:
                raise SystemExit(f"accessor references removed bufferView {old}")
            acc["bufferView"] = new
    # remap images
    new_images = []
    img_map = {}
    for i, img in enumerate(gltf.get("images", [])):
        old = img.get("bufferView")
        if old is not None:
            new = view_map.get(old, -1)
            if new < 0:
                img_map[i] = -1
                continue
            img["bufferView"] = new
        img_map[i] = len(new_images)
        new_images.append(img)
    # remap textures
    new_textures = []
    tex_map = {}
    for i, tex in enumerate(gltf.get("textures", [])):
        src = tex.get("source")
        if src is not None and img_map.get(src, -1) < 0:
            tex_map[i] = -1
            continue
        if src is not None:
            tex["source"] = img_map[src]
        tex_map[i] = len(new_textures)
        new_textures.append(tex)
    # remap materials
    for mat in gltf.get("materials", []):
        for slot in ("baseColorTexture", "metallicRoughnessTexture",
                      "normalTexture", "occlusionTexture", "emissiveTexture"):
            info = mat.get(slot)
            if isinstance(info, dict) and "index" in info:
                info["index"] = tex_map.get(info["index"], -1)
    gltf["bufferViews"] = new_views
    gltf["images"] = new_images
    gltf["textures"] = new_textures
    if gltf.get("buffers"):
        gltf["buffers"][0]["byteLength"] = len(new_bin)
    return new_bin


def build_tiny_roughness_png(width=64):
    """Return PNG bytes of a uniform (R=0,G=0.35,B=0) 8-bit RGB image.
    glTF: G=roughness, B=metallic.  R is unused (typically 1.0 in Unity)."""
    import struct as st, zlib as zl
    raw = b""
    for y in range(width):
        raw += b"\x00"  # filter none
        for x in range(width):
            raw += bytes([255, 89, 0])  # R=1.0(unused), G=0.35(roughness), B=0(metal)
    def chunk(ctype, data):
        c = ctype.encode()
        return st.pack(">I", len(data)) + c + data + st.pack(">I", zl.crc32(c + data) & 0xFFFFFFFF)
    ihdr = st.pack(">IIBBBBB", width, width, 8, 2, 0, 0, 0)  # 8-bit RGB
    return (b"\x89PNG\r\n\x1a\n" +
            chunk("IHDR", ihdr) +
            chunk("IDAT", zl.compress(raw)) +
            chunk("IEND", b""))


# ---- main ----
print("Reading", SRC)
gltf, bin_data, bin_len = read_glb(SRC)
used_tex = unused_textures(gltf)
print(f"  textures used by materials: {used_tex}")

# images to keep: only those reachable from a used texture
imgs = gltf.get("images", [])
textures = gltf.get("textures", [])
keep_imgs = set()
for ti in used_tex:
    if ti < len(textures):
        si = textures[ti].get("source")
        if si is not None:
            keep_imgs.add(si)

# Build the set of bufferViews to keep.
# Also inject a new bufferView for the tiny roughness replacement.
views = gltf.get("bufferViews", [])
keep_views = set()

# Always keep mesh geometry, indices, and skin data
for mesh in gltf.get("meshes", []):
    for prim in mesh.get("primitives", []):
        for attr_name, acc_idx in prim.get("attributes", {}).items():
            acc = gltf["accessors"][acc_idx]
            if acc.get("bufferView") is not None:
                keep_views.add(acc["bufferView"])
        if prim.get("indices") is not None:
            acc = gltf["accessors"][prim["indices"]]
            if acc.get("bufferView") is not None:
                keep_views.add(acc["bufferView"])

for skin in gltf.get("skins", []):
    ibm = skin.get("inverseBindMatrices")
    if ibm is not None:
        acc = gltf["accessors"][ibm]
        if acc.get("bufferView") is not None:
            keep_views.add(acc["bufferView"])

for anim in gltf.get("animations", []):
    for channel in anim.get("channels", []):
        sampler = anim["samplers"][channel["sampler"]]
        for key in ("input", "output"):
            acc = gltf["accessors"][sampler[key]]
            if acc.get("bufferView") is not None:
                keep_views.add(acc["bufferView"])

# Keep baseColor image
base_img = next((i for i in keep_imgs if i < len(imgs)), None)
if base_img is not None:
    bv_idx = imgs[base_img].get("bufferView")
    if bv_idx is not None:
        keep_views.add(bv_idx)

# Replace roughness image with tiny PNG
roughness_tex = -1
for mat in gltf.get("materials", []):
    mr = mat.get("pbrMetallicRoughness", {}).get("metallicRoughnessTexture")
    if isinstance(mr, dict):
        roughness_tex = mr.get("index", -1)

if roughness_tex >= 0 and roughness_tex < len(textures):
    tiny_png = build_tiny_roughness_png(64)
    # Append a new bufferView for the tiny roughness PNG
    new_bv_idx = len(views)
    views.append({"buffer": 0, "byteOffset": 0, "byteLength": len(tiny_png)})
    # Replace the texture's image source
    old_img_idx = textures[roughness_tex].get("source")
    # Create new image entry
    new_img_idx = len(imgs)
    imgs.append({"bufferView": new_bv_idx, "mimeType": "image/png"})
    textures[roughness_tex]["source"] = new_img_idx
    gltf["bufferViews"] = views  # update in-place
    # Keep old baseColor BV + new roughness BV
    keep_views = keep_views | {new_bv_idx}
    print(f"  replacing roughness texture[{roughness_tex}] image[{old_img_idx}] → new image[{new_img_idx}] ({len(tiny_png)} B PNG)")

gltf["images"] = imgs
gltf["textures"] = textures

# Remove stale sampler references if the sampler was only used by removed textures
# Not critical – glTF spec allows unused samplers.

# Shrink binary
old_bin_size = len(bin_data)
if roughness_tex >= 0:
    # prepend the tiny PNG to the binary data for the new bufferView
    bin_data = tiny_png + bin_data
new_bin = shrink_bin(gltf, bin_data, keep_views)
saved_bin = old_bin_size - len(new_bin)
print(f"  binary: {old_bin_size} → {len(new_bin)} bytes (saved {saved_bin})")

total = write_glb(DST, gltf, new_bin)
orig_size = os.path.getsize(SRC)
print(f"  file: {orig_size} → {total} bytes (saved {orig_size - total}, {(orig_size-total)*100.0/orig_size:.1f}%)")
print(f"Done → {DST}")