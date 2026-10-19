"""4-way comparison strips: original / re-audit / final / D-final.   python tools/ui_capture/build_cmp4.py <D-final dir>
Reads Captures/env3d, env3d_reaudit, env3d_final and the given dir; writes <dir>/cmp/<name>.jpg (and cmp/mobile/ for the Mobile overviews)."""
import sys, os, glob
from PIL import Image, ImageDraw
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
CAP = os.path.join(ROOT, "Captures")
new = os.path.abspath(sys.argv[1])
SETS = [("ORIGINAL 14 Oct", os.path.join(CAP, "env3d")), ("RE-AUDIT 15 Oct", os.path.join(CAP, "env3d_reaudit")),
        ("FINAL 16 Oct (Perf Mode ON)", os.path.join(CAP, "env3d_final")), ("D-FINAL 19 Oct (Perf OFF)", new)]
H = 396
def strip(rel, out):
    ims = []
    for label, d in SETS:
        p = os.path.join(d, rel)
        if not os.path.exists(p): ims.append((label, None)); continue
        im = Image.open(p).convert("RGB"); w = int(im.width * H / im.height); ims.append((label, im.resize((w, H), Image.LANCZOS)))
    if sum(1 for _, i in ims if i) < 2: return False
    W = sum((i.width if i else 400) for _, i in ims) + 4 * (len(ims) - 1); canvas = Image.new("RGB", (W, H + 30), (30, 26, 22)); dr = ImageDraw.Draw(canvas); x = 0
    for label, i in ims:
        dr.text((x + 6, 8), label + ("" if i else "  (missing)"), fill=(235, 225, 210))
        if i: canvas.paste(i, (x, 30)); x += i.width + 4
        else: x += 404
    os.makedirs(os.path.dirname(out), exist_ok=True); canvas.save(out, quality=88); return True
n = 0
for sub in ("", "mobile"):
    for p in sorted(glob.glob(os.path.join(new, sub, "*.png"))):
        rel = os.path.relpath(p, new); nm = os.path.splitext(os.path.basename(p))[0]
        if not (nm.startswith("mc_") or nm.startswith("ov_") or nm.startswith("pile_") or nm.startswith("base_")): continue
        n += strip(rel, os.path.join(new, "cmp", sub, nm + ".jpg"))
print("wrote", n, "strips")
