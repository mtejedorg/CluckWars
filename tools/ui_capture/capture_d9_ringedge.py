"""Phase 8 D9: foot-ring edge vs the ground it sits on, per seat, from the MATCH camera, both tiers.

    python tools/ui_capture/capture_d9_ringedge.py <outdir>      (e.g. Captures/env3d_after/D/D9/before)
    ANALYSE_ONLY=1 python tools/ui_capture/capture_d9_ringedge.py <outdir>

Per tier (pc, mobile) the four classes are lined up on four grounds - grass, the dirt path, the base straw and beside a food pile (mound) -
and every ring variant is rendered in ONE script call per spot (no frame passes between the shots, so the only difference is the ring):
  <tier>/<spot>/off.png                 identity + rival rings hidden (the ground under every ring pixel)
  <tier>/<spot>/id_edge.png             identity ring edge only
  <tier>/<spot>/id_stroke_<s>.png       identity colour stroke only, every bird forced to seat s (P1..P4)
  <tier>/<spot>/id_full_<s>.png         identity ring as shipped (stroke + edge + D8 ghost), seat s
  <tier>/<spot>/rv_edge.png / rv_stroke_<s>.png / rv_full_<s>.png       the same for the rival rings (identity rings hidden)
Per pixel: WCAG contrast of the rendered stroke / edge against the SAME pixel with the ring off (= the ground it sits on), split into sunlit
and shaded ground (the birds' own shadows fall across their rings). Core pixels only (the stronger half of each mask, so 1-px anti-aliased
fringes do not dilute the edge). `metrics.json` + `table.txt` per run; "ring" = max(stroke, edge) vs ground = the ring as a whole.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Captures", "env3d_after", "D", "D9", "run"))
os.makedirs(OUT, exist_ok=True)
SEATS = ("P1", "P2", "P3", "P4")
SPOTS = ("grass", "dirt", "straw", "mound")
W, H = 2400, 1080

RG = r"""using Fusion; using UnityEngine; using System.Linq; using System.Reflection; using System.Text; using System.Collections.Generic; using CluckWars.Gameplay; using CluckWars.Visuals; using CluckWars.UI;
public class RG {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy).ToArray(); }
  static void Tp(ChickenController k, Vector3 p) { var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(p); else k.transform.position = p; }
  static float RestY(ChickenController k) { var cc = k.GetComponent<CharacterController>(); return cc.skinWidth + k.transform.lossyScale.y * (cc.height * 0.5f - cc.center.y); }
  // Lines the four classes up along the camera's screen-right axis centred on (cx, cz) (mode "line"), or a 2x2 grid of step `gap` (mode "grid").
  public static string Place(string mode, string gap, string cx, string cz) { float g = float.Parse(gap, CI); string[] order = { "Warrior", "Speedy", "Fatty", "Assassin" }; string o = "";
    var r = MatchCamera.Instance.Camera.transform.right; r.y = 0; r.Normalize(); var f = new Vector3(-r.z, 0, r.x);
    foreach (var k in All()) { int i = System.Array.IndexOf(order, k.Class.ToString()); var c = new Vector3(float.Parse(cx, CI), 0, float.Parse(cz, CI));
      Vector3 p = i < 0 ? new Vector3(-18f, 0, -18f) : mode == "grid" ? c + r * (((i % 2) - 0.5f) * g) + f * (((i / 2) - 0.5f) * g) : c + r * ((i - 1.5f) * g); p.y = RestY(k);
      Tp(k, p); k.transform.rotation = Quaternion.Euler(0, 180, 0); o += k.Class + "@" + p.x.ToString("0.0", CI) + "," + p.z.ToString("0.0", CI) + " "; } return o; }
  // Four spots along the longest dirt path (mesh vertices, world space), ~2.6 m apart, starting from the vertex nearest 12 m from the centre.
  public static string DirtPoints() { var vs = new List<Vector3>();
    foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)) if (mf.name.Contains("DirtPath") && mf.sharedMesh != null && mf.sharedMesh.isReadable) foreach (var v in mf.sharedMesh.vertices) vs.Add(mf.transform.TransformPoint(v));
    if (vs.Count == 0) return "ERROR no readable DirtPath mesh";
    var pts = new List<Vector3> { vs.OrderBy(v => Mathf.Abs(new Vector2(v.x, v.z).magnitude - 12f)).First() };
    while (pts.Count < 4) { var last = pts[pts.Count - 1]; var cand = vs.Where(v => { float d = Vector3.Distance(v, last); return d > 2.4f && d < 3.2f && pts.All(q => Vector3.Distance(q, v) > 2.4f); }).OrderBy(v => -Vector3.Distance(v, pts[0])).FirstOrDefault();
      if (cand == Vector3.zero) break; pts.Add(cand); }
    return string.Join("|", pts.Select(v => v.x.ToString("0.00", CI) + ";" + v.z.ToString("0.00", CI))); }
  public static string PlaceAt(string pts) { var a = pts.Split('|'); string[] order = { "Warrior", "Speedy", "Fatty", "Assassin" }; string o = "";
    foreach (var k in All()) { int i = System.Array.IndexOf(order, k.Class.ToString()); if (i < 0 || i >= a.Length) continue; var t = a[i].Split(';');
      var p = new Vector3(float.Parse(t[0], CI), RestY(k), float.Parse(t[1], CI)); Tp(k, p); k.transform.rotation = Quaternion.Euler(0, 180, 0); o += k.Class + "@" + p.x.ToString("0.0", CI) + "," + p.z.ToString("0.0", CI) + " "; } return o; }
  public static string Freeze(string on) { Time.timeScale = on == "1" ? 0f : 1f; return "timeScale=" + Time.timeScale; }
  // Spot centres: D<dirt path centre>, B<base>, P<pile spot between camera and the pile nearest the arena centre that is NOT the centre pile>.
  public static string Spots() { var sb = new StringBuilder();
    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.name.Contains("DirtPath")) { var c = r.bounds.center; sb.Append("D" + c.x.ToString("0.0", CI) + ";" + c.z.ToString("0.0", CI) + ";" + (r.bounds.size.x * r.bounds.size.z).ToString("0.0", CI) + "|"); }
    foreach (var b in Object.FindObjectsByType<PlayerBase>(FindObjectsSortMode.None)) sb.Append("B" + b.transform.position.x.ToString("0.0", CI) + ";" + b.transform.position.z.ToString("0.0", CI) + "|");
    return sb.ToString(); }
  public static string PileSpot() { var cam = MatchCamera.Instance.Camera; var fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
    var pile = FoodPile.ActivePiles.Where(p => p != null && p.transform.position.magnitude > 3f).OrderBy(p => p.transform.position.magnitude).FirstOrDefault(); if (pile == null) return "ERROR no pile";
    var pos = pile.SurfaceApproachPoint(pile.transform.position - fwd * 100f, 1.6f); return pos.x.ToString("0.00", CI) + ";" + pos.z.ToString("0.00", CI); }
  class R { public SpriteRenderer ring, edge, ghost; public bool rival; public Color c0; public bool e0, ee0, eg0; }
  static List<R> Rings() { var l = new List<R>();
    foreach (var k in All()) {
      var wb = k.GetComponent<ChickenWorldBars>(); if (wb != null) l.Add(Mk(wb, false));
      var ri = k.GetComponent<RivalIndicator>(); if (ri != null) { var r = Mk(ri, true); if (r.ring != null && r.ring.gameObject.activeInHierarchy) l.Add(r); } }
    return l; }
  static R Mk(object o, bool rival) { var t = o.GetType(); var r = new R { rival = rival, ring = (SpriteRenderer)t.GetField("_ring", BF).GetValue(o), edge = (SpriteRenderer)t.GetField("_ringEdge", BF).GetValue(o), ghost = (SpriteRenderer)t.GetField("_ringGhost", BF).GetValue(o) };
    if (r.ring != null) { r.c0 = r.ring.color; r.e0 = r.ring.enabled; } if (r.edge != null) r.ee0 = r.edge.enabled; if (r.ghost != null) r.eg0 = r.ghost.enabled; return r; }
  static void Show(List<R> rs, bool rival, bool stroke, bool edge, bool ghost) { foreach (var r in rs) { bool mine = r.rival == rival;
      if (r.ring != null) r.ring.enabled = mine && stroke; if (r.edge != null) r.edge.enabled = mine && edge; if (r.ghost != null) r.ghost.enabled = mine && ghost; } }
  static void Seat(List<R> rs, int s) { foreach (var r in rs) { var c = PlayerPalette.ForCorner(s); c.a = r.rival ? FeedbackTuning.RivalRingAlpha : ChickenWorldBars.RingAlpha; r.ring.color = c;
      if (r.ghost != null) GroundRingStyle.SyncGhost(r.ring, r.ghost, r.rival ? FeedbackTuning.RivalRingAlpha : ChickenWorldBars.RingAlpha); } }
  static void Save(Camera cam, int W, int H, string path) {
    var rt = CapTier.NewRt(W, H); var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  // Every variant of one spot in one call: nothing moves between the shots.
  public static string ShootAll(string w, string h, string dir) { int W = int.Parse(w), H = int.Parse(h); var cam = MatchCamera.Instance.Camera; System.IO.Directory.CreateDirectory(dir);
    var rs = Rings(); var sb = new StringBuilder("rings id=" + rs.Count(r => !r.rival) + " rival=" + rs.Count(r => r.rival));
    foreach (var r in rs) if (r.edge != null) sb.Append(" | " + (r.rival ? "rv" : "id") + " edge rgba=" + ColorUtility.ToHtmlStringRGBA(r.edge.color));
    try {
      Show(rs, false, false, false, false); Show(rs, true, false, false, false); foreach (var r in rs) { if (r.ring) r.ring.enabled = false; if (r.edge) r.edge.enabled = false; if (r.ghost) r.ghost.enabled = false; }
      Save(cam, W, H, dir + "/off.png");
      // Shadow-free reference: stop every renderer CASTING (turning the light's shadows off swaps the receiver variant and greys the Mobile grass).
      var casters = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(x => x.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off).ToList();
      var modes = casters.Select(x => x.shadowCastingMode).ToList();
      var en = casters.Select(x => x.enabled).ToList();
      // A ShadowsOnly proxy switched to Off would become VISIBLE: disable it instead.
      try { foreach (var x in casters) { if (x.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) x.enabled = false; else x.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; } Save(cam, W, H, dir + "/off_noshadow.png"); }
      finally { for (int i = 0; i < casters.Count; i++) { casters[i].shadowCastingMode = modes[i]; casters[i].enabled = en[i]; } } sb.Append(" | casters=" + casters.Count);
      foreach (var rival in new[] { false, true }) { string p = rival ? "rv" : "id";
        Show(rs, rival, false, true, false); Save(cam, W, H, dir + "/" + p + "_edge.png");
        for (int s = 0; s < 4; s++) { Seat(rs, s);
          Show(rs, rival, true, false, false); Save(cam, W, H, dir + "/" + p + "_stroke_" + s + ".png");
          Show(rs, rival, true, true, true); Save(cam, W, H, dir + "/" + p + "_full_" + s + ".png"); } }
    } finally { foreach (var r in rs) { if (r.ring) { r.ring.color = r.c0; r.ring.enabled = r.e0; } if (r.edge) r.edge.enabled = r.ee0; if (r.ghost) { r.ghost.enabled = r.eg0; GroundRingStyle.SyncGhost(r.ring, r.ghost, r.rival ? FeedbackTuning.RivalRingAlpha : ChickenWorldBars.RingAlpha); } } }
    return sb.ToString(); }
}"""

SIG = {"Freeze": ["on"], "Place": ["mode", "gap", "cx", "cz"], "PlaceAt": ["pts"], "ShootAll": ["w", "h", "dir"]}


def P(*parts):
    return os.path.join(OUT, *parts).replace("\\", "/")


# ---------------------------------------------------------------- analysis (numpy) ----------------------------------------------------------------
def _lin(a):
    a = a / 255.0
    return __import__("numpy").where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def rel_lum(rgb):
    import numpy as np
    l = _lin(np.asarray(rgb, float))
    return l[..., 0] * 0.2126 + l[..., 1] * 0.7152 + l[..., 2] * 0.0722


def wcag(a, b):
    import numpy as np
    la, lb = rel_lum(a), rel_lum(b)
    return (np.maximum(la, lb) + 0.05) / (np.minimum(la, lb) + 0.05)


def load(path):
    import numpy as np
    from PIL import Image
    return np.asarray(Image.open(path).convert("RGB")).astype(float)


def core_mask(img, off, thr=12):
    import numpy as np
    d = np.abs(img - off).max(-1)
    m = d > thr
    if m.sum() == 0:
        return m
    return m & (d >= np.median(d[m]))


def stats(v):
    import numpy as np
    if len(v) == 0:
        return None
    return {"n": int(len(v)), "median": round(float(np.median(v)), 2), "p10": round(float(np.percentile(v, 10)), 2)}


def hue(img):
    import numpy as np
    r, g, b = img[..., 0], img[..., 1], img[..., 2]
    mx, mn = img.max(-1), img.min(-1)
    d = np.where(mx - mn < 1e-6, 1e-6, mx - mn)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return h, (mx - mn) / np.maximum(mx, 1e-6)


def ground_classes(off, ns):
    """Per pixel: 'green' (grass) / 'tan' (dirt path, straw pad, mound) from the shadow-free render, and shadowed = the sun's shadow
    takes more than 25 % of the light (a darker grass texel is not a shadow)."""
    import numpy as np
    h, sat = hue(ns)
    green = (h >= 65) & (h <= 170) & (sat > 0.2)
    tan = (h >= 15) & (h < 65) & (sat > 0.15)
    shadow = rel_lum(off) < 0.75 * rel_lum(ns)
    return {"green": green, "tan": tan}, shadow


GROUPS = (("green", "sun"), ("green", "shadow"), ("tan", "sun"), ("tan", "shadow"))


def analyse_spot(d, prefix):
    import numpy as np
    off = load(os.path.join(d, "off.png"))
    ns = load(os.path.join(d, "off_noshadow.png")) if os.path.exists(os.path.join(d, "off_noshadow.png")) else off
    e = load(os.path.join(d, f"{prefix}_edge.png"))
    em = core_mask(e, off)
    if em.sum() < 20:
        return None
    cls, shadow = ground_classes(off, ns)
    strokes = {s: load(os.path.join(d, f"{prefix}_stroke_{s}.png")) for s in range(4)}
    smask = {s: core_mask(strokes[s], off) for s in range(4)}
    out = {"groups": {}}
    for g, light in GROUPS:
        gm = cls[g] & (shadow if light == "shadow" else ~shadow)
        m = em & gm
        if m.sum() < 30:
            continue
        # Two-tone edge (D9): pixels darker than their ground are the ink band, lighter ones the cream rim. The edge reads by
        # whichever tone parts from the ground more (a tone counts once it holds >= 10 % of the edge pixels).
        dark = rel_lum(e) < rel_lum(off)
        tones = {}
        for tone, tm in (("ink", m & dark), ("rim", m & ~dark)):
            if tm.sum() >= max(15, 0.1 * m.sum()):
                tones[tone] = {"contrast": stats(wcag(e[tm], off[tm])), "rgb": [round(float(x), 1) for x in np.median(e[tm], 0)]}
        best = max(tones.values(), key=lambda t: t["contrast"]["median"])
        ec = dict(best["contrast"]); ec["n"] = int(m.sum())
        inner = tones.get("ink", best)          # the tone that touches the colour stroke
        edgeRGB = np.array(inner["rgb"])
        grp = {"groundRGB": [round(float(x), 1) for x in np.median(off[m], 0)], "edge": ec, "tones": tones,
               "edgeRGB": [round(float(x), 1) for x in edgeRGB], "seats": {}}
        for s in range(4):
            sm = smask[s] & gm
            if sm.sum() < 30:
                continue
            sc = stats(wcag(strokes[s][sm], off[sm]))
            sRGB = np.median(strokes[s][sm], 0)
            grp["seats"][SEATS[s]] = {"stroke": sc, "strokeRGB": [round(float(x), 1) for x in sRGB],
                                      "strokeVsEdge": round(float(wcag(sRGB, edgeRGB)), 2), "ring": round(max(sc["median"], ec["median"]), 2)}
        out["groups"][f"{g}-{light}"] = grp
    return out


def frame_grounds(d):
    """Ground colours over the whole frame (no rings): sunlit grass, grass in a shadow (fences, props, birds), and how dark the darkest shadow gets."""
    import numpy as np
    off = load(os.path.join(d, "off.png"))
    nsp = os.path.join(d, "off_noshadow.png")
    if not os.path.exists(nsp):
        return None
    ns = load(nsp)
    cls, shadow = ground_classes(off, ns)
    sun = cls["green"] & ~shadow
    h_, w_ = sun.shape
    centre = np.zeros_like(sun); centre[h_ // 4: 3 * h_ // 4, w_ // 4: 3 * w_ // 4] = True
    ref = float(np.median(rel_lum(ns[sun & centre])))   # arena grass (the frame centre), not the dark hedge outside the fence
    like = cls["green"] & (np.abs(rel_lum(ns) - ref) < 0.25 * ref)   # grass albedo (not the dark hedge outside the arena)
    out = {}
    for name, m in (("grassSun", like & ~shadow), ("grassShadow", like & shadow)):
        if m.sum() < 50:
            continue
        L = rel_lum(off[m])
        dark = off[m][L <= np.percentile(L, 10)]
        out[name] = {"n": int(m.sum()), "medianRGB": [round(float(x), 1) for x in np.median(off[m], 0)], "lumMedian": round(float(np.median(L)), 4),
                     "lumP10": round(float(np.percentile(L, 10)), 4), "darkestDecileRGB": [round(float(x), 1) for x in np.median(dark, 0)]}
    return out


def analyse_all():
    allm = {}
    lines = []
    weakest = []
    for tier in ("pc", "mobile"):
        for spot in SPOTS:
            d = P(tier, spot)
            if not os.path.isdir(d):
                continue
            fg = frame_grounds(d)
            if fg:
                allm.setdefault(tier, {}).setdefault(spot, {})["frame"] = fg
                lines.append(f"{tier:6} {spot:6} frame grounds: " + "  ".join(f"{k} lum {v['lumMedian']:.3f} (p10 {v['lumP10']:.3f}) rgb {v['medianRGB']}" for k, v in fg.items()))
            for prefix, label in (("id", "identity"), ("rv", "rival")):
                if not os.path.exists(os.path.join(d, f"{prefix}_edge.png")):
                    continue
                m = analyse_spot(d, prefix)
                if m is None:
                    lines.append(f"{tier:6} {spot:6} {label:8} (no ring pixels)")
                    continue
                allm.setdefault(tier, {}).setdefault(spot, {})[label] = m
                for gname, g in m["groups"].items():
                    ec = g["edge"]
                    tt = " ".join(f"{k} {v['contrast']['median']:4.2f}" for k, v in g.get("tones", {}).items())
                    row = f"{tier:6} {spot:6} {label:8} {gname:12} ground {str([round(x) for x in g['groundRGB']]):15} edge {ec['median']:5.2f} (p10 {ec['p10']:5.2f}, n {ec['n']:5}; {tt})"
                    for s in SEATS:
                        st = g["seats"].get(s)
                        if st is None:
                            continue
                        row += f" | {s} stroke {st['stroke']['median']:4.2f} s/e {st['strokeVsEdge']:5.2f} ring {st['ring']:4.2f}"
                        if g["edge"]["n"] >= 100:
                            weakest.append((st["ring"], tier, spot, label, s, gname, st["strokeVsEdge"], ec["n"]))
                    lines.append(row)
    weakest.sort()
    lines.append("")
    lines.append("weakest ring-vs-ground pairs (ring = max(stroke, edge) median contrast; groups with >= 100 edge pixels):")
    for w in weakest[:10]:
        lines.append(f"  {w[0]:5.2f}  {w[1]} {w[2]} {w[3]} {w[4]} {w[5]} (stroke vs edge {w[6]}, edge px {w[7]})")
    json.dump(allm, open(P("metrics.json"), "w"), indent=1)
    open(P("table.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("\n".join(lines))


# ---------------------------------------------------------------- capture ----------------------------------------------------------------
def capture():
    import bridge
    import capture_common
    global RG
    RG = RG + capture_common.CAP_TIER_CS
    # Reuse the E3 helper class (menu driving, quality tier switch, bot freeze, UI hide) from capture_env3d.py without running its main().
    src = open(os.path.join(HERE, "capture_env3d.py"), encoding="utf-8").read()
    prefix = src[:src.index("def P(*parts)")]
    ns = {"__name__": "capture_env3d_prefix", "__file__": os.path.join(HERE, "capture_env3d.py")}
    saved = sys.argv
    sys.argv = [sys.argv[0], os.path.join(OUT, "_scratch")]
    exec(compile(prefix, "capture_env3d_prefix", "exec"), ns)
    sys.argv = saved
    import shutil
    shutil.rmtree(os.path.join(OUT, "_scratch"), ignore_errors=True)
    e3 = ns["cs"]

    def rg(method, *args, timeout=300):
        params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
        m = bridge.call("script-execute", {"csharpCode": RG, "className": "RG", "methodName": method, "parameters": params}, timeout=timeout)
        try:
            return m["result"]["structuredContent"]["result"]["value"]
        except Exception:
            raise RuntimeError(f"RG.{method} failed: " + bridge.text(m)[:800])

    def run_tier(tier, spots):
        sp = [s for s in spots.split("|") if s]
        base = [s[1:].split(";") for s in sp if s.startswith("B")]
        for spot in SPOTS:
            if spot == "grass":
                where = rg("Place", "line", "2.6", "0", "-6")
            elif spot == "straw":
                if not base:
                    print("no base"); continue
                # 2x2 on the pad itself (pad ~5 m across): every ring sits on straw.
                where = rg("Place", "grid", "1.5", base[0][0], base[0][1])
            elif spot == "dirt":
                pts = rg("DirtPoints")
                if pts.startswith("ERROR"):
                    print("dirt:", pts); continue
                where = rg("PlaceAt", pts)
            else:
                rg("Place", "line", "2.6", "0", "-6"); time.sleep(1.2)   # same camera yaw before asking for the pile side
                ps = rg("PileSpot")
                if ps.startswith("ERROR"):
                    print("mound:", ps); continue
                x, z = ps.split(";")
                where = rg("Place", "line", "2.4", x, z)
            print(tier, spot, where); time.sleep(1.8)
            rg("Freeze", "1")
            try:
                print("  ", rg("ShootAll", str(W), str(H), P(tier, spot)))
            finally:
                rg("Freeze", "0")

    def cleanup():
        for label, step in (("unfreeze", lambda: rg("Freeze", "0")),
                            ("restore quality", lambda: print(e3("Quality", "restore"))),
                            ("show UI", lambda: e3("HideUi", "0")),
                            ("stop Play mode", lambda: bridge.call("editor-application-set-state", {"isPlaying": False}))):
            try:
                step()
            except Exception as ex:
                print(f"CLEANUP FAILED ({label}): {ex}")
        time.sleep(4)

    bridge.init()
    with capture_common.perf_mode():
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
            print(e3("PassInfo", "PASS PC"))
            run_tier("pc", spots)
            print(e3("Quality", "mobile")); time.sleep(2.5)
            print(e3("PassInfo", "PASS MOBILE"))
            run_tier("mobile", spots)
        finally:
            cleanup()


if __name__ == "__main__":
    if not os.environ.get("ANALYSE_ONLY"):
        capture()
    analyse_all()
