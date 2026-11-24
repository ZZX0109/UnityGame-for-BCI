"""Generate refined seamless street-surface albedo textures using only the Python stdlib.

Outputs are additive: refined textures land in ArtSource/Game03VisualUpgrade/output/textures
with a name suffix, never overwriting the original project textures. An editor script binds
them to the runtime materials.

  Paving: light concrete slabs, subtle tile-to-tile tone drift, thin grout, fine aggregate.
  Plaster: warm grey stucco with soft low-frequency mottling, no crosshatch diamonds.
"""
import math
import os
import random
import struct
import zlib


def write_png(path, width, height, pixels):
    raw = b"".join(b"\x00" + b"".join(bytes(px) for px in row) for row in pixels)

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def smooth_noise(size, rng, cells):
    grid = [[rng.random() for _ in range(cells + 1)] for _ in range(cells + 1)]
    out = []
    for y in range(size):
        row = []
        fy = y * cells / size
        iy, ty = int(fy), fy - int(fy)
        ty = ty * ty * (3 - 2 * ty)
        for x in range(size):
            fx = x * cells / size
            ix, tx = int(fx), fx - int(fx)
            tx = tx * tx * (3 - 2 * tx)
            a = grid[iy][ix] * (1 - tx) + grid[iy][ix + 1] * tx
            b = grid[iy + 1][ix] * (1 - tx) + grid[iy + 1][ix + 1] * tx
            row.append(a * (1 - ty) + b * ty)
        out.append(row)
    return out


def paving(size=512):
    rng = random.Random(20261123)
    tone = smooth_noise(size, rng, 9)
    fine = smooth_noise(size, rng, 48)
    slabs = 4
    period = size // slabs
    drift = [[rng.uniform(-7, 7) for _ in range(slabs)] for _ in range(slabs)]
    pixels = []
    for y in range(size):
        row = []
        gy = y % period
        cy = y // period
        for x in range(size):
            gx = x % period
            cx = x // period
            joint = min(gx, gy, period - 1 - gx, period - 1 - gy)
            # Stagger alternate rows so the grout is not a perfect grid.
            shifted = (x + (cy % 2) * period // 2) % size
            sx = shifted % period
            joint_h = min(sx, period - 1 - sx)
            edge = joint if cy % 2 == 0 else min(gy, period - 1 - gy, joint_h)
            v = 198 + drift[cy][cx] + (tone[y][x] - .5) * 13 + (fine[y][x] - .5) * 9
            if edge < 2:
                v -= 20
            elif edge < 4:
                v -= 7
            row.append(tuple(max(0, min(255, int(v + ch))) for ch in (2, 1, -3)))
        pixels.append(row)
    return pixels


def plaster(size=512):
    rng = random.Random(20261124)
    soft = smooth_noise(size, rng, 7)
    grain = smooth_noise(size, rng, 40)
    pixels = []
    for y in range(size):
        row = []
        for x in range(size):
            v = 206 + (soft[y][x] - .5) * 20 + (grain[y][x] - .5) * 11
            row.append(tuple(max(0, min(255, int(v + ch))) for ch in (4, 1, -5)))
        pixels.append(row)
    return pixels


def main():
    out = os.path.join(os.path.dirname(os.path.dirname(__file__)), "output", "textures")
    os.makedirs(out, exist_ok=True)
    for name, pixels in (("PavingAlbedo_Refined.png", paving()),
                         ("PlasterAlbedo_Refined.png", plaster())):
        path = os.path.join(out, name)
        write_png(path, 512, 512, pixels)
        print("SURFACE_OK", path, os.path.getsize(path))


if __name__ == "__main__":
    main()
