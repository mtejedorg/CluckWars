"""Phase 8 E4: the four classes side by side at the MATCH camera, both tiers, with a per-class body mask, to measure class-vs-class body colour.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_e4_lineup.py <output_dir>

Solo START, bots frozen, UI hidden, local chicken pinned to SEAT 0 (so the real match camera sits at yaw 225 every run, same as
capture_d1_mound.py), the four classes stood in a row on open grass (Warrior | Speedy | Fatty | Assassin), each turned to the camera +30 deg
(3/4 view, front + flank, the same pose capture_d1_mound.py gives the Fatty), foot rings hidden, animators frozen. Per tier:
    <tier>_lineup.png              the frame as shipped (rings hidden)
    <tier>_lineup_no<Class>.png    that class's body renderers ShadowsOnly -> body mask = pixel diff
    <tier>_lineup_all2.png         the frame again (motion-noise mask)
Then runs e4_class_metrics.py on the folder. Performance Mode OFF (capture_common.perf_mode). Leaves Play mode at the end.
"""
import sys, os, time, subprocess
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bridge
import capture_common
import capture_d1_mound as d1   # menu driving, tier switch, seat pin, bot freeze, UI hide (its main() only runs as __main__)

OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(HERE, "out", "e4_lineup")
os.makedirs(OUT, exist_ok=True)
W, H = 1920, 1080
CLASSES = ("Warrior", "Speedy", "Fatty", "Assassin")

LU = r"""using Fusion; using UnityEngine; using System.Linq; using CluckWars.Gameplay; using CluckWars.Visuals;
public class LU {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  public const float Seat0Yaw = 225f, Turn = 30f;
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy).ToArray(); }
  static ChickenController Of(string cls) { return All().FirstOrDefault(k => k.Class.ToString() == cls); }
  static void Tp(ChickenController k, Vector3 p) { var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(p); else k.transform.position = p; }
  static float RestY(ChickenController k) { var cc = k.GetComponent<CharacterController>(); return cc.skinWidth + k.transform.lossyScale.y * (cc.height * 0.5f - cc.center.y); }
  // Row across the camera's view (perpendicular to the seat-0 view direction), centred on (cx, cz), 'gap' metres apart.
  public static string Line(string gap, string cx, string cz) { float g = float.Parse(gap, CI); var fwd = Quaternion.Euler(0f, Seat0Yaw, 0f) * Vector3.forward; var side = Vector3.Cross(Vector3.up, fwd).normalized;
    string[] order = { "Warrior", "Speedy", "Fatty", "Assassin" }; string o = ""; var c0 = new Vector3(float.Parse(cx, CI), 0f, float.Parse(cz, CI));
    foreach (var k in All()) { int i = System.Array.IndexOf(order, k.Class.ToString()); Vector3 p = i >= 0 ? c0 + side * ((i - 1.5f) * g) : new Vector3(-18f, 0f, -18f); p.y = RestY(k);
      Tp(k, p); k.transform.rotation = Quaternion.Euler(0f, Seat0Yaw + 180f + Turn, 0f); o += k.Class + "@" + p.x.ToString("0.0", CI) + "," + p.z.ToString("0.0", CI) + " "; } return o; }
  public static string Rings(string on) { int n = 0; foreach (var k in All()) foreach (var s in k.GetComponentsInChildren<SpriteRenderer>(true))
      if (s.name.StartsWith("IdentityRing") || s.name.StartsWith("RivalRing")) { s.enabled = on == "1"; n++; } return "rings " + on + " x" + n; }
  static Renderer[] Body(string cls) { var k = Of(cls); if (k == null) throw new System.InvalidOperationException("no " + cls + " in the match");
    var v = k.GetComponentInChildren<ChickenVisuals>(); var rs = v != null && v.TintedRenderers != null ? v.TintedRenderers : k.GetComponentsInChildren<Renderer>(); return rs.Where(r => r != null && r.enabled).ToArray(); }
  public static string Shot(string hide, string path) { var ts = hide == "" ? new Renderer[0] : Body(hide); var old = ts.Select(r => r.shadowCastingMode).ToArray();
    foreach (var r in ts) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    try { var cam = MatchCamera.Instance.Camera; var rt = CapTier.NewRt(""" + str(W) + ", " + str(H) + r"""); var prevT = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prevT;
      var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
      System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
    finally { for (int i = 0; i < ts.Length; i++) ts[i].shadowCastingMode = old[i]; }
    var mc = MatchCamera.Instance.Camera; float dy = Mathf.Abs(Mathf.DeltaAngle(mc.transform.eulerAngles.y, Seat0Yaw)); if (dy > 1f) throw new System.InvalidOperationException("match camera yaw " + mc.transform.eulerAngles.y + " not at seat 0");
    return "saved " + System.IO.Path.GetFileName(path) + " yaw " + mc.transform.eulerAngles.y.ToString("0.0", CI) + " ortho " + mc.orthographicSize.ToString("0.00", CI); }
}""" + capture_common.CAP_TIER_CS

SIG = {"Line": ["gap", "cx", "cz"], "Rings": ["on"], "Shot": ["hide", "path"]}


def lu(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": LU, "className": "LU", "methodName": method, "parameters": params}, timeout=180)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        raise RuntimeError(f"LU.{method} failed: " + bridge.text(m)[:800])


def P(name):
    return os.path.join(OUT, name).replace("\\", "/")


def shoot(tier):
    print(f"PASS {tier}: " + d1.cs("Info"))
    print(d1.cs("FreezeAnim", "0")); print(lu("Line", "2.0", "-4", "-4")); time.sleep(1.6)
    print(lu("Rings", "0")); print(d1.cs("FreezeAnim", "1")); time.sleep(0.3)
    print(lu("Shot", "", P(f"{tier}_lineup.png")))
    for c in CLASSES:
        print(lu("Shot", c, P(f"{tier}_lineup_no{c}.png")))
    print(lu("Shot", "", P(f"{tier}_lineup_all2.png")))
    print(lu("Rings", "1"))


def main():
    try:
        print(d1.cs("Quality", "capture"))
        d1.start(); time.sleep(2.0)
        print(d1.cs("FreezeBots")); print(d1.cs("HideUi", "1")); print(d1.cs("PinSeat0")); time.sleep(2.0)
        shoot("pc")
        print(d1.cs("Quality", "mobile")); time.sleep(2.5)
        shoot("mobile")
    finally:
        for label, step in (("quality", lambda: print(d1.cs("Quality", "restore", strict=False))), ("ui", lambda: d1.cs("HideUi", "0", strict=False)),
                            ("play off", lambda: bridge.call("editor-application-set-state", {"isPlaying": False}))):
            try:
                step()
            except Exception as e:
                print(f"CLEANUP FAILED ({label}): {e}")
        time.sleep(4)


if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        main()
    subprocess.run([sys.executable, os.path.join(HERE, "e4_class_metrics.py"), OUT], check=False)
