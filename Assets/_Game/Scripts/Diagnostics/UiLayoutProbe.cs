using System;
using System.Collections.Generic;
using System.Diagnostics;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Logging;
using CluckWars.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Zenject;

namespace CluckWars.Diagnostics
{
    /// <summary>
    /// UI layout probe (WP8, Source <c>UiProbe</c>, dev tools only): walks every UIDocument and checks the layout pass
    /// criteria U1-U6 of PORTRAIT_AND_LANDSCAPE_SPEC §3.5 (<see cref="UiLayoutRules"/>, accepted exceptions in
    /// <see cref="UiLayoutAllowList"/>). One Info line per scan (<c>ui offenders=N scope=hud|menu|overlay ...</c>), then one
    /// rate-limited Warn per offender with the rule, the element(s) and their panel rects. merge.ps1 files every scope=hud
    /// scan with offenders under the UI category, which verify.ps1 fails on.
    /// </summary>
    /// <remarks>
    /// <para>When: every <see cref="PeriodSeconds"/> s, and <see cref="SettleSeconds"/> after the screen size or a document
    /// root's geometry changes (a rotation, a window resize; never more often than <see cref="MinTriggeredInterval"/>), while
    /// <see cref="HeavyProbes.Enabled"/>; on demand (<see cref="RequestScan"/>, used by <see cref="MenuShotsDriver"/>) always.</para>
    /// <para>Read-only: it never writes a style or a class. The one exception is <c>-cw_touchhud</c>, which pins the touch
    /// HUD layout (<see cref="TouchControlsController.DebugForceMode"/>) so a PC run shows, and checks, the phone's thumb
    /// arc instead of the desktop strip.</para>
    /// <para>Cost: one walk of the visible tree per scan (hidden subtrees are pruned at display:none / opacity 0), text
    /// measured for visible labels only, strings built for offenders only. The scan's ms is in its summary line.</para>
    /// </remarks>
    public sealed class UiLayoutProbe : MonoBehaviour
    {
        private const string Source = "UiProbe";
        public const float PeriodSeconds = 2f;
        public const float SettleSeconds = 0.3f;
        public const float MinTriggeredInterval = 1f;
        private const int MaxWarnsPerScan = 10;
        private const float WarnRepeatSeconds = 30f;
        private const float MinVisibleOpacity = 0.05f;
        private const int LayoutHostSearchDepth = 4;
        private const string GameSceneName = "Game";

        private static bool _requested;
        private static string _requestedPage;

        /// <summary>Scan on the next frame, tagged <paramref name="page"/> (works with the heavy probes off).</summary>
        public static void RequestScan(string page)
        {
            _requestedPage = page;
            _requested = true;
        }

        private ILogService _log;
        private readonly PerKeyRateLimiter _warnLimiter = new PerKeyRateLimiter(WarnRepeatSeconds);
        private EventCallback<GeometryChangedEvent> _onGeometry;
        private readonly HashSet<VisualElement> _watched = new HashSet<VisualElement>();
        private int _sceneHandle = -1;
        private bool _inGameScene;
        private int _screenW, _screenH;
        private bool _dirty;
        private float _dirtyAt, _nextPeriodicAt = -1f, _lastScanAt = -10f;
        private int _scanCount;
        private TouchControlsController _forcedTouch;
        private bool _touchHud;

        // ---- per-scan state (reused) ---------------------------------------------------------------------------------
        private struct Item
        {
            public VisualElement Ve;
            public Rect Rect;
            public bool InScroll;
        }

        private struct Cluster
        {
            public VisualElement Ve;
            public string Doc;
            public UiShape Shape;
        }

        private readonly List<Item> _texts = new List<Item>(256);      // Labels / Buttons with text
        private readonly List<Item> _boxes = new List<Item>(256);      // U1 (menus): text items + every Button
        private readonly List<Item> _targets = new List<Item>(64);
        private readonly List<Cluster> _clusters = new List<Cluster>(16);
        private readonly List<VisualElement> _hosts = new List<VisualElement>(8);
        private readonly Stack<(VisualElement ve, int depth)> _hostStack = new Stack<(VisualElement, int)>(64);
        private readonly HashSet<VisualElement> _flaggedOutside = new HashSet<VisualElement>();
        private readonly int[] _perRule = new int[7];
        private readonly Stopwatch _watch = new Stopwatch();
        private int _offenders, _allowed, _elements, _warns, _suppressedWarns;
        private UiScope _scope;
        private string _page, _doc;
        private Rect _panel, _safe;
        private float _hexRestScale = 1f;

