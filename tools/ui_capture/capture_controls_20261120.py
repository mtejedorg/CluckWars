"""Verification of the 2026-11-20 controls batch in the live Editor (HUD layouts, key badges, Settings row, simulated input).

Usage (Editor open, not in Play mode; Bootstrap is opened by the script):
    python tools/ui_capture/capture_controls_20261120.py <group> [<out_dir>]
Groups: probe, touch, strip, settings, input.   Default out_dir: Captures/controls-20261120
Each group starts its own solo match (a match ends in ~20 s) and leaves Play mode at the end.
Input is simulated through the Input System (queued state events on a Keyboard / Gamepad device), never by poking game fields.
"""
import sys, os, json, time
sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
GROUP = sys.argv[1] if len(sys.argv) > 1 else "probe"
OUT = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(REPO, "Captures", "controls-20261120")
os.makedirs(OUT, exist_ok=True)

CS = r"""using UnityEngine; using UnityEngine.UIElements; using System.Linq; using System.Reflection; using CluckWars.Abilities; using CluckWars.Gameplay; using CluckWars.Input; using CluckWars.Settings; using CluckWars.UI; using Fusion;
using UnityEngine.InputSystem; using UnityEngine.InputSystem.LowLevel;
public class CW {
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
  public static string Compile() { return "fail=" + UnityEditor.EditorUtility.scriptCompilationFailed + " compiling=" + UnityEditor.EditorApplication.isCompiling + " playing=" + UnityEditor.EditorApplication.isPlaying + " scenes=" + UnityEngine.SceneManagement.SceneManager.sceneCount + " active=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path; }
  static ChickenController Local() { return ChickenController.ActiveControllers.FirstOrDefault(k => k != null && k.Object != null && k.Object.IsValid && !k.IsDecoy && k.HasInputAuthority); }
  static string Slots(ChickenController l) { var a = l.Abilities; return string.Join(",", Enumerable.Range(0, 4).Select(i => a.GetSlot(i) == null ? "-" : a.GetSlot(i).name)); }
  // Applies the authored kit of a specialisation to the local bird (a live swap, as the Balance lab does), cooldowns cleared.
  public static string Kit(string sub) { var l = Local(); if (l == null) return "no local";
    var p = UnityEditor.AssetDatabase.FindAssets("t:PassiveAbilitySO").Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<PassiveAbilitySO>(UnityEditor.AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(x => x != null && x.Subclass.ToString() == sub);
    if (p == null) return "no passive " + sub; var k = p.Kit; l.Abilities.SetSlots(p, k[0], k[1], k[2], k[3]);
    for (int i = 0; i < 4; i++) l.Abilities.TriggerCooldown(i, 0.01f); return sub + " -> " + Slots(l); }
  public static string Force(string mode) { TouchControlsController.DebugForceMode = mode == "none" ? (HudDeviceMode?)null : (HudDeviceMode)int.Parse(mode); return "mode=" + (TouchControlsController.Instance == null ? "?" : TouchControlsController.Instance.Mode.ToString()); }
  public static string Mode() { var h = TouchControlsController.Instance; return h == null ? "nohud" : h.Mode.ToString(); }
  public static string Auto(string v) { if (v != "get") PlayerPreferences.AutoPeckEnabled = v == "1"; return PlayerPreferences.AutoPeckEnabled.ToString(); }
  public static string Scheme(string v) { if (v != "get") PlayerPreferences.KeyboardSchemeChoice = v == "1" ? KeyboardScheme.ArrowsQwer : KeyboardScheme.WasdArrows; return PlayerPreferences.KeyboardSchemeChoice.ToString(); }
  public static string Cool(string slot, string secs) { var l = Local(); l.Abilities.TriggerCooldown(int.Parse(slot), float.Parse(secs, System.Globalization.CultureInfo.InvariantCulture)); return "cooling " + slot; }
  public static string Info() { var l = Local(); if (l == null) return "no local"; var a = l.Abilities; var cg = l.GetComponent<ChickenCargo>();
    return "slots=" + Slots(l) + " cd=" + string.Join("/", Enumerable.Range(0, 4).Select(i => a.CooldownRemaining(i).ToString("0.0"))) + " charging=" + a.ChargingSlot + " lastCast=" + a.LastCastSlot
      + " cargo=" + (cg == null ? "?" : cg.Cargo.ToString("0.0")) + " auto=" + PlayerPreferences.AutoPeckEnabled + " scheme=" + PlayerPreferences.KeyboardSchemeChoice + " sheet=" + LeaveSheetState.IsOpen
      + " hud=" + Mode() + " pos=" + l.transform.position + " state=" + State(); }
  public static string Move(string x, string z) { var l = Local(); if (l == null) return "no local"; var ci = System.Globalization.CultureInfo.InvariantCulture;
    var pos = new Vector3(float.Parse(x, ci), l.transform.position.y, float.Parse(z, ci)); var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos; return "local -> " + pos; }
  // Teleports the local bird 0.8 m from the nearest food pile (the Peck checks).
  public static string ToPile() { var l = Local(); if (l == null) return "no local";
    var piles = Object.FindObjectsByType<FoodPile>(FindObjectsSortMode.None); if (piles.Length == 0) return "no piles";
    var p = piles.OrderBy(f => Vector3.Distance(f.transform.position, l.transform.position)).First(); var d = (l.transform.position - p.transform.position); d.y = 0; d = d.normalized * 0.8f;
    var pos = p.transform.position + d; pos.y = l.transform.position.y; var nt = l.GetComponent<NetworkTransform>(); if (nt != null) nt.Teleport(pos); else l.transform.position = pos; return "pile " + p.name + " at " + p.transform.position + " -> " + pos; }
  // ---- Input System simulation ----
  static Keyboard _kb; static Gamepad _gp;
  public static string Devices(string kind) { if (kind == "kb") { _kb = Keyboard.current ?? InputSystem.AddDevice<Keyboard>(); return "kb=" + _kb.name + " id=" + _kb.deviceId + " current=" + (Keyboard.current == _kb) + " canRunInBackground=" + _kb.canRunInBackground + " bg=" + InputSystem.settings.backgroundBehavior + " editorBehavior=" + InputSystem.settings.editorInputBehaviorInPlayMode; }
    _gp = Gamepad.current ?? InputSystem.AddDevice<Gamepad>(); return "gp=" + _gp.name + " id=" + _gp.deviceId + " current=" + (Gamepad.current == _gp) + " canRunInBackground=" + _gp.canRunInBackground; }
  public static string Key(string key, string down) { if (_kb == null) _kb = Keyboard.current; var k = (UnityEngine.InputSystem.Key)System.Enum.Parse(typeof(UnityEngine.InputSystem.Key), key);
    using (StateEvent.From(_kb, out var ptr)) { _kb[k].WriteValueIntoEvent(down == "1" ? 1f : 0f, ptr); InputSystem.QueueEvent(ptr); } return "key " + key + "=" + down; }
  public static string KeyRead(string key) { var k = (UnityEngine.InputSystem.Key)System.Enum.Parse(typeof(UnityEngine.InputSystem.Key), key); return key + " pressed=" + Keyboard.current[k].isPressed; }
  public static string Btn(string b, string down) { if (_gp == null) _gp = Gamepad.current; var g = (GamepadButton)System.Enum.Parse(typeof(GamepadButton), b);
    using (StateEvent.From(_gp, out var ptr)) { _gp[g].WriteValueIntoEvent(down == "1" ? 1f : 0f, ptr); InputSystem.QueueEvent(ptr); } return "btn " + b + "=" + down; }
  public static string BtnRead(string b) { var g = (GamepadButton)System.Enum.Parse(typeof(GamepadButton), b); return b + " pressed=" + Gamepad.current[g].isPressed; }
  public static string ClearInput() { if (_kb != null) { using (StateEvent.From(_kb, out var p)) InputSystem.QueueEvent(p); } if (_gp != null) { using (StateEvent.From(_gp, out var p)) InputSystem.QueueEvent(p); } return "cleared"; }
  public static string Provider() { var gp = Gamepad.current; var kb = Keyboard.current; return "kb.current=" + (kb == null ? "null" : kb.name) + " gp.current=" + (gp == null ? "null" : gp.name) + " devices=" + string.Join(",", InputSystem.devices.Select(d => d.displayName)); }
  // ---- Game view size ----
  public static string GameSize(string w, string h) {
    var ed = typeof(UnityEditor.Editor).Assembly; var gvT = ed.GetType("UnityEditor.GameView"); var gv = UnityEditor.EditorWindow.GetWindow(gvT);
    var sizesT = ed.GetType("UnityEditor.GameViewSizes"); var singT = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesT);
    var inst = singT.GetProperty("instance").GetValue(null); var group = sizesT.GetProperty("currentGroup").GetValue(inst);
    var sizeT = ed.GetType("UnityEditor.GameViewSize"); var typeT = ed.GetType("UnityEditor.GameViewSizeType");
    var size = System.Activator.CreateInstance(sizeT, System.Enum.ToObject(typeT, 1), int.Parse(w), int.Parse(h), "CW " + w + "x" + h);
    group.GetType().GetMethod("AddCustomSize").Invoke(group, new object[] { size });
    int idx = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null) - 1;
    var prop = gvT.GetProperty("selectedSizeIndex", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
    prop.SetValue(gv, idx); gv.Repaint(); return "game view " + w + "x" + h + " (index " + idx + ")"; }
  public static string GameSizeBack(string idx) { var ed = typeof(UnityEditor.Editor).Assembly; var gvT = ed.GetType("UnityEditor.GameView"); var gv = UnityEditor.EditorWindow.GetWindow(gvT);
    var prop = gvT.GetProperty("selectedSizeIndex", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance); prop.SetValue(gv, int.Parse(idx)); gv.Repaint(); return "back to " + idx; }
  public static string GameSizeIndex() { var ed = typeof(UnityEditor.Editor).Assembly; var gvT = ed.GetType("UnityEditor.GameView"); var gv = UnityEditor.EditorWindow.GetWindow(gvT);
    return ((int)gvT.GetProperty("selectedSizeIndex", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(gv)).ToString(); }
  public static string Row() { var d = Doc("KeyboardSchemeRow"); if (d == null) return "norow-doc"; var r = d.rootVisualElement.Q("KeyboardSchemeRow"); var a = r.Q<Button>("KeyboardSchemeWasdBtn"); var b = r.Q<Button>("KeyboardSchemeQwerBtn");
    return "display=" + r.resolvedStyle.display + " wasdOn=" + a.ClassListContains("cw-setting-choice__btn--on") + " qwerOn=" + b.ClassListContains("cw-setting-choice__btn--on") + " wasdText=" + a.text + " qwerText=" + b.text + " scheme=" + PlayerPreferences.KeyboardSchemeChoice; }
  public static string ScrollToRow() { var d = Doc("KeyboardSchemeRow"); var r = d.rootVisualElement.Q("KeyboardSchemeRow"); var sv = r.GetFirstAncestorOfType<ScrollView>(); if (sv == null) return "nosv"; sv.ScrollTo(r); return "scrolled"; }
  static AbilityBaseSO FindAb(string n) { var g = UnityEditor.AssetDatabase.FindAssets(n + " t:AbilityBaseSO").Select(x => UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(x))).FirstOrDefault(x => x != null && x.name == n); return g; }
  // Four named ability assets into the four slots (test set-up only; the input path under test is untouched), cooldowns cleared.
  public static string Loadout(string a, string b, string c, string d) { var l = Local(); if (l == null) return "no local";
    l.Abilities.SetSlots(null, FindAb(a), FindAb(b), FindAb(c), FindAb(d)); for (int i = 0; i < 4; i++) l.Abilities.TriggerCooldown(i, 0.01f); return Slots(l); }
  public static string ResetCd() { var l = Local(); for (int i = 0; i < 4; i++) l.Abilities.TriggerCooldown(i, 0.01f); return "cd reset"; }
  public static string Cap() { var l = Local(); var cg = l.GetComponent<ChickenCargo>(); return cg.Cargo.ToString("0.0") + "/" + cg.Capacity.ToString("0.0"); }
  public static string Play(string on) { UnityEditor.EditorApplication.isPlaying = on == "1"; return "isPlaying<=" + on; }
  public static string OpenBootstrap() { var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(); if (s.isDirty && !string.IsNullOrEmpty(s.path)) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s);
    var o = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Game/Scenes/Bootstrap.unity", UnityEditor.SceneManagement.OpenSceneMode.Single); return o.path + " scenes=" + UnityEngine.SceneManagement.SceneManager.sceneCount; }
}"""

