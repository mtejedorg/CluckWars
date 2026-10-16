"""Phase 8 E3: the grass floor against piles, chickens and the four player rings at the MATCH camera, both tiers, plus an overview.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_e3_grass.py <output_dir>

Solo START, bots frozen, UI hidden, every pile full, local chicken pinned to SEAT 0 (match camera yaw 225, D1 tool rule). Per tier:
  Spots (D2 render pairs, capture_d2_tones.D2): spawn = local at its base, pile = beside corner 0's home pile, centre = beside the centre pile
    <tier>_<spot>.png / _noground (GeneratedGround ShadowsOnly) / _nopile / _nosun / _all2
  Lineup (capture_e4_lineup.LU): the four classes on open grass, rings hidden
    <tier>_lineup.png / _lineup_no<Class>.png / _lineup_all2.png
  Rings: same lineup, every identity ring forced to corner c (0..3), rival rings off, ring on / off pair
    <tier>_ring<c>_on.png / <tier>_ring<c>_off.png
  Overview: a free perspective camera over the whole arena (tier MSAA + tier post), shows the texture repetition
    <tier>_overview.png
Then runs e3_grass_metrics.py. Performance Mode OFF (capture_common.perf_mode). Leaves Play mode at the end.
"""
import sys, os, time, subprocess
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(HERE, "out", "e3")
os.makedirs(OUT, exist_ok=True)
import bridge
import capture_common
import capture_d2_tones as d2      # D2 class: menu driving, tier switch, seat pin, stage, render-pair shots
import capture_e4_lineup as e4     # LU class: the four-class lineup + body-hidden shots
d2.OUT = OUT
e4.OUT = OUT

# D2.Stage knows "corner" and "pile" (home pile); add "centre" (the pile nearest the arena origin).
_OLD = "var pile = Piles().Where(p => p.transform.position.sqrMagnitude > 1f).OrderBy(p => (p.transform.position - home.transform.position).sqrMagnitude).First();"
_NEW = ("var pile = which == \"centre\" ? Piles().OrderBy(p => p.transform.position.sqrMagnitude).First() : "
        "Piles().Where(p => p.transform.position.sqrMagnitude > 1f).OrderBy(p => (p.transform.position - home.transform.position).sqrMagnitude).First();")
assert _OLD in d2.CS, "capture_d2_tones.D2.Stage changed: update the centre patch"
d2.CS = d2.CS.replace(_OLD, _NEW)

W, H = 1920, 1080
CLASSES = ("Warrior", "Speedy", "Fatty", "Assassin")

EG = r"""using Fusion; using UnityEngine; using System.Linq; using System.Reflection; using CluckWars.Gameplay; using CluckWars.Visuals; using CluckWars.UI;
using UnityEngine.Rendering.Universal;
public class EG {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy).ToArray(); }
  static System.Collections.Generic.IEnumerable<SpriteRenderer> RingsOf(ChickenController k) { return k.GetComponentsInChildren<SpriteRenderer>(true).Where(s => s.name.StartsWith("IdentityRing") || s.name.StartsWith("RivalRing")); }
  // which: identity | rival | all
  public static string Rings(string on, string which) { int n = 0;
    foreach (var k in All()) foreach (var s in RingsOf(k)) { bool rival = s.name.StartsWith("RivalRing"); if (which == "identity" && rival) continue; if (which == "rival" && !rival) continue; s.enabled = on == "1"; n++; }
    return "rings " + which + " " + on + " x" + n; }
  // Every class's identity ring takes corner c's colour (same private fields capture_c_rings.py forces).
  public static string ForceCorner(string corner) { var t = typeof(ChickenWorldBars); string o = "";
    foreach (var k in All()) { var wb = k.GetComponent<ChickenWorldBars>(); if (wb == null) continue; var c = PlayerPalette.ForCorner(int.Parse(corner)); c.a = 0.75f;
      var ring = (SpriteRenderer)t.GetField("_ring", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(wb); if (ring == null) continue;
      t.GetField("_appliedRingCorner", BindingFlags.NonPublic|BindingFlags.Instance).SetValue(wb, k.HomeCornerIndex); t.GetField("_ringBaseColor", BindingFlags.NonPublic|BindingFlags.Instance).SetValue(wb, c); ring.color = c; o += k.Class + " "; }
    return "corner " + corner + " rings on " + o; }
  static void Save(Camera cam, string path) { var rt = CapTier.NewRt(""" + str(W) + ", " + str(H) + r"""); var prevT = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = prevT;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  // Editor-only: async shader compilation draws a new variant (e.g. _DETAIL_MULX2 without the main light, the "nosun" shot) as cyan for a few
  // frames; the capture renders synchronously, so turn it off for the run (players have the variants precompiled).
  public static string AsyncCompile(string on) { bool was = UnityEditor.ShaderUtil.allowAsyncCompilation; UnityEditor.ShaderUtil.allowAsyncCompilation = on == "1"; return "allowAsyncCompilation " + was + " -> " + UnityEditor.ShaderUtil.allowAsyncCompilation; }
  public static string Shot(string path) { Save(MatchCamera.Instance.Camera, path); return "saved " + System.IO.Path.GetFileName(path); }
  // Free perspective camera from the seat-0 side, high, the whole 51 m floor in frame.
  public static string Overview(string path) { var mc = MatchCamera.Instance.Camera; var go = new GameObject("__E3Overview");
    try { var cam = go.AddComponent<Camera>(); cam.CopyFrom(mc); cam.orthographic = false; cam.fieldOfView = 42f; cam.nearClipPlane = 0.5f; cam.farClipPlane = 300f;
      cam.transform.position = new Vector3(30f, 46f, 30f); cam.transform.LookAt(new Vector3(-2f, 0f, -2f)); cam.GetUniversalAdditionalCameraData(); bool post = CapTier.ApplyPost(cam);
      Save(cam, path); return "saved " + System.IO.Path.GetFileName(path) + " post=" + post; }
    finally { Object.DestroyImmediate(go); } }
}""" + capture_common.CAP_TIER_CS

