"""Cuts the GPT Image parts sheet into the rig's pixel-art parts.

Usage: python slice_parts.py <parts-sheet.png> <anim-sheet.png> <out-dir>

Expected sheet layout (prompt 1 in the README): row 1 = body / squashed / stretched, row 2 = 4 femurs,
row 3 = 4 tibias, row 4 = 2 pedipalps + eyes (open, half, closed, happy, wide) + small side eye.
Writes one PNG per part plus parts.json (sizes, pivots, eye anchors) into <out-dir>, and icon.png from
the first idle frame of the animation sheet.
"""
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

sheet_path, anim_path, out_dir = sys.argv[1:4]
os.makedirs(out_dir, exist_ok=True)

BODY_WIDTH = 52                       # art px of the main body
FEMUR = [(27, 9), (30, 9), (32, 9), (34, 9)]   # (total length, thickness) front -> back pair
TIBIA = [(42, 8), (50, 8), (57, 8), (64, 8)]
EYE_WIDTH = 15
SIDE_EYE_WIDTH = 6
PALETTE_COLORS = 28
OUTLINE = np.array([42, 24, 16, 255], np.uint8)


def clean_rgba(img):
    """Binary alpha and no red matting fringe (GPT Image leaves a red halo on transparent exports)."""
    a = np.array(img.convert("RGBA")).astype(int)
    r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    solid = ndimage.binary_fill_holes(al > 160)
    # Only red pixels within a few px of the outside are matting; dark red-brown fur inside stays.
    near_edge = ndimage.distance_transform_edt(solid) <= 4
    fringe = (r > 140) & (g < 80) & (b < 80) & near_edge
    keep = solid & ~fringe
    out = a.copy()
    out[..., 3] = np.where(keep, 255, 0)
    return out.astype(np.uint8), keep


