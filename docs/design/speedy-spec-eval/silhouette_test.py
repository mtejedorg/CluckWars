"""Small-scale silhouette test for Cluck Wars concepts.

Background is a smooth VERTICAL gradient (dark amber top -> cream bottom), so a
single global background colour does not work. We estimate the background per
row from the extreme left/right columns, which the subject never touches
(Flux centres the character), then threshold on distance from that.

Emits, per size tier:
  *_rgb_<N>px.png  - the concept at true in-game pixel height, nearest-upscaled
  *_sil_<N>px.png  - pure black mask of the same, the real discriminator

Tiers: 64px subject height (~4% of 1920 per GDD 3.3) and a harsher 32px.
"""
import sys
from PIL import Image
import numpy as np

TIERS = [64, 32]
ZOOM  = 6
PAD   = 10
EDGE  = 0.06     # fraction of width sampled at each side as "known background"
THRESH= 38


def bg_residual(img, ring=0.05):
    """Fit a quadratic surface per channel to the border ring and return residual.

    The Flux backdrop is a radial-ish warm gradient, so neither a global colour
    nor a per-row median describes it. A 2nd-degree 2D polynomial fitted to the
    outer frame (always background, since Flux centres the subject) predicts it
    well; the residual isolates the character cleanly.
    """
    a = np.asarray(img.convert("RGB")).astype(np.float64)
    h, w, _ = a.shape
    yy, xx = np.mgrid[0:h, 0:w]
    Y = yy/h; X = xx/w
    ring_mask = np.zeros((h,w), bool)
    ry, rx = int(h*ring), int(w*ring)
    ring_mask[:ry,:] = True; ring_mask[-ry:,:] = True
    ring_mask[:,:rx] = True; ring_mask[:,-rx:] = True
    B = np.stack([np.ones_like(X), X, Y, X*X, X*Y, Y*Y], -1)
    Bf = B[ring_mask]
    pred = np.zeros_like(a)
    for c in range(3):
        coef, *_ = np.linalg.lstsq(Bf, a[...,c][ring_mask], rcond=None)
        pred[...,c] = B @ coef
    return np.sqrt(((a-pred)**2).sum(-1))

def subject_mask(img):
    dist = bg_residual(img)
    m = dist > 44
    h, w = m.shape
    for y in range(int(h*0.80), h):
        if m[y].mean() > 0.55:
            m[y] = False
    return m

def largest_component(m):
    """Keep only the biggest blob so stray shadow speckle can't inflate bounds."""
    from collections import deque
    h, w = m.shape
    seen = np.zeros_like(m, bool)
    best = None; best_n = 0
    for sy in range(0, h, 3):
        for sx in range(0, w, 3):
            if not m[sy, sx] or seen[sy, sx]:
                continue
            q = deque([(sy, sx)]); seen[sy, sx] = True
            cells = []
            while q:
                y, x = q.popleft(); cells.append((y, x))
                for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
                    ny, nx = y+dy, x+dx
                    if 0 <= ny < h and 0 <= nx < w and m[ny,nx] and not seen[ny,nx]:
                        seen[ny,nx] = True; q.append((ny,nx))
            if len(cells) > best_n:
                best_n = len(cells); best = cells
    out = np.zeros_like(m, bool)
    if best:
        ys, xs = zip(*best); out[list(ys), list(xs)] = True
    return out

def prep(path):
    img = Image.open(path)
    m = largest_component(subject_mask(img))
    ys, xs = np.where(m)
    y0,y1,x0,x1 = ys.min(), ys.max(), xs.min(), xs.max()
    return img.crop((x0,y0,x1+1,y1+1)), m[y0:y1+1, x0:x1+1]

def build(paths, out_prefix):
    prepped = [prep(p) for p in paths]
    for tier in TIERS:
        cols_rgb, cols_sil = [], []
        for crop, m in prepped:
            tw = max(1, int(round(crop.width * (tier/crop.height))))
            cols_rgb.append(crop.convert("RGB").resize((tw,tier), Image.LANCZOS))
            ms = Image.fromarray((m*255).astype(np.uint8)).resize((tw,tier), Image.LANCZOS)
            cols_sil.append(ms.point(lambda v: 0 if v > 110 else 255).convert("RGB"))
        for tag, cols in (("rgb", cols_rgb), ("sil", cols_sil)):
            W = sum(c.width for c in cols) + PAD*(len(cols)+1)
            H = tier + PAD*2
            bgc = (245,238,224) if tag=="rgb" else (255,255,255)
            sheet = Image.new("RGB", (W,H), bgc)
            x = PAD
            for c in cols:
                sheet.paste(c, (x, PAD)); x += c.width + PAD
            out = f"{out_prefix}_{tag}_{tier}px.png"
            sheet.resize((W*ZOOM, H*ZOOM), Image.NEAREST).save(out)
            print("OUT:", out)

if __name__ == "__main__":
    build(sys.argv[2:], sys.argv[1])
