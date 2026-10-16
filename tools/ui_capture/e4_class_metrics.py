"""Phase 8 E4 metrics: class body colours at the match camera.  usage: python tools/ui_capture/e4_class_metrics.py <dir>

Works on two kinds of input in <dir>:
  * capture_e4_lineup.py output (<tier>_lineup.png + _no<Class>.png + _all2.png): per class body mask, lit-half mean (brightest 50 % by Rec.709
    luma, the same rule d1_mound_metrics.py uses for the Fatty), hue, and CIEDE2000 Fatty vs every other class.
  * capture_d1_mound.py output (<tier>_<pile>.png + _nofatty / _nomound / _all2): the Fatty vs the full mound, whole body AND per region.
Fatty regions (rendered pixels, HSV on the 8-bit values): 'red' = comb / wattle (hue < 12 or > 340, sat > 0.6), 'back' = hue < 33 (the
orange-brown back, wings, tail; beak and feet fall here too), 'light' = everything else (the cream breast / face). Region means use ALL the
region's pixels (lit and shaded), so the region numbers are a different statistic from the lit-half body mean.
Body masks: see body_mask() (stricter than d1_mound_metrics.py, so the Fatty numbers here differ slightly from its metrics.json).
Writes <dir>/e4_metrics.json.
"""
import sys, os, json, colorsys
import numpy as np
from PIL import Image, ImageFilter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
# Colour helpers shared with d1_mound_metrics.py (that module runs its pass at import, so take only its function block).
_src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "d1_mound_metrics.py"), encoding="utf-8").read()
_ns = {}
exec(compile(_src[_src.index("def load(n)"):_src.index("out = {}")], "d1_mound_metrics_helpers", "exec"), {"np": np, "math": __import__("math"), "colorsys": colorsys, "Image": Image, "ImageFilter": ImageFilter, "os": os, "D": None, "__builtins__": __builtins__} | _ns, _ns)
srgb_to_lab, de00, hue, mask_of = _ns["srgb_to_lab"], _ns["de00"], _ns["hue"], _ns["mask_of"]

D = sys.argv[1]
K709 = np.array([0.2126, 0.7152, 0.0722])


def load(n):
    return np.asarray(Image.open(os.path.join(D, n)).convert("RGB")).astype(np.float64)


def noise_of(a, k):
    p = os.path.join(D, k + "_all2.png")
    if not os.path.exists(p):
        return np.zeros(a.shape[:2], bool)
    nz = np.abs(a - load(k + "_all2.png")).max(-1) > 6
    return np.asarray(Image.fromarray((nz * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9))) > 0


def body_mask(a, b):
    """Body pixels = |frame - body-hidden frame| > 25 (the 4/255 rule of d1_mound_metrics.py also picks up the faint grass change under the
    feet), eroded 1 px, minus grass-green pixels (the yellow-green food sparkles drawn over the bird; no class body is green)."""
    m = np.abs(a - b).max(-1) > 25
    m = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3))) > 0
    green = (a[..., 1] > a[..., 0] + 10) & (a[..., 1] > a[..., 2])
    return m & ~green


def lit_half(px):
    L = px @ K709
    return px[L >= np.quantile(L, 0.5)].mean(0)


def r1(v):
    return [round(float(x), 1) for x in v]


def regions(px):
    hsv = np.array([colorsys.rgb_to_hsv(*(p / 255.0)) for p in px])
    h, s = hsv[:, 0] * 360, hsv[:, 1]
    red = ((h < 12) | (h > 340)) & (s > 0.6)
    back = ~red & (h < 33)
    light = ~red & ~back
    out = {}
    for name, m in (("red", red), ("back", back), ("light", light)):
        if m.sum() == 0:
            out[name] = {"frac": 0.0}
            continue
        mean = px[m].mean(0)
        out[name] = {"frac": round(float(m.mean()), 3), "meanRGB": r1(mean), "hueDeg": round(hue(mean)[0], 1), "luma709": round(float(mean @ K709), 1)}
    return out


