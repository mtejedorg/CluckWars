"""Phase 8 D4: the surround ground (outside the fence) and the hedges at the MATCH camera, both tiers, plus an overview.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d4_surround.py <output_dir>

Solo START, bots frozen, animators frozen, UI hidden, local chicken pinned to SEAT 0 (match camera yaw 225, D1 tool rule; the
capture_d2_tones.D2 menu / tier / seat plumbing). Per tier, two spots:
    spawn   local at its base (corner 0): the near hedge's OUTER side + crown, the outside ground beside it
    corner  local in the far corner (corner 2's base): the far hedges' INNER side + crown against the outside ground beyond them
  render pairs per spot (masks, the D2 rules):
    <tier>_<spot>.png              the frame as shipped                 (_all2 = the same frame again: noise mask)
    <tier>_<spot>_nosurround.png   MapSurroundings/SurroundGrass ShadowsOnly  -> surround ground mask
    <tier>_<spot>_noground.png     GeneratedGround ShadowsOnly                -> arena grass mask
    <tier>_<spot>_nohedge.png      ArenaHedge/* ShadowsOnly                   -> hedge mask
    <tier>_<spot>_nosun.png        the directional sun disabled               -> sunlit / shaded split
  <tier>_overview.png  free perspective camera over the whole arena + surround (tier MSAA + tier post; the E3 pose).
Then runs d4_surround_metrics.py. Performance Mode OFF (capture_common.perf_mode). Leaves Play mode in `finally` and verifies it.
"""
import sys, os, time, subprocess
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(HERE, "out", "d4")
os.makedirs(OUT, exist_ok=True)
import bridge
import capture_common
import capture_d2_tones as d2      # D2 class: menu driving, tier switch, seat pin, freeze, hide UI
d2.OUT = OUT

W, H = 1920, 1080

SG = r"""using Fusion; using UnityEngine; using System.Linq; using CluckWars.Gameplay; using CluckWars.Visuals;
using UnityEngine.Rendering; using UnityEngine.Rendering.Universal;
public class SG {
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority); }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  // spawn = local at corner 0's base; corner = local at corner 2's base (the far corner from seat 0). Everybody else parked at the arena centre side.
  public static string Stage(string which) { var l = Local(); var bases = Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None);
    var home = bases.First(b => b.CornerIndex == (which == "corner" ? 2 : 0));
    int i = 0; foreach (var k in ChickenController.ActiveControllers.Where(k => k != null && k != l).ToList()) Put(k, new Vector3(-6f + i++ * 1.6f, 0f, 6f));
    Put(l, home.transform.position); return "local at base " + home.CornerIndex + " " + home.transform.position.ToString("F2"); }
  static Renderer[] Targets(string mode) {
    var all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy);
    if (mode == "nosurround") return all.Where(r => r.name == "SurroundGrass").ToArray();
    if (mode == "noground") return all.Where(r => r.name == "GeneratedGround").ToArray();
    if (mode == "nohedge") return all.Where(r => r.transform.parent != null && r.transform.parent.name == "ArenaHedge").ToArray();
    return new Renderer[0]; }
  static Light Sun() { return Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x => x.type == LightType.Directional && x.enabled).OrderByDescending(x => x.intensity).FirstOrDefault(); }
  static void Save(Camera cam, string path) { var rt = CapTier.NewRt(""" + str(W) + ", " + str(H) + r"""); var prevT = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = prevT;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  public static string Shot(string mode, string path) {
    var ts = Targets(mode); if (mode.StartsWith("no") && mode != "nosun" && ts.Length == 0) return "ERROR no targets for " + mode;
    var old = ts.Select(r => r.shadowCastingMode).ToArray(); var sun = Sun(); if (mode == "nosun" && sun == null) return "ERROR no sun";
    foreach (var r in ts) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
    try { if (mode == "nosun") sun.enabled = false; Save(MatchCamera.Instance.Camera, path); }
    finally { for (int i = 0; i < ts.Length; i++) ts[i].shadowCastingMode = old[i]; if (sun != null) sun.enabled = true; }
    var cam = MatchCamera.Instance.Camera; float dy = Mathf.Abs(Mathf.DeltaAngle(cam.transform.eulerAngles.y, 225f)); if (dy > 1f) return "ERROR match camera yaw " + cam.transform.eulerAngles.y + " not at seat 0";
    var sg = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == "SurroundGrass");
    return "saved " + System.IO.Path.GetFileName(path) + " (" + mode + ", " + ts.Length + " rs; SurroundGrass mat " + (sg != null ? sg.sharedMaterial.name : "?") + ")"; }
  // Editor-only: async shader compilation draws a new variant as cyan for a few frames; the capture renders synchronously.
  public static string AsyncCompile(string on) { bool was = UnityEditor.ShaderUtil.allowAsyncCompilation; UnityEditor.ShaderUtil.allowAsyncCompilation = on == "1"; return "allowAsyncCompilation " + was + " -> " + UnityEditor.ShaderUtil.allowAsyncCompilation; }
  // Free perspective camera from the seat-0 side, high, the whole floor and a band of surround in frame (E3 pose).
  public static string Overview(string path) { var mc = MatchCamera.Instance.Camera; var go = new GameObject("__D4Overview");
    try { var cam = go.AddComponent<Camera>(); cam.CopyFrom(mc); cam.orthographic = false; cam.fieldOfView = 42f; cam.nearClipPlane = 0.5f; cam.farClipPlane = 300f;
      cam.transform.position = new Vector3(30f, 46f, 30f); cam.transform.LookAt(new Vector3(-2f, 0f, -2f)); cam.GetUniversalAdditionalCameraData(); bool post = CapTier.ApplyPost(cam);
      Save(cam, path); return "saved " + System.IO.Path.GetFileName(path) + " post=" + post; }
    finally { Object.DestroyImmediate(go); } }
}""" + capture_common.CAP_TIER_CS

