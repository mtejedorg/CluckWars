"""Phase 8 D7 metrics: rendered pennant flag vs rendered base ring (CIEDE2000) and the flag's angle to the camera, from capture_d7_pennant.py output.

    python tools/ui_capture/d7_pennant_metrics.py <dir>

For every <tier>_v<V>_b<B>.png/.json pair: flag colour = median sRGB of the flag's projected triangles (eroded 2 px, so antialiased edges and the
pole never count); ring colour = median of the 90 projected ring samples (3x3 mean each; samples off-screen skipped). Writes <dir>/metrics.json and
<dir>/overlay_<tier>_v<V>_b<B>.png (mask + samples drawn) for the own-seat views, and prints the own-seat table plus the facing angle of all 16 pairs.
"""
import sys, os, json, math, glob
sys.stdout.reconfigure(encoding="utf-8")
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

D = os.path.abspath(sys.argv[1])


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


def hexs(rgb):
    return "#%02x%02x%02x" % tuple(int(round(x)) for x in rgb)


def measure(png, js, overlay=None):
    img = Image.open(png).convert("RGB"); a = np.asarray(img).astype(np.float64); Hh, Ww = a.shape[:2]
    d = json.load(open(js))
    out = {"camYaw": d["camYaw"], "bases": {}}
    ov = img.copy() if overlay else None
    od = ImageDraw.Draw(ov) if ov else None
    for b in d["bases"]:
        m = Image.new("L", (Ww, Hh), 0); md = ImageDraw.Draw(m)
        for t in b["tris"]:
            md.polygon([tuple(p) for p in t], fill=255)
        m = m.filter(ImageFilter.MinFilter(5))
        mk = np.asarray(m) > 0
        rec = {"palette": "#" + b["palette"], "flagVsCameraDeg": b["flagVsCameraDeg"], "flagAzimuthDeg": b["flagAzimuthDeg"], "flagShader": b["flagShader"], "flagBaseColor": b["flagBaseColor"], "flagPx": int(mk.sum())}
        if mk.sum() >= 30:
            flag = np.median(a[mk], axis=0); rec["flag"] = hexs(flag); rec["flagLab"] = srgb_to_lab(flag).tolist()
        samples = []
        for (x, y) in b["ring"]:
            xi, yi = int(round(x)), int(round(y))
            if 2 <= xi < Ww - 2 and 2 <= yi < Hh - 2:
                samples.append(a[yi - 1:yi + 2, xi - 1:xi + 2].reshape(-1, 3).mean(0))
                if od: od.ellipse([xi - 2, yi - 2, xi + 2, yi + 2], outline=(255, 0, 255))
        rec["ringN"] = len(samples)
        if len(samples) >= 20:
            ring = np.median(np.array(samples), axis=0); rec["ring"] = hexs(ring); rec["ringLab"] = srgb_to_lab(ring).tolist()
            rec["ringSpreadDe"] = float(np.percentile([de00(srgb_to_lab(s), srgb_to_lab(ring)) for s in samples], 80))
            labs = srgb_to_lab(np.array(samples)); lit = labs[labs[:, 0] >= np.median(labs[:, 0])]   # the sunlit half of the ring: the unlit flag is never in shadow
            rec["ringLit"] = hexs(np.median(np.array(samples)[labs[:, 0] >= np.median(labs[:, 0])], axis=0)); rec["ringLitLab"] = np.median(lit, axis=0).tolist()
        if "flag" in rec and "ring" in rec:
            rec["dE00"] = de00(rec["flagLab"], rec["ringLab"]); rec["dE00Lit"] = de00(rec["flagLab"], rec["ringLitLab"])
        if od and mk.any():
            ys, xs = np.nonzero(mk); od.rectangle([xs.min() - 3, ys.min() - 3, xs.max() + 3, ys.max() + 3], outline=(255, 255, 0))
        out["bases"][str(b["corner"])] = rec
    if ov:
        ov.save(overlay)
    return out


def main():
    res = {}
    for js in sorted(glob.glob(os.path.join(D, "*_v?_b?.json"))):
        name = os.path.basename(js)[:-5]; tier, v, b = name.split("_"); v = int(v[1:]); b = int(b[1:])
        m = measure(js[:-5] + ".png", js, overlay=os.path.join(D, f"overlay_{name}.png") if v == b else None)
        res.setdefault(tier, {})[f"v{v}_b{b}"] = m["bases"]
    json.dump(res, open(os.path.join(D, "metrics.json"), "w"), indent=1)
    for tier, pairs in res.items():
        print(f"== {tier}: own base (viewer V sees base V): flag vs ring, rendered")
        for v in range(4):
            r = pairs.get(f"v{v}_b{v}", {}).get(str(v))
            if not r: continue
            print(f"  seat {v}: palette {r['palette']}  flag {r.get('flag','-')} ({r['flagPx']} px)  ring {r.get('ring','-')} lit {r.get('ringLit','-')}  dE00 all {r.get('dE00', float('nan')):.1f} / lit {r.get('dE00Lit', float('nan')):.1f}  azimuth {r['flagAzimuthDeg']:.0f} deg  shader {r['flagShader']}")
        print(f"== {tier}: flag plane vs camera view plane, yaw only (deg; 0 = face-on, 90 = edge-on), rows = viewer seat, cols = base")
        for v in range(4):
            print("  viewer %d: " % v + "  ".join("%5.1f" % pairs[f"v{v}_b{b}"][str(b)]["flagAzimuthDeg"] for b in range(4) if f"v{v}_b{b}" in pairs))
        print(f"== {tier}: dE00 flag vs LIT ring, every pair (rows = viewer, cols = base)")
        for v in range(4):
            print("  viewer %d: " % v + "  ".join("%5.1f" % pairs[f"v{v}_b{b}"][str(b)].get("dE00Lit", float("nan")) for b in range(4) if f"v{v}_b{b}" in pairs))


if __name__ == "__main__":
    main()
