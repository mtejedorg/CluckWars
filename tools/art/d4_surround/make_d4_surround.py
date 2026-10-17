"""Phase 8 D4: surround ground grass detail + clumpy, scalloped hedge leaf tiles (PIL/numpy edits of existing textures, no generation).

    python tools/art/d4_surround/make_d4_surround.py [--preview <dir>]

Inputs (left untouched): Assets/_Game/Art/GrassCalm.png (the arena grass tile, E3), Assets/Generated/Env/Textures/hedge_leaf_tile.png
(hedge sides) and hedge_leaf_tile_top.png (hedge crown). Outputs, all in Assets/Generated/Env/Textures/:

  surround_grass_detail.png  1024 RGB, LINEAR. URP Lit Detail Albedo x2 on Surround_Grass.mat (base map stays empty: the Mobile haze
      baker writes its per-texel ramp into the base map of the hazed copy, and the base colour (0.15, 0.22, 0.14) stays the field's mean).
      texel = 0.5 x ratio, ratio = the arena tile / its mean (half per-channel, half luma, so the variation is less saturated than the
      arena's) with its deviation x CONTRAST_GRASS; mean exactly 127.5 per channel = no change of the mean colour.
  The hedge tiles span PU = 2 m along the hedge x 1 m (anisotropic texels; Hedge_Side / Hedge_Top use base tiling (0.5, 1) because the
  HedgeStrip UVs are metres), so the clumps and lobes repeat every 2 m, not every metre.
  hedge_leaf_side_d4.png     1024 RGB sRGB. hedge sides (HedgeStrip submesh 0: u = -x metres, v = height 0..0.78 m; only rows
      v <= 0.78 are ever drawn). Leaf tile (x1.15 detail) x a clump field (round leaf clusters 8-15 cm, top-lit, soft dark gaps) + the
      SCALLOP row: six irregular lobes per 2 m (radius 0.15-0.21 m, small depth jitter) centred ON the side/crown seam (v = 0.78),
      top-lit, dark notches between them, i.e. a scalloped valance hanging from the top edge. Rows above the seam mirror the band.
  hedge_leaf_top_d4.png      1024 RGB sRGB. crown (submesh 1: planar u = -x, v = 1.5 + y): leaf tile x the clump field (shifted).
  hedge_crown_scallop.png    256 x 512 RGB, LINEAR. Detail Albedo x2 on Hedge_Top.mat with detail tiling (1, 1/3): the crown's v range
      0.47..2.52 maps to 0.16..0.84 once, so both crown EDGES (|y| = 0.98, the seam) get their own lobes at the same u as the side lobes
      (one clump reads across the shoulder); 128 = neutral everywhere else.
Every albedo output keeps the source tile's mean colour (per channel, over the rows the mesh draws) and reaches luma std >= 20.
"""
import os, sys, json
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
TEX = os.path.join(ROOT, "Assets", "Generated", "Env", "Textures")
SRC_GRASS = os.path.join(ROOT, "Assets", "_Game", "Art", "GrassCalm.png")
SRC_SIDE = os.path.join(TEX, "hedge_leaf_tile.png")
SRC_TOP = os.path.join(TEX, "hedge_leaf_tile_top.png")
OUT_GRASS = os.path.join(TEX, "surround_grass_detail.png")
OUT_SIDE = os.path.join(TEX, "hedge_leaf_side_d4.png")
OUT_TOP = os.path.join(TEX, "hedge_leaf_top_d4.png")
OUT_SCALLOP = os.path.join(TEX, "hedge_crown_scallop.png")

