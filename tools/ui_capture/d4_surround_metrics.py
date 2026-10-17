"""Phase 8 D4 metrics for capture_d4_surround.py output.  usage: python tools/ui_capture/d4_surround_metrics.py <dir> [<before_dir>]

Per tier (pc, mobile) and spot (spawn, corner), masks from the render pairs (D2 rules):
    noise    = |frame - all2| > 4, dilated 4 px
    surround = |frame - nosurround| > 6 (eroded 2 px) minus noise minus hedge      arena = same with noground
    hedge    = |frame - nohedge| > 6 (eroded 2 px) minus noise
    lit      = luma(frame) - luma(nosun) >= 6
  surround / arena (sunlit pixels): median Rec.709 luma (8-bit), mean RGB, CIELAB L* / C* / h of the mean, HSV saturation of the mean,
      'lowfreq' = std of the mean luma of 32 px blocks that are >= 80 % of the class (repetition / clumping at screen scale),
      'hf' = mean of the per-block luma std (the texture contrast the eye reads inside a 32 px block).
  hedge (all its pixels, lit + shaded): luma mean / std, 'hf' (mean per-16 px-block luma std = leaf/clump contrast without the side-vs-crown
      lighting step) and the same for the top 30 % of its rows (the crown / top edge).
  surround vs arena: luma ratio, dC*, dL*.
Writes <dir>/d4_metrics.json and <dir>/<tier>_<spot>_masks.png. With <before_dir>, prints a before -> after table.
"""
import sys, os, json, math, colorsys
import numpy as np
from PIL import Image
from scipy import ndimage as nd

K709 = np.array([0.2126, 0.7152, 0.0722])
SPOTS = ("spawn", "corner")


def srgb_to_lab(rgb):
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
    xyz = lin @ M.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > (6 / 29) ** 3, np.cbrt(xyz), xyz / (3 * (6 / 29) ** 2) + 4 / 29)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def lch(rgb):
    L, a, b = srgb_to_lab(rgb)
    return float(L), float(math.hypot(a, b)), float(math.degrees(math.atan2(b, a)) % 360)


def load(d, n):
    return np.asarray(Image.open(os.path.join(d, n)).convert("RGB")).astype(np.float64)


