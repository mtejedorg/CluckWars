"""Phase 8 decision E1: rival chevron vs local background contrast (WCAG 2.x ratio) from capture_e1_rivals.py output.

    python tools/ui_capture/e1_chevron_contrast.py <capture dir> [<capture dir> ...]

For every <tier>/<layout>.png with a matching <layout>_bare.png (same frame, rival indicators off) it:
  * masks the chevrons as the pixels that changed (|shot - bare| > 24 summed RGB), one connected blob per chevron,
  * background = the BARE frame in a 6..16 px band around the blob (what the arrow is drawn over),
  * fill = the blob core (eroded 3 px), outline = blob pixels darker than both fill and background (only with an edge),
  * MEASURED: WCAG ratio fill vs background, and (edge builds) outline vs background, fill vs outline,
  * MODEL: every seat colour composited at the shipped alpha (0.85, sRGB - the project is in Gamma colour space) over each
    measured background, so all four seats get a number at every chevron spot, including seats that were the local player.
Writes <capture dir>/chevron_contrast.json and prints a table.
"""
import json, os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

SEATS = {"P1": "3fe8e8", "P2": "0994e8", "P3": "f7367b", "P4": "17785a"}
INK = "1a0e04"  # UiGfx.TextDark / PlayerPalette.RivalPillFill
ALPHA = 0.85     # FeedbackTuning.RivalChevronAlpha
INK_ALPHA = 0.95  # FeedbackTuning.RivalChevronOutlineAlpha (edge builds)


def hex_rgb(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=float)


def lum(rgb):
    c = np.asarray(rgb, dtype=float) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return float(0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2])


def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return round((hi + 0.05) / (lo + 0.05), 2)


def comp(fg, bg, a=ALPHA):
    return fg * a + bg * (1 - a)


def nearest_seat(rgb, bg):
    best, bd = None, 1e9
    for s, h in SEATS.items():
        d = np.abs(comp(hex_rgb(h), bg) - rgb).sum()
        if d < bd:
            best, bd = s, d
    return best, bd


def analyse(shot, bare):
    a = np.asarray(Image.open(shot).convert("RGB"), dtype=float)
    b = np.asarray(Image.open(bare).convert("RGB"), dtype=float)
    diff = np.abs(a - b).sum(axis=2) > 24
    diff = ndimage.binary_opening(diff, iterations=1)
    lab, n = ndimage.label(diff)
    out = []
    for i in range(1, n + 1):
        m = lab == i
        if m.sum() < 150:      # rings are big and handled elsewhere; tiny blobs are noise
            continue
        ys, xs = np.nonzero(m)
        h, w = ys.max() - ys.min() + 1, xs.max() - xs.min() + 1
        if max(h, w) > 220 or m.sum() > 9000:    # a ground ring, not a chevron
            continue
        H, W = m.shape
        cx, cy = xs.mean() / W, 1 - ys.mean() / H   # viewport coords (y up)
        # Chevrons live on the safe-area frame (0.045 margin, top inset 0.17): anything well inside is the local bird animating.
        if not (cx < 0.09 or cx > 0.91 or cy < 0.09 or 0.78 < cy < 0.87):
            continue
        core = ndimage.binary_erosion(m, iterations=3)
        band = ndimage.binary_dilation(m, iterations=16) & ~ndimage.binary_dilation(m, iterations=6)
        bg = np.median(b[band], axis=0)
        fill = np.median(a[core], axis=0) if core.any() else np.median(a[m], axis=0)
        lm = np.array([lum(p) for p in a[m]])
        dark = lm < min(lum(fill), lum(bg)) * 0.55
        seat, resid = nearest_seat(fill, bg)
        if resid > 70:          # changed pixels that are not a seat colour at the chevron alpha (a nameplate, a bird at the frame)
            continue
        entry = {"x": int(xs.mean()), "y": int(ys.mean()), "px": int(m.sum()), "seat": seat, "resid": round(float(resid), 1),
                 "fill": [int(v) for v in fill], "bg": [int(v) for v in bg], "fill_vs_bg": ratio(fill, bg)}
        if dark.sum() > 0.08 * m.sum():
            ol = np.median(a[m][dark], axis=0)
            entry.update({"outline": [int(v) for v in ol], "outline_px": int(dark.sum()),
                          "outline_vs_bg": ratio(ol, bg), "fill_vs_outline": ratio(fill, ol)})
        entry["model"] = {s: {"fill_vs_bg": ratio(comp(hex_rgb(h), bg), bg),
                              "ink_vs_bg": ratio(comp(hex_rgb(INK), bg, INK_ALPHA), bg),
                              "fill_vs_ink": ratio(comp(hex_rgb(h), bg), comp(hex_rgb(INK), bg, INK_ALPHA))}
                          for s, h in SEATS.items()}
        out.append(entry)
    return out


def main():
    for d in sys.argv[1:]:
        res = {}
        for tier in ("pc", "mobile"):
            for layout in ("mixed", "all_off"):
                s, bpath = os.path.join(d, tier, layout + ".png"), os.path.join(d, tier, layout + "_bare.png")
                if not (os.path.exists(s) and os.path.exists(bpath)):
                    continue
                res[f"{tier}/{layout}"] = r = analyse(s, bpath)
                for e in r:
                    line = f"{d} {tier}/{layout} {e['seat']} @({e['x']},{e['y']}) bg={e['bg']} fill={e['fill']} fill:bg={e['fill_vs_bg']}"
                    if "outline_vs_bg" in e:
                        line += f" outline={e['outline']} outline:bg={e['outline_vs_bg']} fill:outline={e['fill_vs_outline']}"
                    print(line)
                    print("    model fill:bg " + " ".join(f"{k}={v['fill_vs_bg']}" for k, v in e["model"].items())
                          + f" | ink:bg={e['model']['P1']['ink_vs_bg']} | fill:ink " + " ".join(f"{k}={v['fill_vs_ink']}" for k, v in e["model"].items()))
        json.dump(res, open(os.path.join(d, "chevron_contrast.json"), "w"), indent=1)


main()
