"""Phase 8 D5 (flower + mud decals) and D6 (dirt paths) at the MATCH camera, both tiers, with render-pair masks.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d5d6_dressing.py <output_dir>

Solo START, bots frozen, animators frozen, UI hidden, local chicken pinned to SEAT 0 (match camera yaw 225; the capture_d2_tones.D2
menu / tier / seat plumbing). The match timer is pushed out to 10 min for the capture (runtime only) so the run never meets the
match-end screen; Play mode is left in `finally` and verified. Spots (local chicken snapped to the nearest NavMesh point):
    spawn    local at corner 0's base (doorstep path, rim path, the quarter's flower + mud decals)
    centre   local beside the centre pile
    flower   local 2.2 m to the camera-left of corner 0's flower patch at (19.6, 10.6)
    mud      local beside corner 0's in-arena mud patch at (8.14, 16.75) (and the empty-pile disc of the home pile in frame)
    path     local beside the hub path run at (11.3, 15.6)
Per tier and spot, one call renders every mode (render pairs, the D2 rules):
    <tier>_<spot>.png            as shipped                         (_all2 = same frame again: noise mask)
    <tier>_<spot>_noflower.png   every Flower_* decal quad ShadowsOnly  -> flower mask
    <tier>_<spot>_nomud.png      every Mud_* decal quad ShadowsOnly     -> mud mask
    <tier>_<spot>_nopath.png     every DirtPaths_Base* ShadowsOnly        -> path mask
    <tier>_<spot>_nodisc.png     every PileZone_* ShadowsOnly             -> pile disc mask
    <tier>_<spot>_noground.png   GeneratedGround ShadowsOnly              -> grass mask
    <tier>_<spot>_nosun.png      the directional sun disabled             -> sunlit / shaded split
Then runs d5d6_metrics.py. Performance Mode OFF (capture_common.perf_mode).
"""
import sys, os, time, subprocess
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(HERE, "out", "d5d6")
os.makedirs(OUT, exist_ok=True)
import bridge
import capture_common
import capture_d2_tones as d2
d2.OUT = OUT

W, H = 1920, 1080
MODES = ("all", "noflower", "nomud", "nopath", "nodisc", "noground", "nosun", "all2")
SPOTS = ("spawn", "centre", "flower", "mud", "path")

DS = r"""using Fusion; using UnityEngine; using UnityEngine.AI; using System.Linq; using CluckWars.Gameplay; using CluckWars.Visuals;
using UnityEngine.Rendering; using UnityEngine.Rendering.Universal;
public class DS {
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority); }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  // Runtime only: push the match timer out so a capture never meets the 45 s match end.
  public static string HoldTimer() { var gm = GameManager.Instance; if (gm == null || !gm.Object.HasStateAuthority) return "ERROR no gm authority";
    gm.MatchTimer = TickTimer.CreateFromSeconds(gm.Runner, 600f); return "match timer -> 600 s"; }
  static Vector3 Fwd() { return Quaternion.Euler(0f, 225f, 0f) * Vector3.forward; }
  public static string Stage(string which) { var l = Local(); var home = Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).First(b => b.CornerIndex == 0);
    var others = ChickenController.ActiveControllers.Where(k => k != null && k != l).ToList(); int i = 0;
    foreach (var k in others) Put(k, -home.transform.position.normalized * 26f + new Vector3(i++ * 1.6f, 0, 0));
    var side = Vector3.Cross(Vector3.up, Fwd());
    Vector3 want;
    if (which == "spawn") want = home.transform.position;
    else if (which == "centre") want = new Vector3(4.6f, 0f, 4.6f);
    else if (which == "flower") want = new Vector3(19.6f, 0f, 10.6f) - side * 2.2f - Fwd() * 0.6f;
    else if (which == "mud") want = new Vector3(8.14f, 0f, 16.75f) - side * 1.4f;
    else if (which == "path") want = new Vector3(11.3f, 0f, 15.6f) + side * 1.2f;
    else return "ERROR spot " + which;
    NavMeshHit hit; if (which != "spawn" && NavMesh.SamplePosition(want, out hit, 3f, NavMesh.AllAreas)) want = hit.position;
    Put(l, want); return "local at " + which + " " + want.ToString("F2"); }
  static bool Under(Transform t, string prefix) { for (; t != null; t = t.parent) if (t.name.StartsWith(prefix)) return true; return false; }
  static Renderer[] Targets(string mode) {
    var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy);
    // decals are static-batched (MeshFilter reports "Combined Mesh"), so match by object name: Flower_* / Mud_* under the baker's roots
    if (mode == "noflower") return all.Where(r => r.name.StartsWith("Flower_") && (Under(r.transform, "MapGroundDressing") || Under(r.transform, "Vignettes"))).ToArray();
    if (mode == "nomud") return all.Where(r => r.name.StartsWith("Mud_") && (Under(r.transform, "MapGroundDressing") || Under(r.transform, "Vignettes"))).ToArray();
    if (mode == "nopath") return all.Where(r => Under(r.transform, "DirtPaths_Base")).ToArray();
    if (mode == "nodisc") return all.Where(r => Under(r.transform, "PileZone_")).ToArray();
    if (mode == "noground") return all.Where(r => r.name == "GeneratedGround").ToArray();
    return new Renderer[0]; }
  static Light Sun() { return Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x => x.type == LightType.Directional && x.enabled).OrderByDescending(x => x.intensity).FirstOrDefault(); }
  static void Save(Camera cam, string path) { var rt = CapTier.NewRt(""" + str(W) + ", " + str(H) + r"""); var prevT = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = prevT;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  // modes: comma list; files <prefix>[_<mode>].png
  public static string Shots(string modes, string prefix) { var cam = MatchCamera.Instance.Camera; var log = new System.Text.StringBuilder();
    foreach (var mode in modes.Split(',')) {
      var ts = Targets(mode); if (mode.StartsWith("no") && mode != "nosun" && ts.Length == 0) { log.Append(mode + ":0rs(skipped) "); continue; }
      var old = ts.Select(r => r.shadowCastingMode).ToArray(); var sun = Sun(); if (mode == "nosun" && sun == null) return "ERROR no sun";
      foreach (var r in ts) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
      try { if (mode == "nosun") sun.enabled = false; Save(cam, prefix + (mode == "all" ? "" : "_" + mode) + ".png"); }
      finally { for (int i = 0; i < ts.Length; i++) ts[i].shadowCastingMode = old[i]; if (sun != null) sun.enabled = true; }
      log.Append(mode + ":" + ts.Length + "rs "); }
    float dy = Mathf.Abs(Mathf.DeltaAngle(cam.transform.eulerAngles.y, 225f)); if (dy > 1f) return "ERROR match camera yaw " + cam.transform.eulerAngles.y + " not at seat 0";
    return "saved " + System.IO.Path.GetFileName(prefix) + " " + log; }
  public static string AsyncCompile(string on) { bool was = UnityEditor.ShaderUtil.allowAsyncCompilation; UnityEditor.ShaderUtil.allowAsyncCompilation = on == "1"; return "allowAsyncCompilation " + was + " -> " + UnityEditor.ShaderUtil.allowAsyncCompilation; }
}""" + capture_common.CAP_TIER_CS

