"""Phase 8 D6 (Speedy x1.3, leaner, orange) captures from the MATCH camera.

    python tools/ui_capture/capture_d6_speedy.py <outdir>   (e.g. Captures/env3d_after/C/D6/before)

Runs a solo match (bots frozen once Active), lines the four classes up in front of the match camera
(Warrior | Speedy | Fatty | Assassin) and writes:
  lineup_desktop.png / lineup_phone.png   all four from the match camera
  lineup_close.png                        match-camera crop around the four (hi-res)
  speedy_close_<yaw>.png                  Speedy alone, perspective close-up from 3 yaws (nameplate / bars / ring in frame)
  speedy_<clip>_<n>.png                   a few frames each of Idle / Walk(run) / Peck on the Speedy, close up
  report.txt                              per-class renderer bounds (height / width), nameplate world Y, body colours
  pixels.json                             median / mean rendered body colour per class (isolated via a no-grass mask pair)
"""
import sys, os, time, json
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Captures", "env3d_after", "C", "D6", "run"))
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.Text; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class D6G {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static UIDocument Doc(string el) { return Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None).FirstOrDefault(x => x.rootVisualElement != null && x.rootVisualElement.Q(el) != null); }
  public static string Press(string name) { var d = Doc(name); if (d == null) return "NOBTN " + name; var b = d.rootVisualElement.Q<Button>(name);
    var c = typeof(Button).GetField("m_Clickable", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(b);
    ((System.Action)typeof(Clickable).GetField("clicked", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c)).Invoke(); return "clicked " + name; }
  public static string Fill() { var r = Doc("ReadyBtn").rootVisualElement; int taps = 0;
    for (int i = 0; i < 14; i++) { var rb = r.Q<Button>("ReadyBtn"); if (rb != null && rb.enabledSelf) break;
      var card = r.Query<VisualElement>(className: "cw-ability-card").ToList().FirstOrDefault(c => !c.ClassListContains("cw-ability-card--picked")); if (card == null) break;
      using (var e = ClickEvent.GetPooled()) { e.target = card; card.SendEvent(e); } taps++; }
    return "taps=" + taps; }
  public static string State() { var gm = GameManager.Instance; return gm == null ? "nogm" : gm.State.ToString() + (gm.IsIntroActive ? "+intro" : ""); }
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy).ToArray(); }
  static ChickenController Of(string cls) { return All().FirstOrDefault(k => k.Class.ToString() == cls); }
  public static string FreezeBots() { string o = ""; foreach (var k in All()) { if (k.HasInputAuthority) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot") && m.enabled) { m.enabled = false; o += m.GetType().Name + " "; } } return "froze " + o; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui hidden=" + on; }
  static void Tp(ChickenController k, Vector3 p) { var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(p); else k.transform.position = p; }
  // Warrior | Speedy | Fatty | Assassin in a row south of the centre pile, facing the camera. Anything else parked far away.
  public static string Line(string gap) { float g = float.Parse(gap, CI); var a = All(); var y = a[0].transform.position.y; const float Z = -6f;
    string[] order = { "Warrior", "Speedy", "Fatty", "Assassin" }; string o = "";
    foreach (var k in a) { int i = System.Array.IndexOf(order, k.Class.ToString()); Vector3 p = i >= 0 ? new Vector3((i - 1.5f) * g, y, Z) : new Vector3(-18f, y, -18f);
      Tp(k, p); k.transform.rotation = Quaternion.Euler(0, 180, 0); o += k.Class + "@" + p.x.ToString("0.0", CI) + " "; }
    return o; }
  public static string Hold(string cls, string speed, string stateName) { var k = Of(cls); if (k == null) return "no " + cls;
    var ca = k.GetComponent<ChickenAnimator>(); if (ca != null) ca.enabled = false; var an = k.GetComponent<Animator>(); an.SetFloat("Speed", float.Parse(speed, CI));
    if (stateName != "-") an.Play(stateName, 0, 0f); return "hold " + cls + " " + speed + " " + stateName + " clips=" + string.Join(",", an.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name)); }
  public static string Peck(string cls) { var k = Of(cls); var an = k.GetComponent<Animator>(); an.SetTrigger("Peck"); return "peck"; }
  static Bounds BodyBounds(ChickenController k) { var rs = k.GetComponent<ChickenVisuals>().TintedRenderers.Where(r => r != null).ToArray(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
  static Bounds AllBounds(ChickenController k) { var rs = k.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
  public static string Report() { var sb = new StringBuilder(); foreach (var k in All().OrderBy(k => k.Class.ToString())) { var vis = k.GetComponent<ChickenVisuals>(); var b = BodyBounds(k); var cc = k.GetComponent<CharacterController>();
      sb.Append(k.Class + " rootScale=" + k.transform.localScale.x.ToString("0.###", CI) + " bodyH=" + b.size.y.ToString("0.000", CI) + " bodyW(x)=" + b.size.x.ToString("0.000", CI) + " bodyD(z)=" + b.size.z.ToString("0.000", CI) + " bodyTopY=" + b.max.y.ToString("0.000", CI) + " feetY=" + b.min.y.ToString("0.000", CI)
        + " cc(h=" + cc.height.ToString("0.00", CI) + ",r=" + cc.radius.ToString("0.00", CI) + ",c=" + cc.center + ") bodyColor=" + vis.BodyColor);
      var np = k.GetComponentInChildren<ChickenNameplate>(); if (np != null) foreach (var tm in np.GetComponentsInChildren<TextMesh>()) { sb.Append(" | text'" + tm.text + "' worldY=" + tm.transform.position.y.ToString("0.000", CI)); break; }
      var wb = k.GetComponentInChildren<ChickenWorldBars>(); if (wb != null) { var bt = wb.transform.Find("Bars"); sb.Append(" | barsWorldY=" + (bt != null ? bt.position.y.ToString("0.000", CI) : "?")); }
      var an = k.transform.Find("GroundAnchor"); if (an != null) sb.Append(" | anchorY=" + an.position.y.ToString("0.000", CI));
      sb.Append("\n"); } return sb.ToString(); }
  static void Save(Camera cam, int W, int H, string path) {
    var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
  public static string Shot(string w, string h, string path) { Save(MatchCamera.Instance.Camera, int.Parse(w), int.Parse(h), path); return "saved"; }
  // Viewport rect of all four lined-up birds (match camera) -> for cropping.
  public static string Vp(string w, string h) { var cam = MatchCamera.Instance.Camera; int W = int.Parse(w), H = int.Parse(h); var prev = cam.aspect; cam.aspect = (float)W / H; var lo = new Vector2(1e9f, 1e9f); var hi = new Vector2(-1e9f, -1e9f);
    foreach (var k in All()) { if (k.transform.position.x < -10f) continue; var b = AllBounds(k); for (int i = 0; i < 8; i++) { var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1)); var v = cam.WorldToViewportPoint(c); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); } }
    cam.ResetAspect(); return string.Format(CI, "{0:0.0000};{1:0.0000};{2:0.0000};{3:0.0000}", lo.x, lo.y, hi.x, hi.y); }
  static Camera Free() { var go = GameObject.Find("__D6Cam"); if (go == null) { var src = MatchCamera.Instance.Camera; var sd = src.GetComponent("UniversalAdditionalCameraData");
      go = new GameObject("__D6Cam"); var c = go.AddComponent<Camera>(); c.enabled = false; c.CopyFrom(src); c.targetTexture = null; var dst = go.AddComponent(sd.GetType());
      foreach (var p in new[] { "renderPostProcessing", "antialiasing" }) { var pi = sd.GetType().GetProperty(p); if (pi != null) pi.SetValue(dst, pi.GetValue(sd)); } }
    var cam = go.GetComponent<Camera>(); cam.enabled = false; return cam; }
  public static string Close(string cls, string yaw, string pitch, string w, string h, string path, string pad) { var k = Of(cls); var b = AllBounds(k); var cam = Free(); int W = int.Parse(w), H = int.Parse(h);
    cam.orthographic = false; cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f; cam.aspect = (float)W / H;
    var rot = Quaternion.Euler(float.Parse(pitch, CI), float.Parse(yaw, CI), 0f); float r = b.extents.magnitude * float.Parse(pad, CI);
    float d = r / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Min(1f, cam.aspect); cam.transform.position = b.center - rot * Vector3.forward * d; cam.transform.rotation = rot; Save(cam, W, H, path); return "saved"; }
  // Isolated body pixels: render the Speedy / Fatty / etc. alone is not possible in-scene, so isolate by hiding every other renderer.
  static System.Collections.Generic.List<Renderer> _hidden = new System.Collections.Generic.List<Renderer>();
  public static string Isolate(string cls, string on) { if (on == "0") { foreach (var r in _hidden) if (r != null) r.enabled = true; _hidden.Clear(); return "restored"; }
    var keep = cls == "None" ? new System.Collections.Generic.HashSet<Renderer>() : new System.Collections.Generic.HashSet<Renderer>(Of(cls).GetComponent<ChickenVisuals>().TintedRenderers.Where(r => r != null));
    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.enabled && !keep.Contains(r) && !(r is ParticleSystemRenderer) && r.GetComponentInParent<Camera>() == null) { r.enabled = false; _hidden.Add(r); }
    return "isolated " + cls + " hid " + _hidden.Count; }
  // Hide every renderer but the class body, render, restore - all inside one call (statics do not survive between script-execute calls).
  public static string IsoShot(string cls, string w, string h, string path) { var keep = cls == "None" ? new System.Collections.Generic.HashSet<Renderer>() : new System.Collections.Generic.HashSet<Renderer>(Of(cls).GetComponent<ChickenVisuals>().TintedRenderers.Where(r => r != null));
    var hid = new System.Collections.Generic.List<Renderer>();
    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.enabled && !keep.Contains(r) && !(r is ParticleSystemRenderer) && r.GetComponentInParent<Camera>() == null) { r.enabled = false; hid.Add(r); }
    try { Save(MatchCamera.Instance.Camera, int.Parse(w), int.Parse(h), path); } finally { foreach (var r in hid) if (r != null) r.enabled = true; }
    return "iso " + cls + " hid " + hid.Count; }
  public static string Park() { var a = All(); var y = a[0].transform.position.y; int i = 0; foreach (var k in a) Tp(k, new Vector3(-30f + 3f * i++, y, -30f)); return "parked"; }
  public static string SetTint(string cls, string strength) { var k = Of(cls); var reg = typeof(ChickenController).GetField("_registry", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(k) as ChickenClassRegistrySO;
    reg.TryGet(k.Class, out var e); k.GetComponent<ChickenVisuals>().ApplyTint(e.TintColor, float.Parse(strength, CI)); return "tint " + cls + " " + strength; }
  public static string ShotVp(string cls, string w, string h, string path) { var k = Of(cls); var cam = MatchCamera.Instance.Camera; int W = int.Parse(w), H = int.Parse(h); Save(cam, W, H, path);
    var b = BodyBounds(k); var prev = cam.aspect; cam.aspect = (float)W / H; var lo = new Vector2(1e9f, 1e9f); var hi = new Vector2(-1e9f, -1e9f);
    for (int i = 0; i < 8; i++) { var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1)); var v = cam.WorldToViewportPoint(c); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
    cam.ResetAspect(); return string.Format(CI, "{0:0.0000};{1:0.0000};{2:0.0000};{3:0.0000}", lo.x, lo.y, hi.x, hi.y); }
}"""

SIG = {"Press": ["name"], "HideUi": ["on"], "Line": ["gap"], "Hold": ["cls", "speed", "stateName"], "Peck": ["cls"],
       "Shot": ["w", "h", "path"], "Vp": ["w", "h"], "Close": ["cls", "yaw", "pitch", "w", "h", "path", "pad"],
       "Isolate": ["cls", "on"], "SetTint": ["cls", "strength"], "Park": [], "IsoShot": ["cls", "w", "h", "path"], "ShotVp": ["cls", "w", "h", "path"]}


def cs(method, *args, timeout=300):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "D6G", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:800]


def P(n):
    return os.path.join(OUT, n).replace("\\", "/")


def measure():
    """D6_MEASURE=1: isolated rendered body colour per class (and Speedy at the old 0.25 tint) from the match camera."""
    import numpy as np
    from PIL import Image
    bridge.init()
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if str(cs("Press", "SoloBtn")).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.8); cs("Press", "NextBtn"); time.sleep(1.0); cs("Fill"); cs("Press", "ReadyBtn"); time.sleep(1.5); cs("Press", "StartBtn")
    res = {}
    try:
        for _ in range(80):
            if str(cs("State")) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.5); cs("FreezeBots"); cs("HideUi", "1"); cs("Line", "2.6"); time.sleep(1.5)
        W, H = 2400, 1350
        cs("IsoShot", "None", str(W), str(H), P("bg.png"))
        bg = np.asarray(Image.open(P("bg.png")).convert("RGB")).astype(float)
        for label, cls, st in (("Speedy_old0.25", "Speedy", "0.25"), ("Speedy_s0.6", "Speedy", "0.6"), ("Speedy_s0.8", "Speedy", "0.8"), ("Speedy_s1.0", "Speedy", "1.0"), ("Fatty", "Fatty", None), ("Warrior", "Warrior", None), ("Assassin", "Assassin", None)):
            if st: cs("SetTint", cls, st); time.sleep(0.5)
            print(cs("IsoShot", cls, str(W), str(H), P(f"iso_{label}.png")))
            im = np.asarray(Image.open(P(f"iso_{label}.png")).convert("RGB")).astype(float)
            m = np.abs(im - bg).max(-1) > 12
            m = m & np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1)
            px = im[m]
            res[label] = {"n": int(m.sum()), "median": [float(v) for v in np.median(px, 0)], "mean": [float(v) for v in px.mean(0)]}
            Image.fromarray((m * 255).astype("uint8")).save(P(f"mask_{label}.png"))
            print(label, res[label])
    finally:
        cs("HideUi", "0")
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)
    json.dump(res, open(P("pixels.json"), "w"), indent=1)


def main():
    from PIL import Image
    bridge.init()
    bridge.call("editor-application-set-state", {"isPlaying": True})
    for _ in range(60):
        if str(cs("Press", "SoloBtn")).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.8); print(cs("Press", "NextBtn")); time.sleep(1.0); print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5); print(cs("Press", "StartBtn"))
    try:
        for _ in range(80):
            if str(cs("State")) == "Active":
                break
            time.sleep(0.5)
        time.sleep(2.5); print(cs("FreezeBots")); print(cs("HideUi", "1"))
        print(cs("Line", "2.6")); time.sleep(2.0)
        report = cs("Report"); print(report)
        for name, w, h in (("desktop", 1920, 1080), ("phone", 2400, 1080)):
            print(cs("Shot", str(w), str(h), P(f"lineup_{name}.png")))
        # hi-res match-camera crop around the four birds
        print(cs("Shot", "3840", "2160", P("lineup_full4k.png")))
        vp = cs("Vp", "3840", "2160"); x0, y0, x1, y1 = (float(v) for v in vp.split(";"))
        im = Image.open(P("lineup_full4k.png")); W, H = im.size; pad = 0.04
        box = (int(max(0, (x0 - pad)) * W), int(max(0, (1 - y1 - pad)) * H), int(min(1, (x1 + pad)) * W), int(min(1, (1 - y0 + pad)) * H))
        im.crop(box).save(P("lineup_close.png")); os.remove(P("lineup_full4k.png"))
        for yaw in (0, 35, 90):
            print(cs("Close", "Speedy", str(yaw), "12", "1200", "1200", P(f"speedy_close_yaw{yaw}.png"), "0.95"))
        for cls in ("Warrior", "Fatty", "Assassin"):
            print(cs("Close", cls, "20", "12", "900", "900", P(f"{cls.lower()}_close_yaw20.png"), "0.95"))
        # clips on the Speedy
        print(cs("Hold", "Speedy", "0", "Idle"))
        for i in range(3):
            time.sleep(0.45); print(cs("Close", "Speedy", "30", "12", "800", "800", P(f"speedy_idle_{i}.png"), "0.95"))
        print(cs("Hold", "Speedy", "1", "-"))
        for i in range(4):
            time.sleep(0.2); print(cs("Close", "Speedy", "30", "12", "800", "800", P(f"speedy_run_{i}.png"), "0.95"))
        print(cs("Hold", "Speedy", "0", "-")); print(cs("Peck", "Speedy"))
        for i in range(4):
            time.sleep(0.12); print(cs("Close", "Speedy", "30", "12", "800", "800", P(f"speedy_peck_{i}.png"), "0.95"))
        # contact sheet
        from PIL import ImageDraw
        names = [f"speedy_idle_{i}" for i in range(3)] + [f"speedy_run_{i}" for i in range(4)] + [f"speedy_peck_{i}" for i in range(4)]
        sheet = Image.new("RGB", (400 * 4, 400 * 3), (24, 24, 24)); d = ImageDraw.Draw(sheet)
        for i, n in enumerate(names):
            t = Image.open(P(n + ".png")).resize((400, 400), Image.LANCZOS); sheet.paste(t, ((i % 4) * 400, (i // 4) * 400)); d.text(((i % 4) * 400 + 6, (i // 4) * 400 + 4), n, fill=(255, 255, 0))
        sheet.save(P("speedy_clips_sheet.png"))
        open(P("report.txt"), "w", encoding="utf-8").write(report)
    finally:
        cs("HideUi", "0")
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


if os.environ.get('D6_MEASURE'): measure()
else: main()
