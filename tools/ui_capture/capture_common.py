"""Shared capture-tool plumbing (Phase 8 D16): render tier -> render-texture MSAA / post-processing, Performance Mode, teleport clearance.

The capture tools render the match camera (and free cameras) into their own RenderTextures. Three things used to differ from the shipped frame:
  * MSAA: URP takes the sample count from the render texture, not the pipeline asset, so a hard-coded 4 made Mobile (ships 2x) look smoother than it is.
  * Post-processing: a free camera copied once from the match camera kept whatever the first tier gave it (PC overviews skipped the D7 post).
  * Performance Mode: a persistent PlayerPrefs value nobody set or logged per pass, which swaps the fence for the lighter twin mesh.

`CAP_TIER_CS` is a C# class (`CapTier`) the tools append to their own script-execute source; it has no `using` lines (fully qualified), so it can follow any
class. `perf_mode()` is the Python side: set the pref explicitly before Play (default OFF; PERF_MODE=1 turns it on), log it, restore on exit.
"""
import contextlib
import json
import os

import bridge

CAP_TIER_CS = r"""
public static class CapTier {
  static System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
  static UnityEngine.Rendering.RenderPipelineAsset Rp() { return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline; }
  // The ACTIVE URP asset's MSAA sample count (PC_RPAsset 4x / Mobile_RPAsset 2x), read live so a tier switch is followed. 1 = no MSAA.
  public static int Msaa() { var rp = Rp(); if (rp == null) return 1; var pi = rp.GetType().GetProperty("msaaSampleCount"); int n = pi != null ? System.Convert.ToInt32(pi.GetValue(rp)) : 1; return n < 1 ? 1 : n; }
  public static UnityEngine.RenderTexture NewRt(int w, int h) { return new UnityEngine.RenderTexture(w, h, 24, UnityEngine.RenderTextureFormat.ARGB32) { antiAliasing = Msaa() }; }
  // Source of truth is RenderTierPolicy (the same call MatchCamera.ApplyRenderTier makes), never a copy of its constant.
  public static bool WantPost() { return CluckWars.Visuals.RenderTierPolicy.PostProcessingFor(Rp()); }
  static UnityEngine.Component Uacd(UnityEngine.Camera cam) { var d = cam.GetComponent("UniversalAdditionalCameraData"); if (d == null) throw new System.InvalidOperationException("camera " + cam.name + " has no UniversalAdditionalCameraData"); return d; }
  // Re-applies the tier's post-processing flag to a camera (free cameras copied from the match camera); returns what it set.
  public static bool ApplyPost(UnityEngine.Camera cam) { var d = Uacd(cam); bool want = WantPost(); d.GetType().GetProperty("renderPostProcessing").SetValue(d, want); return want; }
  static string MatchPost() { var m = CluckWars.Visuals.MatchCamera.Instance; if (m == null || m.Camera == null) return "n/a"; var d = m.Camera.GetComponent("UniversalAdditionalCameraData"); return d == null ? "n/a" : d.GetType().GetProperty("renderPostProcessing").GetValue(d).ToString(); }
  public static bool PerfPref() { return CluckWars.Settings.PlayerPreferences.PerformanceModeEnabled; }
  static string FenceShown() { var f = UnityEngine.Object.FindFirstObjectByType<CluckWars.Visuals.FencePerformanceSwap>(); return f == null ? "n/a" : f.PerformanceShown.ToString(); }
  // One-line pass log: what the capture will actually render with.
  public static string Info() { var rp = Rp(); return "rp=" + (rp ? rp.name : "null") + " msaa=" + Msaa() + " postPolicy=" + WantPost() + " matchCamPost=" + MatchPost()
      + " performanceMode=" + (PerfPref() ? 1 : 0) + " fencePerfMeshShown=" + FenceShown(); }
  public static string Json() { var rp = Rp(); return "{\"rp\":\"" + (rp ? rp.name : "null") + "\",\"rtMsaa\":" + Msaa() + ",\"postPolicy\":" + (WantPost() ? "true" : "false") + ",\"matchCamPost\":\"" + MatchPost()
      + "\",\"performanceMode\":" + (PerfPref() ? 1 : 0) + ",\"fencePerfMeshShown\":\"" + FenceShown() + "\"}"; }
  // Performance Mode pref. mode: "get" | "0" | "1" | "unset". Returns "raw=<0|1|unset> effective=<0|1>".
  public static string Perf(string mode) {
    string key = CluckWars.Settings.PlayerPreferences.PerformanceModeKey;
    if (mode == "0" || mode == "1") CluckWars.Settings.PlayerPreferences.PerformanceModeEnabled = mode == "1";
    else if (mode == "unset") { UnityEngine.PlayerPrefs.DeleteKey(key); UnityEngine.PlayerPrefs.Save();
      var f = typeof(CluckWars.Settings.PlayerPreferences).GetField("_performanceModeLoaded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
      if (f == null) throw new System.InvalidOperationException("PlayerPreferences._performanceModeLoaded not found: cannot restore the unset state"); f.SetValue(null, false); }
    else if (mode != "get") throw new System.ArgumentException("Perf mode " + mode);
    return "raw=" + (UnityEngine.PlayerPrefs.HasKey(key) ? UnityEngine.PlayerPrefs.GetInt(key).ToString() : "unset") + " effective=" + (PerfPref() ? 1 : 0); }
  // Another chicken (decoys included: they render) within r metres of pos in XZ, or null. `self` is excluded.
  public static string NearOther(UnityEngine.Vector3 pos, CluckWars.Gameplay.ChickenController self, float r) {
    foreach (var k in CluckWars.Gameplay.ChickenController.ActiveControllers) { if (k == null || k == self) continue;
      var d = k.transform.position - pos; d.y = 0f; if (d.magnitude < r) return k.name + " at " + d.magnitude.ToString("0.00", CI) + " m"; }
    return null; }
}
"""


