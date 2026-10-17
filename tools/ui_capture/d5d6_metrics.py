"""Phase 8 D5/D6 metrics for capture_d5d6_dressing.py output.  usage: python tools/ui_capture/d5d6_metrics.py <dir> [<before_dir>]

Masks from the render pairs (D2 rules): noise = |frame - all2| > 4 (dilated 4 px); class = |frame - no<class>| > 6 minus noise.
    lit = luma(frame) - luma(nosun) >= 6.
Per tier (pc, mobile) and spot:
  flower : visible flower-decal pixels: mean / median Rec.709 luma (8-bit), dots = connected components >= 6 px, dots per 1000 px of
           patch area (area = the flower mask closed by 9 px + holes filled), mean RGB.
  mud    : mean RGB of mud-decal pixels (lit), CIELAB, dE00 vs the visible pile-disc pixels (lit) of the same frame.
  disc   : mean RGB of the visible PileZone disc pixels (lit).
  path   : median luma of lit path pixels vs lit grass (ground mask minus path/decals), ratio path / grass.
           holes: footprint = path mask closed (7 px) with holes filled; hole = footprint & ~path; distance-to-footprint-edge d;
           local width ~ 2 * p95(d); inner band = d >= 0.2 * width (the inner 60 %); reports the hole fraction of the inner band
           and of the outer band (the edges).
  grass  : median luma of lit grass.
Writes <dir>/d5d6_metrics.json. With <before_dir>, prints before -> after.
"""
import sys, os, json, math
import numpy as np
from PIL import Image
from scipy import ndimage as nd

K709 = np.array([0.2126, 0.7152, 0.0722])
SPOTS = ("spawn", "centre", "flower", "mud", "path")


def srgb_to_lab(rgb):
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
    xyz = lin @ M.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > (6 / 29) ** 3, np.cbrt(xyz), xyz / (3 * (6 / 29) ** 2) + 4 / 29)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def de00(lab1, lab2):
    L1, a1, b1 = lab1; L2, a2, b2 = lab2
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2); Cb = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cb ** 7 / (Cb ** 7 + 25 ** 7)))
    a1p, a2p = a1 * (1 + G), a2 * (1 + G)
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360; h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dLp = L2 - L1; dCp = C2p - C1p
    dh = h2p - h1p
    if C1p * C2p == 0: dh = 0
    elif dh > 180: dh -= 360
    elif dh < -180: dh += 360
    dHp = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lbp = (L1 + L2) / 2; Cbp = (C1p + C2p) / 2
    if C1p * C2p == 0: hbp = h1p + h2p
    elif abs(h1p - h2p) <= 180: hbp = (h1p + h2p) / 2
    elif h1p + h2p < 360: hbp = (h1p + h2p + 360) / 2
    else: hbp = (h1p + h2p - 360) / 2
    T = 1 - 0.17 * math.cos(math.radians(hbp - 30)) + 0.24 * math.cos(math.radians(2 * hbp)) + 0.32 * math.cos(math.radians(3 * hbp + 6)) - 0.20 * math.cos(math.radians(4 * hbp - 63))
    dth = 30 * math.exp(-(((hbp - 275) / 25) ** 2)); Rc = 2 * math.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7))
    Sl = 1 + 0.015 * (Lbp - 50) ** 2 / math.sqrt(20 + (Lbp - 50) ** 2); Sc = 1 + 0.045 * Cbp; Sh = 1 + 0.015 * Cbp * T
    Rt = -math.sin(math.radians(2 * dth)) * Rc
    return math.sqrt((dLp / Sl) ** 2 + (dCp / Sc) ** 2 + (dHp / Sh) ** 2 + Rt * (dCp / Sc) * (dHp / Sh))


def load(d, n):
    p = os.path.join(d, n)
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float64) if os.path.exists(p) else None


def diff_mask(a, b, noise, thr=6, erode=1):
    if b is None:
        return np.zeros(a.shape[:2], bool)
    m = (np.abs(a - b).max(-1) > thr) & ~noise
    return nd.binary_erosion(m, iterations=erode) if erode else m


def rgb_stats(a, m):
    px = a[m]
    if len(px) < 50:
        return None
    gm = px.mean(0); L, aa, bb = srgb_to_lab(gm)
    return {"n": int(m.sum()), "rgb": [round(float(x), 1) for x in gm], "luma_mean": round(float((px @ K709).mean()), 1),
            "luma_med": round(float(np.median(px @ K709)), 1), "lab": [round(float(L), 1), round(float(aa), 1), round(float(bb), 1)]}


