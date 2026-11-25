"""Procedural two-tone icon for the Enforcer passive: a ledger card with three tally strokes and a corn kernel.

Usage: python make_enforcer_icon.py <output.png>
Style matches Icons/Abilities: 256x256 RGBA, transparent, cream #FEF5E0 glyph, ink #2A1A0C outline (~17 px),
interior strokes >= 11 px so they survive a 9 px open (AbilityIconArtTests).
"""
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

CREAM = (254, 245, 224, 255)
INK = (42, 26, 12, 255)
S = 4  # supersample
N = 256 * S


def layer():
    im = Image.new("L", (N, N), 0)
    return im, ImageDraw.Draw(im)


def sc(*v):
    return [x * S for x in v]


def disk_dilate(mask, r):
    dist = ndimage.distance_transform_edt(~mask)
    return dist <= r


def poly_kernel(cx, cy, w, h, ang):
    # teardrop: wide rounded top, pointed bottom, rotated by ang degrees
    pts = []
    for i in range(0, 361, 4):
        t = np.radians(i)
        x = np.sin(t) * w
        y = -np.cos(t) * h * (1.0 if np.cos(t) > 0 else 0.0) - np.cos(t) * h * (0.0 if np.cos(t) > 0 else 1.0)
        # narrow toward the bottom (cos<0) to make a point
        if np.cos(t) < 0:
            x *= (1 + np.cos(t)) ** 0.6
        pts.append((x, y))
    a = np.radians(ang)
    out = []
    for x, y in pts:
        out.append(((cx + x * np.cos(a) - y * np.sin(a)) * S, (cy + x * np.sin(a) + y * np.cos(a)) * S))
    return out


def main(path):
    # card
    card, d = layer()
    d.rounded_rectangle(sc(48, 36, 168, 196), radius=18 * S, fill=255)
    d.polygon(sc(128, 30, 175, 30, 175, 82), fill=0)  # folded top-right corner cut
    card = np.array(card) > 127
    # kernel (overlaps the bottom-right corner)
    kim, d = layer()
    d.polygon(poly_kernel(178, 178, 30, 40, 18), fill=255)
    kern = np.array(kim) > 127
    # tally bars + header band (ink)
    ink, d = layer()
    d.polygon(sc(128, 36, 168, 76, 128, 76), fill=255)  # the fold flap
    for x in (66, 96, 126):
        d.rounded_rectangle(sc(x, 92, x + 17, 168), radius=8 * S, fill=255)
    ink = np.array(ink) > 127
    ink &= card

    kern_ring = disk_dilate(kern, 9 * S)  # ink gap around the kernel where it covers the card
    union = card | kern
    outer = disk_dilate(union, 17 * S)

    rgba = np.zeros((N, N, 4), np.uint8)
    rgba[outer] = INK
    rgba[card] = CREAM
    rgba[ink] = INK
    rgba[kern_ring & ~kern] = np.where((kern_ring & ~kern)[..., None], np.array(INK), rgba)[kern_ring & ~kern]
    rgba[kern] = CREAM
    # kernel centre crease (ink), thick
    cr, d = layer()
    d.line(sc(176, 160, 180, 190), fill=255, width=11 * S)
    cr = (np.array(cr) > 127) & kern
    rgba[cr] = INK
    # tuck the whole glyph into the safe area: bbox centre to (128,138)
    img = Image.fromarray(rgba, "RGBA").resize((256, 256), Image.LANCZOS)
    a = np.array(img)
    ys, xs = np.nonzero(a[..., 3] > 127)
    dx = int(round(128 - (xs.min() + xs.max()) / 2))
    dy = int(round(134 - (ys.min() + ys.max()) / 2))
    out = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    out.alpha_composite(img, (dx, dy))
    out.save(path)


if __name__ == "__main__":
    main(sys.argv[1])