SIG = {"Press": ["name"], "Kit": ["sub"], "Force": ["mode"], "Auto": ["v"], "Scheme": ["v"], "Cool": ["slot", "secs"], "Move": ["x", "z"],
       "Devices": ["kind"], "Key": ["key", "down"], "KeyRead": ["key"], "Btn": ["b", "down"], "BtnRead": ["b"],
       "GameSize": ["w", "h"], "GameSizeBack": ["idx"], "Play": ["on"], "Loadout": ["a", "b", "c", "d"]}


def cs(method, *args):
    params = [{"name": n, "typeName": "System.String", "value": v} for n, v in zip(SIG.get(method, []), args)]
    m = None
    for attempt in range(3):
        try:
            m = bridge.call("script-execute", {"csharpCode": CS, "className": "CW", "methodName": method, "parameters": params}, timeout=90)
            break
        except Exception as e:
            if attempt == 2:
                return "ERR " + str(e)
            time.sleep(2)
    try:
        return str(m["result"]["structuredContent"]["result"]["value"]).strip('"')
    except Exception:
        t = bridge.text(m)
        return (t[len("System.String: "):] if t.startswith("System.String: ") else t[:500]).strip('"')


def wait(prefixes, tries=90):
    s = ""
    for _ in range(tries):
        s = cs("State")
        if str(s).startswith(prefixes):
            return s
        time.sleep(1)
    raise SystemExit("timed out waiting for " + str(prefixes) + " (last: " + str(s) + ")")


