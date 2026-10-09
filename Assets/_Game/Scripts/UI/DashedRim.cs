using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// A dashed ellipse inscribed in its own rect: the rim of an empty pedestal ("a bird goes here"). USS has no
    /// dashed borders, so the dashes are stroked with Painter2D (cream, 5 px by default). The element never takes a tap.
    /// </summary>
    public sealed class DashedRim : VisualElement
    {
        /// <summary>Dashes around the whole ellipse (an even count, so dash and gap alternate).</summary>
        public const int DashCount = 26;
        /// <summary>Fraction of each dash step that is drawn (the rest is the gap).</summary>
        public const float DashFill = 0.58f;

        public Color RimColor { get; set; } = new Color(254f / 255f, 245f / 255f, 224f / 255f, 1f);
        public float RimWidth { get; set; } = 5f;

        public DashedRim()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("cw-dashed-rim");
            generateVisualContent += OnGenerate;
        }

        /// <summary>The start / end angles (radians) of dash <paramref name="i"/> out of <paramref name="count"/>.</summary>
        public static (float Start, float End) DashAngles(int i, int count, float fill)
        {
            float step = Mathf.PI * 2f / count;
            float start = i * step;
            return (start, start + step * fill);
        }

        private void OnGenerate(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width <= 1f || r.height <= 1f || float.IsNaN(r.width)) return;
            var p = mgc.painter2D;
            p.lineWidth = RimWidth;
            p.strokeColor = RimColor;
            p.lineCap = LineCap.Round;
            // Pedestal_Ring (512x128) is drawn scale-to-fit and centred in the pedestal element, so the rim is
            // placed in the art's own space: the top face's inner ring is centred (256, 54), 222 x 33 units.
            float scale = Mathf.Min(r.width / 512f, r.height / 128f);
            var origin = new Vector2(r.center.x - 256f * scale, r.center.y - 64f * scale);
            var c = origin + new Vector2(256f, 54f) * scale;
            float rx = 222f * scale, ry = 33f * scale;
            const int stepsPerDash = 4;
            for (int i = 0; i < DashCount; i++)
            {
                var (a0, a1) = DashAngles(i, DashCount, DashFill);
                p.BeginPath();
                for (int k = 0; k <= stepsPerDash; k++)
                {
                    float a = Mathf.Lerp(a0, a1, k / (float)stepsPerDash);
                    var pt = new Vector2(c.x + Mathf.Cos(a) * rx, c.y + Mathf.Sin(a) * ry);
                    if (k == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.Stroke();
            }
        }
    }
}
