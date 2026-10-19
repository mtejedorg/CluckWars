"""Phase 8 chunk D / edge-arrow polish: rival arrows hidden while the body is on screen, and kept out of the HUD control zones.

    python tools/ui_capture/capture_rival_arrows.py <outdir>      (e.g. Captures/env3d_after/D/arrows/after)

Solo match with bots (bots frozen once Active, UI hidden, Performance Mode OFF). Parks the local bird at the screen centre and the
three rivals at VIEWPORT positions (ground point under the match camera, 2400x1080 aspect), writes match-camera shots per layout,
a copy with the Touch-layout HUD control zones (joystick + 4 hexes, panel px from TouchControls.uss) drawn on top, and status text
with each chevron's viewport position. Layouts:
  feet_below   rival whose feet are just below the bottom edge but whose body is in view (no arrow expected), one far below, one bottom-left
  left_right   rivals off the left edge at joystick height and off the right edge at ability-hex height
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge
import capture_common

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Captures", "env3d_after", "D", "E1", "run"))
os.makedirs(OUT, exist_ok=True)

_src = open(os.path.join(HERE, "capture_env3d.py"), encoding="utf-8").read()
_prefix = _src[:_src.index("def P(*parts)")]
_ns = {"__name__": "capture_env3d_prefix", "__file__": os.path.join(HERE, "capture_env3d.py")}
_saved_argv = sys.argv
sys.argv = [sys.argv[0], os.path.join(OUT, "_scratch")]
exec(compile(_prefix, "capture_env3d_prefix", "exec"), _ns)
sys.argv = _saved_argv
import shutil
shutil.rmtree(os.path.join(OUT, "_scratch"), ignore_errors=True)
e3 = _ns["cs"]

RV = r"""using Fusion; using UnityEngine; using System.Linq; using System.Text; using CluckWars.Gameplay; using CluckWars.Visuals; using CluckWars.UI; using CluckWars.Input;
public class RV {
  public static string ForceMode(string m) { TouchControlsController.DebugForceMode = m == "touch" ? (HudDeviceMode?)HudDeviceMode.Touch : null; return "mode=" + (TouchControlsController.Instance != null ? TouchControlsController.Instance.Mode.ToString() : "noHud"); }
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy)
      .OrderBy(k => k.HasInputAuthority ? -1 : k.HomeCornerIndex).ToArray(); }
  static void Tp(ChickenController k, Vector3 p) { var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(p); else k.transform.position = p; }
  static float RestY(ChickenController k) { var cc = k.GetComponent<CharacterController>(); return cc.skinWidth + k.transform.lossyScale.y * (cc.height * 0.5f - cc.center.y); }
  // spec: "x,z;x,z;x,z;x,z" = local first, then the rivals by seat.
  public static string Place(string spec) { var a = All(); var pts = spec.Split(';'); string o = "";
    for (int i = 0; i < a.Length && i < pts.Length; i++) { var xz = pts[i].Split(','); var p = new Vector3(float.Parse(xz[0], CI), RestY(a[i]), float.Parse(xz[1], CI)); Tp(a[i], p); a[i].transform.rotation = Quaternion.Euler(0, 180, 0);
      o += (a[i].HasInputAuthority ? "LOCAL" : "rival") + "/" + a[i].Class + "/seat" + a[i].HomeCornerIndex + "@" + p.x.ToString("0.0", CI) + "," + p.z.ToString("0.0", CI) + " "; } return o; }
  static Vector3 GroundAt(float vx, float vy) { var cam = MatchCamera.Instance.Camera; var r = cam.ViewportPointToRay(new Vector3(vx, vy, 0f)); float t = -r.origin.y / r.direction.y; return r.origin + r.direction * t; }
  // spec: "vx,vy;..." viewport positions of the FEET, local first then rivals by seat.
  public static string PlaceVp(string spec) { var a = All(); var pts = spec.Split(';'); string o = "";
    for (int i = 0; i < a.Length && i < pts.Length; i++) { var xy = pts[i].Split(','); var g = GroundAt(float.Parse(xy[0], CI), float.Parse(xy[1], CI)); var p = new Vector3(g.x, RestY(a[i]), g.z); Tp(a[i], p); a[i].transform.rotation = Quaternion.Euler(0, 180, 0);
      o += (a[i].HasInputAuthority ? "LOCAL" : "rival") + "/" + a[i].Class + "/seat" + a[i].HomeCornerIndex + "@vp" + xy[0] + "," + xy[1] + " "; } return o; }
  public static string Aspect(string w, string h) { MatchCamera.Instance.Camera.aspect = float.Parse(w, CI) / float.Parse(h, CI); return "aspect=" + MatchCamera.Instance.Camera.aspect.ToString("0.000", CI); }
  public static string AspectReset() { MatchCamera.Instance.Camera.ResetAspect(); return "reset"; }
  static string Hex(Color c) { return ColorUtility.ToHtmlStringRGBA(c); }
  public static string Status() { var sb = new StringBuilder();     foreach (var k in All()) { if (k.HasInputAuthority) { sb.Append("LOCAL " + k.Class + " seat" + k.HomeCornerIndex + " own=" + Hex(PlayerPalette.ForCorner(k.HomeCornerIndex)) + "\n"); continue; }
      var ri = k.GetComponent<RivalIndicator>(); var t = typeof(RivalIndicator); var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
      var ring = (SpriteRenderer)t.GetField("_ring", fl).GetValue(ri); var chev = (SpriteRenderer)t.GetField("_chevron", fl).GetValue(ri);
      sb.Append("rival " + k.Class + " seat" + k.HomeCornerIndex + " palette=" + Hex(PlayerPalette.ForCorner(k.HomeCornerIndex)) + " ring=" + (ring != null && ring.gameObject.activeSelf ? Hex(ring.color) : "off")
        + " chevron=" + (chev != null && chev.gameObject.activeSelf ? Hex(chev.color) + " vp=" + MatchCamera.Instance.Camera.WorldToViewportPoint(chev.transform.position).ToString("0.000") : "off") + " feetVp=" + MatchCamera.Instance.Camera.WorldToViewportPoint(k.transform.position).ToString("0.000") + "\n"); } return sb.ToString(); }
  // Same frame with every rival ring / chevron renderer (and their edge children) switched off: the per-pixel mask for contrast.
  public static string ShotBare(string w, string h, string path) { var off = new System.Collections.Generic.List<Renderer>();
    var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    foreach (var k in All()) { var ri = k.GetComponent<RivalIndicator>(); if (ri == null) continue;
      foreach (var f in new[] { "_ring", "_chevron" }) { var sr = (SpriteRenderer)typeof(RivalIndicator).GetField(f, fl).GetValue(ri); if (sr == null) continue;
        foreach (var r in sr.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; off.Add(r); } } }
    try { return Shot(w, h, path); } finally { foreach (var r in off) r.enabled = true; } }
  public static string Shot(string w, string h, string path) { var cam = MatchCamera.Instance.Camera; int W = int.Parse(w), H = int.Parse(h);
    var rt = CapTier.NewRt(W, H); var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); return "saved"; }
}""" + capture_common.CAP_TIER_CS

RV_SIG = {"ForceMode": ["m"], "Place": ["spec"], "PlaceVp": ["spec"], "Aspect": ["w", "h"], "Shot": ["w", "h", "path"], "ShotBare": ["w", "h", "path"]}


def rv(method, *args, timeout=300):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(RV_SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": RV, "className": "RV", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        raise RuntimeError(f"RV.{method} failed: " + bridge.text(m)[:800])


def P(*parts):
    return os.path.join(OUT, *parts).replace("\\", "/")


# viewport positions of the feet: local first, then the three rivals by seat.
LAYOUTS = {
    "feet_below": "0.5,0.5;0.5,-0.03;0.3,-0.3;-0.2,-0.2",
    "left_right": "0.5,0.5;-0.25,0.22;1.25,0.17;1.25,0.45",
}


def overlay_zones(src, dst, W, H):
    """Draw the Touch-layout HUD control zones (TouchControls.uss, panel px) on the capture."""
    from PIL import Image, ImageDraw
    import math
    aspect = W / H
    ph = math.sqrt(1920 * 1080 / aspect); pw = aspect * ph
    pad = 20
    hexes = [(95, 95), (142, 361), (302, 269), (365, 95)]
    rects = [(80 - pad, 80 - pad, 360 + pad, 360 + pad)]            # panel px (x0, y0, x1, y1) from bottom-left
    for r, b in hexes:
        rects.append((pw - r - 150 - pad, b - pad, pw - r + pad, b + 150 + pad))
    im = Image.open(src).convert("RGBA"); ov = Image.new("RGBA", im.size, (0, 0, 0, 0)); d = ImageDraw.Draw(ov)
    for x0, y0, x1, y1 in rects:
        d.rectangle([x0 / pw * W, H - y1 / ph * H, x1 / pw * W, H - y0 / ph * H], fill=(255, 255, 255, 50), outline=(255, 255, 0, 255), width=3)
    Image.alpha_composite(im, ov).convert("RGB").save(dst)


def run_tier(tier):
    os.makedirs(P(tier), exist_ok=True)
    W, H = 2400, 1080
    print(rv("Aspect", str(W), str(H)))
    time.sleep(1.0)
    print(rv("ForceMode", "touch")); time.sleep(1.0); print(rv("ForceMode", "touch"))
    for name, spec in LAYOUTS.items():
        print(name, rv("PlaceVp", spec)); time.sleep(2.2)
        st = rv("Status"); print(st)
        open(P(tier, f"status_{name}.txt"), "w", encoding="utf-8").write(st)
        rv("Shot", str(W), str(H), P(tier, f"{name}.png"))
        overlay_zones(P(tier, f"{name}.png"), P(tier, f"{name}_zones.png"), W, H)
    print(rv("AspectReset")); print(rv("ForceMode", "auto"))


def cleanup():
    for label, step in (("restore quality", lambda: print(e3("Quality", "restore"))),
                        ("show UI", lambda: e3("HideUi", "0")),
                        ("stop Play mode", lambda: bridge.call("editor-application-set-state", {"isPlaying": False}))):
        try:
            step()
        except Exception as e:
            print(f"CLEANUP FAILED ({label}): {e}")
    time.sleep(4)


def main():
    bridge.init()
    with capture_common.perf_mode():
        _main()


def _main():
    try:
        print(e3("Quality", "capture"))
        bridge.call("editor-application-set-state", {"isPlaying": True})
        for _ in range(60):
            if str(e3("Press", "SoloBtn", strict=False)).startswith("clicked"):
                break
            time.sleep(1)
        time.sleep(0.8); print(e3("Press", "NextBtn")); time.sleep(1.0); print(e3("Fill")); print(e3("Press", "ReadyBtn")); time.sleep(1.5); print(e3("Press", "StartBtn"))
        for _ in range(80):
            if str(e3("State", strict=False)) == "Active":
                break
            time.sleep(0.5)
        else:
            raise RuntimeError("match never reached Active")
        time.sleep(2.5); print(e3("FreezeBots")); print(e3("HideUi", "1"))
        print(e3("PassInfo", "PASS PC"))
        run_tier("pc")
        print(e3("Quality", "mobile")); time.sleep(2.5)
        print(e3("PassInfo", "PASS MOBILE"))
        run_tier("mobile")
    finally:
        cleanup()


main()