def blocks(lum, mask, B, frac=0.8):
    hh, ww = (lum.shape[0] // B) * B, (lum.shape[1] // B) * B
    m = mask[:hh, :ww]; l = lum[:hh, :ww] * m
    cnt = m.reshape(hh // B, B, ww // B, B).sum((1, 3))
    s1 = l.reshape(hh // B, B, ww // B, B).sum((1, 3)); s2 = (l * l).reshape(hh // B, B, ww // B, B).sum((1, 3))
    ok = cnt >= frac * B * B
    mean = s1[ok] / cnt[ok]; var = np.maximum(s2[ok] / cnt[ok] - mean ** 2, 0)
    return mean, np.sqrt(var)


def stats(a, lum, m):
    px = a[m]
    if len(px) < 200:
        return None
    gm = px.mean(0); L, C, h = lch(gm)
    bm, bs = blocks(lum, m, 32)
    return {"n": int(m.sum()), "luma_med": round(float(np.median(px @ K709)), 1), "rgb_mean": [round(float(x), 1) for x in gm], "L": round(L, 1), "C": round(C, 1),
            "h": round(h, 1), "hsv_sat": round(colorsys.rgb_to_hsv(*(gm / 255))[1], 3),
            "lowfreq_std": round(float(bm.std()), 2) if len(bm) > 4 else None, "hf": round(float(bs.mean()), 2) if len(bs) else None, "blocks": int(len(bm))}


def hedge_stats(lum, m):
    if m.sum() < 200:
        return None
    v = lum[m]
    _, bs = blocks(lum, m, 16, 0.9)
    rows = np.where(m.any(1))[0]
    out = {"n": int(m.sum()), "luma_mean": round(float(v.mean()), 1), "luma_std": round(float(v.std()), 2), "hf16": round(float(bs.mean()), 2) if len(bs) else None}
    # top edge: per column, the top 25 px of hedge pixels (the crown edge / scallop row as the camera sees it)
    top = np.zeros_like(m)
    cols = np.where(m.any(0))[0]
    for x in cols:
        ys = np.where(m[:, x])[0]
        top[ys[:25], x] = True
    tv = lum[top]
    out["top_luma_std"] = round(float(tv.std()), 2)
    # top-edge profile roughness: std along x of the mean luma of each column's top band (the scallop rhythm), high-passed by a 60 px box
    prof = np.array([lum[top[:, x], x].mean() for x in cols]) if len(cols) else np.array([0.0])
    if len(prof) > 80:
        hp = prof - nd.uniform_filter1d(prof, 60)
        out["top_profile_hp_std"] = round(float(hp.std()), 2)
    return out


def spot(d, tier, s):
    base = f"{tier}_{s}"
    a = load(d, base + ".png")
    lum = a @ K709
    diff = lambda n: np.abs(a - load(d, f"{base}_{n}.png")).max(-1)
    noise = nd.binary_dilation(diff("all2") > 4, iterations=4)
    hedge = nd.binary_erosion(diff("nohedge") > 6, iterations=2) & ~noise
    lit = (lum - load(d, f"{base}_nosun.png") @ K709) >= 6
    sur = nd.binary_erosion(diff("nosurround") > 6, iterations=2) & ~noise & ~hedge
    arena = nd.binary_erosion(diff("noground") > 6, iterations=2) & ~noise & ~hedge & ~sur
    ov = (a * 0.35).astype(np.uint8); ov[sur & lit] = (0, 90, 255); ov[sur & ~lit] = (0, 40, 120); ov[arena & lit] = (0, 200, 0); ov[hedge] = (255, 200, 0)
    Image.fromarray(ov).save(os.path.join(d, f"{base}_masks.png"))
    out = {"surround": stats(a, lum, sur & lit), "surround_all": stats(a, lum, sur), "arena": stats(a, lum, arena & lit), "hedge": hedge_stats(lum, hedge)}
    s_, g_ = out["surround"], out["arena"]
    if s_ and g_:
        out["surround_vs_arena"] = {"luma_ratio": round(s_["luma_med"] / g_["luma_med"], 3), "dL": round(s_["L"] - g_["L"], 1), "dC": round(s_["C"] - g_["C"], 1),
                                    "sat_ratio": round(s_["hsv_sat"] / g_["hsv_sat"], 3)}
    return out


def run(d):
    res = {}
    for tier in ("pc", "mobile"):
        r = {s: spot(d, tier, s) for s in SPOTS if os.path.exists(os.path.join(d, f"{tier}_{s}.png"))}
        if r:
            res[tier] = r
    json.dump(res, open(os.path.join(d, "d4_metrics.json"), "w"), indent=1)
    return res


def flat(res):
    out = {}
    for tier, r in res.items():
        for s, v in r.items():
            for k, o in v.items():
                if isinstance(o, dict):
                    for kk, vv in o.items():
                        if isinstance(vv, (int, float)) and kk not in ("n", "blocks"):
                            out[f"{tier}.{s}.{k}.{kk}"] = vv
    return out


if __name__ == "__main__":
    res = run(sys.argv[1])
    print(json.dumps(res, indent=1))
    if len(sys.argv) > 2:
        b = flat(json.load(open(os.path.join(sys.argv[2], "d4_metrics.json"))))
        a = flat(res)
        lines = [f"{k:55s} {b.get(k, '-')!s:>8} -> {a[k]!s:>8}" for k in a]
        print("\n".join(lines))
        open(os.path.join(sys.argv[1], "before_after_table.txt"), "w").write("\n".join(lines))