K = np.array([0.299, 0.587, 0.114])      # luma weights (the audit's std 7.6 / the S3 numbers use Rec.601 luma on 8-bit values)
SEAM_V = 0.78                            # side/crown seam in the side UVs (HedgeStrip)
CROWN_EDGE = 0.98                        # |y| of the seam in the crown's planar UVs (v = 1.5 + y)
# Scallop lobes per 1 m of hedge: (u centre, radius m). Shared by the side tile and the crown detail so they line up (both u = -x).
PU = 2.0                                 # hedge texture period along the hedge (m): base tiling (0.5, 1) on Hedge_Side / Hedge_Top
LOBES = [(0.15, 0.17, 0.00), (0.47, 0.20, 0.03), (0.80, 0.15, -0.01), (1.10, 0.19, 0.02), (1.43, 0.16, 0.00), (1.74, 0.21, 0.03)]  # (u m, radius m, depth offset m)
CONTRAST_GRASS = 1.35
FINE_GAIN = 1.15                         # leaf-scale detail amplification
SEED = 11


def luma(a):
    return a[..., :3] @ K


def dither_u8(x, rng):
    return np.clip(np.floor(x + rng.uniform(0, 1, x.shape)), 0, 255).astype(np.uint8)


# ---- surround grass ---------------------------------------------------------------------------------------------------

def grass_detail(rng):
    t = np.asarray(Image.open(SRC_GRASS).convert("RGB")).astype(np.float64)
    mean = t.reshape(-1, 3).mean(0)
    per = t / mean
    lum = (t @ K) / (mean @ K)
    ratio = 0.5 * per + 0.5 * lum[..., None]
    ratio = 1 + CONTRAST_GRASS * (ratio - 1)
    ratio /= ratio.reshape(-1, 3).mean(0)
    v = 127.5 * ratio
    return dither_u8(v, rng), ratio


# ---- hedge: one texture period = PU metres along the hedge x 1 m (the materials use base tiling (1 / PU, 1)) -----------------

def wrap(d, period):
    return (d + 0.5 * period) % period - 0.5 * period


def grid(N, M=None):
    """Pixel centres in METRES: u in [0, PU) (columns), v in [0, 1) (rows, v = 1 - row fraction: PNG row 0 is the top)."""
    M = M or N
    y, x = (np.mgrid[0:M, 0:N] + 0.5) / np.array([M, N]).reshape(2, 1, 1)
    return x * PU, 1 - y