        [Inject]
        public void Construct(ILogService log) => _log = log;

        private void Start()
        {
            if (_log == null) ProjectContext.Instance.Container.Inject(this);
            DiagnosticsObjects.KeepAcrossScenes(gameObject);
            _onGeometry = OnRootGeometry;
            _touchHud = UiProbeArgs.TouchHud;
            _log?.Info(Source, $"UiProbe on: rules U1-U6 every {PeriodSeconds:0} s while heavy probes run (now {(HeavyProbes.Enabled ? "on" : "off")}), " +
                $"{UiLayoutAllowList.Entries.Length} allow-list entries, touchhud={(_touchHud ? 1 : 0)}.");
        }

        private void OnRootGeometry(GeometryChangedEvent evt)
        {
            if (evt.oldRect.size == evt.newRect.size) return;
            _dirty = true;
            _dirtyAt = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (_touchHud) PinTouchHud();
            float now = Time.realtimeSinceStartup;

            if (_requested)
            {
                _requested = false;
                Scan("request", _requestedPage);
                _requestedPage = null;
                return;
            }
            if (!HeavyProbes.Enabled) { _nextPeriodicAt = -1f; return; }

            if (Screen.width != _screenW || Screen.height != _screenH)
            {
                _screenW = Screen.width;
                _screenH = Screen.height;
                _dirty = true;
                _dirtyAt = now;
            }
            if (_dirty && now - _dirtyAt >= SettleSeconds && now - _lastScanAt >= MinTriggeredInterval)
            {
                _dirty = false;
                Scan("geometry", null);
                return;
            }
            if (_nextPeriodicAt < 0f) _nextPeriodicAt = now + PeriodSeconds;
            else if (now >= _nextPeriodicAt) Scan("periodic", null);
        }

        /// <summary><c>-cw_touchhud</c>: every TouchControlsController that appears is pinned to the touch layout (its Awake clears the pin).</summary>
        private void PinTouchHud()
        {
            var tc = TouchControlsController.Instance;
            if (tc == null || (tc == _forcedTouch && TouchControlsController.DebugForceMode == HudDeviceMode.Touch)) return;
            TouchControlsController.DebugForceMode = HudDeviceMode.Touch;
            _forcedTouch = tc;
            _log?.Info(Source, "UiProbe touchhud=1: HUD pinned to the touch layout (joystick + thumb-arc hexes) for this match scene.");
        }

        // ==============================================================================================================
        //  SCAN
        // ==============================================================================================================

        private void Scan(string reason, string page)
        {
            float now = Time.realtimeSinceStartup;
            _lastScanAt = now;
            _nextPeriodicAt = now + PeriodSeconds;
            DiagnosticsReadout.ProbeWorkFrame = Time.frameCount;

            var docs = FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (docs.Length == 0)
            {
                // Nothing on screen to judge is a legal state for a periodic scan; a requested one (a menu shot) expected a page.
                if (page != null) _log?.Warn(Source, $"ui scan for page={page} found no UIDocument; no verdict for that page.");
                return;
            }

            _watch.Restart();
            Array.Clear(_perRule, 0, _perRule.Length);
            _offenders = _allowed = _elements = _warns = _suppressedWarns = 0;
            _clusters.Clear();
            _page = page;
            _scope = CurrentScope();
            _hexRestScale = HudLayoutRules.HexRestScale(TouchControlsController.Instance != null ? TouchControlsController.Instance.Mode : HudDeviceMode.Touch);
            _panel = default;
            int scannedDocs = 0;

            for (int d = 0; d < docs.Length; d++)
            {
                var doc = docs[d];
                if (doc == null || !doc.isActiveAndEnabled) continue;
                var root = doc.rootVisualElement;
                var panelRoot = root?.panel?.visualTree;
                if (panelRoot == null || !UiLayoutRules.IsMeasurable(panelRoot.layout)) continue;
                if (_watched.Add(root)) root.RegisterCallback(_onGeometry);

                if (!UiLayoutRules.IsMeasurable(_panel))
                {
                    _panel = panelRoot.layout;
                    _safe = UiLayoutRules.ScreenSafeToPanel(SafeAreaSource.Current, Screen.width, Screen.height, _panel);
                }
                _doc = doc.visualTreeAsset != null ? doc.visualTreeAsset.name : doc.gameObject.name;
                scannedDocs++;
                ScanDocument(root, panelRoot.layout);
            }
            if (_scope == UiScope.Hud) CheckClusters();
            _watch.Stop();

            if (scannedDocs == 0)
            {
                if (page != null) _log?.Warn(Source, $"ui scan for page={page}: no enabled UIDocument with a laid-out panel; no verdict for that page.");
                return;
            }
            _scanCount++;
            _log?.Info(Source, UiLayoutRules.Summary(_offenders, _scope, page, reason, _scanCount, _perRule, _allowed, scannedDocs,
                _elements, _panel.width, _panel.height, _safe, (float)_watch.Elapsed.TotalMilliseconds));
            if (_suppressedWarns > 0)
                _log?.Warn(Source, $"ui offender lines: {_suppressedWarns} more this scan not printed (cap {MaxWarnsPerScan} per scan, " +
                    $"or the same offender within {WarnRepeatSeconds:0} s); see the per-rule counts above.");
        }

