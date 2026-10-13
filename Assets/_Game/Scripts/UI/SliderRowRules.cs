using System.Globalization;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// Pure rules of the settings volume rows (Phase 7 B20): a press anywhere on the row drags the slider, and the row
    /// reads out a percentage next to its name. No UI Toolkit types, so every branch is EditMode-testable.
    /// </summary>
    public static class SliderRowRules
    {
        /// <summary>Minimum width of the draggable knob in panel px (~24 dp at this canvas): the touch-target floor.</summary>
        public const float MinThumbPx = 56f;

        /// <summary>
        /// Slider fraction (0..1) for a pointer at <paramref name="pointerX"/>. The knob centre travels from half a knob
        /// inside the track's left edge to half a knob inside its right edge, exactly like the slider's own dragging, so a
        /// press on the header above the track lands the knob under the finger. Clamped: a press left or right of the
        /// track pins the volume to 0 / 100%.
        /// </summary>
        public static float FractionFromPointer(float pointerX, float trackLeft, float trackWidth, float thumbWidth)
        {
            float travel = trackWidth - thumbWidth;
            if (travel <= 1f) return 0f;
            return Mathf.Clamp01((pointerX - trackLeft - thumbWidth * 0.5f) / travel);
        }

        /// <summary>The value for <paramref name="fraction"/> on a slider running <paramref name="low"/>..<paramref name="high"/>.</summary>
        public static float ValueFromFraction(float fraction, float low, float high) =>
            low + Mathf.Clamp01(fraction) * (high - low);

        /// <summary>"73%": the volume as a whole percentage, culture-independent (es-ES must not print "73 %").</summary>
        public static string Percent(float value01) =>
            Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
