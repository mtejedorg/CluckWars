using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CluckWars.Logging;
using CluckWars.UI;
using UnityEngine;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// The WP8 dev keys, read from the command line (and, in the Editor, the <see cref="AutoTest.EditorConfigure"/> spec)
    /// through the auto-test's own parser. Not Android intent extras: the orientation captures are a PC tool.
    /// </summary>
    public static class UiProbeArgs
    {
        /// <summary><c>-cw_menushots [main,settings,disconnect,class,gear,coop,coopjoin]</c>: walk the menu pages before anything else.</summary>
        public const string MenuShotsKey = "cw_menushots";
        /// <summary><c>-cw_touchhud</c>: pin the in-match HUD to the touch layout, so a PC window is checked like a phone.</summary>
        public const string TouchHudKey = "cw_touchhud";

        private static Dictionary<string, string> _map;

        private static Dictionary<string, string> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                AutoTestConfig.AddCommandLine(Environment.GetCommandLineArgs(), _map);
#if UNITY_EDITOR
                AutoTestConfig.AddSpec(AutoTest.EditorSpec, _map);
#endif
                return _map;
            }
        }

        public static bool MenuShotsRequested => DevTools.Available && Map.ContainsKey(MenuShotsKey);

        public static string MenuShotsValue => Map.TryGetValue(MenuShotsKey, out var v) ? v : null;

        public static bool TouchHud => DevTools.Available && Map.TryGetValue(TouchHudKey, out var v) && v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);

        /// <summary><c>cw_shottag</c>, sanitised like the in-match shots' tag; null when absent.</summary>
        public static string ShotTag => Map.TryGetValue(AutoTestConfig.ShotTagKey, out var v) ? AutoTestConfig.SanitizeProfile(v) : null;
    }

    /// <summary>
    /// <c>-cw_menushots</c> (WP8, dev tools only): once the Bootstrap menu is built, shows each listed page through
    /// <see cref="MenuUiController.AutoTestShowPage"/>, lets it settle, asks <see cref="UiLayoutProbe"/> for a scan tagged with
    /// the page (<c>ui offenders=N scope=menu page=&lt;id&gt;</c>) and saves <c>menu-&lt;page&gt;[-&lt;tag&gt;].png</c> next to the
    /// in-match <c>-cw_shots</c> (<c>&lt;logdir&gt;/shots</c>). Then it returns to the main menu and releases the hold on
    /// <see cref="MenuUiController.AutoTestIdle"/>, so a <c>-cw_autosolo</c> / <c>-cw_autohost</c> flow starts only afterwards.
    /// </summary>
    /// <remarks>
    /// Pages are shown, never acted on: no button handler runs, no lobby is created (THE COOP is opened in Solo or Join
    /// mode only). A page that cannot be shown is a Warn and the walk goes on; no menu within <see cref="MenuWaitSeconds"/>
    /// is an Error and the hold is released.
    /// </remarks>
    public sealed class MenuShotsDriver : MonoBehaviour
    {
        private const string Source = "AutoTest";
        private const float MenuWaitSeconds = 45f;
        /// <summary>Page transition (120 + 200 ms) plus the staggered entrance of tiles / cards / seats.</summary>
        private const float SettleSeconds = 1.2f;
        private const float AfterShotSeconds = 0.4f;

        private static bool _done;

        /// <summary>True from boot until the walk ends when <c>-cw_menushots</c> is given: the menu auto-test waits for it.</summary>
        public static bool Holding => !_done && UiProbeArgs.MenuShotsRequested;

        private ILogService _log;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Start()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
            if (!UiProbeArgs.MenuShotsRequested) { enabled = false; return; }
            _done = false;   // a static survives an Editor Play session without a domain reload

            var problems = new List<string>();
            var pages = UiLayoutRules.ParseMenuPages(UiProbeArgs.MenuShotsValue, problems);
            foreach (var p in problems) _log.Warn(Source, "AutoTest menushots: " + p);
            if (pages.Length == 0)
            {
                _log.Warn(Source, "AutoTest menushots: no valid page id; nothing to capture.");
                _done = true;
                return;
            }
            StartCoroutine(Walk(pages));
        }

        private IEnumerator Walk(string[] pages)
        {
            float t0 = Time.realtimeSinceStartup;
            MenuUiController menu = null;
            while (menu == null || !menu.AutoTestPagesReady)
            {
                if (Time.realtimeSinceStartup - t0 > MenuWaitSeconds)
                {
                    _log.Error(Source, $"AutoTest menushots: no built main menu after {MenuWaitSeconds:0} s; no menu pages captured (the hold on the menu auto-test is released).");
                    _done = true;
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.25f);
                menu = FindFirstObjectByType<MenuUiController>();
            }
            yield return new WaitForSecondsRealtime(SettleSeconds);   // the first page's slide-in

            string dir = AutoTestRules.ShotDirectoryFromArgs(Environment.GetCommandLineArgs()) ?? Path.Combine(Application.persistentDataPath, "shots");
            bool canSave = true;
            try { Directory.CreateDirectory(dir); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                canSave = false;
                _log.Error(Source, $"AutoTest menushots: cannot create {dir}: {e.Message}; pages are probed but no PNG is saved.");
            }
            string tag = UiProbeArgs.ShotTag;
            _log.Info(Source, $"AutoTest menushots: {string.Join(",", pages)} into {dir} (window {Screen.width}x{Screen.height}).");

            int shown = 0;
            foreach (var page in pages)
            {
                if (menu == null) { _log.Error(Source, "AutoTest menushots: the menu went away mid-walk; stopping."); break; }
                if (!menu.AutoTestShowPage(page)) { _log.Warn(Source, $"AutoTest menushots: page '{page}' could not be shown; skipped."); continue; }
                yield return new WaitForSecondsRealtime(SettleSeconds);
                UiLayoutProbe.RequestScan(page);
                yield return null;   // the probe scans in its Update this frame; the capture below is taken at its end
                if (canSave)
                {
                    var path = Path.Combine(dir, UiLayoutRules.MenuShotFileName(page, tag));
                    ScreenCapture.CaptureScreenshot(path, 1);
                    _log.Info(Source, $"AutoTest menushot page={page} window={Screen.width}x{Screen.height} -> {path}");
                }
                shown++;
                yield return new WaitForSecondsRealtime(AfterShotSeconds);
            }

            if (menu != null) menu.AutoTestShowPage("main");
            _done = true;
            _log.Info(Source, $"AutoTest menushots done: {shown}/{pages.Length} page(s); the menu auto-test may start.");
        }
    }
}