def shot(name):
    time.sleep(0.4)
    p = bridge.shot("_cw_" + name)
    if p:
        dest = os.path.join(OUT, name + ".png")
        os.replace(p, dest)
        print("  saved", dest)
        return dest
    print("  NO SHOT", name)


def start_match():
    """Opens Bootstrap, enters Play, starts a solo match, returns once the round is Active."""
    print("compile:", cs("Compile"))
    print(cs("OpenBootstrap")); time.sleep(1)
    print(cs("Play", "1"))
    for _ in range(90):
        if str(cs("Press", "SoloBtn")).startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(0.9)
    print(cs("Press", "NextBtn")); time.sleep(1.0)
    print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)
    print(cs("Press", "StartBtn"))
    wait(("Active",)); time.sleep(2.0)


def end_play():
    print(cs("Force", "none")); print(cs("ClearInput"))
    print(cs("Play", "0")); time.sleep(5)


def tap(key, ms=150, pad=False):
    f = "Btn" if pad else "Key"
    r1 = cs(f, key, "1"); time.sleep(ms / 1000.0); r2 = cs(f, key, "0")
    return r1 + " | " + r2


LOGPATH = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Unity", "Editor", "Editor.log")


def log_mark():
    return os.path.getsize(LOGPATH)


def log_since(mark, *needles):
    with open(LOGPATH, "rb") as f:
        f.seek(mark)
        txt = f.read().decode("utf-8", "replace")
    return [l.strip()[:200] for l in txt.splitlines() if any(n in l for n in needles)]