EG_SIG = {"AsyncCompile": ["on"], "Rings": ["on", "which"], "ForceCorner": ["corner"], "Shot": ["path"], "Overview": ["path"]}


def eg(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(EG_SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": EG, "className": "EG", "methodName": method, "parameters": params}, timeout=180)
    try:
        v = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        v = None
    if v is None or str(v).startswith("ERROR"):
        raise RuntimeError(f"EG.{method} failed: " + (str(v) if v is not None else bridge.text(m)[:800]))
    return v


def P(name):
    return os.path.join(OUT, name).replace("\\", "/")


def shoot(tier):
    print(f"PASS {tier.upper()}: " + d2.cs("Info"))
    for spot, stage in (("spawn", "corner"), ("pile", "pile"), ("centre", "centre")):
        print(d2.cs("FillPiles"))
        print(d2.cs("FreezeAnim", "0")); print(d2.cs("Stage", stage)); time.sleep(1.8); print(d2.cs("FreezeAnim", "1")); time.sleep(0.3)
        for mode in ("all", "noground", "nopile", "nosun", "all2"):
            print(d2.cs("Shot", mode, "", P(f"{tier}_{spot}" + ("" if mode == "all" else "_" + mode) + ".png")))
    print(eg("Overview", P(f"{tier}_overview.png")))
    # Lineup on open grass (E4 pose), rings hidden.
    print(d2.cs("FreezeAnim", "0")); print(e4.lu("Line", "2.0", "-4", "-4")); time.sleep(1.6)
    print(e4.lu("Rings", "0")); print(d2.cs("FreezeAnim", "1")); time.sleep(0.3)
    print(e4.lu("Shot", "", P(f"{tier}_lineup.png")))
    for c in CLASSES:
        print(e4.lu("Shot", c, P(f"{tier}_lineup_no{c}.png")))
    print(e4.lu("Shot", "", P(f"{tier}_lineup_all2.png")))
    # Rings: identity rings in each corner colour, on / off.
    for c in range(4):
        print(eg("ForceCorner", c)); print(eg("Rings", "0", "all")); print(eg("Rings", "1", "identity")); time.sleep(0.3)
        print(eg("Shot", P(f"{tier}_ring{c}_on.png")))
        print(eg("Rings", "0", "identity"))
        print(eg("Shot", P(f"{tier}_ring{c}_off.png")))
    print(eg("Rings", "1", "all"))


def main():
    try:
        print(eg("AsyncCompile", "0"))
        print(d2.cs("Quality", "capture"))
        d2.start(); time.sleep(2.0)
        print(d2.cs("FreezeBots")); print(d2.cs("HideUi", "1")); print(d2.cs("PinSeat0")); time.sleep(2.0)
        shoot("pc")
        print(d2.cs("Quality", "mobile")); time.sleep(2.5)
        shoot("mobile")
    finally:
        for label, step in (("async compile", lambda: print(eg("AsyncCompile", "1"))), ("quality", lambda: print(d2.cs("Quality", "restore", strict=False))), ("ui", lambda: d2.cs("HideUi", "0", strict=False)),
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
    subprocess.run([sys.executable, os.path.join(HERE, "e3_grass_metrics.py"), OUT], check=False)
