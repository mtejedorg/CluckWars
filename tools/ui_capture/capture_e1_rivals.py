"""Phase 8 chunk D / decision E1: rival ring + screen-edge chevron colours from the MATCH camera, both tiers.

    python tools/ui_capture/capture_e1_rivals.py <outdir>      (e.g. Captures/env3d_after/D/E1/before)

Solo match with bots (bots frozen once Active, UI hidden, Performance Mode OFF via capture_common.perf_mode). Per tier (pc, mobile) it parks the local
bird and the three rivals in a few layouts and writes phone-aspect (2400x1080) match-camera shots:
  <tier>/all_on.png       all three rivals on screen: three rival rings, no chevron
  <tier>/mixed.png        one rival on screen (ring) + two off screen (two edge chevrons)
  <tier>/all_off.png      all three rivals off screen: three edge chevrons
  <tier>/<layout>_bare.png  the same frame with every rival ring / chevron renderer off (contrast mask; e1_chevron_contrast.py)
  <tier>/status_<layout>.txt   per chicken: seat, class, ring/chevron active + colour (what the shot should show)
The camera aspect is pinned to the capture aspect while the layout settles, so the chevrons (positioned in LateUpdate from the camera) match the render.
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

RV = r"""using Fusion; using UnityEngine; using System.Linq; using System.Text; using CluckWars.Gameplay; using CluckWars.Visuals; using CluckWars.UI;
public class RV {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy)
      .OrderBy(k => k.HasInputAuthority ? -1 : k.HomeCornerIndex).ToArray(); }
  static void Tp(ChickenController k, Vector3 p) { var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(p); else k.transform.position = p; }
  static float RestY(ChickenController k) { var cc = k.GetComponent<CharacterController>(); return cc.skinWidth + k.transform.lossyScale.y * (cc.height * 0.5f - cc.center.y); }
  // spec: "x,z;x,z;x,z;x,z" = local first, then the rivals by seat.
  public static string Place(string spec) { var a = All(); var pts = spec.Split(';'); string o = "";
    for (int i = 0; i < a.Length && i < pts.Length; i++) { var xz = pts[i].Split(','); var p = new Vector3(float.Parse(xz[0], CI), RestY(a[i]), float.Parse(xz[1], CI)); Tp(a[i], p); a[i].transform.rotation = Quaternion.Euler(0, 180, 0);
      o += (a[i].HasInputAuthority ? "LOCAL" : "rival") + "/" + a[i].Class + "/seat" + a[i].HomeCornerIndex + "@" + p.x.ToString("0.0", CI) + "," + p.z.ToString("0.0", CI) + " "; } return o; }
  public static string Aspect(string w, string h) { MatchCamera.Instance.Camera.aspect = float.Parse(w, CI) / float.Parse(h, CI); return "aspect=" + MatchCamera.Instance.Camera.aspect.ToString("0.000", CI); }
  public static string AspectReset() { MatchCamera.Instance.Camera.ResetAspect(); return "reset"; }
  static string Hex(Color c) { return ColorUtility.ToHtmlStringRGBA(c); }
  public static string Status() { var sb = new StringBuilder();     foreach (var k in All()) { if (k.HasInputAuthority) { sb.Append("LOCAL " + k.Class + " seat" + k.HomeCornerIndex + " own=" + Hex(PlayerPalette.ForCorner(k.HomeCornerIndex)) + "\n"); continue; }
      var ri = k.GetComponent<RivalIndicator>(); var t = typeof(RivalIndicator); var fl = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
      var ring = (SpriteRenderer)t.GetField("_ring", fl).GetValue(ri); var chev = (SpriteRenderer)t.GetField("_chevron", fl).GetValue(ri);
      sb.Append("rival " + k.Class + " seat" + k.HomeCornerIndex + " palette=" + Hex(PlayerPalette.ForCorner(k.HomeCornerIndex)) + " ring=" + (ring != null && ring.gameObject.activeSelf ? Hex(ring.color) : "off")
        + " chevron=" + (chev != null && chev.gameObject.activeSelf ? Hex(chev.color) : "off") + "\n"); } return sb.ToString(); }
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

RV_SIG = {"Place": ["spec"], "Aspect": ["w", "h"], "Shot": ["w", "h", "path"], "ShotBare": ["w", "h", "path"]}


def rv(method, *args, timeout=300):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(RV_SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": RV, "className": "RV", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        raise RuntimeError(f"RV.{method} failed: " + bridge.text(m)[:800])


def P(*parts):
    return os.path.join(OUT, *parts).replace("\\", "/")


# local first, then the three rivals by seat.
LAYOUTS = {
    "all_on": "0,-4;-3.2,-4;3.4,-3;0.5,-1.5",
    "mixed": "0,-4;-3.4,-4;17,-4;0,17",
    "all_off": "0,-4;-17,-4;17,-3;0,17",
}


def run_tier(tier):
    os.makedirs(P(tier), exist_ok=True)
    W, H = 2400, 1080
    print(rv("Aspect", str(W), str(H)))
    for name, spec in LAYOUTS.items():
        print(name, rv("Place", spec)); time.sleep(2.2)
        st = rv("Status"); print(st)
        open(P(tier, f"status_{name}.txt"), "w", encoding="utf-8").write(st)
        rv("Shot", str(W), str(H), P(tier, f"{name}.png"))
        rv("ShotBare", str(W), str(H), P(tier, f"{name}_bare.png"))
    print(rv("AspectReset"))


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