def _cap(method, *args, timeout=120):
    params = [{"name": n, "typeName": "System.String", "value": str(v)} for n, v in zip(("mode",), args)]
    m = bridge.call("script-execute", {"csharpCode": CAP_TIER_CS, "className": "CapTier", "methodName": method, "parameters": params}, timeout=timeout)
    try:
        value = m["result"]["structuredContent"]["result"]["value"]
    except Exception:
        value = None
    if value is None:
        raise RuntimeError(f"CapTier.{method} failed: " + bridge.text(m)[:800])
    return str(value)


def cap_info():
    """One-line description of what the capture renders with right now (needs a loaded render pipeline; MatchCam fields are n/a before Play)."""
    return _cap("Info")


def cap_json():
    return json.loads(_cap("Json"))


def perf_requested():
    return "1" if os.environ.get("PERF_MODE", "").strip().lower() in ("1", "true", "on", "yes") else "0"


@contextlib.contextmanager
def perf_mode(log=print):
    """Sets Performance Mode explicitly for the whole run (default OFF, PERF_MODE=1 for ON), logs it, and puts the pre-run value back in `finally`.
    Enter it in Edit mode BEFORE Play starts: the S5 fence swap reads the pref live and builds its twins when first wanted, so the value must already
    be in place when the match scene loads. Needs bridge.init() done."""
    pre = _cap("Perf", "get")
    want = perf_requested()
    log(f"PERF MODE pre-run [{pre}] -> setting {want} ({'PERF_MODE env' if os.environ.get('PERF_MODE') else 'default OFF'})")
    log("PERF MODE now [" + _cap("Perf", want) + "]")
    try:
        yield want
    finally:
        raw = pre.split("raw=")[1].split()[0]
        try:
            log("PERF MODE restored [" + _cap("Perf", raw) + f"] (pre-run [{pre}])")
        except Exception as e:  # report, never swallow: the pref stays at the capture value and the orchestrator has to fix it
            log(f"PERF MODE RESTORE FAILED (pref left at {want}, pre-run was [{pre}]): {e}")