        private UiScope CurrentScope()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.handle != _sceneHandle)
            {
                _sceneHandle = scene.handle;
                _inGameScene = scene.name == GameSceneName;
                _watched.Clear();   // the old scene's roots are gone
            }
            var gm = GameManager.Instance;
            bool active = gm != null && gm.Object != null && gm.Object.IsValid && gm.State == MatchState.Active;
            return UiLayoutRules.ScopeFor(_inGameScene, active, RevealState.CoverShown);
        }

        private void ScanDocument(VisualElement root, Rect panel)
        {
            _texts.Clear();
            _boxes.Clear();
            _targets.Clear();
            _flaggedOutside.Clear();

            Walk(root, 1f, false, default);
            CheckOrientationHosts(root, panel);
            CheckTexts();
            CheckTargets();
            // The in-match HUD documents' U1 is the cluster rule: inside a cluster, labels stack by design (a hex's
            // cooldown digits over its fallback label, the stick's slow label over the stick).
            if (_scope == UiScope.Menu || (_scope == UiScope.Overlay && !IsHudDocument(_doc))) CheckTextOverlaps();
            CheckSafeArea();
        }

        /// <summary>The two in-match HUD documents (UXML asset names), whose U1 is <see cref="CheckClusters"/>.</summary>
        private static bool IsHudDocument(string doc) => doc == "MatchTopBar" || doc == "TouchControls";

        private void Walk(VisualElement ve, float opacity, bool inScroll, Rect clip)
        {
            var rs = ve.resolvedStyle;
            if (rs.display == DisplayStyle.None || rs.visibility == Visibility.Hidden) return;
            float op = opacity * rs.opacity;
            if (op < MinVisibleOpacity) return;
            var wb = ve.worldBound;
            if (!UiLayoutRules.IsMeasurable(wb))
            {
                // A zero-size container (an empty status strip) can still hold nothing visible; its children are
                // laid out relative to it, so keep walking.
                WalkChildren(ve, op, inScroll, clip);
                return;
            }
            if (inScroll && UiLayoutRules.FullyOutside(wb, clip)) return;   // scrolled out of the viewport
            _elements++;
            // An element's OWN scale is juice (the countdown slam, the held-hex pop, a pip's pop), not layout: judge it at
            // its laid-out size about its centre. Ancestors' scale still applies (the desktop hex strip's 0.75).
            var ownScale = rs.scale.value;
            if (ownScale.x != 1f || ownScale.y != 1f)
                wb = UiLayoutRules.Unscaled(wb, ownScale.x, ownScale.y);

            bool isButton = ve is Button;
            if ((ve is Label || isButton) && !string.IsNullOrWhiteSpace(((TextElement)ve).text))
            {
                var item = new Item { Ve = ve, Rect = wb, InScroll = inScroll };
                _texts.Add(item);
                _boxes.Add(item);
            }
            else if (isButton) _boxes.Add(new Item { Ve = ve, Rect = wb, InScroll = inScroll });

            bool hex = ve.ClassListContains("cw-hex");
            if (IsTarget(ve, isButton, hex))
                _targets.Add(new Item { Ve = ve, Rect = hex ? HexRestRect(ve, wb) : wb, InScroll = inScroll });

            if (_scope == UiScope.Hud && IsCluster(ve, hex))
                _clusters.Add(new Cluster
                {
                    Ve = ve, Doc = _doc,
                    Shape = hex ? UiShape.Disc(wb.center, UiLayoutRules.HexRingRadiusPx * _hexRestScale * ParentScale(ve)) : UiShape.Box(wb),
                });

            if (ve is ScrollView sv && sv.contentViewport != null)
            {
                var vp = sv.contentViewport.worldBound;
                WalkChildren(ve, op, true, inScroll ? Intersect(clip, vp) : vp);
                return;
            }
            WalkChildren(ve, op, inScroll, clip);
        }

        private void WalkChildren(VisualElement ve, float op, bool inScroll, Rect clip)
        {
            var h = ve.hierarchy;
            for (int i = 0; i < h.childCount; i++) Walk(h[i], op, inScroll, clip);
        }

        private static Rect Intersect(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));

        private static float ParentScale(VisualElement ve) => ve.parent != null ? ve.parent.worldTransform.lossyScale.x : 1f;

        /// <summary>The hex at rest: the transient held pop (FeedbackTuning.HexHeldScale) is feedback, not layout.</summary>
        private Rect HexRestRect(VisualElement ve, Rect wb) => UiLayoutRules.HexRestRect(wb.center, _hexRestScale * ParentScale(ve));

        /// <summary>U2 candidates (spec §3.5): every Button, hex, joystick base, class tile, deck card, slot box and seat that takes a tap.</summary>
        private static bool IsTarget(VisualElement ve, bool isButton, bool hex)
        {
            if (ve.pickingMode != PickingMode.Position) return false;   // desktop / pad hexes are an information strip
            return isButton || hex || ve.ClassListContains("cw-joy-base") || ve.ClassListContains("cw-class-tile")
                   || ve.ClassListContains("cw-ability-card") || ve.ClassListContains("cw-slot-box") || ve.ClassListContains("cw-seat");
        }

        /// <summary>The U1 HUD clusters (spec §3.5): leaderboard, timer column, status strip, joystick, each hex, hold hint, event banner.</summary>
        private static bool IsCluster(VisualElement ve, bool hex)
        {
            if (hex) return true;
            switch (ve.name)
            {
                case "LeaderboardPanel":
                case "TimerContainer":
                case "StatusStrip":
                case "JoystickBase":
                case "HoldHint":
                    return true;
            }
            return ve.ClassListContains("cw-eventbanner__plaque");
        }

        // ---- U1 (HUD clusters) ---------------------------------------------------------------------------------------

        private void CheckClusters()
        {
            for (int i = 0; i < _clusters.Count; i++)
            for (int j = i + 1; j < _clusters.Count; j++)
            {
                var a = _clusters[i];
                var b = _clusters[j];
                if (!UiLayoutRules.ClustersConflict(a.Shape, b.Shape, out float gap)) continue;
                _doc = a.Doc == b.Doc ? a.Doc : a.Doc + "+" + b.Doc;
                Offend(UiRule.U1, a.Ve, a.Shape.Bounds, b.Ve, b.Shape.Bounds,
                    gap < 0f ? $"overlap={-gap:0.0}" : $"gap={gap:0.0} (need >= {UiLayoutRules.ClusterMinGapPx:0})");
            }
        }

        // ---- U1 (menus / overlays): Buttons and Labels ---------------------------------------------------------------

        private void CheckTextOverlaps()
        {
            for (int i = 0; i < _boxes.Count; i++)
            for (int j = i + 1; j < _boxes.Count; j++)
            {
                var a = _boxes[i];
                var b = _boxes[j];
                if (!UiLayoutRules.TextBoxesOverlap(a.Rect, b.Rect)) continue;
                if (a.Ve.Contains(b.Ve) || b.Ve.Contains(a.Ve)) continue;
                Offend(UiRule.U1, a.Ve, a.Rect, b.Ve, b.Rect, $"overlap={UiLayoutRules.IntersectionArea(a.Rect, b.Rect):0}px2");
            }
        }

        // ---- U2 ------------------------------------------------------------------------------------------------------

        private void CheckTargets()
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                var t = _targets[i];
                if (UiLayoutRules.TargetTooSmall(t.Rect))
                    Offend(UiRule.U2, t.Ve, t.Rect, null, default, $"size {t.Rect.width:0}x{t.Rect.height:0} (need >= {UiLayoutRules.TouchTargetMinPx:0})");
                for (int j = i + 1; j < _targets.Count; j++)
                {
                    var o = _targets[j];
                    if (t.Ve.Contains(o.Ve) || o.Ve.Contains(t.Ve)) continue;
                    if (!UiLayoutRules.TargetsTooClose(t.Rect, o.Rect, out float gap)) continue;
                    Offend(UiRule.U2, t.Ve, t.Rect, o.Ve, o.Rect,
                        gap < 0f ? "overlapping targets" : $"gap={gap:0.0} (need >= {UiLayoutRules.TouchTargetMinGapPx:0})");
                }
            }
        }

        // ---- U3 / U4 -------------------------------------------------------------------------------------------------

        private void CheckTexts()
        {
            float floor = UiLayoutRules.FontFloor(_scope);
            for (int i = 0; i < _texts.Count; i++)
            {
                var it = _texts[i];
                var te = (TextElement)it.Ve;
                var rs = te.resolvedStyle;

                // Ancestors' scale only: the element's own scale is juice (a pop from 0.x would read as tiny type).
                float px = rs.fontSize * (te.parent != null ? te.parent.worldTransform.lossyScale.y : 1f);
                if (UiLayoutRules.BelowFontFloor(px, floor))
                    Offend(UiRule.U4, te, it.Rect, null, default, $"font={px:0.0}px (floor {floor:0})", px);

                if (!it.InScroll && UiLayoutRules.PartlyOutside(it.Rect, _panel))
                {
                    _flaggedOutside.Add(te);
                    Offend(UiRule.U3, te, it.Rect, null, default, $"partly off the panel {UiLayoutRules.Fmt(_panel)}");
                    continue;
                }

                var content = te.contentRect;
                if (!UiLayoutRules.IsMeasurable(content)) continue;
                if (rs.whiteSpace == WhiteSpace.Normal)
                {
                    var m = te.MeasureTextSize(te.text, content.width, VisualElement.MeasureMode.Exactly, 0f, VisualElement.MeasureMode.Undefined);
                    if (UiLayoutRules.WrappedTextClipped(m.y, content.height))
                        Offend(UiRule.U3, te, it.Rect, null, default, $"text height {m.y:0.0} > content {content.height:0.0} \"{Clip(te.text)}\"");
                }
                else
                {
                    var m = te.MeasureTextSize(te.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
                    if (UiLayoutRules.TextClipped(m.x, content.width))
                        Offend(UiRule.U3, te, it.Rect, null, default, $"text width {m.x:0.0} > content {content.width:0.0} \"{Clip(te.text)}\"");
                }
            }
        }

        private static string Clip(string s) => s.Length <= 24 ? s : s.Substring(0, 24) + "...";

        // ---- U5 ------------------------------------------------------------------------------------------------------

        private void CheckSafeArea()
        {
            if (!UiLayoutRules.IsMeasurable(_safe)) return;
            CheckSafe(_targets);
            CheckSafe(_texts);
            if (_scope != UiScope.Hud) return;
            for (int i = 0; i < _clusters.Count; i++)
            {
                var c = _clusters[i];
                if (c.Doc != _doc) continue;   // each document checks its own clusters once
                var r = c.Shape.Bounds;
                if (!UiLayoutRules.FullyOutside(r, _panel) && !UiLayoutRules.Contains(_safe, r))
                    Offend(UiRule.U5, c.Ve, r, null, default, $"outside the safe area {UiLayoutRules.Fmt(_safe)}");
            }
        }

        private void CheckSafe(List<Item> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.InScroll || _flaggedOutside.Contains(it.Ve)) continue;
                if (UiLayoutRules.FullyOutside(it.Rect, _panel) || UiLayoutRules.Contains(_safe, it.Rect)) continue;
                _flaggedOutside.Add(it.Ve);   // a label that is also a target is reported once
                Offend(UiRule.U5, it.Ve, it.Rect, null, default, $"outside the safe area {UiLayoutRules.Fmt(_safe)}");
            }
        }

        // ---- U6 ------------------------------------------------------------------------------------------------------

        /// <summary>The elements carrying an orientation class (LayoutClasses targets), searched regardless of visibility.</summary>
        private void CheckOrientationHosts(VisualElement root, Rect panel)
        {
            _hosts.Clear();
            _hostStack.Clear();
            _hostStack.Push((root, 0));
            while (_hostStack.Count > 0)
            {
                var (ve, depth) = _hostStack.Pop();
                if (ve.ClassListContains(LayoutClasses.Portrait) || ve.ClassListContains(LayoutClasses.Landscape)) _hosts.Add(ve);
                if (depth >= LayoutHostSearchDepth) continue;
                var h = ve.hierarchy;
                for (int i = 0; i < h.childCount; i++) _hostStack.Push((h[i], depth + 1));
            }

            if (_hosts.Count == 0)
            {
                OffendDoc($"no element within depth {LayoutHostSearchDepth} carries {LayoutClasses.Portrait} or {LayoutClasses.Landscape}");
                return;
            }
            for (int i = 0; i < _hosts.Count; i++)
            {
                var ve = _hosts[i];
                bool p = ve.ClassListContains(LayoutClasses.Portrait), l = ve.ClassListContains(LayoutClasses.Landscape);
                var v = UiLayoutRules.CheckOrientation(p, l, panel.width, panel.height);
                if (v == OrientationVerdict.Ok) continue;
                Offend(UiRule.U6, ve, ve.worldBound, null, default,
                    $"{v}: portrait={(p ? 1 : 0)} landscape={(l ? 1 : 0)} panel={panel.width:0}x{panel.height:0}");
            }
        }

        private void OffendDoc(string detail)
        {
            string doc = _doc;
            if (UiLayoutAllowList.Find(UiRule.U6, sel => UiLayoutAllowList.Matches(sel, doc, null), null) != null) { _allowed++; return; }
            Count(UiRule.U6, UiLayoutRules.OffenderKey(UiRule.U6, doc, null),
                () => UiLayoutRules.OffenderLine(UiRule.U6, _scope, _page, doc, doc, _panel, null, default, detail));
        }

        // ---- offender bookkeeping ------------------------------------------------------------------------------------

        private void Offend(UiRule rule, VisualElement a, Rect ra, VisualElement b, Rect rb, string detail, float fontPx = 0f)
        {
            string doc = _doc;
            Func<string, bool> matchA = sel => UiLayoutAllowList.Matches(sel, a.name, a.ClassListContains)
                                               || (rule == UiRule.U6 && UiLayoutAllowList.Matches(sel, doc, null));
            Func<string, bool> matchB = b == null ? null : sel => UiLayoutAllowList.Matches(sel, b.name, b.ClassListContains);
            if (UiLayoutAllowList.Find(rule, matchA, matchB, fontPx) != null) { _allowed++; return; }

            string ea = Describe(a), eb = b != null ? Describe(b) : null;
            Count(rule, UiLayoutRules.OffenderKey(rule, ea, eb),
                () => UiLayoutRules.OffenderLine(rule, _scope, _page, doc, ea, ra, eb, rb, detail));
        }

        private void Count(UiRule rule, int key, Func<string> line)
        {
            _offenders++;
            _perRule[(int)rule]++;
            // Requested scans (menu shots: one per page) always print their offenders; periodic ones are rate-limited.
            bool allow = _page != null ? _warns < MaxWarnsPerScan : _warns < MaxWarnsPerScan && _warnLimiter.Allow(key, Time.realtimeSinceStartup, out _);
            if (!allow) { _suppressedWarns++; return; }
            _warns++;
            _log?.Warn(Source, line());
        }

        /// <summary><c>Name</c> or <c>.first-class</c>, then the nearest named ancestor (<c>LbName0&lt;LbRow0</c>).</summary>
        private static string Describe(VisualElement ve)
        {
            string self = !string.IsNullOrEmpty(ve.name) ? ve.name : FirstClass(ve) ?? ve.GetType().Name;
            for (var p = ve.parent; p != null; p = p.parent)
                if (!string.IsNullOrEmpty(p.name)) return self + "<" + p.name;
            return self;
        }

        private static string FirstClass(VisualElement ve)
        {
            foreach (var c in ve.GetClasses())
                if (!c.StartsWith("unity-", StringComparison.Ordinal)) return "." + c;
            return null;
        }
    }
}
