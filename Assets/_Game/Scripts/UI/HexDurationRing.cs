using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The "ability is running" duration ring on a touch-HUD hex (Phase 6, A1): a hexagonal outline just
    /// outside the hex's rim that drains clockwise-from-the-top as the ability's remaining duration falls.
    /// A shape cue (the ring emptying), not a colour cue. Drawn with <see cref="Painter2D"/>; never takes a tap.
    /// </summary>
    /// <remarks>
    /// Geometry is the pointy-top hexagon the HUD hex sprite is: for a 150 px hex the vertices sit on a 75 px
    /// radius circle (top and bottom vertices touch the box). The ring is laid out larger than the hex
    /// (see <c>.cw-hex-ring</c> in TouchControls.uss) and centred on it, so it can stroke outside the hex's
    /// own bounds.
    /// </remarks>
    public sealed class HexDurationRing : VisualElement
    {
        public const string BaseClass = "cw-hex-ring";

        /// <summary>Hex circumradius (px) the ring strokes at: the hex's own 75 px plus its ink rim and a 1 px gap.</summary>
        public const float RingRadiusPx = 80f;

        private const float StrokePx = 7f;
        private static readonly Color Fill  = new Color(0.996f, 0.961f, 0.878f, 1f); // cream
        private static readonly Color Track = new Color(0.165f, 0.102f, 0.047f, 0.65f); // ink

        private float _fraction;

        public HexDurationRing()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList(BaseClass);
            generateVisualContent += OnGenerate;
        }

        /// <summary>Remaining fraction, 0..1. Repaints only when the drawn arc would visibly move (~0.5%).</summary>
        public void SetFraction(float fraction01)
        {
            float f = Mathf.Clamp01(fraction01);
            bool moved = Mathf.Abs(f - _fraction) >= 0.005f || ((f == 0f || f == 1f) && f != _fraction);
            if (!moved) return;
            _fraction = f;
            MarkDirtyRepaint();
        }

        /// <summary>Vertex <paramref name="i"/> (0 = top, then clockwise on screen, y down) of the pointy-top hexagon.</summary>
        public static Vector2 Vertex(int i, Vector2 center, float radius)
        {
            float a = Mathf.PI / 3f * (i % 6);
            return center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
        }

        /// <summary>The point <paramref name="fraction01"/> of the way round the hexagon's perimeter from the top vertex.</summary>
        public static Vector2 PerimeterPoint(float fraction01, Vector2 center, float radius)
        {
            float t = Mathf.Clamp01(fraction01) * 6f;
            int edge = Mathf.Min(5, Mathf.FloorToInt(t));
            return Vector2.Lerp(Vertex(edge, center, radius), Vertex(edge + 1, center, radius), t - edge);
        }

        private void OnGenerate(MeshGenerationContext mgc)
        {
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;

            Vector2 c = r.center;
            var p = mgc.painter2D;
            p.lineWidth = StrokePx;
            p.lineJoin = LineJoin.Round;

            // Track: the full hexagon, so "how much was there" stays readable under the draining arc.
            p.strokeColor = Track;
            p.BeginPath();
            p.MoveTo(Vertex(0, c, RingRadiusPx));
            for (int i = 1; i < 6; i++) p.LineTo(Vertex(i, c, RingRadiusPx));
            p.ClosePath();
            p.Stroke();

            if (_fraction <= 0f) return;

            // Remaining arc, from the top vertex clockwise.
            p.strokeColor = Fill;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(Vertex(0, c, RingRadiusPx));
            float t = _fraction * 6f;
            int whole = Mathf.Min(6, Mathf.FloorToInt(t));
            for (int i = 1; i <= whole; i++) p.LineTo(Vertex(i, c, RingRadiusPx));
            if (whole < 6) p.LineTo(PerimeterPoint(_fraction, c, RingRadiusPx));
            p.Stroke();
        }
    }
}