def wait_no_intro(tries=40):
    for _ in range(tries):
        if cs("State") == "Active":
            return
        time.sleep(0.5)
    print("  (intro did not end:", cs("State"), ")")


def crop(name, box_frac, suffix="_strip"):
    """Saves a crop of <name>.png (fractions l,t,r,b) as <name><suffix>.png, upscaled 2x, for reading the badges."""
    from PIL import Image
    im = Image.open(os.path.join(OUT, name + ".png")).convert("RGB")
    w, h = im.size
    c = im.crop((int(box_frac[0] * w), int(box_frac[1] * h), int(box_frac[2] * w), int(box_frac[3] * h)))
    c = c.resize((c.width * 2, c.height * 2), Image.LANCZOS)
    dest = os.path.join(OUT, name + suffix + ".png")
    c.save(dest)
    print("  saved", dest)


def g_probe():
    start_match()
    print(cs("Info"))
    shot("probe")
    end_play()


def g_touch():
    size_idx = None
    for tag, w, h in (("landscape_1920x1080", "1920", "1080"), ("portrait_1080x2400", "1080", "2400")):
        start_match()
        if size_idx is None:
            size_idx = cs("GameSizeIndex")
        print(cs("Force", "0")); print(cs("GameSize", w, h)); time.sleep(2.0)
        print(cs("Auto", "0"))
        print(cs("Kit", "Warrior_Bully")); time.sleep(1.0); print("  ", cs("Info"))
        shot("touch_" + tag + "_bully")
        print(cs("Auto", "1")); time.sleep(0.8)
        shot("touch_" + tag + "_bully_autopeck")
        print(cs("Auto", "0"))
        print(cs("Kit", "Assassin_Spoiler")); time.sleep(1.0); print("  ", cs("Info"))
        shot("touch_" + tag + "_spoiler")
        print(cs("GameSizeBack", size_idx))
        end_play()


