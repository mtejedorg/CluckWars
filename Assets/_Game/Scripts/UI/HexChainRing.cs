using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The "Peck is chaining" cue on the Peck hex (Phase 6 chunk 4, A6): a dashed hexagonal outline just outside
    /// the duration ring that slowly circles the hex while the auto-chain runs. Drawn with
    /// <see cref="Painter2D"/> in the same style as <see cref="HexDurationRing"/>; never takes a tap. Static
    /// (phase 0) under Reduced Motion.
    /// </summary>
    public sealed class HexChainRing : VisualElement
    {
        public const string BaseClass = "cw-hex-chain";

        /// <summary>Hex circumradius (px) the dashes sit at: outside <see cref="HexDurationRing.RingRadiusPx"/>.</summary>
        public const float RadiusPx = 92f;

        /// <summary>Dashes around the perimeter, and the share of each period that is ink.</summary>
        public const int DashCount = 18;
        public const float DashShare = 0.55f;

        /// <summary>Full laps per second.</summary>
        public const float LapsPerSecond = 0.5f;

        private const float StrokePx = 6f;
        private static readonly Color Ink = new Color(0.996f, 0.961f, 0.878f, 1f); // cream, like the duration ring

        private float _phase;

        public HexChainRing()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList(BaseClass);
            generateVisualContent += OnGenerate;
        }

        /// <summary>Advance the spin to <paramref name="time"/> (seconds). Reduced Motion pins the phase to 0.</summary>
        public void Spin(float time, bool reducedMotion)
        {
            float phase = reducedMotion ? 0f : Mathf.Repeat(time * LapsPerSecond, 1f);
            if (Mathf.Approximately(phase, _phase) && phase == 0f) return;
            _phase = phase;
            MarkDirtyRepaint();
        }

        /// <summary>Perimeter fraction (0..2, so a dash may cross the top vertex) to its point on the hexagon.</summary>
        private static Vector2 At(float fraction, Vector2 c) =>
            HexDurationRing.PerimeterPoint(Mathf.Repeat(fraction, 1f), c, RadiusPx);

        private void OnGenerate(MeshGenerationContext mgc)
        {
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;

            Vector2 c = r.center;
            var p = mgc.painter2D;
            p.lineWidth = StrokePx;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = Ink;

            float period = 1f / DashCount;
            for (int k = 0; k < DashCount; k++)
            {
                float a = (k + _phase * DashCount) * period;
                float b = a + period * DashShare;

                p.BeginPath();
                p.MoveTo(At(a, c));
                // Keep the dash on the hexagon's edges: pass through every vertex it spans.
                int firstVertex = Mathf.FloorToInt(a * 6f) + 1;
                for (int v = firstVertex; v / 6f < b; v++)
                    p.LineTo(HexDurationRing.Vertex(v % 6, c, RadiusPx));
                p.LineTo(At(b, c));
                p.Stroke();
            }
        }
    }
}
