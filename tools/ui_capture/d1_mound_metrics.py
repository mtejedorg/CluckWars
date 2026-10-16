"""Phase 8 D1 / E4 metrics for capture_d1_mound.py output.  usage: python tools/ui_capture/d1_mound_metrics.py <dir>

Masks: mound = |frame - nomound| > 4/255, Fatty = |frame - nofatty| > 4/255, both eroded 2 px, minus motion noise (|frame - all2| > 6, dilated 4 px),
mound minus the Fatty. Lit top = mean of the brightest 25 % (Rec.709 luma on the 8-bit values) of the mound. Grain = std of high-pass luma
(pixel minus 7x7 box mean) over the lit half. E4: the Fatty's lit body (brightest 50 %) vs the mound's lit half (same rule): hue, CIEDE2000
(sRGB D65 -> CIELAB), luma (Rec.709 weights on the 8-bit values) and relative luminance Y (Rec.709 on linearised sRGB, 0-100).
Writes <dir>/metrics.json and <dir>/<shot>_masks.png (red mound, green Fatty)."""
import sys, os, json, colorsys, math
import numpy as np
from PIL import Image, ImageFilter

D = sys.argv[1]


def load(n):
    return np.asarray(Image.open(os.path.join(D, n)).convert("RGB")).astype(np.float64)


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


def hue(rgb):
    h, s, v = colorsys.rgb_to_hsv(*(np.asarray(rgb) / 255.0)); return h * 360, s, v


def rel_lum(rgb):  # CIE Y of the sRGB colour, 0..100
    c = np.asarray(rgb, dtype=np.float64) / 255.0; lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return float(100 * (lin @ np.array([0.2126, 0.7152, 0.0722])))


def luma709(rgb):  # on the sRGB-encoded values (what the eye reads off the screen), 0..255
    r, g, b = rgb; return 0.2126 * r + 0.7152 * g + 0.0722 * b


def mask_of(a, b, thr=4):
    m = np.abs(a - b).max(-1) > thr
    # erode by 1 px (edge AA pixels are a blend with the ground)
    im = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(5))
    return np.asarray(im) > 0


def stats(img, m, top_frac):
    px = img[m]; L = px @ np.array([0.2126, 0.7152, 0.0722])
    thr = np.quantile(L, 1 - top_frac); sel = px[L >= thr]
    return px, L, sel


out = {}
GOLD = (245, 200, 66)
for tier in ("pc", "mobile"):
    for which in ("home", "centre", "centrelow"):
        k = f"{tier}_{which}"
        if not os.path.exists(os.path.join(D, k + ".png")): continue
        A = load(k + ".png"); M = mask_of(A, load(k + "_nomound.png"))
        Fm = mask_of(A, load(k + "_nofatty.png")) if os.path.exists(os.path.join(D, k + "_nofatty.png")) else np.zeros(M.shape, bool)
        noise = np.zeros(M.shape, bool)
        if os.path.exists(os.path.join(D, k + "_all2.png")):
            nz = np.abs(A - load(k + "_all2.png")).max(-1) > 6
            noise = np.asarray(Image.fromarray((nz * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9))) > 0
        M &= ~Fm & ~noise; Fm &= ~noise
        px, L, top = stats(A, M, 0.25)
        lit_mean = top.mean(0)
        # grain detail: high-pass luminance (pixel - 7x7 box blur) std over the lit half of the mound
        Limg = A @ np.array([0.2126, 0.7152, 0.0722])
        blur = np.asarray(Image.fromarray(np.clip(Limg, 0, 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(3))).astype(np.float64)
        lit_half = M & (Limg >= np.quantile(L, 0.5))
        hp = (Limg - blur)[lit_half]
        clipped = float((px.max(-1) >= 254).mean())
        h, s, v = hue(lit_mean)
        r = {"moundPixels": int(M.sum()), "litTopMeanRGB": [round(float(x), 1) for x in lit_mean], "litTopHueDeg": round(h, 1), "litTopSat": round(s, 3),
             "litTopR_p95": round(float(np.quantile(top[:, 0], 0.95)), 1), "litTopR_max": float(top[:, 0].max()), "moundMeanRGB": [round(float(x), 1) for x in px.mean(0)],
             "moundLumaStd": round(float(L.std()), 2), "litHalfLumaStd": round(float(Limg[lit_half].std()), 2), "grainHighPassStd": round(float(hp.std()), 2),
             "fracAnyChannel>=254": round(clipped, 4), "goldTargetHue": round(hue(GOLD)[0], 1)}
        if Fm.sum() > 50:
            fpx = A[Fm]; fL = fpx @ np.array([0.2126, 0.7152, 0.0722]); flit = fpx[fL >= np.quantile(fL, 0.5)]; fmean = flit.mean(0)
            mound_ref = px[L >= np.quantile(L, 0.5)].mean(0)  # lit half of the mound, same selection rule as the body
            fh = hue(fmean)[0]; mh = hue(mound_ref)[0]
            r["fatty"] = {"pixels": int(Fm.sum()), "litBodyMeanRGB": [round(float(x), 1) for x in fmean], "hueDeg": round(fh, 1), "luma709": round(luma709(fmean), 1), "relLumY": round(rel_lum(fmean), 1),
                          "litTop25RGB": [round(float(x), 1) for x in fpx[fL >= np.quantile(fL, 0.75)].mean(0)],
                          "moundLitHalfRGB": [round(float(x), 1) for x in mound_ref], "moundHueDeg": round(mh, 1), "moundLuma709": round(luma709(mound_ref), 1), "moundRelLumY": round(rel_lum(mound_ref), 1),
                          "hueDiffDeg": round(abs((fh - mh + 180) % 360 - 180), 1),
                          "dE00": round(de00(srgb_to_lab(fmean), srgb_to_lab(mound_ref)), 2),
                          "dE00_vsLitTop": round(de00(srgb_to_lab(fmean), srgb_to_lab(lit_mean)), 2)}
            Image.fromarray((np.stack([M, Fm, np.zeros_like(M)], -1) * 255).astype(np.uint8)).save(os.path.join(D, k + "_masks.png"))
        out[k] = r
json.dump(out, open(os.path.join(D, "metrics.json"), "w"), indent=1)
for k, r in out.items():
    f = r.get("fatty", {})
    print(f"{k}: litTop {r['litTopMeanRGB']} hue {r['litTopHueDeg']} R p95 {r['litTopR_p95']} max {r['litTopR_max']} clip {r['fracAnyChannel>=254']} | std mound {r['moundLumaStd']} litHalf {r['litHalfLumaStd']} hp {r['grainHighPassStd']} | px {r['moundPixels']}")
    if f: print(f"   Fatty {f['litBodyMeanRGB']} hue {f['hueDeg']} luma {f['luma709']} Y {f['relLumY']} vs mound {f['moundLitHalfRGB']} hue {f['moundHueDeg']} luma {f['moundLuma709']} Y {f['moundRelLumY']} | dHue {f['hueDiffDeg']} dE00 {f['dE00']} (vs lit top {f['dE00_vsLitTop']})")