def g_strip():
    start_match()
    size_idx = cs("GameSizeIndex")
    print(cs("GameSize", "1920", "1080")); time.sleep(2.0)
    print(cs("Kit", "Warrior_Bully"))
    print(cs("Force", "1")); print(cs("Auto", "0"))
    print(cs("Scheme", "0")); time.sleep(0.8); print("  ", cs("Info"))
    shot("strip_desktop_schemeA"); crop("strip_desktop_schemeA", (0.3, 0.72, 0.7, 1.0))
    print(cs("Scheme", "1")); time.sleep(0.8); print("  ", cs("Info"))
    shot("strip_desktop_schemeB"); crop("strip_desktop_schemeB", (0.3, 0.72, 0.7, 1.0))
    print(cs("Force", "2")); time.sleep(0.8); print("  ", cs("Info"))
    shot("strip_pad"); crop("strip_pad", (0.3, 0.72, 0.7, 1.0))
    print(cs("Scheme", "0"))
    print(cs("GameSizeBack", size_idx))
    end_play()


def g_settings():
    print("compile:", cs("Compile")); print(cs("OpenBootstrap")); time.sleep(1)
    print(cs("Play", "1"))
    for _ in range(90):
        if cs("Press", "SettingsBtn").startswith("clicked"):
            break
        time.sleep(1)
    time.sleep(1.0)
    size_idx = "0"
    print(cs("GameSize", "1920", "1080")); time.sleep(1.5)
    print(cs("Scheme", "0")); time.sleep(0.5)
    print(cs("ScrollToRow")); time.sleep(0.5); print("  row:", cs("Row"))
    shot("settings_keyboard_row_A")
    print(cs("Press", "KeyboardSchemeQwerBtn")); time.sleep(0.6); print("  row after Qwer:", cs("Row"))
    shot("settings_keyboard_row_B")
    print(cs("Press", "SettingsCloseBtn")); time.sleep(0.6)
    print(cs("Press", "SoloBtn")); time.sleep(0.9)
    print(cs("Press", "NextBtn")); time.sleep(1.0)
    print(cs("Fill")); print(cs("Press", "ReadyBtn")); time.sleep(1.5)
    print(cs("Press", "StartBtn"))
    wait(("Active",)); time.sleep(1.5)
    print(cs("Force", "1")); time.sleep(0.8); print("  ", cs("Info"))
    shot("settings_then_strip_schemeB"); crop("settings_then_strip_schemeB", (0.3, 0.72, 0.7, 1.0))
    print(cs("Scheme", "0"))
    print(cs("GameSizeBack", size_idx))
    end_play()


