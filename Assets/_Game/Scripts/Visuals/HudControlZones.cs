using System.Collections.Generic;
using CluckWars.Diagnostics;
using CluckWars.Input;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Where the in-match HUD's CONTROLS and INFORMATION panels sit, as viewport rectangles (origin bottom-left, 0..1), so
    /// world-anchored screen furniture (the off-screen rival chevron, rival nameplates) can stay out from under a thumb and
    /// from under the HUD. Phase 8 chunk D, Maestro-approved edge-arrow polish: the bottom-left arrow spot used to be inside
    /// the mobile joystick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The numbers are mirrored from <c>Assets/UI/Styles/TouchControls.uss</c> and <c>MatchTopBar.uss</c></b> (joystick
    /// base 80/80/280, the four 150 px hexes, the desktop strip, the status strip, the top band and its portrait override)
    /// because USS is not readable from C# at runtime. <c>Phase8ChunkDRivalArrowTests</c> parses those sheets and fails if
    /// either side drifts, so there is exactly one place to update on a re-layout.
    /// </para>
    /// <para>
    /// <b>Panel size.</b> The HUD panel is <c>Resources/PanelSettings.asset</c>: ScaleWithScreenSize, reference 1920x1080,
    /// MatchWidthOrHeight 0.5. UI Toolkit blends LINEARLY (unlike UGUI's CanvasScaler, which blends in log space): scale =
    /// (W/1920 + H/1080) / 2, so the panel's size in panel px depends on the aspect alone (2400x1080 gives 2133x960, a 9:20
    /// phone 776x1724, measured by the WP8 UiProbe). That is why zones are built from the camera aspect, not from a pixel size.
    /// </para>
    /// <para>
    /// <b>Two lists.</b> <see cref="Build"/> gives the CONTROL zones (joystick, hexes, desktop strip): a thumb needs them
    /// free, so a chevron slides out of them. <see cref="BuildInfoZones"/> gives the INFORMATION zones (WP7, decisions C6 / F6,
    /// both orientations): the top HUD band, and the status strip while it shows rows. Those cover the arena, so the screen
    /// minus the info zones is the "clear viewport" (<see cref="ScreenEdgeProjection.ClampToClearViewport"/>): a rival
    /// mostly under one of them loses its nameplate and gets a chevron at the zone's edge (<see cref="RivalIndicator"/>).
    /// The status strip used to be deliberately left out (CW-009, "not until a playtest shows overlap"); Maestro overrode
    /// that on the portrait evidence of an arrow drawn on the SLOW strip. The hold-hint plate is still not a zone: it shows
    /// only while a hex is held, for the first five holds.
    /// </para>
    /// </remarks>
    public static class HudControlZones
    {
        // PanelSettings.asset
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;
        public const float ReferenceMatch = 0.5f;

        // TouchControls.uss: .cw-joy-base { left: 80px; bottom: 80px; width: 280px; height: 280px }
        public const float JoystickLeftPx = 80f;
        public const float JoystickBottomPx = 80f;
        public const float JoystickSizePx = 280f;

        /// <summary>Portrait touch: <c>.layout--portrait .cw-joy-base { left: 40px }</c> (the 776 px panel of a 9:20 phone).</summary>
        public const float JoystickLeftPortraitPx = 40f;

        // TouchControls.uss: .cw-hex { width/height: 150px }
        public const float HexSizePx = 150f;

        /// <summary>The hex's duration ring (10 px) and Peck chain ring (20 px) are drawn outside its 150 px box; zones grow
        /// by the larger so the whole control, rings included, is covered.</summary>
        public const float ControlPadPx = 20f;

        /// <summary>Touch layout: (right, bottom) offsets in panel px of hex a1..a4, from <c>.cw-hex--aN</c>.</summary>
        public static readonly Vector2[] TouchHexRightBottomPx =
        {
            new Vector2(95f, 95f),
            new Vector2(142f, 361f),
            new Vector2(302f, 269f),
            new Vector2(365f, 95f),
        };

        /// <summary>Portrait touch layout: the 2 x 2 grid of <c>.layout--portrait .cw-hex--aN</c> (right, bottom).</summary>
        public static readonly Vector2[] TouchHexRightBottomPortraitPx =
        {
            new Vector2(80f, 95f),
            new Vector2(80f, 270f),
            new Vector2(254f, 270f),
            new Vector2(254f, 95f),
        };

        /// <summary>Desktop / pad layout: hex a1..a4 left edge relative to the zone's horizontal centre
        /// (<c>margin-left</c> in <c>.cw-hud--desktop .cw-hex--aN</c>), all at <see cref="DesktopStripBottomPx"/>.</summary>
        public static readonly float[] DesktopStripLeftFromCentrePx = { -279f, -143f, -7f, 129f };

        public const float DesktopStripBottomPx = 4f;

        // ---- Information zones (mirrored from MatchTopBar.uss / TouchControls.uss; Phase8ChunkDRivalArrowTests parses them) ----

        // MatchTopBar.uss: .cw-topbar { height: 330px } (border-box: the safe-area padding sits inside it).
        public const float TopBandLandscapePx = 330f;

        /// <summary>The bottom 40 px of the landscape bar's gradient: the fade, arena showing through (portrait's twin is
        /// <see cref="TopBandPortraitFadePx"/>).</summary>
        public const float TopBandLandscapeFadePx = 40f;

        /// <summary>
        /// <b>Decided (Maestro 2026-10-23, 1a): the landscape fade is NOT HUD</b>, so the info zone is 290 px, the fade excluded
        /// in both orientations like portrait's (WP7 had shipped the whole 330 px bar). This one line is the switch; the dev
        /// override <c>-cw_band 290|330</c> still compares the two. Known and accepted: the lower ~34 px of a full four-row board
        /// (20 + 24 + 4 x 70 = 324 px) hangs below the zone.
        /// </summary>
        public const bool LandscapeTopBandIncludesFade = false;

        /// <summary>
        /// Dev-tools-only runtime override of the landscape band (<c>-cw_band 290|330</c>, panel px; 0 = none), so one build can
        /// be screenshotted both ways. Ignored unless <see cref="DevTools.Available"/>; production keeps
        /// <see cref="LandscapeTopBandIncludesFade"/>.
        /// </summary>
        public static int DevBandOverride
        {
            get => _devBandOverride;
            set { _devBandOverride = value; _cacheAspect = -1f; }   // the cached info zones were built with the old band
        }
        private static int _devBandOverride;

        /// <summary>Height of the landscape top-band info zone in panel px: the dev override when set, else per <see cref="LandscapeTopBandIncludesFade"/>.</summary>
        public static float TopBandLandscapeZonePx =>
            _devBandOverride > 0 && DevTools.Available
                ? _devBandOverride
                : LandscapeTopBandIncludesFade ? TopBandLandscapePx : TopBandLandscapePx - TopBandLandscapeFadePx;

        // MatchTopBar.uss: .layout--portrait .cw-topbar-layout { min-height: 316px; padding-bottom: 40px }. The band is the
        // layout minus its 40 px fade (the gradient's tail, arena showing through), below the safe-area top inset.
        public const float TopBandPortraitLayoutPx = 316f;
        public const float TopBandPortraitFadePx = 40f;

        // TouchControls.uss: .cw-status-strip { left: 80px; bottom: 380px; width: 380px }, portrait width 280px; desktop / pad
        // left 40px bottom 60px. Each row is .cw-status-row { height: 62px; margin-top: 8px }, and the strip grows upward.
        public const float StatusStripLeftPx = 80f;
        public const float StatusStripBottomPx = 380f;
        public const float StatusStripWidthPx = 380f;
        public const float StatusStripPortraitWidthPx = 280f;
        /// <summary>Portrait touch: <c>.layout--portrait .cw-status-strip { left: 40px }</c>, over the moved joystick.</summary>
        public const float StatusStripPortraitLeftPx = 40f;
        public const float StatusStripDesktopLeftPx = 40f;
        public const float StatusStripDesktopBottomPx = 60f;
        public const float StatusRowPitchPx = 62f + 8f;

        /// <summary>HUD panel size in panel px for a screen of the given aspect (width / height).</summary>
        /// <remarks>UI Toolkit's ScaleWithScreenSize + MatchWidthOrHeight: scale = lerp(W/RefW, H/RefH, match), linear. With
        /// W = aspect x H and panelH = H / scale: panelH = 1 / ((1 - match) x aspect / RefW + match / RefH). Measured by the
        /// WP8 probe: 960x540 -> 1920x1080, 540x960 -> 923x1641, 540x1200 -> 776x1724, 2400x1080 -> 2133x960. (It used to be
        /// the area rule sqrt(RefW x RefH / aspect), which is UGUI's log-space blend, and put a 9:20 panel at 966x2147.)</remarks>
        public static Vector2 PanelSize(float aspect)
        {
            aspect = Mathf.Max(0.1f, aspect);
            float h = 1f / ((1f - ReferenceMatch) * aspect / ReferenceWidth + ReferenceMatch / ReferenceHeight);
            return new Vector2(aspect * h, h);
        }

        /// <summary>Builds the control zones, in viewport units, for <paramref name="mode"/> at <paramref name="aspect"/>.
        /// <paramref name="safeInsets"/> is Screen.safeArea as viewport fractions (left, bottom, right, top).</summary>
        public static void Build(List<Rect> into, HudDeviceMode mode, float aspect, Vector4 safeInsets)
        {
            into.Clear();
            Vector2 panel = PanelSize(aspect);
            float pw = panel.x, ph = panel.y;
            float L = safeInsets.x, B = safeInsets.y, R = safeInsets.z;
            float pad = ControlPadPx;

            float FromLeft(float px) => L + px / pw;
            float FromRight(float px) => 1f - R - px / pw;
            float FromBottom(float px) => B + px / ph;

            if (mode == HudDeviceMode.Touch)
            {
                bool portrait = aspect < 1f;
                float joyLeft = portrait ? JoystickLeftPortraitPx : JoystickLeftPx;
                into.Add(Rect.MinMaxRect(
                    FromLeft(joyLeft - pad), FromBottom(JoystickBottomPx - pad),
                    FromLeft(joyLeft + JoystickSizePx + pad), FromBottom(JoystickBottomPx + JoystickSizePx + pad)));

                foreach (var rb in portrait ? TouchHexRightBottomPortraitPx : TouchHexRightBottomPx)
                {
                    into.Add(Rect.MinMaxRect(
                        FromRight(rb.x + HexSizePx + pad), FromBottom(rb.y - pad),
                        FromRight(rb.x - pad), FromBottom(rb.y + HexSizePx + pad)));
                }
                return;
            }

            // Keyboard / mouse and gamepad: no joystick; the hexes are an information strip at the bottom centre.
            float centre = 0.5f * (L + (1f - R));
            foreach (float left in DesktopStripLeftFromCentrePx)
            {
                into.Add(Rect.MinMaxRect(
                    centre + (left - pad) / pw, FromBottom(DesktopStripBottomPx - pad),
                    centre + (left + HexSizePx + pad) / pw, FromBottom(DesktopStripBottomPx + HexSizePx + pad)));
            }
        }

        /// <summary>
        /// Builds the information zones, in viewport units: the top HUD band (always), then the status strip while it shows
        /// <paramref name="statusRows"/> &gt; 0 rows. Portrait is <paramref name="aspect"/> &lt; 1, the rule
        /// <c>LayoutClasses</c> uses (the panel has the screen's aspect). <paramref name="safeInsets"/> as in <see cref="Build"/>,
        /// with w = top.
        /// </summary>
        /// <remarks>The top band is the full four-row board even while the phone board is compact (two rows): the portrait
        /// layout's min-height keeps the gradient that size, as the landscape bar's fixed height does, so the zone matches what
        /// is drawn and does not move when a rank changes.</remarks>
        public static void BuildInfoZones(List<Rect> into, HudDeviceMode mode, float aspect, Vector4 safeInsets, int statusRows)
        {
            into.Clear();
            Vector2 panel = PanelSize(aspect);
            float pw = panel.x, ph = panel.y;
            float L = safeInsets.x, B = safeInsets.y, T = safeInsets.w;
            bool portrait = aspect < 1f;

            float bandBottom = portrait
                ? 1f - T - (TopBandPortraitLayoutPx - TopBandPortraitFadePx) / ph
                : 1f - TopBandLandscapeZonePx / ph;
            into.Add(Rect.MinMaxRect(0f, Mathf.Clamp01(bandBottom), 1f, 1f));

            if (statusRows <= 0) return;
            bool touch = mode == HudDeviceMode.Touch;
            float left = !touch ? StatusStripDesktopLeftPx : portrait ? StatusStripPortraitLeftPx : StatusStripLeftPx;
            float bottom = touch ? StatusStripBottomPx : StatusStripDesktopBottomPx;
            float width = portrait ? StatusStripPortraitWidthPx : StatusStripWidthPx;
            into.Add(Rect.MinMaxRect(
                L + left / pw, B + bottom / ph,
                L + (left + width) / pw, B + (bottom + statusRows * StatusRowPitchPx) / ph));
        }

        // ---- Runtime glue -------------------------------------------------------------------------

        private static readonly List<Rect> Cache = new List<Rect>(5);        // joystick + 4 hexes
        private static readonly List<Rect> InfoCache = new List<Rect>(2);    // top band + status strip
        private static readonly List<Rect> AvoidCache = new List<Rect>(7);
        private static HudDeviceMode _cacheMode = (HudDeviceMode)255;
        private static float _cacheAspect = -1f;
        private static Vector4 _cacheInsets = new Vector4(-1f, -1f, -1f, -1f);
        private static int _cacheStatusRows = -1;

        /// <summary>The live layout's HUD control zones for <paramref name="cam"/>. Cached; rebuilt only when the layout
        /// mode, the aspect, the safe area or the status strip's row count changes, so calling it per rival per frame
        /// allocates nothing.</summary>
        public static IReadOnlyList<Rect> Current(Camera cam)
        {
            Refresh(cam);
            return Cache;
        }

        /// <summary>The live information zones (<see cref="BuildInfoZones"/>); same cache as <see cref="Current"/>.</summary>
        public static IReadOnlyList<Rect> CurrentInfo(Camera cam)
        {
            Refresh(cam);
            return InfoCache;
        }

        /// <summary>Control zones followed by information zones: everything an edge chevron must stay out of.</summary>
        public static IReadOnlyList<Rect> CurrentAvoid(Camera cam)
        {
            Refresh(cam);
            return AvoidCache;
        }

        private static void Refresh(Camera cam)
        {
            float aspect = cam != null ? cam.aspect : 16f / 9f;
            var touch = TouchControlsController.Instance;
            HudDeviceMode mode = touch != null
                ? touch.Mode
                : (Application.isMobilePlatform ? HudDeviceMode.Touch : HudDeviceMode.KeyboardMouse);
            int statusRows = touch != null && touch.isActiveAndEnabled ? touch.StatusRowCount : 0;   // a disabled HUD draws no strip
            Vector4 insets = SafeInsets();

            if (mode == _cacheMode && Mathf.Approximately(aspect, _cacheAspect) && insets == _cacheInsets
                && statusRows == _cacheStatusRows) return;

            Build(Cache, mode, aspect, insets);
            BuildInfoZones(InfoCache, mode, aspect, insets, statusRows);
            AvoidCache.Clear();
            AvoidCache.AddRange(Cache);
            AvoidCache.AddRange(InfoCache);
            _cacheMode = mode; _cacheAspect = aspect; _cacheInsets = insets; _cacheStatusRows = statusRows;
        }

        /// <summary>The safe area (<see cref="CluckWars.UI.SafeAreaSource.Current"/>) as viewport fractions (left, bottom,
        /// right, top); zero on a screen with no cutout.</summary>
        private static Vector4 SafeInsets()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return Vector4.zero;
            Rect sa = CluckWars.UI.SafeAreaSource.Current;   // same source as the UI padding (dev -cw_safearea override)
            return new Vector4(
                Mathf.Clamp01(sa.xMin / Screen.width),
                Mathf.Clamp01(sa.yMin / Screen.height),
                Mathf.Clamp01((Screen.width - sa.xMax) / Screen.width),
                Mathf.Clamp01((Screen.height - sa.yMax) / Screen.height));
        }
    }
}
