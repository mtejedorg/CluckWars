"""Phase 8 D2 metrics for capture_d2_tones.py output.  usage: python tools/ui_capture/d2_tone_metrics.py <dir>

Per tier and spot, masks from that tier's own render pairs (PC variants reuse the PC masks: same frame geometry, everything frozen):
    fence  = |frame - nofence|  > 6/255 (eroded 2 px)       ground = |frame - noground| > 6/255 (eroded 2 px), minus fence and pile
    pile   = |frame - nopile|   > 6/255 (eroded 2 px)       noise  = |frame - all2| > 4/255 (dilated 4 px), removed from every mask
    shaded = luma(frame) - luma(nosun) < 6/255 (no direct sun reaches the pixel: facing away or in shadow)
    shadow = ground & shaded      grass = ground & ~shaded      fence_shaded = fence & shaded
Value per region = median Rec.709 luma (8-bit values, the D7/D8 method) and median CIELAB L*; fence shaded faces also p10 L*.
Ratio column = PC variant / Mobile. Writes <dir>/d2_metrics.json and <dir>/<tier>_<spot>_masks.png.
"""
import sys, os, json
import numpy as np
from PIL import Image
from scipy import ndimage as nd

D = sys.argv[1]
REGIONS = ("fence", "fence_shaded", "shadow", "grass", "pile")


def load(n):
    return np.asarray(Image.open(os.path.join(D, n)).convert("RGB")).astype(np.float64)


def luma(px):
    return px @ np.array([0.2126, 0.7152, 0.0722])


def lstar(px):
    c = px / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    y = lin @ np.array([0.2126729, 0.7151522, 0.0721750])
    f = np.where(y > (6 / 29) ** 3, np.cbrt(y), y / (3 * (6 / 29) ** 2) + 4 / 29)
    return 116 * f - 16


def masks(tier, spot):
    base = f"{tier}_{spot}"
    a = load(base + ".png")
    diff = lambda n: np.abs(a - load(f"{base}_{n}.png")).max(-1)
    noise = nd.binary_dilation(diff("all2") > 4, iterations=4)
    m = {k: nd.binary_erosion(diff(k2) > 6, iterations=2) & ~noise for k, k2 in (("fence", "nofence"), ("ground", "noground"), ("pile", "nopile"))}
    m["ground"] &= ~m["fence"] & ~m["pile"]
    shaded = (luma(a) - luma(load(f"{base}_nosun.png"))) < 6
    m["shadow"] = m["ground"] & shaded
    m["grass"] = m["ground"] & ~shaded
    m["fence_shaded"] = m["fence"] & shaded
    ov = (a * 0.35).astype(np.uint8)
    for k, col in (("grass", (0, 200, 0)), ("shadow", (0, 80, 255)), ("fence", (255, 160, 0)), ("fence_shaded", (255, 0, 255)), ("pile", (255, 255, 0))):
        ov[m[k]] = col
    Image.fromarray(ov).save(os.path.join(D, f"{base}_masks.png"))
    return m


def stats(img, m):
    out = {}
    for k in REGIONS:
        px = img[m[k]]
        if len(px) < 200:
            out[k] = None; continue
        L = lstar(px)
        out[k] = {"n": int(len(px)), "luma_med": round(float(np.median(luma(px))), 1), "L_med": round(float(np.median(L)), 1),
                  "L_p10": round(float(np.percentile(L, 10)), 1), "L_p05": round(float(np.percentile(L, 5)), 1)}
    return out


def run():
    cands = []
    cf = os.path.join(D, "cands.txt")
    if os.path.exists(cf):
        cands = [c for c in open(cf).read().split("\n") if c.strip()]
    res = {}
    for spot in ("corner", "pile"):
        if not os.path.exists(os.path.join(D, f"mobile_{spot}.png")):
            continue
        mm, mp = masks("mobile", spot), masks("pc", spot)
        r = {"mobile": stats(load(f"mobile_{spot}.png"), mm), "pc": stats(load(f"pc_{spot}.png"), mp)}
        for v in ("pcnopost", "pcraw") + tuple(f"pccand{i}" for i in range(len(cands))):
            if os.path.exists(os.path.join(D, f"{v}_{spot}.png")):
                r[v] = stats(load(f"{v}_{spot}.png"), mp)
        res[spot] = r
        print(f"\n== {spot} ==   (luma median, ratio vs Mobile; L* median; fence_shaded also L* p10)")
        print(f"{'variant':10s} " + " ".join(f"{k:>24s}" for k in REGIONS))
        for v, s in r.items():
            label = v if not v.startswith("pccand") else v + "[" + cands[int(v[6:])] + "]"
            cells = []
            for k in REGIONS:
                if s[k] is None or r["mobile"][k] is None:
                    cells.append(f"{'-':>24s}"); continue
                ratio = s[k]["luma_med"] / max(r["mobile"][k]["luma_med"], 1e-6)
                extra = f" p10 {s[k]['L_p10']:4.1f}" if k == "fence_shaded" else ""
                cells.append(f"{s[k]['luma_med']:5.1f} x{ratio:4.2f} L{s[k]['L_med']:4.1f}{extra}".rjust(24))
            print(f"{label[:34]:10s} " + " ".join(cells))
    res["cands"] = cands
    json.dump(res, open(os.path.join(D, "d2_metrics.json"), "w"), indent=1)


run()
