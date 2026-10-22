using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The one place the UI decides "which layout am I in". UI Toolkit has no media queries, so every
    /// root that styles by screen shape carries these classes, set from its panel geometry
    /// (PORTRAIT_AND_LANDSCAPE_SPEC §1.1). The thresholds live only here, so the menu, the waiting room,
    /// the HUD top bar and the touch controls cannot disagree about the screen they are on.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>layout--portrait</c> when w &lt; h, else <c>layout--landscape</c>; exactly one is set.</item>
    /// <item><c>layout--tall</c> when h/w ≥ 1.95 (9:19.5 and taller phones held upright).</item>
    /// <item><c>layout--short</c> when h &lt; 1100 panel units (16:9 at 1080, 20:9 phones in landscape).</item>
    /// <item><c>layout--narrow</c> when 1 ≤ w/h &lt; 1.55 (4:3 tablets in landscape). A portrait screen is
    /// never "narrow": portrait has its own class and its own overrides.</item>
    /// </list>
    /// The panel's <see cref="GeometryChangedEvent"/> is the rotation event for UI (Unity has no reliable
    /// rotation callback). An unmeasured rect (zero or NaN, before the first layout pass) changes nothing.
    /// </remarks>
    public static class LayoutClasses
    {
        public const string Portrait  = "layout--portrait";
        public const string Landscape = "layout--landscape";
        public const string Tall      = "layout--tall";
        public const string Short     = "layout--short";
        public const string Narrow    = "layout--narrow";

        /// <summary>h/w at or above which a portrait screen is "tall".</summary>
        public const float TallAspect = 1.95f;
        /// <summary>Panel height below which pages compress their headers and titles.</summary>
        public const float ShortHeight = 1100f;
        /// <summary>w/h below which a landscape screen stacks its wide two-column pages.</summary>
        public const float NarrowAspect = 1.55f;

        /// <summary>The classes a panel of <paramref name="width"/> x <paramref name="height"/> carries.</summary>
        public readonly struct Shape
        {
            public readonly bool Valid, Portrait, Tall, Short, Narrow;
            public bool Landscape => Valid && !Portrait;

            public Shape(bool valid, bool portrait, bool tall, bool isShort, bool narrow)
            {
                Valid = valid; Portrait = portrait; Tall = tall; Short = isShort; Narrow = narrow;
            }
        }

        /// <summary>Pure classification; <see cref="Shape.Valid"/> is false for an unmeasured rect.</summary>
        public static Shape Classify(float width, float height)
        {
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f) return default;
            // The explicit (float) casts are load-bearing: the runtime may evaluate a float division at double precision,
            // and then 1950/1000 = 1.95 (double) compares BELOW 1.95f widened (1.9500000477), so a screen exactly on a
            // threshold flipped class. A cast forces the quotient to float, the precision the constants are written in.
            float wh = (float)(width / height);
            float hw = (float)(height / width);
            return new Shape(
                valid:    true,
                portrait: width < height,
                tall:     hw >= TallAspect,
                isShort:  height < ShortHeight,
                narrow:   wh >= 1f && wh < NarrowAspect);
        }

        /// <summary>Sets the classes for <paramref name="panelRect"/> on <paramref name="target"/>; an unmeasured rect is ignored.</summary>
        public static void Apply(VisualElement target, Rect panelRect)
        {
            if (target == null) return;
            var s = Classify(panelRect.width, panelRect.height);
            if (!s.Valid) return;
            target.EnableInClassList(Portrait, s.Portrait);
            target.EnableInClassList(Landscape, s.Landscape);
            target.EnableInClassList(Tall, s.Tall);
            target.EnableInClassList(Short, s.Short);
            target.EnableInClassList(Narrow, s.Narrow);
        }

        /// <summary>
        /// Keeps <paramref name="target"/> (default: <paramref name="measured"/> itself) classified from
        /// <paramref name="measured"/>'s geometry, now and on every geometry change. Measure a full-panel
        /// element (a UIDocument's root): a band such as the top bar is not the screen's shape. Safe to call
        /// again on a re-bind; the previous registration on <paramref name="measured"/> is replaced.
        /// </summary>
        /// <remarks>
        /// <b>One target per measured element.</b> The callback is registered once per <paramref name="measured"/>, so a
        /// second <c>Bind(measured, otherTarget)</c> replaces the first: only <c>otherTarget</c> keeps updating, and the
        /// first target keeps whatever classes it last had. To classify several elements from one root, bind the root itself and style with
        /// descendant selectors (<c>.layout--portrait .cw-x</c>), or measure a different element per target.
        /// </remarks>
        public static void Bind(VisualElement measured, VisualElement target = null)
        {
            if (measured == null) return;
            target ??= measured;
            measured.UnregisterCallback<GeometryChangedEvent, VisualElement>(OnGeometry);
            measured.RegisterCallback<GeometryChangedEvent, VisualElement>(OnGeometry, target);
            Apply(target, measured.layout);
        }

        private static void OnGeometry(GeometryChangedEvent evt, VisualElement target) => Apply(target, evt.newRect);
    }
}