def components(keep, min_area=800):
    lab, _ = ndimage.label(ndimage.binary_closing(keep, iterations=2))
    comps = []
    for i, sl in enumerate(ndimage.find_objects(lab)):
        m = lab[sl] == i + 1
        if m.sum() >= min_area:
            comps.append((sl, m))
    comps.sort(key=lambda c: (c[0][0].start // 150, c[0][1].start))
    return comps


def crop(rgba, sl, m):
    part = rgba[sl].copy()
    part[..., 3] = np.where(m, part[..., 3], 0)
    return Image.fromarray(part, "RGBA")


def straighten(img):
    """Rotates a limb so its long axis is horizontal (PCA of the opaque pixels), joint ball on the left."""
    a = np.array(img)
    ys, xs = np.nonzero(a[..., 3] > 0)
    pts = np.stack([xs, ys], 1).astype(float)
    centre = pts.mean(0)
    _, _, vt = np.linalg.svd(pts - centre, full_matrices=False)
    dx, dy = vt[0]
    if dx < 0:
        dx, dy = -dx, -dy
    angle = np.degrees(np.arctan2(dy, dx))
    rotated = img.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)
    arr = np.array(rotated)
    arr[..., 3] = np.where(arr[..., 3] > 128, 255, 0)
    rotated = Image.fromarray(arr, "RGBA")
    return rotated.crop(rotated.getbbox())


def reduce(img, size):
    return img.resize(size, Image.Resampling.BOX)


def add_outline(arr):
    mask = arr[..., 3] > 0
    pad = np.pad(mask, 1)
    edge = mask & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    arr[edge] = OUTLINE
    return arr


rgba, keep = clean_rgba(Image.open(sheet_path))
comps = components(keep)
if len(comps) != 19:
    sys.exit(f"esperava 19 peças na prancha, achei {len(comps)}")

bodies, femurs, tibias, row4 = comps[0:3], comps[3:7], comps[7:11], comps[11:19]
k = BODY_WIDTH / (bodies[0][0][1].stop - bodies[0][0][1].start)

parts = {}  # name -> (reduced RGBA image, outline?)

for name, (sl, m) in zip(["body", "body_squash", "body_stretch"], bodies):
    img = crop(rgba, sl, m)
    parts[name] = (reduce(img, (round(img.width * k), round(img.height * k))), True)

for i, (sl, m) in enumerate(femurs):
    parts[f"femur{i}"] = (reduce(straighten(crop(rgba, sl, m)), FEMUR[i]), True)
for i, (sl, m) in enumerate(tibias):
    parts[f"tibia{i}"] = (reduce(straighten(crop(rgba, sl, m)), TIBIA[i]), True)

palp_l, palp_r = row4[0], row4[1]
for name, (sl, m) in [("palp_l", palp_l), ("palp_r", palp_r)]:
    img = crop(rgba, sl, m)
    parts[name] = (reduce(img, (round(img.width * k), round(img.height * k))), True)

eye_names = ["eye_open", "eye_half", "eye_closed", "eye_happy", "eye_wide"]
open_w = row4[2][0][1].stop - row4[2][0][1].start
ke = EYE_WIDTH / open_w
for name, (sl, m) in zip(eye_names, row4[2:7]):
    img = crop(rgba, sl, m)
    parts[name] = (reduce(img, (max(1, round(img.width * ke)), max(1, round(img.height * ke)))), False)
side = crop(rgba, *row4[7])
parts["eye_side"] = (reduce(side, (SIDE_EYE_WIDTH, SIDE_EYE_WIDTH)), False)

# One shared palette so every part matches.
items = list(parts.items())
montage_w = sum(img.width for _, (img, _) in items) + len(items)
montage_h = max(img.height for _, (img, _) in items)
montage = Image.new("RGB", (montage_w, montage_h), (0, 0, 0))
x = 0
for _, (img, _) in items:
    flat = Image.new("RGB", img.size, (0, 0, 0))
    flat.paste(img, mask=img.split()[3])
    montage.paste(flat, (x, 0))
    x += img.width + 1
palette_img = montage.quantize(colors=PALETTE_COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)

meta = {"body_scale": k, "parts": {}}
for name, (img, outline) in items:
    arr = np.array(img)
    alpha = ndimage.binary_fill_holes(arr[..., 3] > 110)
    q = np.array(img.convert("RGB").quantize(palette=palette_img, dither=Image.Dither.NONE).convert("RGB"))
    out = np.zeros_like(arr)
    out[..., :3] = q
    out[..., 3] = np.where(alpha, 255, 0)
    if outline:
        out = add_outline(out)
    Image.fromarray(out, "RGBA").save(os.path.join(out_dir, f"{name}.png"))
    meta["parts"][name] = {"size": [img.width, img.height]}

# Limb pivots: centre of the joint ball at the left end; segment length = ball centre -> far end centre.
for i, (length, thick) in enumerate(FEMUR):
    meta["parts"][f"femur{i}"].update(pivot=[thick / 2, thick / 2], length=length - thick)
for i, (length, thick) in enumerate(TIBIA):
    meta["parts"][f"tibia{i}"].update(pivot=[thick / 2, thick / 2], length=length - thick)

# Eye anchors as fractions of each body's box (measured on the main body of the sheet).
meta["eyes"] = {"left": [0.345, 0.6], "right": [0.655, 0.6], "side_left": [0.175, 0.55], "side_right": [0.825, 0.55]}
meta["palps"] = {"left": [0.37, 0.89], "right": [0.63, 0.89]}

with open(os.path.join(out_dir, "parts.json"), "w") as f:
    json.dump(meta, f, indent=2)

# Tray icon: first idle frame of the animation sheet.
anim, akeep = clean_rgba(Image.open(anim_path))
acomps = [c for c in components(akeep, 5000)]
first = min(acomps, key=lambda c: (c[0][0].start // 150, c[0][1].start))
icon = crop(anim, *first)
# Square canvas so the .ico isn't stretched.
side = max(icon.size)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.alpha_composite(icon, ((side - icon.width) // 2, (side - icon.height) // 2))
square.resize((256, 256), Image.Resampling.LANCZOS).save(
    os.path.join(out_dir, "..", "webi.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
icon.thumbnail((64, 64), Image.Resampling.LANCZOS)
icon.save(os.path.join(out_dir, "icon.png"))

print(json.dumps({n: meta["parts"][n]["size"] for n in meta["parts"]}))
