"""Phase 8 D3 metrics for capture_d3_mound.py output.  usage: python tools/ui_capture/d3_mound_metrics.py <dir>

Per shot <tier>_<pile><fill> (pile home | centre, fill "" | mid | low | empty):
  mask      mound = |frame - nomound| > 4/255 (eroded), minus the Fatty and motion noise (same rules as d1_mound_metrics.py)
  litTop    D1 rule: mean RGB of the brightest 25 % (Rec.709 luma) of the mound, hue, R, fraction clipped (any channel >= 254)
  grain     D1 rule: std of high-pass luma (pixel minus its 7x7 box mean) over the lit half
  lit/shaded  from the _white frame (mound drawn white + untextured, same tint and light = lighting only): the brightest 30 %
            of the white mound = LIT side, darkest 30 % = SHADED side; reported = the shipped frame's mean RGB / luma / hue there,
            and lit/shaded luma ratio
  dip/crest from the _probe frame (unlit probe texture: R = baked dip term, G = height, B = kernels): dip = R > 0.55,
            crest = G > 0.75 and R < 0.1 and B < 0.3 (the Mobile tier's probe frame is used for both tiers: no post-processing on it). Reported: shipped luma on each and the ratio, plus the albedo-only ratio
            (shipped / white per pixel, i.e. lighting divided out). Before D3 the same probe classifies the same pixels.
  kernels   B > 0.5 pixels: luma vs the crest ring around them (local contrast)
  Fatty     full fill: D1 / E4 rule, lit body (brightest 50 %) vs the mound's lit half: hue gap, CIEDE2000
Writes <dir>/metrics_d3.json."""
import sys, os, json, colorsys, math
import numpy as np
from PIL import Image, ImageFilter

D = sys.argv[1]
W709 = np.array([0.2126, 0.7152, 0.0722])


def load(n):
    return np.asarray(Image.open(os.path.join(D, n)).convert("RGB")).astype(np.float64)


def has(n):
    return os.path.exists(os.path.join(D, n))


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
    return colorsys.rgb_to_hsv(*(np.asarray(rgb) / 255.0))[0] * 360


def mask_of(a, b, thr=4):
    m = np.abs(a - b).max(-1) > thr
    return np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(5))) > 0


def rgb(x):
    return [round(float(v), 1) for v in x]


out = {}
for tier in ("pc", "mobile"):
    for pile in ("home", "centre"):
        for fill in ("", "mid", "low", "empty"):
            k = f"{tier}_{pile}{fill}"
            if not has(k + ".png"): continue
            A = load(k + ".png"); M = mask_of(A, load(k + "_nomound.png"))
            Fm = mask_of(A, load(k + "_nofatty.png")) if has(k + "_nofatty.png") else np.zeros(M.shape, bool)
            noise = np.zeros(M.shape, bool)
            if has(k + "_all2.png"):
                nz = np.abs(A - load(k + "_all2.png")).max(-1) > 6
                noise = np.asarray(Image.fromarray((nz * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9))) > 0
            M &= ~Fm & ~noise; Fm &= ~noise
            Limg = A @ W709; px = A[M]; L = Limg[M]
            top = px[L >= np.quantile(L, 0.75)]; lit_top = top.mean(0)
            blur = np.asarray(Image.fromarray(np.clip(Limg, 0, 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(3))).astype(np.float64)
            lit_half = M & (Limg >= np.quantile(L, 0.5))
            r = {"moundPixels": int(M.sum()), "litTopRGB": rgb(lit_top), "litTopHue": round(hue(lit_top), 1), "litTopR": round(float(lit_top[0]), 1),
                 "clipped": round(float((px.max(-1) >= 254).mean()), 4), "grainStd": round(float((Limg - blur)[lit_half].std()), 2),
                 "meanLuma": round(float(L.mean()), 1)}
            if has(k + "_white.png"):
                Wl = load(k + "_white.png") @ W709
                wl = Wl[M]; lit = M & (Wl >= np.quantile(wl, 0.7)); sh = M & (Wl <= np.quantile(wl, 0.3))
                lr, sr = A[lit].mean(0), A[sh].mean(0)
                r.update({"litSideRGB": rgb(lr), "litSideLuma": round(float(Limg[lit].mean()), 1), "shadedSideRGB": rgb(sr), "shadedSideLuma": round(float(Limg[sh].mean()), 1),
                          "shadedSideHue": round(hue(sr), 1), "litOverShaded": round(float(Limg[lit].mean() / Limg[sh].mean()), 3),
                          "whiteLitOverShaded": round(float(Wl[lit].mean() / Wl[sh].mean()), 3)})
                pk = f"mobile_{pile}{fill}_probe.png"   # the Mobile probe (no post: raw probe values); same camera + size -> same pixels on PC
                if has(pk):
                    P = load(pk) / 255.0
                    dip = M & (P[..., 0] > 0.55); crest = M & (P[..., 1] > 0.75) & (P[..., 0] < 0.1) & (P[..., 2] < 0.3)
                    alb = Limg / np.maximum(Wl, 1)
                    if dip.sum() > 30 and crest.sum() > 30:
                        r.update({"dipPx": int(dip.sum()), "crestPx": int(crest.sum()), "dipLuma": round(float(Limg[dip].mean()), 1), "crestLuma": round(float(Limg[crest].mean()), 1),
                                  "dipOverCrest": round(float(Limg[dip].mean() / Limg[crest].mean()), 3),
                                  "dipOverCrestAlbedo": round(float(alb[dip].mean() / alb[crest].mean()), 3), "dipRGB": rgb(A[dip].mean(0)), "dipHue": round(hue(A[dip].mean(0)), 1)})
                    ker = M & (P[..., 2] > 0.5)
                    if ker.sum() > 5:
                        ring = np.asarray(Image.fromarray((ker * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9))) > 0
                        ring &= M & ~ker & (P[..., 2] < 0.05)
                        r.update({"kernelPx": int(ker.sum()), "kernelLuma": round(float(Limg[ker].mean()), 1), "kernelRingLuma": round(float(Limg[ring].mean()), 1) if ring.sum() else None})
            if Fm.sum() > 50:
                fpx = A[Fm]; fL = fpx @ W709; fmean = fpx[fL >= np.quantile(fL, 0.5)].mean(0)
                mref = px[L >= np.quantile(L, 0.5)].mean(0)
                r["fatty"] = {"bodyRGB": rgb(fmean), "moundLitHalfRGB": rgb(mref), "hueGap": round(abs((hue(fmean) - hue(mref) + 180) % 360 - 180), 1),
                              "dE00": round(de00(srgb_to_lab(fmean), srgb_to_lab(mref)), 2)}
            out[k] = r
json.dump(out, open(os.path.join(D, "metrics_d3.json"), "w"), indent=1)
for k, r in out.items():
    f = r.get("fatty")
    print(f"{k}: litTop {r['litTopRGB']} hue {r['litTopHue']} clip {r['clipped']} grain {r['grainStd']} mean {r['meanLuma']} | lit {r.get('litSideLuma')} shaded {r.get('shadedSideLuma')} "
          f"(hue {r.get('shadedSideHue')}) ratio {r.get('litOverShaded')} | dip/crest {r.get('dipOverCrest')} albedo {r.get('dipOverCrestAlbedo')} dip hue {r.get('dipHue')} | "
          f"kern {r.get('kernelLuma')} vs {r.get('kernelRingLuma')}" + (f" | Fatty dE00 {f['dE00']} hue gap {f['hueGap']}" if f else ""))
