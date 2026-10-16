"""Phase 8 chunk C R3: player-colour foot rings from the MATCH camera, both tiers.

    python tools/ui_capture/capture_c_rings.py <outdir>      (e.g. Captures/env3d_after/C/rings/before)

Per tier (pc, mobile) it lines the four classes up (Warrior | Speedy | Fatty | Assassin) on the grass, on the dirt path and on the base
straw, and writes phone-aspect (2400x1080) match-camera shots with the natural ring colours, plus a per-class WITH/WITHOUT-rings pair with the
corner colour forced to P4 (#17785A, the worst case) used to measure ring visibility and contrast against the grass:
  <tier>/grass_natural.png, dirt_natural.png, straw_natural.png           the lineup as shipped (+ per-class crops)
  <tier>/p4_<class>_with.png / p4_<class>_without.png                     ring forced to P4, rival rings hidden, ring on / off
  <tier>/stats.json                                                       per class: ring pixels, mean ring colour, WCAG contrast vs the surrounding grass
  <tier>/report.txt                                                       ring sizes per class
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Captures", "env3d_after", "C", "rings", "run"))
os.makedirs(OUT, exist_ok=True)

# Reuse the E3 helper class (menu driving, quality tier switch, bot freeze, UI hide) from capture_env3d.py without running its main().
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


RG = r"""using Fusion; using UnityEngine; using System.Linq; using System.Reflection; using System.Text; using CluckWars.Gameplay; using CluckWars.Visuals; using CluckWars.UI;
public class RG {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy).ToArray(); }
  static ChickenController Of(string cls) { return All().FirstOrDefault(k => k.Class.ToString() == cls); }
  static void Tp(ChickenController k, Vector3 p) { var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(p); else k.transform.position = p; }
  // Root Y that rests the capsule a skin width above the flat ground (y 0): the root sits at capsule mid-height and carries the class scale.
  static float RestY(ChickenController k) { var cc = k.GetComponent<CharacterController>(); return cc.skinWidth + k.transform.lossyScale.y * (cc.height * 0.5f - cc.center.y); }
  public static string Line(string gap, string cx, string cz) { float g = float.Parse(gap, CI); var a = All(); string[] order = { "Warrior", "Speedy", "Fatty", "Assassin" }; string o = "";
    foreach (var k in a) { int i = System.Array.IndexOf(order, k.Class.ToString()); Vector3 p = i >= 0 ? new Vector3(float.Parse(cx, CI) + (i - 1.5f) * g, RestY(k), float.Parse(cz, CI)) : new Vector3(-18f, RestY(k), -18f);
      Tp(k, p); k.transform.rotation = Quaternion.Euler(0, 180, 0); o += k.Class + "@" + p.x.ToString("0.0", CI) + "," + p.z.ToString("0.0", CI) + " "; } return o; }
  public static string Freeze(string on) { Time.timeScale = on == "1" ? 0f : 1f; return "timeScale=" + Time.timeScale; }
  public static string Spots() { var sb = new StringBuilder();
    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.name.Contains("DirtPath")) { var c = r.bounds.center; sb.Append("D" + r.name + ";" + c.x.ToString("0.0", CI) + ";" + c.z.ToString("0.0", CI) + ";" + r.bounds.size.x.ToString("0.0", CI) + ";" + r.bounds.size.z.ToString("0.0", CI) + "|"); }
    foreach (var b in Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None)) sb.Append("B" + b.transform.position.x.ToString("0.0", CI) + ";" + b.transform.position.z.ToString("0.0", CI) + "|"); return sb.ToString(); }
  static System.Collections.Generic.IEnumerable<SpriteRenderer> RingsOf(ChickenController k) { return k.GetComponentsInChildren<SpriteRenderer>(true).Where(s => s.name.StartsWith("IdentityRing") || s.name.StartsWith("RivalRing")); }
  public static string Rings(string cls, string on, string which) { int n = 0;
    foreach (var k in All()) { if (cls != "All" && k.Class.ToString() != cls) continue; foreach (var s in RingsOf(k)) { bool isRival = s.name.StartsWith("RivalRing");
        if (which == "identity" && isRival) continue; if (which == "rival" && !isRival) continue; s.enabled = on == "1"; n++; } } return "rings " + on + " x" + n; }
  public static string ForceCorner(string cls, string corner) { var k = Of(cls); var wb = k.GetComponent<ChickenWorldBars>(); var t = typeof(ChickenWorldBars);
    var c = PlayerPalette.ForCorner(int.Parse(corner)); c.a = 0.75f; var ring = (SpriteRenderer)t.GetField("_ring", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(wb);
    t.GetField("_appliedRingCorner", BindingFlags.NonPublic|BindingFlags.Instance).SetValue(wb, k.HomeCornerIndex); t.GetField("_ringBaseColor", BindingFlags.NonPublic|BindingFlags.Instance).SetValue(wb, c); ring.color = c; return cls + " ring=" + ColorUtility.ToHtmlStringRGBA(c); }
  static void Save(Camera cam, int W, int H, string path) {
    var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  public static string Shot(string w, string h, string path) { Save(MatchCamera.Instance.Camera, int.Parse(w), int.Parse(h), path); return "saved"; }
  public static string Vp(string cls, string w, string h) { var cam = MatchCamera.Instance.Camera; var k = Of(cls); int W = int.Parse(w), H = int.Parse(h); var prev = cam.aspect; cam.aspect = (float)W / H;
    var lo = new Vector2(1e9f, 1e9f); var hi = new Vector2(-1e9f, -1e9f);
    foreach (var r in k.GetComponentsInChildren<Renderer>()) { if (r is ParticleSystemRenderer) continue; var b = r.bounds; if (r is SpriteRenderer sr && !sr.name.StartsWith("IdentityRing")) continue;
      for (int i = 0; i < 8; i++) { var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1)); var v = cam.WorldToViewportPoint(c); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); } }
    cam.ResetAspect(); return string.Format(CI, "{0:0.0000};{1:0.0000};{2:0.0000};{3:0.0000}", lo.x, lo.y, hi.x, hi.y); }
  public static string Report() { var sb = new StringBuilder();
    foreach (var k in All().OrderBy(k => k.Class.ToString())) { var cc = k.GetComponent<CharacterController>(); float s = k.transform.lossyScale.y; sb.Append(k.Class + " rootScale=" + s.ToString("0.###", CI) + " capsuleR(world)=" + (cc.radius * s).ToString("0.000", CI));
      foreach (var r in RingsOf(k)) sb.Append(" | " + r.name + " worldDia=" + r.bounds.size.x.ToString("0.000", CI));
      sb.Append("\n"); } return sb.ToString(); }
}"""

RG_SIG = {"Freeze": ["on"], "Line": ["gap", "cx", "cz"], "Rings": ["cls", "on", "which"], "ForceCorner": ["cls", "corner"], "Shot": ["w", "h", "path"], "Vp": ["cls", "w", "h"]}


def rg(method, *args, timeout=300):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(RG_SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": RG, "className": "RG", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        raise RuntimeError(f"RG.{method} failed: " + bridge.text(m)[:800])


def P(*parts):
    return os.path.join(OUT, *parts).replace("\\", "/")


def lum(c):
    c = [x / 255.0 for x in c]
    c = [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def wcag(a, b):
    la, lb = lum(a), lum(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


def analyse(tier, cls):
    import numpy as np
    from PIL import Image
    from scipy import ndimage
    a = np.asarray(Image.open(P(tier, f"p4_{cls}_with.png")).convert("RGB")).astype(float)
    b = np.asarray(Image.open(P(tier, f"p4_{cls}_without.png")).convert("RGB")).astype(float)
    mask = np.abs(a - b).max(-1) > 10
    n = int(mask.sum())
    if n == 0:
        return {"ringPixels": 0}
    # Sunlit grass reference: a band 22-45 px outside the ring (clear of the bird's own shadow), upper half by luminance.
    near = ndimage.binary_dilation(mask, iterations=45) & ~ndimage.binary_dilation(mask, iterations=22)
    band = b[near]
    bl = np.array([lum(px) for px in band])
    grass = np.median(band[bl >= np.median(bl)], 0)
    ring = a[mask]
    L = np.array([lum(px) for px in ring])
    order = np.argsort(L)
    stroke = ring[order[: max(1, int(len(L) * 0.4))]]      # darkest 40 %: the colour stroke (P4 teal) without the light edge / feather
    edge = ring[order[-max(1, int(len(L) * 0.2)):]]        # lightest 20 %: the cream edge (before the change: just the feathered rim)
    return {"ringPixels": n, "grassRGB": [round(float(v), 1) for v in grass],
            "strokeRGB": [round(float(v), 1) for v in stroke.mean(0)], "strokeContrast": round(float(wcag(stroke.mean(0), grass)), 3),
            "edgeRGB": [round(float(v), 1) for v in edge.mean(0)], "edgeContrast": round(float(wcag(edge.mean(0), grass)), 3)}


def crop(src, dst, vp, W, H, pad=0.03):
    from PIL import Image
    x0, y0, x1, y1 = (float(v) for v in vp.split(";"))
    im = Image.open(src)
    box = (int(max(0, x0 - pad) * W), int(max(0, 1 - y1 - pad * 2) * H), int(min(1, x1 + pad) * W), int(min(1, 1 - y0 + pad * 2) * H))
    im.crop(box).save(dst)


def run_tier(tier, spots):
    os.makedirs(P(tier), exist_ok=True)
    W, H = 2400, 1080
    stats = {}
    print(rg("Line", "2.6", "0", "-6")); time.sleep(1.6)
    rg("Shot", str(W), str(H), P(tier, "grass_natural.png"))
    report = rg("Report"); open(P(tier, "report.txt"), "w", encoding="utf-8").write(report); print(report)
    classes = ("Warrior", "Speedy", "Fatty", "Assassin")
    vps = {c: rg("Vp", c, str(W), str(H)) for c in classes}
    for c in classes:
        crop(P(tier, "grass_natural.png"), P(tier, f"grass_natural_{c}.png"), vps[c], W, H)
    rg("Rings", "All", "0", "rival")
    rg("Freeze", "1")   # animators stop, so the WITH / WITHOUT pair differs only by the ring
    for c in classes:
        print(rg("ForceCorner", c, "3"))
    time.sleep(0.4)
    for c in classes:
        rg("Rings", "All", "0", "identity"); rg("Rings", c, "1", "identity")
        rg("Shot", str(W), str(H), P(tier, f"p4_{c}_with.png"))
        rg("Rings", c, "0", "identity")
        rg("Shot", str(W), str(H), P(tier, f"p4_{c}_without.png"))
        rg("Rings", "All", "1", "identity")
        stats[c] = analyse(tier, c)
        crop(P(tier, f"p4_{c}_with.png"), P(tier, f"p4_{c}_crop.png"), vps[c], W, H)
        print(tier, c, stats[c])
    rg("Rings", "All", "1", "all")
    rg("Freeze", "0")
    json.dump(stats, open(P(tier, "stats.json"), "w"), indent=1)
    for label, key in (("dirt", "D"), ("straw", "B")):
        sp = [s.split(";") for s in spots.split("|") if s.startswith(key)]
        if not sp:
            print("no", label, "spot")
            continue
        if key == "D":
            sp.sort(key=lambda s: -float(s[3]) * float(s[4]))
            x, z = float(sp[0][1]), float(sp[0][2])
        else:
            x, z = float(sp[0][0][1:]) * 0.85, float(sp[0][1]) * 0.85
        print(label, x, z, rg("Line", "2.6", f"{x:.2f}", f"{z:.2f}")); time.sleep(1.8)
        rg("Shot", str(W), str(H), P(tier, f"{label}_natural.png"))
        for c in classes:
            crop(P(tier, f"{label}_natural.png"), P(tier, f"{label}_natural_{c}.png"), rg("Vp", c, str(W), str(H)), W, H)
    return stats


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
        spots = rg("Spots"); print("spots", spots)
        allstats = {"pc": run_tier("pc", spots)}
        print(e3("Quality", "mobile")); time.sleep(2.5)
        allstats["mobile"] = run_tier("mobile", spots)
        json.dump(allstats, open(P("stats_all.json"), "w"), indent=1)
    finally:
        cleanup()


def reanalyse():
    allstats = {}
    for tier in ("pc", "mobile"):
        allstats[tier] = {c: analyse(tier, c) for c in ("Warrior", "Speedy", "Fatty", "Assassin")}
        json.dump(allstats[tier], open(P(tier, "stats.json"), "w"), indent=1)
    json.dump(allstats, open(P("stats_all.json"), "w"), indent=1)
    for t, v in allstats.items():
        for c, st in v.items():
            print(t, c, st)


if os.environ.get("ANALYSE_ONLY"):
    reanalyse()
else:
    main()