def clump_field(N, rng, count=170):
    """Height field of overlapping leaf clusters (spheres, r 8-15 cm) on the PU x 1 m torus; top-lit shade x soft gap occlusion, mean 1."""
    u, v = grid(N)
    H = np.zeros((N, N)); GX = np.zeros((N, N)); GY = np.zeros((N, N))
    for _ in range(count):
        cu, cv, r = rng.uniform(0, PU), rng.uniform(0, 1), rng.uniform(0.08, 0.15)
        du = wrap(u - cu, PU); dv = wrap(v - cv, 1.0)
        h = np.sqrt(np.maximum(r * r - du * du - dv * dv, 0))
        m = h > H
        safe = np.where(h > 1e-4, h, 1)
        H = np.where(m, h, H); GX = np.where(m, du / safe, GX); GY = np.where(m, dv / safe, GY)
    nrm = np.stack([GX, GY, np.ones_like(H)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    L = np.array([-0.3, 0.6, 0.74]); L /= np.linalg.norm(L)    # light from above-left (+v = up)
    shade = np.clip(nrm @ L, 0, 1)
    occ = np.clip(H / 0.1, 0, 1) ** 0.6
    f = (0.45 + 0.55 * shade) * (0.5 + 0.5 * occ)
    return f / f.mean()


def lobe_field(u, w, sign_into):
    """Scallop lobes along the seam line w = 0 (w = metres from the seam INTO the drawn face; u in metres, period PU).
    Returns (factor, band mask). Inside a lobe: top-lit dome (bright near the seam, dark rim). Between lobes near the seam: dark notch."""
    best = np.full(u.shape, np.inf); rr = np.ones(u.shape); du = np.zeros(u.shape); wl = np.zeros(u.shape)
    for uc, r, dw in LOBES:
        dd = wrap(u - uc, PU)
        d = np.hypot(dd, w - dw) / r
        m = d < best
        best = np.where(m, d, best); rr = np.where(m, r, rr); du = np.where(m, dd / r, du); wl = np.where(m, (w - dw) / r, wl)
    inside = (best < 1) & (w >= 0)
    z = np.sqrt(np.clip(1 - best ** 2, 0, 1))
    nrm = np.stack([du, wl * sign_into, z], -1)
    nrm /= np.maximum(np.linalg.norm(nrm, axis=-1, keepdims=True), 1e-6)
    L = np.array([-0.25, 0.55, 0.80]); L /= np.linalg.norm(L)
    shade = np.clip(nrm @ L, 0, 1)
    rim = np.clip((1 - best) / 0.3, 0, 1) ** 0.7             # dark crescent at the lobe's rim
    dome = (0.8 + 1.1 * shade) * (0.3 + 0.7 * rim)
    rmax = max(r + dw for _, r, dw in LOBES)
    band = (w >= 0) & (w < rmax * 1.05)
    # Between the lobes: a dark notch at the seam that fades out with depth (the scallop's V), not a dark stripe.
    notch = 1 - 0.72 * np.exp(-np.clip(w, 0, None) / 0.09)
    f = np.where(inside, dome, np.where(band, notch, 1.0))
    return f, band


def leaf_source(src, N):
    """The 1 m leaf tile repeated PU times along u, resampled to N x N (anisotropic texels: PU m x 1 m)."""
    t = Image.open(src).convert("RGB")
    wide = Image.new("RGB", (t.width * int(PU), t.height))
    for i in range(int(PU)):
        wide.paste(t, (i * t.width, 0))
    return np.asarray(wide.resize((N, N), Image.LANCZOS)).astype(np.float64), np.asarray(t).astype(np.float64).reshape(-1, 3).mean(0)


def hedge_albedo(src, rng, clumps, side):
    t, mean = leaf_source(src, clumps.shape[0])
    N = t.shape[0]
    lum = t @ K
    fine = np.clip(1 + FINE_GAIN * (lum / lum.mean() - 1), 0.15, None)   # leaf-scale detail
    f = fine * clumps
    u, v = grid(N)
    if side:
        w = np.abs(SEAM_V - v)                               # metres below the seam, mirrored above it (no foreign texels at the seam)
        lf, band = lobe_field(u, w, sign_into=-1.0)
        f = np.where(band, lf * fine ** 0.8, f)
        drawn = v <= SEAM_V
    else:
        drawn = np.ones((N, N), bool)
    out = t * f[..., None]
    # Warm the lit leaf tops a touch (yellow-green highlights, blue-green gaps), then restore the mean colour on the drawn rows.
    hi = np.clip(f - 1, 0, None)[..., None]; lo = np.clip(1 - f, 0, None)[..., None]
    out = out * (1 + hi * np.array([0.10, 0.04, -0.08])) * (1 + lo * np.array([-0.10, 0.0, 0.10]))
    for _ in range(4):
        out *= (mean / out[drawn].mean(0))
        out = np.clip(out, 0, 255)
    return dither_u8(out, rng), drawn


def crown_scallop():
    """Detail map for the crown: detail uv = (base u, v / 3), so u spans PU m and v 0..1 spans the crown's planar v 0..3 (y = v - 1.5)."""
    W, Hh = 256, 512
    u, dv = grid(W, Hh)
    yy = 3 * dv - 1.5                                        # crown planar coordinate y (metres)
    w = CROWN_EDGE - np.abs(yy)                              # metres inward from the nearer crown edge
    lf, band = lobe_field(u, np.abs(w), sign_into=1.0)
    f = np.where(band, lf, 1.0)
    bm = band & (w >= 0)
    f = np.where(bm, f / f[bm].mean(), f)                    # band average back to 1: the crown keeps its mean brightness
    tex = np.clip(127.5 * f, 0, 255)
    return np.repeat(tex[..., None], 3, -1), f


def stats(img, drawn=None):
    a = img.astype(np.float64)
    m = drawn if drawn is not None else np.ones(a.shape[:2], bool)
    L = luma(a)
    small = np.asarray(Image.fromarray(img).resize((a.shape[1] // 8, a.shape[0] // 8), Image.BOX)).astype(np.float64)
    ms = m[::8, ::8][: small.shape[0], : small.shape[1]]
    return {"mean_rgb": [round(float(c), 2) for c in a[m].mean(0)], "luma_mean": round(float(L[m].mean()), 2), "luma_std": round(float(L[m].std()), 2),
            "luma_std_full_tile": round(float(L.std()), 2), "luma_std_mip3": round(float(luma(small)[ms].std()), 2),
            "clip_lo_pct": round(float((a[m] <= 1).any(-1).mean() * 100), 2), "clip_hi_pct": round(float((a[m] >= 254).any(-1).mean() * 100), 2)}


def main():
    rng = np.random.default_rng(SEED)
    prev = sys.argv[sys.argv.index("--preview") + 1] if "--preview" in sys.argv else None
    rep = {}
    g, ratio = grass_detail(rng)
    Image.fromarray(g).save(OUT_GRASS)
    rep["surround_grass_detail"] = {"mean": [round(float(c), 2) for c in g.reshape(-1, 3).mean(0)], "ratio_std": round(float((ratio @ K).std()), 3),
                                    "ratio_min": round(float(ratio.min()), 3), "ratio_max": round(float(ratio.max()), 3)}
    clumps = clump_field(1024, rng)
    for name, src, dst, side in (("side", SRC_SIDE, OUT_SIDE, True), ("top", SRC_TOP, OUT_TOP, False)):
        before = np.asarray(Image.open(src).convert("RGB"))
        img, drawn = hedge_albedo(src, rng, clumps if side else np.roll(clumps, (311, 517), (0, 1)), side)
        Image.fromarray(img).save(dst)
        rep[name] = {"before": stats(before, drawn), "after": stats(img, drawn)}
    sc, f = crown_scallop()
    Image.fromarray(sc.astype(np.uint8)).save(OUT_SCALLOP)
    rep["crown_scallop"] = {"min": round(float(f.min()), 3), "max": round(float(f.max()), 3)}
    print(json.dumps(rep, indent=1))
    if prev:   # camera-scale previews (~150 px per metre, the match camera at the near hedge): 4 m of side (drawn rows) under 4 m of crown
        os.makedirs(prev, exist_ok=True)
        ppm = 150
        side = np.asarray(Image.open(OUT_SIDE).resize((int(PU * ppm), ppm), Image.BOX)).astype(np.float64)
        strip = np.concatenate([side[int(round((1 - SEAM_V) * ppm)):]] * 2, 1)
        top = np.asarray(Image.open(OUT_TOP).resize((int(PU * ppm), ppm), Image.BOX)).astype(np.float64)
        vs = np.linspace(2.52, 0.47, int(2.05 * ppm)); us = np.arange(int(2 * PU * ppm)) / ppm
        tr = ((1 - (vs % 1)) * ppm).astype(int) % ppm; tc = ((us / PU % 1) * PU * ppm).astype(int) % int(PU * ppm)
        dr = ((1 - vs / 3) * 512).astype(int).clip(0, 511); dc = ((us / PU % 1) * 256).astype(int) % 256
        crown = top[tr][:, tc] * (2 * sc[dr][:, dc] / 255.0)
        comp = np.concatenate([np.clip(crown, 0, 255), strip], 0).astype(np.uint8)
        Image.fromarray(comp).resize((comp.shape[1] * 2, comp.shape[0] * 2), Image.NEAREST).save(os.path.join(prev, "hedge_camera_preview.png"))

if __name__ == "__main__":
    main()
