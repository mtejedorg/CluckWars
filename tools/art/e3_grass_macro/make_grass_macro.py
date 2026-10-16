"""Phase 8 E3: the grass floor's large soft colour variation, a URP Lit Detail Albedo x2 map (PIL/numpy, no generation).

    python tools/art/e3_grass_macro/make_grass_macro.py [amp_hue] [amp_luma] [seed]

Writes Assets/Generated/Env/Textures/grass_macro_variation.png (256 x 256 RGB, tiles seamlessly). GrassFloor.mat samples it with a detail
tiling of 1 / base tiling, so ONE period spans the whole 51.3 m floor (the cube's top face is UV 0..1). The shader multiplies the albedo by
2 x texel (_DETAIL_MULX2; the project renders in gamma space so the texel is used as stored): 128 = no change.

Two independent zero-mean fields made of integer-frequency waves (1-3 cycles per period, so they tile; amplitude ~ 1/|k|^1.3, random phase):
  hue  field h: +h = toward olive / yellow (R up, B down), -h = a cooler, deeper green   R x(1 + 0.08 a_h h)  G x(1 + 0.01 a_h h)  B x(1 - 0.05 a_h h)
  luma field l: every channel x(1 + 0.03 a_l l)
Both fields are scaled so their 99th |value| percentile is 1, then clipped. Rounding to 8 bits is dithered so the mean stays at 127.5
(an exact neutral: the floor's average brightness is unchanged).
"""
import os, sys
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Generated", "Env", "Textures", "grass_macro_variation.png")
N = 256
AMP_H = float(sys.argv[1]) if len(sys.argv) > 1 else 1.0
AMP_L = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0
SEED = int(sys.argv[3]) if len(sys.argv) > 3 else 3


def field(rng, kmax=3, slope=1.3):
    y, x = np.mgrid[0:N, 0:N] / N
    f = np.zeros((N, N))
    for kx in range(-kmax, kmax + 1):
        for ky in range(0, kmax + 1):
            if (ky == 0 and kx <= 0) or kx * kx + ky * ky > kmax * kmax + 1:
                continue
            k = np.hypot(kx, ky)
            f += k ** -slope * np.cos(2 * np.pi * (kx * x + ky * y) + rng.uniform(0, 2 * np.pi))
    f -= f.mean()
    return np.clip(f / np.percentile(np.abs(f), 99), -1, 1)


def main():
    rng = np.random.default_rng(SEED)
    h, l = field(rng), field(rng)
    mult = np.stack([1 + 0.08 * AMP_H * h, 1 + 0.01 * AMP_H * h, 1 - 0.05 * AMP_H * h], -1) * (1 + 0.03 * AMP_L * l)[..., None]
    v = 127.5 * mult
    v = np.floor(v + rng.uniform(0, 1, v.shape))          # dithered rounding: E[v] unchanged
    img = np.clip(v, 0, 255).astype(np.uint8)
    Image.fromarray(img, "RGB").save(OUT)
    flat = img.reshape(-1, 3).astype(np.float64); m = flat.mean(0)
    print(f"wrote {OUT}  mean {m.round(2)}  min {flat.min(0)}  max {flat.max(0)}  (x2 multiplier range "
          f"{(2 * flat.min(0) / 255).round(3)} .. {(2 * flat.max(0) / 255).round(3)})")


if __name__ == "__main__":
    main()
