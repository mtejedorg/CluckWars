"""Phase 8 E3 metrics for capture_e3_grass.py output.  usage: python tools/ui_capture/e3_grass_metrics.py <dir> [<before_dir>]

Per tier (pc, mobile):
  spots (spawn / pile / centre), masks from that spot's render pairs (the D2 rules):
      ground = |frame - noground| > 6 (eroded 2 px) minus pile and noise   pile = |frame - nopile| > 6 (eroded 2 px) minus noise
      shaded = luma(frame) - luma(nosun) < 6                                grass = ground & ~shaded (sunlit grass)
    grass: median Rec.709 luma (8-bit, the D8 method), mean RGB, CIELAB L* / C* / h of the mean, median per-pixel C*, HSV saturation;
           'lowfreq' = std of the mean luma of 48 px blocks that are >= 80 % grass (the large-scale variation the detail map adds).
    pile (all its pixels, and its lit half = brightest 50 % by luma): WCAG contrast ratio and CIEDE2000 vs the grass mean.
  lineup: class body masks (e4_class_metrics rule), lit-half mean per class vs the sunlit grass in a 25-60 px band round the four bodies
          (green pixels only, brightest half): dE00 + WCAG.
  rings (corner 0..3 = P1..P4 forced on every identity ring): ring mask = |on - off| > 10, split into the four rings; per ring the stroke
          (darkest 40 %) and the cream edge (lightest 20 %) vs the sunlit grass 22-45 px outside it (c_rings rule): WCAG + dE00.
Writes <dir>/e3_metrics.json and <dir>/<tier>_<spot>_masks.png. With <before_dir>, also prints a before -> after table.
"""
import sys, os, json, math, colorsys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage as nd

K709 = np.array([0.2126, 0.7152, 0.0722])
CLASSES = ("Warrior", "Speedy", "Fatty", "Assassin")
SPOTS = ("spawn", "pile", "centre")


def srgb_to_lab(rgb):
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
    xyz = lin @ M.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > (6 / 29) ** 3, np.cbrt(xyz), xyz / (3 * (6 / 29) ** 2) + 4 / 29)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def de00(c1, c2):
    (L1, a1, b1), (L2, a2, b2) = srgb_to_lab(c1), srgb_to_lab(c2)
    C1, C2 = math.hypot(a1, b1), math.hypot(a2, b2); Cb = (C1 + C2) / 2
    G = 0.5 * (1 - math.sqrt(Cb ** 7 / (Cb ** 7 + 25 ** 7)))
    a1p, a2p = (1 + G) * a1, (1 + G) * a2
    C1p, C2p = math.hypot(a1p, b1), math.hypot(a2p, b2)
    h1p = math.degrees(math.atan2(b1, a1p)) % 360; h2p = math.degrees(math.atan2(b2, a2p)) % 360
    dLp, dCp = L2 - L1, C2p - C1p
    dh = h2p - h1p
    if C1p * C2p == 0: dh = 0
    elif dh > 180: dh -= 360
    elif dh < -180: dh += 360
    dHp = 2 * math.sqrt(C1p * C2p) * math.sin(math.radians(dh / 2))
    Lbp, Cbp = (L1 + L2) / 2, (C1p + C2p) / 2
    if C1p * C2p == 0: hbp = h1p + h2p
    elif abs(h1p - h2p) <= 180: hbp = (h1p + h2p) / 2
    elif h1p + h2p < 360: hbp = (h1p + h2p + 360) / 2
    else: hbp = (h1p + h2p - 360) / 2
    T = 1 - 0.17 * math.cos(math.radians(hbp - 30)) + 0.24 * math.cos(math.radians(2 * hbp)) + 0.32 * math.cos(math.radians(3 * hbp + 6)) - 0.20 * math.cos(math.radians(4 * hbp - 63))
    dth = 30 * math.exp(-(((hbp - 275) / 25) ** 2))
    Rc = 2 * math.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7))
    Sl = 1 + 0.015 * (Lbp - 50) ** 2 / math.sqrt(20 + (Lbp - 50) ** 2); Sc = 1 + 0.045 * Cbp; Sh = 1 + 0.015 * Cbp * T
    Rt = -math.sin(math.radians(2 * dth)) * Rc
    return math.sqrt((dLp / Sl) ** 2 + (dCp / Sc) ** 2 + (dHp / Sh) ** 2 + Rt * (dCp / Sc) * (dHp / Sh))


def rel_lum(rgb):
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    return float(np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4) @ K709)


