using System.Collections.Generic;
using CluckWars.Input;
using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Where the in-match HUD's CONTROLS sit, as viewport rectangles (origin bottom-left, 0..1), so world-anchored
    /// screen furniture (the off-screen rival chevron) can stay out from under a thumb. Phase 8 chunk D,
    /// Maestro-approved edge-arrow polish: the bottom-left arrow spot used to be inside the mobile joystick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The numbers are mirrored from <c>Assets/UI/Styles/TouchControls.uss</c></b> (joystick base 80/80/280, the four
    /// 150 px hexes, the desktop strip) because USS is not readable from C# at runtime. <c>Phase8ChunkDRivalArrowTests</c>
    /// parses that sheet and fails if either side drifts, so there is exactly one place to update on a re-layout.
    /// </para>
    /// <para>
    /// <b>Panel size.</b> The HUD panel is <c>Resources/PanelSettings.asset</c>: ScaleWithScreenSize, reference 1920x1080,
    /// match 0.5, so its scale is sqrt((W/1920) x (H/1080)) and its size in panel px depends on the aspect alone
    /// (2400x1080 gives 2147x966). That is why zones are built from the camera aspect, not from a pixel size.
    /// </para>
    /// <para>
    /// Only controls are zones. The status strip above the joystick and the hold-hint plate are transient information,
    /// not something a thumb needs free, and are deliberately left out.
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

        /// <summary>Desktop / pad layout: hex a1..a4 left edge relative to the zone's horizontal centre
        /// (<c>margin-left</c> in <c>.cw-hud--desktop .cw-hex--aN</c>), all at <see cref="DesktopStripBottomPx"/>.</summary>
        public static readonly float[] DesktopStripLeftFromCentrePx = { -279f, -143f, -7f, 129f };

        public const float DesktopStripBottomPx = 4f;

        /// <summary>HUD panel size in panel px for a screen of the given aspect (width / height).</summary>
        /// <remarks>scale^2 = (W/RefW) x (H/RefH) with W = aspect x H, and panelH = H / scale, so
        /// panelH^2 = RefW x RefH / aspect. Closed form only because <see cref="ReferenceMatch"/> is 0.5; the test pins the
        /// constant against the PanelSettings asset so a changed match value fails loudly instead of drifting.</remarks>
        public static Vector2 PanelSize(float aspect)
        {
            aspect = Mathf.Max(0.1f, aspect);
            float h = Mathf.Sqrt(ReferenceWidth * ReferenceHeight / aspect);
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
                into.Add(Rect.MinMaxRect(
                    FromLeft(JoystickLeftPx - pad), FromBottom(JoystickBottomPx - pad),
                    FromLeft(JoystickLeftPx + JoystickSizePx + pad), FromBottom(JoystickBottomPx + JoystickSizePx + pad)));

                foreach (var rb in TouchHexRightBottomPx)
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

        // ---- Runtime glue -------------------------------------------------------------------------

        private static readonly List<Rect> Cache = new List<Rect>(4);
        private static HudDeviceMode _cacheMode = (HudDeviceMode)255;
        private static float _cacheAspect = -1f;
        private static Vector4 _cacheInsets = new Vector4(-1f, -1f, -1f, -1f);

        /// <summary>The live layout's HUD control zones for <paramref name="cam"/>. Cached; rebuilt only when the layout
        /// mode, the aspect or the safe area changes, so calling it per rival per frame allocates nothing.</summary>
        public static IReadOnlyList<Rect> Current(Camera cam)
        {
            float aspect = cam != null ? cam.aspect : 16f / 9f;
            HudDeviceMode mode = TouchControlsController.Instance != null
                ? TouchControlsController.Instance.Mode
                : (Application.isMobilePlatform ? HudDeviceMode.Touch : HudDeviceMode.KeyboardMouse);
            Vector4 insets = SafeInsets();

            if (mode != _cacheMode || !Mathf.Approximately(aspect, _cacheAspect) || insets != _cacheInsets)
            {
                Build(Cache, mode, aspect, insets);
                _cacheMode = mode; _cacheAspect = aspect; _cacheInsets = insets;
            }
            return Cache;
        }

        /// <summary>Screen.safeArea as viewport fractions (left, bottom, right, top); zero on a screen with no cutout.</summary>
        private static Vector4 SafeInsets()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return Vector4.zero;
            Rect sa = Screen.safeArea;
            return new Vector4(
                Mathf.Clamp01(sa.xMin / Screen.width),
                Mathf.Clamp01(sa.yMin / Screen.height),
                Mathf.Clamp01((Screen.width - sa.xMax) / Screen.width),
                Mathf.Clamp01((Screen.height - sa.yMax) / Screen.height));
        }
    }
}
