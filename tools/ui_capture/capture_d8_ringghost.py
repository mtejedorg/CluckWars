"""Phase 8 chunk D D8: foot rings that run under a food-pile mound, from the MATCH camera, both tiers.

    python tools/ui_capture/capture_d8_ringghost.py <outdir>      (e.g. Captures/env3d_after/D/D8/before)

Per tier (pc, mobile), per pile (home = the non-centre pile nearest the local spawn, centre) and per layout:
  A  local chicken on the NEAR side of the pile (between camera and mound), a rival on the FAR side
  B  swapped (local far, rival near)
Both chickens stand where the pile's solid blocker leaves them (pile surface + their capsule radius), so the ring runs under the mound
exactly as when pecking. Writes:
  <tier>/<pile>_<layout>.png                    the shot as shipped (2400x1080, all rings on)
  <tier>/<pile>_<layout>_<who>.png              crop round each chicken
  <tier>/stats.json                             per ring (local identity, rival identity, rival ring): ring pixels drawn with the mound
                                                hidden (= the whole ring as the camera would see it on open grass), visible with the mound,
                                                and (after D8) how many of those are ghost pixels; fraction = visible / open
Ring pixels = max-channel difference > 10/255 between a render with only that ring on and one with it off (animation frozen).
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge
import capture_common

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Captures", "env3d_after", "D", "D8", "run"))
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

W, H = 2400, 1080

# CIELAB / CIEDE2000 helpers shared with the D1 mound metrics (copied out of the module: it runs on import).
_d1 = open(os.path.join(HERE, "d1_mound_metrics.py"), encoding="utf-8").read()
_d1ns = {}
exec("import math; import numpy as np\n" + _d1[_d1.index("def srgb_to_lab"):_d1.index("def hue(")], _d1ns)
srgb_to_lab, de00 = _d1ns["srgb_to_lab"], _d1ns["de00"]

RG = r"""using Fusion; using UnityEngine; using System.Linq; using System.Reflection; using System.Text; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals;
public class D8 {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy).ToArray(); }
  static ChickenController Local() { return All().FirstOrDefault(k => k.HasInputAuthority); }
  static ChickenController Rival() { var l = Local(); var r = All().Where(k => !k.HasInputAuthority).ToArray(); return r.FirstOrDefault(k => k.Class.ToString() == "Fatty" && (l == null || k.Class != l.Class)) ?? r.FirstOrDefault(k => l == null || k.Class != l.Class) ?? r.FirstOrDefault(); }
  static void Tp(ChickenController k, Vector3 p) { var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(p); else k.transform.position = p; }
  static float RestY(ChickenController k) { var cc = k.GetComponent<CharacterController>(); return cc.skinWidth + k.transform.lossyScale.y * (cc.height * 0.5f - cc.center.y); }
  static float CapR(ChickenController k) { var cc = k.GetComponent<CharacterController>(); return cc.radius * k.transform.lossyScale.x; }
  static Vector3 Spawn;
  public static string Init() { var l = Local(); if (l == null) return "ERROR no local"; Spawn = l.transform.position; return "spawn " + Spawn + " local " + l.Class + " rival " + (Rival() != null ? Rival().Class.ToString() : "none"); }
  static FoodPile Pile(string which) { var piles = Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None);
    if (which == "centre") return piles.FirstOrDefault(p => p.IsCentre);
    return piles.Where(p => !p.IsCentre).OrderBy(p => (p.transform.position - Spawn).sqrMagnitude).FirstOrDefault(); }
  static Renderer Mound(FoodPile p) { var t = p.transform.Find("Mound/MoundMesh"); return t != null ? t.GetComponent<Renderer>() : null; }
  public static string Place(string which, string layout) { var p = Pile(which); if (p == null) return "ERROR no pile " + which;
    var l = Local(); var r = Rival(); var fwd = MatchCamera.Instance.Camera.transform.forward; fwd.y = 0; fwd.Normalize();
    var c = p.transform.position; c.y = 0;
    ChickenController nearK = layout == "A" ? l : r, farK = layout == "A" ? r : l;
    var nearP = p.SurfaceApproachPoint(c - fwd * 100f, CapR(nearK) + 0.04f); nearP.y = RestY(nearK);
    var farP = p.SurfaceApproachPoint(c + fwd * 100f, CapR(farK) + 0.04f); farP.y = RestY(farK);
    Tp(nearK, nearP); Tp(farK, farP);
    var face = Quaternion.LookRotation(-fwd); nearK.transform.rotation = face; farK.transform.rotation = face;
    int i = 0; foreach (var k in All()) { if (k == l || k == r) continue; var side = Vector3.Cross(Vector3.up, fwd); var q = c - fwd * 17f + side * (i * 1.6f - 2f); q.y = RestY(k); Tp(k, q); i++; }
    return string.Format(CI, "{0} pile {1} amount {2:0}/{3:0} near {4}@{5} far {6}@{7}", which, p.name, p.Amount, p.MaxAmount, nearK.Class, nearP, farK.Class, farP); }
  public static string Freeze(string on) { Time.timeScale = on == "1" ? 0f : 1f; return "timeScale=" + Time.timeScale; }
  static IEnumerable<SpriteRenderer> RingsOf(ChickenController k) { return k.GetComponentsInChildren<SpriteRenderer>(true).Where(s => s.name.StartsWith("IdentityRing") || s.name.StartsWith("RivalRing")); }
  static void AllRings(bool on) { foreach (var k in All()) foreach (var s in RingsOf(k)) s.enabled = on; }
  // ring = "identity" | "rival"; parts: the ring renderer + its edge (+ ghosts); ghost renderers are the ones whose name ends in "Ghost".
  static List<SpriteRenderer> Parts(ChickenController k, string ring) { string pre = ring == "identity" ? "IdentityRing" : "RivalRing"; return RingsOf(k).Where(s => s.name.StartsWith(pre)).ToList(); }
  static Color32[] Render() { var cam = MatchCamera.Instance.Camera; var rt = CapTier.NewRt(W, H); var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    var px = tex.GetPixels32(); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); return px; }
  const int W = 2400, H = 1080;
  static Vector3 MeanWith, MeanWithout;
  static int Diff(Color32[] a, Color32[] b) { int n = 0; var sa = Vector3.zero; var sb = Vector3.zero; for (int i = 0; i < a.Length; i++) { int d = System.Math.Max(System.Math.Abs(a[i].r - b[i].r), System.Math.Max(System.Math.Abs(a[i].g - b[i].g), System.Math.Abs(a[i].b - b[i].b)));
      if (d > 10) { n++; sa += new Vector3(a[i].r, a[i].g, a[i].b); sb += new Vector3(b[i].r, b[i].g, b[i].b); } }
    MeanWith = n > 0 ? sa / n : Vector3.zero; MeanWithout = n > 0 ? sb / n : Vector3.zero; return n; }
  static string V(Vector3 v) { return string.Format(CI, "[{0:0.0},{1:0.0},{2:0.0}]", v.x, v.y, v.z); }
  static int Count(ChickenController k, string ring, bool ghostOnly) { AllRings(false); var parts = Parts(k, ring); if (parts.Count == 0) return -1;
    foreach (var s in parts) s.enabled = true; var with = Render();
    foreach (var s in parts) if (!ghostOnly || s.name.EndsWith("Ghost")) s.enabled = false; var without = Render(); return Diff(with, without); }
  public static string Measure(string which) { var p = Pile(which); var m = Mound(p); if (m == null) return "ERROR no mound"; var l = Local(); var r = Rival(); var sb = new StringBuilder("{");
    var cases = new List<KeyValuePair<string, KeyValuePair<ChickenController, string>>> {
      new KeyValuePair<string, KeyValuePair<ChickenController, string>>("local_identity", new KeyValuePair<ChickenController, string>(l, "identity")),
      new KeyValuePair<string, KeyValuePair<ChickenController, string>>("rival_identity", new KeyValuePair<ChickenController, string>(r, "identity")),
      new KeyValuePair<string, KeyValuePair<ChickenController, string>>("rival_ring", new KeyValuePair<ChickenController, string>(r, "rival")) };
    bool first = true;
    foreach (var cs in cases) { var k = cs.Value.Key; string ring = cs.Value.Value;
      // The rival ring is lit by RivalIndicator only while on screen; make sure its GameObject is active for the measurement.
      m.enabled = true; int vis = Count(k, ring, false); int ghost = 0; string gc = "null,null"; if (Parts(k, ring).Any(s => s.name.EndsWith("Ghost"))) { ghost = Count(k, ring, true); gc = V(MeanWith) + "," + V(MeanWithout); }
      m.enabled = false; int open = Count(k, ring, false); m.enabled = true;
      if (!first) sb.Append(","); first = false;
      sb.Append(string.Format(CI, "\"{0}\":{{\"class\":\"{1}\",\"open\":{2},\"visible\":{3},\"ghost\":{4},\"fraction\":{5:0.000},\"solidFraction\":{6:0.000},\"ghostRGB\":{7}}}", cs.Key, k.Class, open, vis, ghost,
        open > 0 ? (float)vis / open : 0f, open > 0 ? (float)(vis - ghost) / open : 0f, "[" + gc + "]")); }
    AllRings(true); sb.Append("}"); return sb.ToString(); }
  public static string Shot(string path) { var cam = MatchCamera.Instance.Camera; var rt = CapTier.NewRt(W, H); var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); return "saved"; }
  // D8 cost A/B (D8_COST=1): ghosts + the mound stencil mark on / off at the same pose; Game-view stats of the last frame.
  public static string Ghosts(string on) { int g = 0, m = 0; var mark = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/MoundStencilMark.mat");
    foreach (var k in All()) foreach (var s in RingsOf(k)) if (s.name.EndsWith("Ghost")) { s.enabled = on == "1"; g++; }
    foreach (var p in Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None)) { var r = Mound(p); if (r == null) continue; var mats = r.sharedMaterials;
      if (on == "1" && mats.Length == 1) r.sharedMaterials = new[] { mats[0], mark }; if (on == "0" && mats.Length == 2) r.sharedMaterials = new[] { mats[0] }; m++; }
    return "ghosts " + g + " mounds " + m + " on=" + on; }
  public static string Stats() { return UnityEditor.UnityStats.batches + ";" + UnityEditor.UnityStats.setPassCalls + ";" + UnityEditor.UnityStats.drawCalls + ";" + UnityEditor.UnityStats.triangles; }
  public static string Vp(string who) { var cam = MatchCamera.Instance.Camera; var k = who == "local" ? Local() : Rival(); var prev = cam.aspect; cam.aspect = (float)W / H;
    var lo = new Vector2(1e9f, 1e9f); var hi = new Vector2(-1e9f, -1e9f);
    foreach (var rr in k.GetComponentsInChildren<Renderer>()) { if (rr is ParticleSystemRenderer) continue; if (rr is SpriteRenderer sr && !sr.name.StartsWith("RivalRing") && !sr.name.StartsWith("IdentityRing")) continue; var b = rr.bounds;
      for (int i = 0; i < 8; i++) { var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1)); var v = cam.WorldToViewportPoint(c); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); } }
    cam.ResetAspect(); return string.Format(CI, "{0:0.0000};{1:0.0000};{2:0.0000};{3:0.0000}", lo.x, lo.y, hi.x, hi.y); }
}""" + capture_common.CAP_TIER_CS

SIG = {"Ghosts": ["on"], "Place": ["which", "layout"], "Freeze": ["on"], "Measure": ["which"], "Shot": ["path"], "Vp": ["who"]}


def rg(method, *args, timeout=300):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": RG, "className": "D8", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        raise RuntimeError(f"D8.{method} failed: " + bridge.text(m)[:800])


def P(*parts):
    return os.path.join(OUT, *parts).replace("\\", "/")


def crop(src, dst, vp, pad=0.04):
    from PIL import Image
    x0, y0, x1, y1 = (float(v) for v in vp.split(";"))
    im = Image.open(src)
    box = (int(max(0, x0 - pad) * W), int(max(0, 1 - y1 - pad * 2) * H), int(min(1, x1 + pad) * W), int(min(1, 1 - y0 + pad * 2) * H))
    im.crop(box).save(dst)


def run_tier(tier):
    os.makedirs(P(tier), exist_ok=True)
    stats = {}
    for pile in ("home", "centre"):
        for layout in ("A", "B"):
            rg("Freeze", "0")
            print(rg("Place", pile, layout)); time.sleep(2.0)
            rg("Freeze", "1"); time.sleep(0.3)
            key = f"{pile}_{layout}"
            rg("Shot", P(tier, key + ".png"))
            for who in ("local", "rival"):
                crop(P(tier, key + ".png"), P(tier, f"{key}_{who}.png"), rg("Vp", who))
            stats[key] = json.loads(rg("Measure", pile))
            for v in stats[key].values():
                g = v.get("ghostRGB")
                if g and g[0] is not None:   # ghost pixels: drawn colour vs the mound under them (CIEDE2000)
                    v["ghostVsMoundDE00"] = round(de00(srgb_to_lab(g[0]), srgb_to_lab(g[1])), 1)
            print(tier, key, e3("State", strict=False), json.dumps(stats[key]))
    rg("Freeze", "0")
    json.dump(stats, open(P(tier, "stats.json"), "w"), indent=1)
    return stats


def cleanup():
    for label, step in (("unfreeze", lambda: rg("Freeze", "0")),
                        ("restore quality", lambda: print(e3("Quality", "restore"))),
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
        cost_ab() if os.environ.get("D8_COST") else _main()


def start_match():
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
    time.sleep(1.5); print(e3("FreezeBots")); print(e3("HideUi", "1"))
    info = rg("Init"); print(info)
    return info


def cost_ab():
    """D8_COST=1: Mobile tier, local + rival at the home and centre piles (layout B), ghosts/mark on vs off, Game-view stats (median of 15 frames)."""
    res = {}
    try:
        print(e3("Quality", "capture")); start_match(); print(e3("Quality", "mobile")); time.sleep(2.5)
        for pile in ("home", "centre"):
            print(rg("Place", pile, "B")); time.sleep(2.0)
            for on in ("1", "0", "1", "0"):
                print(rg("Ghosts", on)); time.sleep(0.8)
                xs = []
                for _ in range(15):
                    xs.append([int(v) for v in rg("Stats").split(";")]); time.sleep(0.1)
                med = [sorted(c)[len(c) // 2] for c in zip(*xs)]
                res.setdefault(pile, {}).setdefault("on" if on == "1" else "off", []).append(dict(zip(("batches", "setPass", "draws", "tris"), med)))
                print(pile, on, med)
        rg("Ghosts", "1")
        json.dump(res, open(P("cost_ab.json"), "w"), indent=1)
    finally:
        cleanup()


def _main():
    # One Play session per tier: a solo match ends after MatchConfig's 45 s, and one tier's pass takes about that long.
    allstats = {}
    try:
        for tier in ("pc", "mobile"):
            print(e3("Quality", "capture"))
            allstats[tier + "_seat"] = start_match()
            if tier == "mobile":
                print(e3("Quality", "mobile")); time.sleep(2.5)
            print(e3("PassInfo", "PASS " + tier.upper()))
            allstats[tier] = run_tier(tier)
            cleanup()
        json.dump(allstats, open(P("stats_all.json"), "w"), indent=1)
    finally:
        cleanup()

main()