def dE(a, b):
    return round(de00(srgb_to_lab(a), srgb_to_lab(b)), 2)


def dHue(a, b):
    ha, hb = hue(a)[0], hue(b)[0]
    return round(abs((ha - hb + 180) % 360 - 180), 1)


res = {}
for tier in ("pc", "mobile"):
    k = f"{tier}_lineup"
    if os.path.exists(os.path.join(D, k + ".png")):
        A = load(k + ".png"); nz = noise_of(A, k); cls = {}
        for c in ("Warrior", "Speedy", "Fatty", "Assassin"):
            m = body_mask(A, load(f"{k}_no{c}.png")) & ~nz
            if m.sum() < 50:
                cls[c] = {"pixels": int(m.sum())}
                continue
            lh = lit_half(A[m])
            cls[c] = {"pixels": int(m.sum()), "litHalfRGB": r1(lh), "hueDeg": round(hue(lh)[0], 1), "_lh": lh, "_px": A[m]}
        r = {}
        if "_lh" in cls.get("Fatty", {}):
            f = cls["Fatty"]["_lh"]
            r["fattyRegions"] = regions(cls["Fatty"]["_px"])
            for c in ("Speedy", "Warrior", "Assassin"):
                if "_lh" in cls[c]:
                    r[f"Fatty_vs_{c}"] = {"dE00": dE(f, cls[c]["_lh"]), "dHueDeg": dHue(f, cls[c]["_lh"])}
            if "_lh" in cls["Speedy"]:
                bk = r["fattyRegions"].get("back", {}).get("meanRGB")
                if bk:
                    # the back region against Speedy's whole body (same all-pixel statistic for both)
                    r["FattyBack_vs_Speedy_allPx"] = dE(bk, cls["Speedy"]["_px"].mean(0))
                    r["FattyBack_vs_Warrior_allPx"] = dE(bk, cls["Warrior"]["_px"].mean(0)) if "_px" in cls["Warrior"] else None
        r["classes"] = {c: {kk: vv for kk, vv in v.items() if not kk.startswith("_")} for c, v in cls.items()}
        res[k] = r
    for which in ("home", "centre"):
        k = f"{tier}_{which}"
        if not os.path.exists(os.path.join(D, k + "_nofatty.png")):
            continue
        A = load(k + ".png"); nz = noise_of(A, k)
        Fm = body_mask(A, load(k + "_nofatty.png")) & ~nz
        Mm = mask_of(A, load(k + "_nomound.png")) & ~nz & ~Fm
        mound = lit_half(A[Mm]); mound_all = A[Mm].mean(0)
        f = lit_half(A[Fm]); reg = regions(A[Fm])
        r = {"fattyLitHalfRGB": r1(f), "fattyHue": round(hue(f)[0], 1), "moundLitHalfRGB": r1(mound), "moundHue": round(hue(mound)[0], 1),
             "dE00_litHalf": dE(f, mound), "dHue_litHalf": dHue(f, mound),
             "fattyAllPxRGB": r1(A[Fm].mean(0)), "moundAllPxRGB": r1(mound_all),
             "dE00_allPx": dE(A[Fm].mean(0), mound_all), "dHue_allPx": dHue(A[Fm].mean(0), mound_all), "regions": reg}
        for name in ("back", "light"):
            if reg[name].get("meanRGB"):
                r[f"{name}_vs_mound"] = {"dE00": dE(reg[name]["meanRGB"], mound_all), "dHueDeg": dHue(reg[name]["meanRGB"], mound_all),
                                         "dLuma": round(float(np.array(reg[name]["meanRGB"]) @ K709 - mound_all @ K709), 1)}
        res[k] = r

json.dump(res, open(os.path.join(D, "e4_metrics.json"), "w"), indent=1)
print(json.dumps(res, indent=1))
