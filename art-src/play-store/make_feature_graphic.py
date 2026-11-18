"""Builds the Google Play feature graphic (1024x500 PNG) from the shipped art.
Run: python art-src/play-store/make_feature_graphic.py   -> art-src/play-store/feature_graphic_1024x500.png
Inputs: Bg_MainMenu.png, the four Hero_*_cheer.png chickens, LilitaOne-Regular.ttf (the game's display font).
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
UI = ROOT / "Assets/_Game/Art/UI"
OUT = Path(__file__).with_name("feature_graphic_1024x500.png")
W, H = 1024, 500

bg = Image.open(UI / "Backgrounds/Bg_MainMenu.png").convert("RGB")
ch = round(bg.width * H / W)                      # crop to the 1024:500 ratio, centred
top = (bg.height - ch) // 2
img = bg.crop((0, top, bg.width, top + ch)).resize((W, H), Image.LANCZOS).convert("RGBA")

def chicken(name, height):
    c = Image.open(UI / f"Chickens/Hero_{name}_cheer.png").convert("RGBA")
    c = c.crop(c.getbbox())
    return c.resize((round(c.width * height / c.height), height), Image.LANCZOS)

# four chickens along the bottom, left to right
order = [("warrior", 300), ("fatty", 285), ("speedy", 285), ("assassin", 300)]
xs = [150, 360, 575, 790]
for (name, h), cx in zip(order, xs):
    c = chicken(name, h)
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ell = Image.new("L", img.size, 0)
    ImageDraw.Draw(ell).ellipse((cx - c.width * 0.42, H - 42, cx + c.width * 0.42, H - 14), fill=110)
    sh.putalpha(ell.filter(ImageFilter.GaussianBlur(7)))
    img = Image.alpha_composite(img, sh)
    img.alpha_composite(c, (round(cx - c.width / 2), H - 22 - c.height))

# title, top centre
font = ImageFont.truetype(str(ROOT / "Assets/_Game/Resources/Fonts/LilitaOne-Regular.ttf"), 112)
title = "CLUCK WARS"
d = ImageDraw.Draw(img)
bx = d.textbbox((0, 0), title, font=font)
x, y = (W - (bx[2] - bx[0])) // 2 - bx[0], 22 - bx[1]
d.text((x + 0, y + 7), title, font=font, fill=(70, 28, 16, 255), stroke_width=9, stroke_fill=(70, 28, 16, 255))
d.text((x, y), title, font=font, fill=(255, 214, 64, 255), stroke_width=9, stroke_fill=(120, 44, 20, 255))

img.convert("RGB").save(OUT, "PNG", optimize=True)
print(OUT, img.size, OUT.stat().st_size // 1024, "KB")
