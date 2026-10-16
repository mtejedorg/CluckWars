"""E4: recolour the Fatty albedo atlas to a cream breast / orange-brown back. Geometry from the Unity mesh dump (fatty_labels.npz)."""
import numpy as np, sys, json, colorsys
from PIL import Image
from scipy import ndimage
SRC, LABELS, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
cfg = json.loads(sys.argv[4]) if len(sys.argv) > 4 else {}
BACK = np.array(cfg.get("back", [190, 118, 78]), float)    # post-wash texel targets (sRGB 0-255) at full light
CREAM = np.array(cfg.get("cream", [246, 233, 202]), float)
TS = float(cfg.get("tint_strength", 0.25))   # Fatty registry TintStrength the texture is authored under
WASH = np.array([1, 1, 1]) * (1 - TS) + np.array([0.9607843, 0.8431373, 0.3529412]) * TS   # ChickenVisuals.ApplyTint: lerp(white, TintColor #F5D75A, strength)
d = np.load(LABELS); lab = d["lab"]; nrm = d["nrm"]; pos = d["pos"]; fwd = d["fwd"]; bones = list(d["bones"])
alb = np.asarray(Image.open(SRC).convert("RGB")).astype(float)
cov = lab >= 0
side = np.array([-fwd[1], fwd[0], 0.0])
f = pos @ np.array([fwd[0], fwd[1], 0.0]); lat = pos @ side; z = pos[..., 2]
nn = nrm / np.maximum(np.linalg.norm(nrm, axis=-1, keepdims=True), 1e-6)
nf = nn @ np.array([fwd[0], fwd[1], 0.0]); nz = nn[..., 2]
hsv = np.asarray(Image.fromarray(alb.astype(np.uint8)).convert("HSV")).astype(float)
H, S, V = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255
# --- keep-original parts -------------------------------------------------------------------------------
red = ((H < 14) | (H > 335)) & (S > 0.55)
comb = red & (z > 1.27)
wattle = red & (f > 0.25) & (z > 0.68) & (z < 1.08)
beak = (f > 0.515) & (z > 0.95) & (z < 1.3) & (np.abs(lat) < 0.22) & (H < 42)
eyes = (z > 1.02) & (f > 0.2) & ((S < 0.32) | (V < 0.32))
marks = V < 0.2
# legs: axis from the pure-shin slab (below the belly), then a radius around it; toes below z 0.09
shin = cov & (z > 0.10) & (z < 0.135)
legs = cov & (z < 0.09)
for sgn in (-1, 1):
    m = shin & (np.sign(lat) == sgn)
    c = np.array([f[m].mean(), lat[m].mean()])
    legs |= cov & (z < 0.27) & (np.hypot(f - c[0], lat - c[1]) < 0.085)
keep = comb | wattle | beak | eyes | marks | legs
# --- breast weight ------------------------------------------------------------------------------------
def ss(a, b, x): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)
w = np.maximum(ss(0.05, 0.55, nf), ss(0.15, 0.55, -nz))
w *= 1 - ss(0.5, 0.85, nz)
wing = np.isin(lab, [bones.index(b) for b in ("WingL", "WingTipL", "WingR", "WingTipR")])
tail = lab == bones.index("Tail")
wing &= (np.abs(lat) > 0.3) | (nf < 0.35)   # the wing bones also win a few chest texels: keep those cream
cf = cov.astype(float)   # normalised blurs: uncovered padding must not leak into island borders
def nblur(x, s): return ndimage.gaussian_filter(x * cf, s) / np.maximum(ndimage.gaussian_filter(cf, s), 1e-3)
wingw = nblur(wing.astype(float), 3.0); tailw = nblur(tail.astype(float), 3.0)
w = w * (1 - 0.85 * wingw) * (1 - 0.8 * tailw)
w *= 1 - 0.85 * ss(0.36, 0.50, np.abs(lat))   # outer flanks / wing shoulders stay brown
w = nblur(w, 1.5)
# --- shading preserved from the original luminance ----------------------------------------------------
Y = alb @ np.array([0.2126, 0.7152, 0.0722])
body = cov & ~keep
Yref = np.quantile(Y[body], 0.9)
shade = np.clip(Y / Yref, 0.0, 1.08) ** float(cfg.get("gamma", 0.85))
tgt = BACK[None, None] * (1 - w[..., None]) + CREAM[None, None] * w[..., None]
new = tgt * shade[..., None] / WASH
out = alb.copy()
out[body] = np.clip(new[body], 0, 255)
# padding / uncovered texels: copy the nearest covered texel's result (keeps mip/bilinear seams in colour)
idx = ndimage.distance_transform_edt(~cov, return_distances=False, return_indices=True)
out[~cov] = out[idx[0][~cov], idx[1][~cov]]
Image.fromarray(np.round(out).astype(np.uint8)).save(OUT)
dbg = np.zeros_like(alb); dbg[body] = [120, 120, 120]
for m, col in ((comb, (255, 0, 0)), (wattle, (255, 0, 255)), (beak, (255, 160, 0)), (eyes, (0, 255, 255)), (marks, (0, 0, 255)), (legs, (0, 255, 0))):
    dbg[m & cov] = col
Image.fromarray(dbg.astype(np.uint8)).save(OUT.replace(".png", "_keepmask.png"))
print("body texels", body.sum(), "keep", (keep & cov).sum(), "Yref", round(Yref, 1), "breast w mean", round(float(w[body].mean()), 3))