import re


def parse(info):
    m = re.search(r"cd=([\d.,/]+) charging=(\d+) lastCast=(-?\d+) cargo=([\d.,]+) auto=(\w+) .* sheet=(\w+)", info)
    cd = [float(x.replace(",", ".")) for x in m.group(1).split("/")]
    return {"cd": cd, "charging": int(m.group(2)), "last": int(m.group(3)), "cargo": float(m.group(4).replace(",", ".")), "auto": m.group(5) == "True", "sheet": m.group(6) == "True"}


KIT = ("SpeedBurst", "Invisibility", "EggShell", "FeatherAura")
results = []


def check(name, ok, detail):
    results.append((name, ok))
    print("  [%s] %s  %s" % ("PASS" if ok else "FAIL", name, detail))


def g_input_keys(scheme):
    start_match(); wait_no_intro()
    print(cs("Force", "1")); print(cs("Scheme", scheme)); print(cs("Devices", "kb")); print(cs("Loadout", *KIT))
    table = {"0": ["DownArrow", "UpArrow", "RightArrow", "LeftArrow"], "1": ["Q", "W", "R", "E"]}[scheme]
    digits = ["Digit1", "Digit2", "Digit3", "Digit4"]
    for label, keys in (("scheme-key", table), ("digit", digits)):
        if label == "digit":
            time.sleep(3.0)   # the slot-4 aura of the first round runs 6.5 s; a live one refuses a recast
        for slot in range(4):
            cs("ResetCd"); time.sleep(0.3)
            mark = log_mark()
            print("  ", tap(keys[slot], 200)); time.sleep(0.5)
            i = parse(cs("Info")); logs = log_since(mark, "Activated slot")
            ok = i["cd"][slot] > 0 and all(c == 0 for k, c in enumerate(i["cd"]) if k != slot) and i["last"] == slot
            check("scheme %s %s %s fires slot %d" % ("A" if scheme == "0" else "B", label, keys[slot], slot + 1), ok, "cd=%s last=%d log=%s" % (i["cd"], i["last"], logs[:1]))
    print(cs("Scheme", "0")); end_play()


