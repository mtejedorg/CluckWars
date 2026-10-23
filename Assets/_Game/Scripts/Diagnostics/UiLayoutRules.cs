using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CluckWars.Diagnostics
{
    /// <summary>The pass criteria of PORTRAIT_AND_LANDSCAPE_SPEC §3.5 that <see cref="UiLayoutProbe"/> checks.</summary>
    public enum UiRule : byte
    {
        /// <summary>No overlap: HUD clusters (intersection 0, gap ≥ 8 px); menu Buttons / Labels (intersection 0 unless one contains the other).</summary>
        U1 = 1,
        /// <summary>Touch targets ≥ 116 x 116 panel px (48 dp), adjacent targets ≥ 19 px (8 dp) apart.</summary>
        U2 = 2,
        /// <summary>No clipped text: the measured text fits the content box (ellipsis allow-list aside); no Label partly off the panel.</summary>
        U3 = 3,
        /// <summary>Type floors from resolvedStyle.fontSize (x the element's world scale): HUD ≥ 28 px, menu ≥ 30 px.</summary>
        U4 = 4,
        /// <summary>Every interactive or informational element inside the (simulated) safe area.</summary>
        U5 = 5,
        /// <summary>Each UIDocument's layout host carries exactly one of layout--portrait / layout--landscape, matching its aspect.</summary>
        U6 = 6,
    }

    /// <summary>
    /// What is on screen when a scan runs. Only <see cref="Hud"/> (the Game scene during an Active round, arena revealed) is
    /// gated by verify.ps1; <see cref="Menu"/> (the Bootstrap menu) and <see cref="Overlay"/> (Game scene, any other state:
    /// waiting room, GET READY, results) are reported for triage.
    /// </summary>
    public enum UiScope : byte { Menu = 0, Hud = 1, Overlay = 2 }

    /// <summary>The U6 verdict for one layout host.</summary>
    public enum OrientationVerdict : byte { Ok = 0, Missing = 1, Both = 2, Mismatch = 3 }

    /// <summary>
    /// A HUD cluster's footprint for U1: a box, or a disc (a hex with its ring pad: the duration ring is a hexagonal outline
    /// at <see cref="UiLayoutRules.HexRingRadiusPx"/> around the hex centre, so the disc of that radius is its honest shape;
    /// the hexes' bounding boxes overlap their neighbours' rings by construction).
    /// </summary>
    public readonly struct UiShape
    {
        public readonly Rect Bounds;
        public readonly bool IsDisc;
        public readonly Vector2 Centre;
        public readonly float Radius;

        private UiShape(Rect bounds, bool disc, Vector2 centre, float radius)
        {
            Bounds = bounds; IsDisc = disc; Centre = centre; Radius = radius;
        }

        public static UiShape Box(Rect r) => new UiShape(r, false, r.center, 0f);

        public static UiShape Disc(Vector2 centre, float radius) =>
            new UiShape(new Rect(centre.x - radius, centre.y - radius, 2f * radius, 2f * radius), true, centre, radius);
    }

    /// <summary>
    /// Pure geometry and rule decisions of the UI layout probe (WP8, PORTRAIT_AND_LANDSCAPE_SPEC §3.5 U1-U6) plus its log
    /// line formats. Every rect is in panel px (UI Toolkit world space: origin top-left, y down). Pinned by
    /// <c>UiLayoutRulesTests</c> on synthetic rects; <see cref="UiLayoutProbe"/> only gathers rects and feeds them here.
    /// </summary>
    public static class UiLayoutRules
    {
        /// <summary>U1: the smallest gap between two HUD clusters.</summary>
        public const float ClusterMinGapPx = 8f;
        /// <summary>U2: 48 dp at 1 dp = 2.35 panel px (CluckWarsTheme.uss sizing contract).</summary>
        public const float TouchTargetMinPx = 116f;
        /// <summary>U2: 8 dp between adjacent targets.</summary>
        public const float TouchTargetMinGapPx = 19f;
        /// <summary>U4: glanceable HUD type (TouchControls.uss / MatchTopBar.uss floor).</summary>
        public const float HudFontFloorPx = 28f;
        /// <summary>U4: load-bearing menu type (the 12 sp caption floor of the sizing contract is 30 px).</summary>
        public const float MenuFontFloorPx = 30f;
        /// <summary>TouchControls.uss .cw-hex: 150 px box.</summary>
        public const float HexHalfPx = 75f;
        /// <summary>HexDurationRing's radius around the hex centre (TouchControls.uss .cw-hex-ring): the hex's U1 ring pad.</summary>
        public const float HexRingRadiusPx = 80f;
        /// <summary>Layout rounds to the pixel grid and glyph metrics are fractional: a text this much wider still fits.</summary>
        public const float TextFitTolerancePx = 1.5f;
        /// <summary>Edge tolerance for containment (sub-pixel rounding at a panel / safe-area edge).</summary>
        public const float EdgeTolerancePx = 0.5f;
        /// <summary>Overlaps smaller than this (px²) are rounding, not an overlap.</summary>
        public const float MinOverlapAreaPx2 = 1f;
        /// <summary>A font this much under the floor still meets it (float rounding of scale x size).</summary>
        public const float FontTolerancePx = 0.25f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- geometry ------------------------------------------------------------------------------------------------

        /// <summary>A rect that layout has produced: finite, non-negative size, not empty.</summary>
        public static bool IsMeasurable(Rect r) =>
            !float.IsNaN(r.x) && !float.IsNaN(r.y) && !float.IsNaN(r.width) && !float.IsNaN(r.height) &&
            !float.IsInfinity(r.width) && !float.IsInfinity(r.height) && r.width > 0f && r.height > 0f;

        public static float IntersectionArea(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        /// <summary>Euclidean distance between two rects' edges; 0 when they touch or overlap.</summary>
        public static float Gap(Rect a, Rect b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
            float dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        public static bool Contains(Rect outer, Rect inner, float tolerance = EdgeTolerancePx) =>
            inner.xMin >= outer.xMin - tolerance && inner.yMin >= outer.yMin - tolerance &&
            inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;

        /// <summary>Not a single pixel inside <paramref name="bounds"/> (an element slid off screen, scrolled away).</summary>
        public static bool FullyOutside(Rect r, Rect bounds) =>
            r.xMax <= bounds.xMin || r.xMin >= bounds.xMax || r.yMax <= bounds.yMin || r.yMin >= bounds.yMax;

        /// <summary>Partly in, partly out: the cut-off case (a wholly off-screen element is a different, legal state).</summary>
        public static bool PartlyOutside(Rect r, Rect bounds, float tolerance = EdgeTolerancePx) =>
            !FullyOutside(r, bounds) && !Contains(bounds, r, tolerance);

        /// <summary>
        /// The box an element scaled by its own <paramref name="scaleX"/> x <paramref name="scaleY"/> (about its centre, the
        /// default transform origin) occupies at scale 1: transient juice scale is not layout. A zero scale leaves the box as is.
        /// </summary>
        public static Rect Unscaled(Rect scaled, float scaleX, float scaleY)
        {
            if (scaleX == 0f || scaleY == 0f) return scaled;
            float w = scaled.width / Mathf.Abs(scaleX), h = scaled.height / Mathf.Abs(scaleY);
            var c = scaled.center;
            return new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h);
        }

        /// <summary>The box an element moved by its own translate (<paramref name="dx"/>, <paramref name="dy"/> in its parent's
        /// units, <paramref name="parentScale"/> to panel px) occupies where layout put it: a shake / follow offset is juice.</summary>
        public static Rect Untranslated(Rect moved, float dx, float dy, float parentScale) =>
            new Rect(moved.x - dx * parentScale, moved.y - dy * parentScale, moved.width, moved.height);

        /// <summary>Signed gap between two discs (negative = overlap depth).</summary>
        public static float DiscGap(Vector2 c1, float r1, Vector2 c2, float r2) => Vector2.Distance(c1, c2) - r1 - r2;

        /// <summary>Signed gap between a disc and a rect (negative = the disc reaches into the rect).</summary>
        public static float DiscRectGap(Vector2 c, float r, Rect rect)
        {
            float nx = Mathf.Clamp(c.x, rect.xMin, rect.xMax);
            float ny = Mathf.Clamp(c.y, rect.yMin, rect.yMax);
            bool inside = c.x > rect.xMin && c.x < rect.xMax && c.y > rect.yMin && c.y < rect.yMax;
            if (inside)
            {
                float toEdge = Mathf.Min(Mathf.Min(c.x - rect.xMin, rect.xMax - c.x), Mathf.Min(c.y - rect.yMin, rect.yMax - c.y));
                return -(toEdge + r);
            }
            return Vector2.Distance(c, new Vector2(nx, ny)) - r;
        }

        /// <summary>
        /// Signed gap between two cluster shapes: positive = clear distance, 0 = touching, negative = overlap (for two
        /// boxes, minus the smaller overlap extent).
        /// </summary>
        public static float ShapeGap(in UiShape a, in UiShape b)
        {
            if (a.IsDisc && b.IsDisc) return DiscGap(a.Centre, a.Radius, b.Centre, b.Radius);
            if (a.IsDisc) return DiscRectGap(a.Centre, a.Radius, b.Bounds);
            if (b.IsDisc) return DiscRectGap(b.Centre, b.Radius, a.Bounds);
            if (IntersectionArea(a.Bounds, b.Bounds) > 0f)
            {
                float w = Mathf.Min(a.Bounds.xMax, b.Bounds.xMax) - Mathf.Max(a.Bounds.xMin, b.Bounds.xMin);
                float h = Mathf.Min(a.Bounds.yMax, b.Bounds.yMax) - Mathf.Max(a.Bounds.yMin, b.Bounds.yMin);
                return -Mathf.Min(w, h);
            }
            return Gap(a.Bounds, b.Bounds);
        }

        // ---- U1 / U2 -------------------------------------------------------------------------------------------------

        /// <summary>U1 (HUD): two clusters overlap or sit closer than <see cref="ClusterMinGapPx"/>.</summary>
        public static bool ClustersConflict(in UiShape a, in UiShape b, out float gap)
        {
            gap = ShapeGap(a, b);
            return gap < ClusterMinGapPx;
        }

        /// <summary>
        /// U1 (menus): two Buttons / Labels overlap. A pair where one rect contains the other is a containment, not an
        /// overlap (a label on its card's button); the caller also skips hierarchy ancestors.
        /// </summary>
        public static bool TextBoxesOverlap(Rect a, Rect b) =>
            IntersectionArea(a, b) >= MinOverlapAreaPx2 && !Contains(a, b, 0f) && !Contains(b, a, 0f);

        /// <summary>U2: either side under <see cref="TouchTargetMinPx"/>.</summary>
        public static bool TargetTooSmall(Rect r) =>
            r.width < TouchTargetMinPx - EdgeTolerancePx || r.height < TouchTargetMinPx - EdgeTolerancePx;

        /// <summary>U2: two targets closer than <see cref="TouchTargetMinGapPx"/> (containment pairs are not adjacent targets).</summary>
        public static bool TargetsTooClose(Rect a, Rect b, out float gap)
        {
            gap = Gap(a, b);
            if (Contains(a, b, 0f) || Contains(b, a, 0f)) return false;
            if (IntersectionArea(a, b) > 0f) gap = -1f;
            return gap < TouchTargetMinGapPx - EdgeTolerancePx;
        }

        /// <summary>The rest-state box of a hex whose centre is <paramref name="centre"/> at layout scale <paramref name="restScale"/>
        /// (1 on the thumb arc, 0.75 on the desktop strip; the transient held pop is not the layout).</summary>
        public static Rect HexRestRect(Vector2 centre, float restScale)
        {
            float h = HexHalfPx * restScale;
            return new Rect(centre.x - h, centre.y - h, 2f * h, 2f * h);
        }

        // ---- U3 / U4 -------------------------------------------------------------------------------------------------

        /// <summary>U3, single-line text: the measured text is wider than the content box.</summary>
        public static bool TextClipped(float measuredWidth, float contentWidth) =>
            measuredWidth > contentWidth + TextFitTolerancePx;

        /// <summary>U3, wrapping text measured at the content width: taller than the content box.</summary>
        public static bool WrappedTextClipped(float measuredHeight, float contentHeight) =>
            measuredHeight > contentHeight + TextFitTolerancePx;

        /// <summary>
        /// U3, wrapping text (WP9): clipped only when its one-line width does NOT fit the content box AND the wrapped
        /// text is taller than the box. A label sized to its own text (a centred class line, the solo hint) gets a box
        /// exactly its natural width; re-measured "wrap at exactly that width" a sub-pixel rounding breaks it onto two
        /// lines that the renderer never draws (it renders on one line, verified on the 2400x1080 COOP capture), so a
        /// text whose single line fits is never clipped.
        /// </summary>
        public static bool WrappedTextClipped(float naturalWidth, float contentWidth, float wrappedHeight, float contentHeight) =>
            TextClipped(naturalWidth, contentWidth) && WrappedTextClipped(wrappedHeight, contentHeight);

        // ---- occlusion (WP9) -----------------------------------------------------------------------------------------

        /// <summary>A layer this opaque (background alpha x inherited opacity) hides what was painted before it.</summary>
        public const float OccluderMinAlpha = 0.8f;

        /// <summary>
        /// A modal layer: a near-opaque background (the menu sheets' 0.88 scrim, GET READY's 0.94, the results / waiting room
        /// overlays) covering the whole safe area. Everything painted before it under its rect is not on screen, so U1-U5 do not
        /// judge it (the dimmed main menu under the Settings sheet is not "overlapping" the sheet). Only a layer that covers the
        /// whole safe area counts: a smaller opaque element over a label is exactly the U1 overlap the probe must report.
        /// </summary>
        public static bool IsOccluder(float effectiveAlpha, Rect layer, Rect safe) =>
            effectiveAlpha >= OccluderMinAlpha && IsMeasurable(layer) && IsMeasurable(safe) && Contains(layer, safe, EdgeTolerancePx);

        /// <summary>
        /// The part of <paramref name="r"/> a scroll viewport shows (WP9: the GEAR UP deck scrolls in portrait). A card cut by
        /// the viewport's edge is only drawn - and only takes taps - inside it, so U1 / U2 judge that part; <paramref name="cut"/>
        /// is true when the viewport cuts it, and a cut target is not judged on its size (half a card scrolled away is small
        /// on screen by design; its full size is judged when it is in view).
        /// </summary>
        public static Rect VisibleInViewport(Rect r, Rect viewport, out bool cut)
        {
            cut = !Contains(viewport, r, EdgeTolerancePx);
            if (!cut) return r;
            float xMin = Mathf.Max(r.xMin, viewport.xMin), yMin = Mathf.Max(r.yMin, viewport.yMin);
            float xMax = Mathf.Min(r.xMax, viewport.xMax), yMax = Mathf.Min(r.yMax, viewport.yMax);
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        /// <summary>An item painted before an occluding <paramref name="layer"/> is hidden when the layer covers all of it.</summary>
        public static bool HiddenUnder(Rect item, Rect layer) => Contains(layer, item, EdgeTolerancePx);

        public static float FontFloor(UiScope scope) => scope == UiScope.Menu ? MenuFontFloorPx : HudFontFloorPx;

        /// <summary>U4: <paramref name="effectivePx"/> (resolvedStyle.fontSize x world scale) is under <paramref name="floorPx"/>.</summary>
        public static bool BelowFontFloor(float effectivePx, float floorPx) => effectivePx < floorPx - FontTolerancePx;

        // ---- U5 -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The safe area (screen px, origin bottom-left, as <c>Screen.safeArea</c> / <c>SafeAreaSource.Current</c>) in panel
        /// space (origin top-left). ScaleWithScreenSize scales both axes alike, so a per-axis ratio is exact.
        /// </summary>
        public static Rect ScreenSafeToPanel(Rect safeScreen, float screenWidth, float screenHeight, Rect panel)
        {
            if (screenWidth <= 0f || screenHeight <= 0f || !IsMeasurable(panel)) return panel;
            float sx = panel.width / screenWidth, sy = panel.height / screenHeight;
            float xMin = panel.xMin + safeScreen.xMin * sx;
            float xMax = panel.xMin + safeScreen.xMax * sx;
            float yMin = panel.yMin + (screenHeight - safeScreen.yMax) * sy;
            float yMax = panel.yMin + (screenHeight - safeScreen.yMin) * sy;
            return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
        }

        // ---- U6 -------------------------------------------------------------------------------------------------------

        /// <summary>U6 for one host: exactly one orientation class, and the one <c>LayoutClasses</c> would pick for the panel.</summary>
        public static OrientationVerdict CheckOrientation(bool hasPortrait, bool hasLandscape, float panelWidth, float panelHeight)
        {
            if (!hasPortrait && !hasLandscape) return OrientationVerdict.Missing;
            if (hasPortrait && hasLandscape) return OrientationVerdict.Both;
            var shape = CluckWars.UI.LayoutClasses.Classify(panelWidth, panelHeight);
            if (!shape.Valid) return OrientationVerdict.Ok;   // an unmeasured panel decides nothing
            return shape.Portrait == hasPortrait ? OrientationVerdict.Ok : OrientationVerdict.Mismatch;
        }

        // ---- scope ----------------------------------------------------------------------------------------------------

        /// <summary>Hud only while a round is playable on the Game scene with the arena uncovered; the menu scene is Menu.</summary>
        public static UiScope ScopeFor(bool gameScene, bool roundActive, bool coverShown) =>
            !gameScene ? UiScope.Menu : roundActive && !coverShown ? UiScope.Hud : UiScope.Overlay;

        public static string ScopeName(UiScope s) => s == UiScope.Hud ? "hud" : s == UiScope.Overlay ? "overlay" : "menu";

        // ---- log lines ------------------------------------------------------------------------------------------------

        /// <summary><c>x,y,wxh</c> in whole panel px.</summary>
        public static string Fmt(Rect r) =>
            r.x.ToString("0", Inv) + "," + r.y.ToString("0", Inv) + "," + r.width.ToString("0", Inv) + "x" + r.height.ToString("0", Inv);

        /// <summary>
        /// The one Info line per scan. It starts with <c>ui offenders=N scope=hud|menu|overlay</c> (merge.ps1 keys on that
        /// prefix; only scope=hud is gated), then the page, the trigger, per-rule counts, allow-listed cases and the setup.
        /// </summary>
        public static string Summary(int offenders, UiScope scope, string page, string reason, int scan, int[] perRule,
            int allowed, int docs, int elements, float panelW, float panelH, Rect safePanel, float ms)
        {
            var sb = new StringBuilder(220);
            sb.Append("ui offenders=").Append(offenders.ToString(Inv))
              .Append(" scope=").Append(ScopeName(scope))
              .Append(" page=").Append(string.IsNullOrEmpty(page) ? "-" : page)
              .Append(" reason=").Append(reason ?? "-")
              .Append(" scan=").Append(scan.ToString(Inv));
            for (int i = 1; i <= 6; i++)
                sb.Append(" U").Append(i.ToString(Inv)).Append('=').Append((perRule != null && i < perRule.Length ? perRule[i] : 0).ToString(Inv));
            sb.Append(" allowed=").Append(allowed.ToString(Inv))
              .Append(" docs=").Append(docs.ToString(Inv))
              .Append(" elements=").Append(elements.ToString(Inv))
              .Append(" panel=").Append(panelW.ToString("0", Inv)).Append('x').Append(panelH.ToString("0", Inv))
              .Append(" orient=").Append(panelW < panelH ? "portrait" : "landscape")
              .Append(" safe=").Append(Fmt(safePanel))
              .Append(" ms=").Append(ms.ToString("0.00", Inv));
            return sb.ToString();
        }

        /// <summary><c>ui offender U2 scope=hud doc=TouchControls el=Ability2 [x,y,wxh] other=Ability3 [x,y,wxh] gap=10.0 (need >= 19)</c>.</summary>
        public static string OffenderLine(UiRule rule, UiScope scope, string page, string doc, string element, Rect a,
            string other, Rect b, string detail)
        {
            var sb = new StringBuilder(200);
            sb.Append("ui offender ").Append(rule.ToString())
              .Append(" scope=").Append(ScopeName(scope));
            if (!string.IsNullOrEmpty(page)) sb.Append(" page=").Append(page);
            sb.Append(" doc=").Append(doc ?? "-")
              .Append(" el=").Append(element ?? "-").Append(" [").Append(Fmt(a)).Append(']');
            if (other != null) sb.Append(" other=").Append(other).Append(" [").Append(Fmt(b)).Append(']');
            if (!string.IsNullOrEmpty(detail)) sb.Append(' ').Append(detail);
            return sb.ToString();
        }

        /// <summary>A stable key for the per-offender Warn rate limiter (rule + element + other).</summary>
        public static int OffenderKey(UiRule rule, string element, string other)
        {
            unchecked
            {
                int h = (int)rule * 397;
                h = h * 31 + StableHash(element);
                h = h * 31 + StableHash(other);
                return h;
            }
        }

        private static int StableHash(string s)
        {
            if (s == null) return 0;
            unchecked
            {
                int h = 23;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h;
            }
        }

        /// <summary>The page ids <c>-cw_menushots</c> accepts, in capture order.</summary>
        public static readonly string[] MenuPages = { "main", "settings", "disconnect", "class", "gear", "coop", "coopjoin" };

        /// <summary>
        /// <c>-cw_menushots main,class</c>: the listed pages in the given order, de-duplicated; a bare flag (value <c>1</c>),
        /// <c>all</c> or an empty value = every page. Unknown ids are reported and skipped.
        /// </summary>
        public static string[] ParseMenuPages(string value, System.Collections.Generic.ICollection<string> problems)
        {
            var v = (value ?? string.Empty).Trim();
            if (v.Length == 0 || v == "1" || string.Equals(v, "all", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
                return (string[])MenuPages.Clone();
            var list = new System.Collections.Generic.List<string>();
            foreach (var raw in v.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var id = raw.Trim().ToLowerInvariant();
                if (Array.IndexOf(MenuPages, id) < 0) { problems?.Add($"cw_menushots page '{raw.Trim()}' is not one of {string.Join("|", MenuPages)}; skipped"); continue; }
                if (!list.Contains(id)) list.Add(id);
            }
            return list.ToArray();
        }

        /// <summary><c>menu-&lt;page&gt;[-&lt;tag&gt;].png</c>.</summary>
        public static string MenuShotFileName(string page, string tag) =>
            "menu-" + page + (string.IsNullOrEmpty(tag) ? string.Empty : "-" + tag) + ".png";
    }
}
