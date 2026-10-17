"""Phase 8 D5: quieter flower patch + wet grey-blue mud puddle, derived from the Stage 2 decal sources (PIL / numpy, deterministic).

usage: python tools/art/d5_decals/make_d5_decals.py

Inputs  Assets/Generated/Env/Textures/flower_patch_decal.png, mud_puddle_decal.png  (512 RGBA, untouched)
Outputs Assets/Generated/Env/Textures/flower_patch_decal_d5.png, mud_puddle_decal_d5.png
GroundDressingBaker.BuildDecalAtlas packs the _d5 pair into Art/Textures/GroundDecalsAtlas.png (re-run the baker's EnsureTextures).

Flowers (audit L05: "bright white dot clusters, the busiest marks in the frame"): each flower is one connected alpha blob.
  * fewer dots: KEEP_FRACTION of the blobs survive (seeded, the dropped ones go to alpha 0);
  * palette: the white daisies become warm cream (most) or yellow, the original yellows stay, a few pinks stay (warmer, softer):
    target share cream ~50 % / yellow ~37 % / pink ~13 % of the kept blobs;
  * darker: then every flower texel is scaled so the opaque-texel mean luma is DARKEN x the source's (~30 % darker).
Mud (L05: "brown ovals in the empty-pile colour family"): per texel luma kept x MUD_LUMA, hue moved to a wet blue-grey (dark core
  bluer, the drying rim a neutral grey), so it no longer reads as a pile disc (PileZone.mat (0.34, 0.25, 0.17)).
"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage as nd

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SRC = os.path.join(ROOT, "Assets", "Generated", "Env", "Textures")
K601 = np.array([0.299, 0.587, 0.114])

KEEP_FRACTION = 0.58
DARKEN = 0.70
SEED = 5
CREAM = np.array([1.00, 0.91, 0.68])     # multiplier on the white daisy (petal 255 -> 255/232/173 before DARKEN)
YELLOW = np.array([1.00, 0.84, 0.34])    # white daisy -> buttercup yellow
PINK = np.array([1.00, 0.90, 0.86])      # existing pinks: a touch warmer / softer
MUD_LUMA = 0.90
MUD_CORE = np.array([0.84, 0.98, 1.20])  # dark wet blue-grey (luma-normalised below)
MUD_RIM = np.array([0.97, 1.00, 1.04])   # drying rim: near-neutral grey


def norm_luma(v):
    return v / float(v @ K601)


def flowers():
    a = np.asarray(Image.open(os.path.join(SRC, "flower_patch_decal.png")).convert("RGBA")).astype(np.float64)
    rgb, al = a[..., :3], a[..., 3]
    # one label per flower: label the opaque cores, then every other texel (soft halo, background) joins its nearest core
    core = al >= 0.35 * 255
    cores, n = nd.label(core)
    _, (iy, ix) = nd.distance_transform_edt(~core, return_indices=True)
    blobs = cores[iy, ix]
    rng = np.random.default_rng(SEED)
    kinds = []
    for i in range(1, n + 1):
        m = rgb[cores == i].mean(0)
        if m[2] < 100 and m[1] > 150:          # solid yellow (~229/208/72); a white daisy's yellow eye keeps its mean B > 120
            kinds.append("yellow")
        elif m[0] - m[1] > 25:
            kinds.append("pink")
        else:
            kinds.append("white")
    kinds = np.array(kinds, dtype=object)
    print("source blobs:", {k: int((kinds == k).sum()) for k in ("white", "yellow", "pink")})
    order = rng.permutation(n)
    keep = np.zeros(n, bool)
    # yellows all stay; pinks: 8 stay; whites fill the rest of the KEEP_FRACTION budget
    budget = int(round(KEEP_FRACTION * n))
    keep[kinds == "yellow"] = True
    pinks = [j for j in order if kinds[j] == "pink"][:8]
    keep[pinks] = True
    whites = [j for j in order if kinds[j] == "white"]
    nw = max(0, budget - int(keep.sum()))
    keep[whites[:nw]] = True
    target = kinds.copy()
    kept_whites = whites[:nw]
    ny = int(round(0.37 * budget)) - int((kinds == "yellow").sum())
    for j in kept_whites[:max(ny, 0)]:
        target[j] = "to_yellow"
    for j in kept_whites[max(ny, 0):]:
        target[j] = "cream"

    out = rgb.copy()
    lab = blobs - 1
    mult = np.tile(CREAM, rgb.shape[:2] + (1,))          # background / dropped texels: cream (keeps the mip bleed warm)
    for j in range(n):
        mask = lab == j
        t = target[j]
        if t == "yellow":
            mult[mask] = 1.0
        elif t == "pink":
            mult[mask] = PINK
        elif t == "to_yellow":
            mult[mask] = YELLOW
    out = out * mult
    new_al = al.copy()
    for j in range(n):
        if not keep[j]:
            new_al[lab == j] = 0
    opaque_src = al >= 0.35 * 255
    src_mean = float((rgb[opaque_src] @ K601).mean())
    opaque_new = new_al >= 0.35 * 255
    k = DARKEN * src_mean / float((out[opaque_new] @ K601).mean())
    out = np.clip(out * k, 0, 255)
    res = np.dstack([out, new_al]).round().astype(np.uint8)
    Image.fromarray(res, "RGBA").save(os.path.join(SRC, "flower_patch_decal_d5.png"))
    kept = target[keep]
    print(f"flowers: {n} blobs -> {int(keep.sum())} kept (cream {int((kept == 'cream').sum())}, yellow {int(((kept == 'yellow') | (kept == 'to_yellow')).sum())}, "
          f"pink {int((kept == 'pink').sum())}); opaque luma {src_mean:.1f} -> {float((out[opaque_new] @ K601).mean()):.1f} (x{k:.3f} after tint)")


def mud():
    a = np.asarray(Image.open(os.path.join(SRC, "mud_puddle_decal.png")).convert("RGBA")).astype(np.float64)
    rgb, al = a[..., :3], a[..., 3]
    y = rgb @ K601
    op = al > 200
    lo, hi = np.percentile(y[op], 10), np.percentile(y[op], 95)
    t = np.clip((y - lo) / max(hi - lo, 1), 0, 1)[..., None]
    direction = (1 - t) * norm_luma(MUD_CORE) + t * norm_luma(MUD_RIM)
    out = np.clip(direction * (y * MUD_LUMA)[..., None], 0, 255)
    res = np.dstack([out, al]).round().astype(np.uint8)
    Image.fromarray(res, "RGBA").save(os.path.join(SRC, "mud_puddle_decal_d5.png"))
    print(f"mud: opaque mean rgb {rgb[op].mean(0).round(1)} -> {out[op].mean(0).round(1)}")


if __name__ == "__main__":
    flowers()
    mud()