SG_SIG = {"AsyncCompile": ["on"], "Stage": ["which"], "Shot": ["mode", "path"], "Overview": ["path"]}


def sg(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(SG_SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": SG, "className": "SG", "methodName": method, "parameters": params}, timeout=180)
    try:
        v = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        v = None
    if v is None or str(v).startswith("ERROR"):
        raise RuntimeError(f"SG.{method} failed: " + (str(v) if v is not None else bridge.text(m)[:800]))
    return v


def P(name):
    return os.path.join(OUT, name).replace("\\", "/")


def shoot(tier):
    print(f"PASS {tier.upper()}: " + d2.cs("Info"))
    for spot in ("spawn", "corner"):
        print(d2.cs("FreezeAnim", "0")); print(sg("Stage", spot)); time.sleep(1.8); print(d2.cs("FreezeAnim", "1")); time.sleep(0.3)
        for mode in ("all", "nosurround", "noground", "nohedge", "nosun", "all2"):
            print(sg("Shot", mode, P(f"{tier}_{spot}" + ("" if mode == "all" else "_" + mode) + ".png")))
    print(sg("Overview", P(f"{tier}_overview.png")))


def main():
    try:
        print(sg("AsyncCompile", "0"))
        print(d2.cs("Quality", "capture"))
        d2.start(); time.sleep(2.0)
        print(d2.cs("FreezeBots")); print(d2.cs("HideUi", "1")); print(d2.cs("PinSeat0")); time.sleep(2.0)
        shoot("pc")
        print(d2.cs("Quality", "mobile")); time.sleep(2.5)
        shoot("mobile")
    finally:
        for label, step in (("async compile", lambda: print(sg("AsyncCompile", "1"))), ("quality", lambda: print(d2.cs("Quality", "restore", strict=False))),
                            ("ui", lambda: d2.cs("HideUi", "0", strict=False)), ("play off", lambda: bridge.call("editor-application-set-state", {"isPlaying": False}))):
            try:
                step()
            except Exception as e:
                print(f"CLEANUP FAILED ({label}): {e}")
        st = ""
        for _ in range(20):
            time.sleep(2)
            st = bridge.text(bridge.call("editor-application-get-state", {}, timeout=30))
            if '"IsPlaying":false' in st and '"IsPlayingOrWillChangePlaymode":false' in st:
                print("Play mode OFF (verified)"); break
        else:
            print("WARNING: could not verify Play mode off: " + st[:300])


if __name__ == "__main__":
    bridge.init()
    with capture_common.perf_mode():
        main()
    subprocess.run([sys.executable, os.path.join(HERE, "d4_surround_metrics.py"), OUT], check=False)
