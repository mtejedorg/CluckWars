"""Render the four class chickens to 1024 px transparent PNGs from the real models (menu overhaul Phase 2).

Usage (Editor open, not in Play mode):
    python tools/ui_capture/render_heroes.py <output_dir> [--pose idle|cheer|both] [--yaw DEG] [--t T]

Runs hero_render.cs.txt through the MCP bridge's script-execute. The render happens in an editor PREVIEW scene
(EditorSceneManager.NewPreviewScene) that is closed afterwards, so no open scene is touched or dirtied. Poses:
idle = the class Idle clip at t=0.25; cheer = the Flare cast clip at t=0.6 (head back, chest out - there is no
dedicated cheer clip). Output is auto-cropped on alpha: square, centred, feet 5% above the bottom edge.
Writes Hero_<cls>_<pose>.png. Copy the keepers into Assets/_Game/Art/UI/Chickens/ only after looking at them.
"""
import sys, os, json, argparse
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ap = argparse.ArgumentParser()
ap.add_argument("out")
ap.add_argument("--pose", default="both")
ap.add_argument("--yaw", default="-28")
ap.add_argument("--t", default=None, help="normalised clip time (default idle 0.25, cheer 0.6)")
ap.add_argument("--size", default="1024")
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
CS = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "hero_render.cs.txt"), encoding="utf-8").read()
poses = {"idle": ("Idle", "0.25"), "cheer": ("Flare", "0.6")}
bridge.init()
for cls in ["Warrior", "Speedy", "Fatty", "Assassin"]:
    for pose, (clip, t) in poses.items():
        if a.pose not in ("both", pose):
            continue
        path = os.path.abspath(os.path.join(a.out, f"Hero_{cls.lower()}_{pose}.png")).replace("\\", "/")
        args = [cls, clip, a.t or t, a.yaw, path, a.size]
        names = ["cls", "clipName", "tNorm", "yawDeg", "outPath", "sizeStr"]
        params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(names, args)]
        r = bridge.text(bridge.call("script-execute", {"csharpCode": CS, "className": "HeroRender", "methodName": "Render", "parameters": params}, timeout=120))
        try:
            print(cls, pose, json.loads(r)["result"]["value"])
        except Exception:
            print(cls, pose, r[:300])
