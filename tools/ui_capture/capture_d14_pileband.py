"""Phase 8 D14 (pile slow-band apron): home / contested / centre pile at full and half fill, MATCH camera (seat 0), both tiers, with and without the apron.

Usage (Editor open, Bootstrap.unity the only open scene, not in Play mode):
    python tools/ui_capture/capture_d14_pileband.py <output_dir>     (D14_QUICK=1: home pile, full fill, PC only - tuning loop)

Solo START, bots frozen, animators frozen, UI hidden, local chicken pinned to SEAT 0 (match camera yaw 225; the capture_d2_tones.D2
menu / tier / seat plumbing). The match timer is pushed out to 10 min for the capture (runtime only) so the run never meets the
match-end screen; Play mode is left in `finally` and verified. Piles: home (corner 0), contested, centre; each at full and half fill
(runtime Amount only), the local chicken snapped to the nearest NavMesh point beside it. Per tier (pc, mobile) and pile, a render pair
at the same frame:
    after/<tier>_<pile>_<fill>.png    as shipped (apron on)
    before/<tier>_<pile>_<fill>.png   every pile's SlowBand renderer forceRenderingOff -> the apron mask is the pixel difference
Performance Mode OFF (capture_common.perf_mode).
"""
import sys, os, time, subprocess
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(HERE, "out", "d12")
os.makedirs(os.path.join(OUT, "before"), exist_ok=True); os.makedirs(os.path.join(OUT, "after"), exist_ok=True)
import bridge
import capture_common
import capture_d2_tones as d2
d2.OUT = OUT

W, H = 1920, 1080
MODES = ("all",)
QUICK = bool(os.environ.get("D14_QUICK"))   # one pile, full fill, PC only (tuning loop)
PILES = ("home",) if QUICK else ("home", "contested", "centre")
FILLS = (("full", "1"),) if QUICK else (("full", "1"), ("half", "0.5"))

