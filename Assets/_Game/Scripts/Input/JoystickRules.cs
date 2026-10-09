using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>
    /// Pure rules of the touch move stick (Phase 6 chunk 4, A11): a 0.12 dead zone with a rescaled ramp, and a
    /// throw of at least 60 dp measured with the same dp conversion as <see cref="HoldCancelRules"/>.
    /// </summary>
    public static class JoystickRules
    {
        public const float DeadZone = 0.12f;
        public const float ThrowDp = 60f;

        /// <summary>The previous fixed throw in reference panel px (about 46 dp on a Pixel 9). The throw never
        /// goes below it, so a low-dpi screen keeps today's feel.</summary>
        public const float MinThrowPanelPx = 108f;

        /// <summary>Output ramps from 0 at the dead-zone edge to 1 at full deflection, direction kept.</summary>
        public static Vector2 ApplyDeadZone(Vector2 raw)
        {
            float mag = raw.magnitude;
            if (mag <= DeadZone) return Vector2.zero;
            float scaled = Mathf.Min(1f, (mag - DeadZone) / (1f - DeadZone));
            return raw / mag * scaled;
        }

        /// <summary>
        /// Throw radius in panel px: <see cref="ThrowDp"/> converted to physical pixels with the screen dpi
        /// (fallback 160) and then into the scaled panel's px, floored at <see cref="MinThrowPanelPx"/>.
        /// </summary>
        public static float ThrowRadiusPanelPx(float dpi, float panelWidth, float screenWidth)
        {
            float screenPx = ThrowDp * (HoldCancelRules.SanitiseDpi(dpi) / 160f);
            float panelPerScreen = panelWidth > 1f && screenWidth > 1f ? panelWidth / screenWidth : 1f;
            return Mathf.Max(MinThrowPanelPx, screenPx * panelPerScreen);
        }
    }
}