def g_input_peck():
    start_match(); wait_no_intro()
    print(cs("Force", "1")); print(cs("Scheme", "0")); print(cs("Devices", "kb")); print(cs("Auto", "0")); print(cs("Kit", "Warrior_Bully"))
    print(cs("ToPile")); time.sleep(1.0)
    c0 = parse(cs("Info"))["cargo"]; print("  cargo before tap:", cs("Cap"))
    mark = log_mark()
    print("  ", tap("DownArrow", 150)); time.sleep(1.2)
    i = parse(cs("Info")); logs = log_since(mark, "Activated slot", "Peck")
    check("4b tap on Peck pecks (cargo rises)", i["cargo"] > c0 and i["auto"] is False, "cargo %.1f -> %.1f auto=%s log=%s" % (c0, i["cargo"], i["auto"], logs[:2]))
    # long press away from any pile: flips once, adds no peck
    print(cs("Move", "22", "21")); time.sleep(0.8)
    a0 = parse(cs("Info")); mark = log_mark()
    print("  ", cs("Key", "DownArrow", "1")); time.sleep(0.9)
    mid = parse(cs("Info")); print("  ", cs("Key", "DownArrow", "0")); time.sleep(1.0)
    a1 = parse(cs("Info")); logs = log_since(mark, "Activated slot", "Auto-Peck", "AutoPeck", "Peck")
    check("4b 0.9 s hold flips AutoPeck exactly once (off->on, stays on after release)", (not a0["auto"]) and mid["auto"] and a1["auto"], "auto before=%s during=%s after=%s cargo %.1f->%.1f log=%s" % (a0["auto"], mid["auto"], a1["auto"], a0["cargo"], a1["cargo"], logs[:3]))
    # flip back at the pile with auto on: nothing may peck after the release
    print(cs("ToPile")); time.sleep(1.5)
    print("  cargo with auto on at pile:", cs("Cap"))
    ca = parse(cs("Info"))
    print("  ", cs("Key", "DownArrow", "1")); time.sleep(0.7); print("  ", cs("Key", "DownArrow", "0")); time.sleep(1.2)
    c1 = parse(cs("Info")); time.sleep(2.0); c2 = parse(cs("Info"))
    check("4b long press flips auto back off, no peck after release", (not c1["auto"]) and c1["cargo"] == c2["cargo"], "auto=%s cargo before hold %.1f, 1.2 s after release %.1f, 3.2 s after %.1f (cap %s)" % (c1["auto"], ca["cargo"], c1["cargo"], c2["cargo"], cs("Cap")))
    print(cs("Auto", "0")); end_play()


def g_input_pad():
    start_match(); wait_no_intro()
    print(cs("Force", "none")); print(cs("Devices", "gp")); print(cs("Loadout", *KIT))
    for label, btns in (("face", ["South", "North", "East", "West"]), ("shoulder/trigger", ["RightShoulder", "RightTrigger", "LeftShoulder", "LeftTrigger"])):
        if label != "face":
            time.sleep(3.0)   # the slot-4 aura of the first round runs 6.5 s; a live one refuses a recast
        for slot in range(4):
            cs("ResetCd"); time.sleep(0.3); mark = log_mark()
            print("  ", tap(btns[slot], 200, pad=True)); time.sleep(0.5)
            i = parse(cs("Info")); logs = log_since(mark, "Activated slot")
            ok = i["cd"][slot] > 0 and all(c == 0 for k, c in enumerate(i["cd"]) if k != slot) and i["last"] == slot
            check("pad %s %s fires slot %d" % (label, btns[slot], slot + 1), ok, "cd=%s last=%d hud=%s log=%s" % (i["cd"], i["last"], cs("Mode"), logs[:1]))
    for variant, hold in (("B held 0.6 s across the close", 0.6), ("B quick tap 0.1 s", 0.1)):
        print("  ", tap("South", 150, pad=True)); time.sleep(0.5)   # last cast = slot 1, so a stray slot-3 cast would change it
        cs("ResetCd"); time.sleep(0.3); mark = log_mark()
        print("  ", tap("Start", 150, pad=True)); time.sleep(0.6)
        o = parse(cs("Info")); print("  sheet open:", o["sheet"], "state", cs("State"))
        before = o["last"]
        print("  ", cs("Btn", "East", "1")); time.sleep(hold); mid = parse(cs("Info")); print("  ", cs("Btn", "East", "0")); time.sleep(0.8)
        a = parse(cs("Info")); logs = log_since(mark, "Activated slot", "Leave sheet")
        check("4c Start opens the Leave sheet (" + variant + ")", o["sheet"], "sheet=%s" % o["sheet"])
        check("4c B closes sheet, slot 3 does not cast (" + variant + ")", (not a["sheet"]) and a["cd"][2] == 0 and mid["cd"][2] == 0 and a["last"] == before, "sheet after=%s cd=%s mid cd=%s last %d->%d log=%s" % (a["sheet"], a["cd"], mid["cd"], before, a["last"], logs))
    end_play()