def wcag(a, b):
    la, lb = rel_lum(a), rel_lum(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


def lch(rgb):
    L, a, b = srgb_to_lab(rgb)
    return float(L), float(math.hypot(a, b)), float(math.degrees(math.atan2(b, a)) % 360)


def r1(v):
    return [round(float(x), 1) for x in v]


class Dir:
    def __init__(self, d):
        self.d = d

    def has(self, n):
        return os.path.exists(os.path.join(self.d, n))

    def load(self, n):
        return np.asarray(Image.open(os.path.join(self.d, n)).convert("RGB")).astype(np.float64)


def lit_half(px):
    L = px @ K709
    return px[L >= np.median(L)].mean(0)


def spot(D, tier, s):
    base = f"{tier}_{s}"
    a = D.load(base + ".png")
    diff = lambda n: np.abs(a - D.load(f"{base}_{n}.png")).max(-1)
    noise = nd.binary_dilation(diff("all2") > 4, iterations=4)
    pile = nd.binary_erosion(diff("nopile") > 6, iterations=2) & ~noise
    ground = nd.binary_erosion(diff("noground") > 6, iterations=2) & ~noise & ~pile
    shaded = (a @ K709 - D.load(f"{base}_nosun.png") @ K709) < 6
    grass = ground & ~shaded
    ov = (a * 0.35).astype(np.uint8); ov[grass] = (0, 200, 0); ov[ground & shaded] = (0, 80, 255); ov[pile] = (255, 255, 0)
    Image.fromarray(ov).save(os.path.join(D.d, f"{base}_masks.png"))
    g = a[grass]
    gm = g.mean(0)
    L, C, h = lch(gm)
    labs = srgb_to_lab(g[:: max(1, len(g) // 20000)])
    # Large-scale variation: 48 px blocks that are mostly sunlit grass.
    B = 48; hh, ww = (a.shape[0] // B) * B, (a.shape[1] // B) * B
    lum = (a @ K709)[:hh, :ww] * grass[:hh, :ww]
    cnt = grass[:hh, :ww].reshape(hh // B, B, ww // B, B).sum((1, 3))
    tot = lum.reshape(hh // B, B, ww // B, B).sum((1, 3))
    ok = cnt >= 0.8 * B * B
    blocks = tot[ok] / cnt[ok]
    out = {"grass": {"n": int(grass.sum()), "luma_med": round(float(np.median(g @ K709)), 1), "rgb_mean": r1(gm), "L": round(L, 1), "C": round(C, 1),
                     "h": round(h, 1), "C_med_px": round(float(np.median(np.hypot(labs[:, 1], labs[:, 2]))), 1),
                     "hsv_sat": round(colorsys.rgb_to_hsv(*(gm / 255))[1], 3),
                     "lowfreq_std": round(float(blocks.std()), 2) if len(blocks) > 4 else None, "blocks": int(len(blocks))}}
    if pile.sum() > 200:
        p = a[pile]; pm, pl = p.mean(0), lit_half(p)
        out["pile"] = {"n": int(pile.sum()), "rgb_mean": r1(pm), "rgb_lit": r1(pl), "wcag_mean": round(wcag(pm, gm), 2), "wcag_lit": round(wcag(pl, gm), 2),
                       "de00_mean": round(de00(pm, gm), 1), "de00_lit": round(de00(pl, gm), 1)}
    return out


def body_mask(a, b):
    m = np.abs(a - b).max(-1) > 25
    m = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3))) > 0
    green = (a[..., 1] > a[..., 0] + 10) & (a[..., 1] > a[..., 2])
    return m & ~green


def band_grass(img, mask, lo, hi):
    near = nd.binary_dilation(mask, iterations=hi) & ~nd.binary_dilation(mask, iterations=lo)
    px = img[near]
    green = (px[:, 1] > px[:, 0]) & (px[:, 1] > px[:, 2])
    px = px[green]
    L = px @ K709
    return px[L >= np.median(L)].mean(0) if len(px) > 50 else None


def lineup(D, tier):
    a = D.load(f"{tier}_lineup.png")
    masks = {c: body_mask(a, D.load(f"{tier}_lineup_no{c}.png")) for c in CLASSES}
    union = np.any(np.stack(list(masks.values())), 0)
    grass = band_grass(a, nd.binary_dilation(union, iterations=6), 25, 60)
    out = {"grass_band": r1(grass)}
    for c, m in masks.items():
        if m.sum() < 100:
            out[c] = None; continue
        lit = lit_half(a[m])
        out[c] = {"rgb_lit": r1(lit), "de00": round(de00(lit, grass), 1), "wcag": round(wcag(lit, grass), 2)}
    return out


def rings(D, tier):
    out = {}
    for c in range(4):
        on, off = D.load(f"{tier}_ring{c}_on.png"), D.load(f"{tier}_ring{c}_off.png")
        m = np.abs(on - off).max(-1) > 10
        lab, n = nd.label(nd.binary_dilation(m, iterations=3))
        sizes = nd.sum(m, lab, range(1, n + 1))
        comps = [i + 1 for i in np.argsort(sizes)[::-1][:4] if sizes[i] > 150]
        per = []
        for ci in comps:
            mm = m & (lab == ci)
            ring = on[mm]; L = ring @ K709; order = np.argsort(L)
            stroke = ring[order[: max(1, int(len(L) * 0.4))]].mean(0)
            edge = ring[order[-max(1, int(len(L) * 0.2)):]].mean(0)
            g = band_grass(off, mm, 22, 45)
            if g is None:
                continue
            per.append({"stroke": r1(stroke), "grass": r1(g), "stroke_wcag": round(wcag(stroke, g), 2), "stroke_de00": round(de00(stroke, g), 1),
                        "edge_wcag": round(wcag(edge, g), 2)})
        out[f"P{c + 1}"] = {"rings": per,
                            "stroke_wcag_mean": round(float(np.mean([p["stroke_wcag"] for p in per])), 2) if per else None,
                            "stroke_wcag_min": round(float(np.min([p["stroke_wcag"] for p in per])), 2) if per else None,
                            "stroke_de00_mean": round(float(np.mean([p["stroke_de00"] for p in per])), 1) if per else None}
    return out


def run(d):
    D = Dir(d)
    res = {}
    for tier in ("pc", "mobile"):
        if not D.has(f"{tier}_spawn.png"):
            continue
        r = {s: spot(D, tier, s) for s in SPOTS if D.has(f"{tier}_{s}.png")}
        if D.has(f"{tier}_lineup.png"):
            r["lineup"] = lineup(D, tier)
        if D.has(f"{tier}_ring0_on.png"):
            r["rings"] = rings(D, tier)
        res[tier] = r
    json.dump(res, open(os.path.join(d, "e3_metrics.json"), "w"), indent=1)
    return res


def table(after, before=None):
    rows = []
    def row(label, f):
        for tier in ("pc", "mobile"):
            try:
                va = f(after[tier])
            except Exception:
                va = None
            vb = None
            if before is not None:
                try:
                    vb = f(before[tier])
                except Exception:
                    vb = None
            rows.append((f"{tier:6s} {label}", vb, va))
    for s in SPOTS:
        row(f"{s} grass luma", lambda t: t[s]["grass"]["luma_med"])
        row(f"{s} grass C*", lambda t: t[s]["grass"]["C"])
        row(f"{s} grass C* px med", lambda t: t[s]["grass"]["C_med_px"])
        row(f"{s} grass h", lambda t: t[s]["grass"]["h"])
        row(f"{s} grass lowfreq std", lambda t: t[s]["grass"]["lowfreq_std"])
        row(f"{s} pile WCAG mean/lit", lambda t: f"{t[s]['pile']['wcag_mean']}/{t[s]['pile']['wcag_lit']}")
        row(f"{s} pile dE00 mean/lit", lambda t: f"{t[s]['pile']['de00_mean']}/{t[s]['pile']['de00_lit']}")
    for c in CLASSES:
        row(f"{c} vs grass dE00/WCAG", lambda t: f"{t['lineup'][c]['de00']}/{t['lineup'][c]['wcag']}")
    for p in ("P1", "P2", "P3", "P4"):
        row(f"ring {p} stroke WCAG mean/min", lambda t: f"{t['rings'][p]['stroke_wcag_mean']}/{t['rings'][p]['stroke_wcag_min']}")
        row(f"ring {p} stroke dE00", lambda t: t["rings"][p]["stroke_de00_mean"])
    for label, vb, va in rows:
        print(f"{label:40s} {str(vb):>14s} -> {str(va):>14s}" if before is not None else f"{label:40s} {str(va):>14s}")


if __name__ == "__main__":
    after = run(sys.argv[1])
    before = None
    if len(sys.argv) > 2:
        bp = os.path.join(sys.argv[2], "e3_metrics.json")
        before = json.load(open(bp)) if os.path.exists(bp) else run(sys.argv[2])
    table(after, before)
