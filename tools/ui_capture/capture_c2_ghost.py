"""Phase 8 C2 ghost-fade captures from the MATCH camera: a chicken at opacity 1.0 / 0.4 / 0.2, side by side.

    python tools/ui_capture/capture_c2_ghost.py [outdir]   (default Captures/env3d_after/C/C2)

Runs a solo match (bots frozen once Active), lines the local bird and two bots up in front of the match camera and
drives their opacity through AbilityEffectStack.SetOpacity (the same path Invisibility / Smoke Roost use), so
ChickenVisuals swaps the ghost material in exactly as in play. Writes:
  lineup_<res>.png         local 1.0 | bot 0.4 | bot 0.2 in one match-camera frame (rival view of 0.4 / 0.2)
  owner_<op>.png           the LOCAL bird alone at each opacity (owner view), plus owner_side_by_side.png (crops)
  state.txt                per-chicken ghost / shader / shadow state at each step
"""
import sys, os, time
sys.stdout.reconfigure(encoding="utf-8")
import bridge

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Captures", "env3d_after", "C", "C2"))
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using System.Text; using CluckWars.Gameplay; using CluckWars.Visuals; using Fusion;
public class C2G {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  const int Src = 777;
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
  static ChickenController[] All() { return ChickenController.ActiveControllers.Where(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy).OrderBy(k => k.HasInputAuthority ? 0 : 1).ToArray(); }
  public static string FreezeBots() { string o = ""; foreach (var k in All()) { if (k.HasInputAuthority) continue;
      foreach (var m in k.GetComponents<MonoBehaviour>()) if (m != null && m.GetType().Name.Contains("Bot") && m.enabled) { m.enabled = false; o += m.GetType().Name + " "; } } return "froze " + o; }
  public static string HideUi(string on) { foreach (var d in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) if (d.rootVisualElement != null) d.rootVisualElement.style.display = on == "1" ? DisplayStyle.None : DisplayStyle.Flex; return "ui hidden=" + on; }
  static void Tp(ChickenController k, Vector3 p) { var nt = k.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(p); else k.transform.position = p; }
  // Clear grass south of the centre pile. solo=1: the local bird alone (owner view), bots parked in a far corner.
  public static string Line(string gap, string solo) { float g = float.Parse(gap, CI); bool one = solo == "1"; var a = All(); var y = a[0].transform.position.y; const float Z = -6f;
    for (int i = 0; i < a.Length; i++) { Vector3 p = i == 0 ? new Vector3(one ? 0f : -g, y, Z) : (!one && i == 1) ? new Vector3(0, y, Z) : (!one && i == 2) ? new Vector3(g, y, Z) : new Vector3(-18f + 2f * i, y, -18f); Tp(a[i], p); a[i].transform.rotation = Quaternion.Euler(0, 180, 0); }
    return "lined " + a.Length; }
  public static string Op(string idx, string op) { var a = All(); var k = a[int.Parse(idx)]; float v = float.Parse(op, CI);
    if (v >= 1f) k.Effects.RemoveOpacity(Src); else k.Effects.SetOpacity(Src, v); return k.name + " op=" + v; }
  public static string Report() { var sb = new StringBuilder(); foreach (var k in All()) { var vis = k.GetComponent<ChickenVisuals>();
      sb.Append((k.HasInputAuthority ? "LOCAL " : "bot ") + k.name + " VisualOpacity=" + k.VisualOpacity.ToString("0.00", CI) + " ghost=" + vis.IsGhosted + " body=" + vis.BodyColor);
      foreach (var r in vis.TintedRenderers) if (r != null) sb.Append(" | " + r.GetType().Name + " " + string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "/" + m.shader.name : "null")) + " shadows=" + r.shadowCastingMode);
      sb.Append("\n"); } return sb.ToString(); }
  static Rect Save(Camera cam, int W, int H, string path) {
    var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
    var prev = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); return new Rect(); }
  public static string Shot(string w, string h, string path, string idx) { var cam = MatchCamera.Instance.Camera; int W = int.Parse(w), H = int.Parse(h); Save(cam, W, H, path);
    var k = All()[int.Parse(idx)]; var rs = k.GetComponent<ChickenVisuals>().TintedRenderers.Where(r => r != null).ToArray(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
    var prevAspect = cam.aspect; cam.aspect = (float)W / H; var lo = new Vector2(1e9f, 1e9f); var hi = new Vector2(-1e9f, -1e9f);
    for (int i = 0; i < 8; i++) { var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1)); var v = cam.WorldToViewportPoint(c); lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
    cam.ResetAspect();
    return string.Format(CI, "{0:0.0000};{1:0.0000};{2:0.0000};{3:0.0000}", lo.x, lo.y, hi.x, hi.y); }
}"""

SIG = {"Press": ["name"], "HideUi": ["on"], "Line": ["gap", "solo"], "Op": ["idx", "op"], "Shot": ["w", "h", "path", "idx"]}


def cs(method, *args, timeout=300):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = bridge.call("script-execute", {"csharpCode": CS, "className": "C2G", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        return m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        return bridge.text(m)[:800]


def P(n):
    return os.path.join(OUT, n).replace("\\", "/")


def crop(path, vp, pad=0.6):
    from PIL import Image
    im = Image.open(path)
    W, H = im.size
    x0, y0, x1, y1 = (float(v) for v in vp.split(";"))
    cx, cy = (x0 + x1) / 2 * W, (1 - (y0 + y1) / 2) * H
    half = max((x1 - x0) * W, (y1 - y0) * H) * (0.5 + pad)
    return im.crop((int(cx - half), int(cy - half), int(cx + half), int(cy + half))).resize((600, 600), Image.LANCZOS)


def main():
    bridge.init()
    state = []
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
        print(cs("Line", "2.2", "0")); time.sleep(1.5)
        # Rival view: local 1.0 | bot 0.4 | bot 0.2 in one frame.
        print(cs("Op", "1", "0.4")); print(cs("Op", "2", "0.2")); time.sleep(1.0)
        state.append("== lineup: local 1.0 | bot 0.4 | bot 0.2 ==\n" + cs("Report"))
        for name, w, h in (("desktop", 1920, 1080), ("phone", 2400, 1080)):
            print(cs("Shot", str(w), str(h), P(f"lineup_{name}.png"), "0"))
        print(cs("Op", "1", "1")); print(cs("Op", "2", "1")); time.sleep(1.0)
        state.append("== restored bots to 1.0 ==\n" + cs("Report"))
        # Owner view: the local bird alone at 1.0 / 0.4 / 0.2 (bots parked far away).
        print(cs("Line", "0", "1")); time.sleep(1.5)
        crops = []
        for op in ("1.0", "0.4", "0.2"):
            print(cs("Op", "0", op)); time.sleep(1.0)
            state.append(f"== owner {op} ==\n" + cs("Report"))
            vp = cs("Shot", "1920", "1080", P(f"owner_{op}.png"), "0")
            print(op, vp)
            crops.append(crop(P(f"owner_{op}.png"), vp))
        print(cs("Op", "0", "1")); time.sleep(1.0)
        state.append("== owner restored ==\n" + cs("Report"))
        from PIL import Image, ImageDraw
        sheet = Image.new("RGB", (600 * 3 + 40, 640), (24, 24, 24))
        d = ImageDraw.Draw(sheet)
        for i, (c, label) in enumerate(zip(crops, ("opacity 1.0", "0.4 (stun-level opacity)", "0.2 (Invisibility / Smoke Roost)"))):
            sheet.paste(c, (10 + i * 610, 30)); d.text((14 + i * 610, 8), label, fill=(240, 240, 240))
        sheet.save(P("owner_side_by_side.png"))
        open(P("state.txt"), "w", encoding="utf-8").write("\n".join(state))
        print("\n".join(state))
    finally:
        cs("HideUi", "0")
        bridge.call("editor-application-set-state", {"isPlaying": False}); time.sleep(4)


main()
