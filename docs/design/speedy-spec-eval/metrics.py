"""Relational posture metrics on the GAME-SCALE silhouette (64px tall).

Deliberately not bounding-box scores: each number is a posture/feature relation
that the art direction actually names.
  squat      - width/height. Agile should be LOWER (leaner, taller).
  cg_height  - centroid height as fraction of the figure. Poise = mass carried
               higher; panic = weight dumped forward and down.
  crown_mass - mass in the top 30% (head/neck held up vs ducked).
  roughness  - perimeter^2/area, normalised. Ruffled/spiked plumage raises it;
               clean streamlined plumage lowers it. This is the feather channel.
  leg_gap    - empty fraction of the bottom 18%. Standing tall on visible legs
               vs body dumped to the floor.
"""
import sys, numpy as np
from PIL import Image
exec(open("silhouette_test.py",encoding="utf-8").read().split('if __name__')[0])

def metrics(path, tier=64):
    crop, m = prep(path)
    tw = max(1,int(round(crop.width*(tier/crop.height))))
    ms = Image.fromarray((m*255).astype(np.uint8)).resize((tw,tier), Image.LANCZOS)
    b = np.asarray(ms) > 110
    h,w = b.shape; area = b.sum()
    ys,xs = np.where(b)
    cg = 1.0 - (ys.mean()/h)                      # 1 = mass at top
    crown = b[:int(h*0.30)].sum()/area
    per = 0
    for dy,dx in ((1,0),(-1,0),(0,1),(0,-1)):
        sh = np.roll(b,(dy,dx),(0,1))
        per += (b & ~sh).sum()
    rough = per**2/area/ (4*np.pi) / 10
    legband = b[int(h*0.82):]
    leg_gap = 1.0 - legband.sum()/legband.size
    return dict(squat=w/h, cg=cg, crown=crown, rough=rough, gap=leg_gap)

names = ["agile_v01","agile_v02","agile_v03","anxious_v01","anxious_v02","anxious_v03"]
D="C:/Users/MARCO/Documents/GitHub/CluckWars/Assets/AIConcepts"
print(f"{'variant':<14}{'squat':>7}{'cg':>7}{'crown':>7}{'rough':>8}{'leg_gap':>9}")
res={}
for n in names:
    r = metrics(f"{D}/speedy_{n}.png"); res[n]=r
    print(f"{n:<14}{r['squat']:>7.2f}{r['cg']:>7.3f}{r['crown']:>7.3f}{r['rough']:>8.2f}{r['gap']:>9.3f}")
print()
for k,lab in [('squat','squat (lower=leaner)'),('cg','cg (higher=poised)'),('crown','crown'),('rough','rough (higher=ruffled)'),('gap','leg_gap (higher=stands tall)')]:
    a=[res[n][k] for n in names[:3]]; x=[res[n][k] for n in names[3:]]
    print(f"{lab:<30} agile mean {np.mean(a):6.3f} | anxious mean {np.mean(x):6.3f}  -> sep {np.mean(x)-np.mean(a):+.3f}")