SIG = {"AsyncCompile": ["on"], "Stage": ["which"], "Shots": ["modes", "prefix"]}


def ds(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": DS, "className": "DS", "methodName": method, "parameters": params}, timeout=240)
    try:
        v = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        v = None
    if v is None or str(v).startswith("ERROR"):
        raise RuntimeError(f"DS.{method} failed: " + (str(v) if v is not None else bridge.text(m)[:800]))
    return v


def P(name):
    return os.path.join(OUT, name).replace("\\", "/")


def shoot(tier):
    print(f"PASS {tier.upper()}: " + d2.cs("Info"))
    for spot in SPOTS:
        print(ds("HoldTimer"))
        print(d2.cs("FreezeAnim", "0")); print(ds("Stage", spot)); time.sleep(1.8); print(d2.cs("FreezeAnim", "1")); time.sleep(0.3)
        print(ds("Shots", ",".join(MODES), P(f"{tier}_{spot}")))


def main():
    try:
        print(ds("AsyncCompile", "0"))
        print(d2.cs("Quality", "capture"))
        d2.start(); time.sleep(1.0)
        print(ds("HoldTimer"))
        print(d2.cs("FreezeBots")); print(d2.cs("HideUi", "1")); print(d2.cs("PinSeat0")); time.sleep(2.0)
        shoot("pc")
        print(d2.cs("Quality", "mobile")); time.sleep(2.5)
        shoot("mobile")
    finally:
        for label, step in (("async compile", lambda: print(ds("AsyncCompile", "1"))), ("quality", lambda: print(d2.cs("Quality", "restore", strict=False))),
                            ("ui", lambda: d2.cs("HideUi", "0", strict=False)), ("play off", lambda: bridge.call("editor-application-set-state", {"isPlaying": False}))):
            try:
                step()
            except Exception as e:
                print(f"CLEANUP FAILED ({label}): {e}")
        st = ""
        for _ in range(20):
            time.sleep(2)
            try:
                st = bridge.text(bridge.call("editor-application-get-state", {}, timeout=30))
            except Exception as e:
                st = str(e)
            if '"IsPlaying":false' in st and '"IsPlayingOrWillChangePlaymode":false' in st:
                print("Play mode OFF (verified)"); break
            try:
                bridge.call("editor-application-set-state", {"isPlaying": False}, timeout=30)
            except Exception:
                pass
        else:
            print("WARNING: could not verify Play mode off: " + st[:300])


if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        main()
    subprocess.run([sys.executable, os.path.join(HERE, "d5d6_metrics.py"), OUT], check=False)
