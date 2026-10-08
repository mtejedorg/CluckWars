using System.Collections.Generic;
using CluckWars.Gameplay;
using UnityEngine;

namespace CluckWars.UI
{
    /// <summary>
    /// Pure geometry of the post-match podium (round-2 finding 6): how big each bird is drawn and
    /// where the winner's crown sits. No Unity objects beyond vectors, so EditMode tests cover it.
    /// </summary>
    /// <remarks>
    /// Each podium chicken element shows a square render (static hero PNG or the live stage texture)
    /// drawn <c>contain</c>, centred and bottom-aligned, so the bird is a square of side
    /// <c>min(width, height)</c> standing on the element's bottom edge. The steps have different
    /// heights, so those squares differ per step; <see cref="ChickenScales"/> evens them out and makes
    /// the winner exactly <see cref="WinnerScale"/> times the others (USS <c>scale</c>, origin bottom
    /// centre: the feet stay on the ring and nothing re-lays out).
    /// </remarks>
    public static class PodiumLayout
    {
        /// <summary>The winner is drawn this much bigger than every other bird on the podium.</summary>
        public const float WinnerScale = 1.25f;

        /// <summary>Fraction of the crown's height that sits below the top of the head (on the comb).</summary>
        public const float CrownSink = 0.38f;

        /// <summary>
        /// Top of the head (the comb) in the 1024 px <c>Hero_&lt;cls&gt;_cheer.png</c> renders the winner's
        /// step shows with Performance Mode ON, as (x from the left, y from the top) of the square.
        /// Measured from the PNGs' top-most opaque pixels; <c>PodiumLayoutTests</c> re-measures them, so a
        /// re-render that moves a head fails the suite instead of floating the crown.
        /// </summary>
        public static Vector2 StaticHeadAnchor(ChickenClass cls) => cls switch
        {
            ChickenClass.Speedy   => new Vector2(0.476f, 0.040f),
            ChickenClass.Fatty    => new Vector2(0.479f, 0.040f),
            ChickenClass.Assassin => new Vector2(0.508f, 0.040f),
            _                     => new Vector2(0.543f, 0.040f),   // Warrior
        };

        /// <summary>
        /// Top-left of the crown inside the chicken element (its parent), so that its centre is over
        /// the head and its lower <see cref="CrownSink"/> overlaps the comb.
        /// </summary>
        /// <param name="anchor">Head top as (x from left, y from top) of the drawn square, 0..1.</param>
        public static Vector2 CrownTopLeft(float elementW, float elementH, Vector2 anchor, float crownW, float crownH)
        {
            float side = Mathf.Min(elementW, elementH);
            float squareLeft = (elementW - side) * 0.5f;
            float squareTop = elementH - side;
            float headX = squareLeft + anchor.x * side;
            float headY = squareTop + anchor.y * side;
            return new Vector2(headX - crownW * 0.5f, headY - crownH * (1f - CrownSink));
        }

        /// <summary>
        /// USS scale per podium step so every bird is drawn at the same size and the winner at
        /// <see cref="WinnerScale"/> times that. <paramref name="drawnSides"/> holds each step's drawn
        /// square (<c>min(w, h)</c>); a step that is hidden or not laid out yet has 0 / NaN and gets 1.
        /// </summary>
        /// <param name="winnerSlot">The crowned step, or -1 (no winner: everyone at the common size).</param>
        public static float[] ChickenScales(IReadOnlyList<float> drawnSides, int winnerSlot)
        {
            var scales = new float[drawnSides.Count];
            float common = float.MaxValue;
            for (int i = 0; i < drawnSides.Count; i++)
            {
                scales[i] = 1f;
                if (Valid(drawnSides[i])) common = Mathf.Min(common, drawnSides[i]);
            }
            if (common == float.MaxValue) return scales;
            for (int i = 0; i < drawnSides.Count; i++)
            {
                if (!Valid(drawnSides[i])) continue;
                float target = i == winnerSlot ? common * WinnerScale : common;
                scales[i] = target / drawnSides[i];
            }
            return scales;
        }

        /// <summary>
        /// Sets a step's static size scale. Not motion (it never animates, so Reduced Motion has nothing
        /// to gate): it lives here so the overlay controller keeps no style.scale of its own
        /// (MenuJuiceGuardTests: motion goes through MenuJuice only).
        /// </summary>
        public static void ApplyScale(UnityEngine.UIElements.VisualElement chicken, float scale) =>
            chicken.style.scale = new UnityEngine.UIElements.Scale(new Vector3(scale, scale, 1f));

        private static bool Valid(float side) => !float.IsNaN(side) && side > 1f;
    }
}