def spot_metrics(d, tier, spot):
    a = load(d, f"{tier}_{spot}.png")
    if a is None:
        return None
    a2 = load(d, f"{tier}_{spot}_all2.png")
    noise = nd.binary_dilation(np.abs(a - a2).max(-1) > 4, iterations=4) if a2 is not None else np.zeros(a.shape[:2], bool)
    lum = a @ K709
    nosun = load(d, f"{tier}_{spot}_nosun.png")
    lit = (lum - nosun @ K709) >= 6 if nosun is not None else np.ones(lum.shape, bool)
    flower = diff_mask(a, load(d, f"{tier}_{spot}_noflower.png"), noise, erode=0)
    mud = diff_mask(a, load(d, f"{tier}_{spot}_nomud.png"), noise)
    path = diff_mask(a, load(d, f"{tier}_{spot}_nopath.png"), noise, erode=0)
    disc = diff_mask(a, load(d, f"{tier}_{spot}_nodisc.png"), noise)
    ground = diff_mask(a, load(d, f"{tier}_{spot}_noground.png"), noise, erode=2)
    grass = ground & ~nd.binary_dilation(path | flower | mud | disc, iterations=3)
    out = {}
    g = rgb_stats(a, grass & lit)
    out["grass"] = g
    # flowers
    fm = nd.binary_opening(flower, iterations=1) if flower.sum() > 0 else flower
    if fm.sum() >= 50:
        lab, n = nd.label(fm)
        sizes = nd.sum(fm, lab, range(1, n + 1)) if n else np.array([])
        dots = int((sizes >= 6).sum())
        area = nd.binary_fill_holes(nd.binary_closing(fm, iterations=9)).sum()
        st = rgb_stats(a, fm)
        st.update({"dots": dots, "patch_area": int(area), "dots_per_kpx": round(1000.0 * dots / max(area, 1), 2),
                   "cover": round(float(fm.sum()) / max(area, 1), 3)})
        out["flower"] = st
    # mud + disc
    ms = rgb_stats(a, mud & lit) or rgb_stats(a, mud)
    ds = rgb_stats(a, disc & lit) or rgb_stats(a, disc)
    if ms: out["mud"] = ms
    if ds: out["disc"] = ds
    if ms and ds:
        out["mud_vs_disc_de00"] = round(de00(ms["lab"], ds["lab"]), 1)
    # path
    if path.sum() >= 500:
        ps = rgb_stats(a, path & lit)
        if ps and g:
            ps["ratio_vs_grass"] = round(ps["luma_med"] / g["luma_med"], 3)
        foot = nd.binary_fill_holes(nd.binary_closing(path, iterations=7))
        dist = nd.distance_transform_edt(foot)
        width = 2 * np.percentile(dist[foot], 95)
        hole = foot & ~path
        inner = foot & (dist >= 0.2 * width)
        outer = foot & ~inner
        if ps is None:
            ps = {}
        ps.update({"width_px": round(float(width), 1), "hole_frac_inner": round(float(hole[inner].mean()), 4) if inner.any() else None,
                   "hole_frac_edge": round(float(hole[outer].mean()), 4) if outer.any() else None,
                   "holes_inner_share": round(float((hole & inner).sum()) / max(hole.sum(), 1), 3)})
        out["path"] = ps
    return out


def run(d):
    res = {}
    for tier in ("pc", "mobile"):
        for spot in SPOTS:
            m = spot_metrics(d, tier, spot)
            if m is not None:
                res[f"{tier}_{spot}"] = m
    with open(os.path.join(d, "d5d6_metrics.json"), "w") as f:
        json.dump(res, f, indent=1)
    return res


def brief(m):
    s = []
    if "grass" in m and m["grass"]: s.append(f"grass {m['grass']['luma_med']}")
    if "flower" in m: f = m["flower"]; s.append(f"flower luma {f['luma_mean']} dots {f['dots']} ({f['dots_per_kpx']}/kpx) rgb {f['rgb']}")
    if "mud" in m: s.append(f"mud rgb {m['mud']['rgb']}")
    if "disc" in m: s.append(f"disc rgb {m['disc']['rgb']}")
    if "mud_vs_disc_de00" in m: s.append(f"mud-disc dE00 {m['mud_vs_disc_de00']}")
    if "path" in m:
        p = m["path"]; s.append(f"path luma {p.get('luma_med')} x{p.get('ratio_vs_grass')} holes inner {p['hole_frac_inner']} edge {p['hole_frac_edge']} innerShare {p['holes_inner_share']}")
    return " | ".join(s)


if __name__ == "__main__":
    d = sys.argv[1]
    res = run(d)
    before = run(sys.argv[2]) if len(sys.argv) > 2 else None
    for k, m in res.items():
        if before and k in before:
            print(f"{k}\n   before: {brief(before[k])}\n   after : {brief(m)}")
        else:
            print(f"{k}: {brief(m)}")