DS = r"""using Fusion; using UnityEditor; using UnityEngine; using UnityEngine.AI; using System.Linq; using CluckWars.Gameplay; using CluckWars.Visuals;
using UnityEngine.Rendering; using UnityEngine.Rendering.Universal;
public class DS {
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority); }
  static void Put(ChickenController k, Vector3 pos) { pos.y = k.transform.position.y; var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else k.transform.position = pos; }
  // Runtime only: push the match timer out so a capture never meets the 45 s match end.
  public static string HoldTimer() { var gm = GameManager.Instance; if (gm == null || !gm.Object.HasStateAuthority) return "ERROR no gm authority";
    gm.MatchTimer = TickTimer.CreateFromSeconds(gm.Runner, 600f); return "match timer -> 600 s"; }
  static Vector3 Fwd() { return Quaternion.Euler(0f, 225f, 0f) * Vector3.forward; }
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static FoodPile[] Piles() { return Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None); }
  public static string FillPiles() { string o = ""; foreach (var p in Piles()) { if (p.Object.HasStateAuthority) p.Amount = p.MaxAmount; } return "piles full x" + Piles().Length; }
  static FoodPile Subject(string which) { var ps = Piles();
    if (which == "centre") return ps.OrderBy(p => p.transform.position.sqrMagnitude).First();
    Vector3 want = which == "home" ? new Vector3(16.23f, 0f, 16.23f) : new Vector3(20.25f, 0f, 0f);
    return ps.OrderBy(p => (p.transform.position - want).sqrMagnitude).First(); }
  public static string Stage(string which, string frac) { var l = Local(); var pile = Subject(which); var fwd = Fwd();
    var home = Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None).First(b => b.CornerIndex == 0);
    var others = ChickenController.ActiveControllers.Where(k => k != null && k != l).ToList(); int i = 0;
    foreach (var k in others) Put(k, -home.transform.position.normalized * 26f + new Vector3(i++ * 1.6f, 0, 0));
    pile.Amount = pile.MaxAmount * float.Parse(frac, CI);   // runtime only (the match is thrown away)
    var side = Vector3.Cross(Vector3.up, fwd);
    var want = pile.SurfaceApproachPoint(pile.transform.position - (fwd - side * 0.6f).normalized * 100f, 3.2f);
    NavMeshHit hit; if (NavMesh.SamplePosition(want, out hit, 3f, NavMesh.AllAreas)) want = hit.position;
    Put(l, want); SessionState.SetInt("d14_pile", pile.GetInstanceID());
    return "pile " + pile.name + " pos " + pile.transform.position.ToString("F2") + " yaw " + pile.transform.eulerAngles.y.ToString("F0") + " footprint " + pile.CurrentFootprint.ToString("F2") + " amount " + pile.Amount + "/" + pile.MaxAmount + " centre=" + pile.IsCentre + " local " + want.ToString("F2"); }
  public static string BandInfo() { var pile = Piles().FirstOrDefault(p => p.GetInstanceID() == SessionState.GetInt("d14_pile", 0)); if (pile == null) return "ERROR staged pile gone"; var t = pile.transform.Find("SlowBand"); if (t == null) return "no SlowBand child";
    var r = t.GetComponent<Renderer>(); var mf = t.GetComponent<MeshFilter>(); return "SlowBand enabled=" + r.enabled + " verts=" + (mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1) + " lossy=" + t.lossyScale.ToString("F3") + " worldBounds=" + r.bounds.size.ToString("F2"); }
  static Renderer[] Bands() { return Piles().Select(p => p.transform.Find("SlowBand")).Where(t => t != null).Select(t => t.GetComponent<Renderer>()).ToArray(); }
  static bool Under(Transform t, string prefix) { for (; t != null; t = t.parent) if (t.name.StartsWith(prefix)) return true; return false; }
  static Renderer[] Targets(string mode) {
    if (mode == "noband") return Bands();
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
      var forced = mode == "noband" ? ts : new Renderer[0]; foreach (var r in forced) r.forceRenderingOff = true;
      try { if (mode == "nosun") sun.enabled = false; Save(cam, prefix + (mode == "all" || mode == "noband" ? "" : "_" + mode) + ".png"); }
      finally { for (int i = 0; i < ts.Length; i++) ts[i].shadowCastingMode = old[i]; foreach (var r in forced) r.forceRenderingOff = false; if (sun != null) sun.enabled = true; }
      log.Append(mode + ":" + ts.Length + "rs "); }
    return "saved " + System.IO.Path.GetFileName(prefix) + " camyaw " + cam.transform.eulerAngles.y.ToString("F1") + " " + log; }
  public static string AsyncCompile(string on) { bool was = UnityEditor.ShaderUtil.allowAsyncCompilation; UnityEditor.ShaderUtil.allowAsyncCompilation = on == "1"; return "allowAsyncCompilation " + was + " -> " + UnityEditor.ShaderUtil.allowAsyncCompilation; }
}""" + capture_common.CAP_TIER_CS

SIG = {"AsyncCompile": ["on"], "Stage": ["which", "frac"], "Shots": ["modes", "prefix"]}


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
    for which in PILES:
        for tag, frac in FILLS:
            print(ds("HoldTimer")); print(ds("FillPiles"))
            print(d2.cs("FreezeAnim", "0")); print(ds("Stage", which, frac)); time.sleep(2.0); print(d2.cs("FreezeAnim", "1")); time.sleep(0.3)
            print(ds("BandInfo"))
            print(ds("Shots", "all", P(f"after/{tier}_{which}_{tag}")))
            print(ds("Shots", "noband", P(f"before/{tier}_{which}_{tag}")))


def main():
    try:
        print(ds("AsyncCompile", "0"))
        print(d2.cs("Quality", "capture"))
        d2.start(); time.sleep(1.0)
        print(ds("HoldTimer"))
        print(d2.cs("FreezeBots")); print(d2.cs("HideUi", "1")); print(d2.cs("PinSeat0")); time.sleep(2.0)
        shoot("pc")
        if not QUICK:
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
    pass
