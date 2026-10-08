using UnityEngine;

namespace CluckWars.Input
{
    /// <summary>Which physical screen edge a held pointer is closest to.</summary>
    public enum ScreenEdge : byte { None = 0, Left = 1, Right = 2, Top = 3, Bottom = 4 }

    /// <summary>How a hex's pointer interaction ended.</summary>
    public enum HexPointerEnd : byte
    {
        /// <summary>PointerUpEvent: the finger lifted.</summary>
        Up = 0,
        /// <summary>PointerCancelEvent: the OS took the touch (system edge gesture, palm rejection).</summary>
        Cancel = 1,
        /// <summary>PointerCaptureOutEvent: the capture was lost abnormally.</summary>
        CaptureOut = 2,
    }

    /// <summary>What an ended hex interaction means for the move.</summary>
    public enum HexRelease : byte { Fire = 0, Cancel = 1 }

    /// <summary>
    /// Pure rules of the touch "edge-band cancel" (Phase 6, A4): while a hex is held, dragging the finger to
    /// within <see cref="ArmDp"/> of any PHYSICAL screen edge (raw position, not the safe area) arms the cancel;
    /// it only disarms once the finger is back beyond <see cref="DisarmDp"/> (hysteresis, so a finger riding the
    /// threshold does not flicker). Releasing while armed cancels. No Unity types beyond <see cref="Vector2"/>,
    /// so every branch is EditMode-testable.
    /// </summary>
    public static class HoldCancelRules
    {
        public const float ArmDp = 20f;
        public const float DisarmDp = 28f;

        /// <summary>1 dp = dpi / 160 px. Platforms that report no dpi (0, or negative) fall back to 160 (1 dp = 1 px).</summary>
        public const float FallbackDpi = 160f;

        public static float PxToDp(float px, float dpi) => px / (SanitiseDpi(dpi) / 160f);

        public static float SanitiseDpi(float dpi) => dpi > 1f ? dpi : FallbackDpi;

        /// <summary>Distance in screen pixels from a screen-space point (origin top-left, y down) to the nearest
        /// physical screen edge, and which edge that is.</summary>
        public static float NearestEdgePx(Vector2 screenPos, float screenWidth, float screenHeight, out ScreenEdge edge)
        {
            float left = screenPos.x;
            float right = screenWidth - screenPos.x;
            float top = screenPos.y;
            float bottom = screenHeight - screenPos.y;

            float best = left; edge = ScreenEdge.Left;
            if (right < best)  { best = right;  edge = ScreenEdge.Right; }
            if (top < best)    { best = top;    edge = ScreenEdge.Top; }
            if (bottom < best) { best = bottom; edge = ScreenEdge.Bottom; }
            return best;
        }

        /// <summary>Next armed state given the previous one and the finger's distance to the nearest edge in dp.
        /// Arms at or inside <see cref="ArmDp"/>; once armed, stays armed until beyond <see cref="DisarmDp"/>.</summary>
        public static bool NextArmed(bool wasArmed, float distanceDp) =>
            wasArmed ? distanceDp <= DisarmDp : distanceDp <= ArmDp;

        /// <summary>Convenience for the controller: position + screen size + dpi in, armed state and nearest edge out.</summary>
        public static bool Evaluate(bool wasArmed, Vector2 screenPos, float screenWidth, float screenHeight,
                                    float dpi, out ScreenEdge edge)
        {
            float px = NearestEdgePx(screenPos, screenWidth, screenHeight, out edge);
            return NextArmed(wasArmed, PxToDp(px, dpi));
        }

        /// <summary>
        /// What ending the interaction means. A deliberate lift fires unless the cancel is armed; a PointerCancel
        /// or a lost capture is an involuntary loss of tracking and is NEVER a fire.
        /// </summary>
        public static HexRelease ResolveRelease(HexPointerEnd end, bool armed)
        {
            if (end != HexPointerEnd.Up) return HexRelease.Cancel;
            return armed ? HexRelease.Cancel : HexRelease.Fire;
        }
    }
}