def g_input_cancel():
    start_match(); wait_no_intro()
    print(cs("Force", "none")); print(cs("Devices", "gp")); print(cs("Loadout", *KIT))
    cs("ResetCd"); time.sleep(0.3)
    print("  ", cs("Btn", "North", "1")); time.sleep(0.4); h = parse(cs("Info")); print("  ", cs("Btn", "North", "0")); time.sleep(0.5); a = parse(cs("Info"))
    check("4d control: hold slot 2 then release casts (cooldown spent)", a["cd"][1] > 0, "charging while held=%d cd after=%s last=%d" % (h["charging"], a["cd"], a["last"]))
    time.sleep(2.5)   # the control cast's 2 s active window must be over, or the next press is refused
    cs("ResetCd"); time.sleep(0.3); mark = log_mark(); last0 = parse(cs("Info"))["last"]
    print("  ", cs("Btn", "North", "1")); time.sleep(0.4); h = parse(cs("Info"))
    print("  ", cs("Btn", "LeftStick", "1")); time.sleep(0.2); h2 = parse(cs("Info")); print("  ", cs("Btn", "LeftStick", "0")); time.sleep(0.3)
    print("  ", cs("Btn", "North", "0")); time.sleep(0.6); a = parse(cs("Info")); logs = log_since(mark, "Activated slot")
    check("4d L3 cancels a held ability, no cooldown spent", h["charging"] != 0 and h2["charging"] == 0 and all(c == 0 for c in a["cd"]) and a["last"] == last0, "charging held=%d after L3=%d, cd after release=%s lastCast %d->%d (bot Peck lines in the log window ignored: %d)" % (h["charging"], h2["charging"], a["cd"], last0, a["last"], len(logs)))
    end_play()


def g_input_peck2():
    start_match(); wait_no_intro()
    print(cs("Force", "1")); print(cs("Scheme", "0")); print(cs("Devices", "kb")); print(cs("Kit", "Warrior_Bully"))
    print(cs("Auto", "1")); print(cs("ToPile")); time.sleep(1.2)
    series = []
    def sample(tag):
        i = parse(cs("Info")); series.append((tag, i["cargo"], i["auto"])); return i
    sample("t0")
    print("  ", cs("Key", "DownArrow", "1"))
    for k in range(3):
        time.sleep(0.25); sample("hold%d" % k)
    print("  ", cs("Key", "DownArrow", "0"))
    for k in range(10):
        time.sleep(0.3); sample("rel%d" % k)
    print("  series (tag, cargo, auto):", series)
    held = [c for t, c, a in series if t.startswith("hold")]
    rel = [(c, a) for t, c, a in series if t.startswith("rel")]
    auto_worked = max(c for _, c, _ in series) > series[0][1]
    flat_after_release = len(set(c for c, _ in rel[2:])) == 1
    check("4b auto-on at pile pecks by itself (control)", auto_worked, "cargo %.1f -> max %.1f" % (series[0][1], max(c for _, c, _ in series)))
    check("4b long press with auto on flips it off and the release adds no peck", rel[-1][1] is False and flat_after_release and rel[-1][0] < 14, "auto after=%s cargo after release=%s" % (rel[-1][1], [c for c, _ in rel]))
    print(cs("Auto", "0")); end_play()


GROUPS = {"probe": g_probe, "touch": g_touch, "strip": g_strip, "settings": g_settings,
          "input_a": lambda: g_input_keys("0"), "input_b": lambda: g_input_keys("1"), "input_peck": g_input_peck, "input_peck2": g_input_peck2, "input_pad": g_input_pad, "input_cancel": g_input_cancel}
bridge.init()
try:
    GROUPS[GROUP]()
except BaseException:
    try:
        end_play()
    except Exception:
        pass
    raise
print("SUMMARY:", sum(1 for _, ok in results if ok), "pass /", sum(1 for _, ok in results if not ok), "fail")
