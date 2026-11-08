using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Pixel metrics on the shipped icon PNG bytes (read from disk, not the imported texture), shared by
    /// <see cref="AbilityIconArtTests"/> and <see cref="StateGlyphArtTests"/>. The locked icon style is a
    /// cream fill inside a thick ink outline, so "ink" = an opaque pixel with r+g+b &lt; 300 (the kept set's
    /// ink is #2A1A0C, its cream #FEF5E0, anti-aliasing is quantised between them).
    /// </summary>
    internal static class IconStrokeMetrics
    {
        /// <summary>Diameter of the stroke probe at 256 px: an ink feature thinner than this does not survive.</summary>
        public const int OpenDiameter = 9;

        public sealed class Masks
        {
            public int W, H;
            public bool[] Opaque, Ink;
        }

        public static Masks Load(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
                var px = tex.GetPixels32();
                var m = new Masks { W = tex.width, H = tex.height, Opaque = new bool[px.Length], Ink = new bool[px.Length] };
                for (int i = 0; i < px.Length; i++)
                {
                    m.Opaque[i] = px[i].a > 127;
                    m.Ink[i] = m.Opaque[i] && px[i].r + px[i].g + px[i].b < 300;
                }
                return m;
            }
            finally { Object.DestroyImmediate(tex); }
        }

        private static List<Vector2Int> Disk(int diameter)
        {
            int r = diameter / 2;                       // 9 -> radius 4, x^2+y^2 <= r^2+r (a round 9 px disc)
            var d = new List<Vector2Int>();
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r + r) d.Add(new Vector2Int(x, y));
            return d;
        }

        /// <summary>
        /// Fraction of the ink pixels that survive a morphological open (erode then dilate) with a
        /// <paramref name="diameter"/> px disc, scaled to the icon's size relative to 256. 1 = every ink
        /// stroke is at least that thick; hairlines and thin dashes drag it down.
        /// </summary>
        public static float InkKeptByOpen(Masks m, int diameter = OpenDiameter)
        {
            int d = Mathf.Max(1, Mathf.RoundToInt(diameter * m.W / 256f));
            if (d % 2 == 0) d++;
            var disk = Disk(d);
            int w = m.W, h = m.H, total = 0;
            var eroded = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!m.Ink[y * w + x]) continue;
                    total++;
                    bool fits = true;
                    foreach (var o in disk)
                    {
                        int xx = x + o.x, yy = y + o.y;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h || !m.Ink[yy * w + xx]) { fits = false; break; }
                    }
                    eroded[y * w + x] = fits;
                }
            if (total == 0) return 0f;
            var opened = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!eroded[y * w + x]) continue;
                    foreach (var o in disk)
                    {
                        int xx = x + o.x, yy = y + o.y;
                        if (xx >= 0 && yy >= 0 && xx < w && yy < h) opened[yy * w + xx] = true;
                    }
                }
            int kept = 0;
            for (int i = 0; i < opened.Length; i++) if (opened[i] && m.Ink[i]) kept++;
            return kept / (float)total;
        }

        /// <summary>
        /// Speed-line detector: 4-connected components of the opaque, ink and cream masks that are thin
        /// horizontal bars (height &lt;= 36 px, width &gt;= 2x height and &gt;= 12 px at 256) centred in the
        /// left half, i.e. trailing dashes behind a bird facing right. Returns one "mask w x h @ cx" per hit.
        /// </summary>
        public static List<string> ThinHorizontalBarsLeftHalf(Masks m)
        {
            var hits = new List<string>();
            float k = m.W / 256f;
            var cream = new bool[m.Opaque.Length];
            for (int i = 0; i < cream.Length; i++) cream[i] = m.Opaque[i] && !m.Ink[i];
            foreach (var (name, mask) in new[] { ("opaque", m.Opaque), ("ink", m.Ink), ("cream", cream) })
            {
                var seen = new bool[mask.Length];
                var stack = new Stack<int>();
                for (int s = 0; s < mask.Length; s++)
                {
                    if (!mask[s] || seen[s]) continue;
                    int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
                    seen[s] = true; stack.Push(s);
                    while (stack.Count > 0)
                    {
                        int p = stack.Pop(), x = p % m.W, y = p / m.W;
                        x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                        if (x > 0) Visit(p - 1); if (x < m.W - 1) Visit(p + 1);
                        if (y > 0) Visit(p - m.W); if (y < m.H - 1) Visit(p + m.W);
                    }
                    int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
                    float cx = (x0 + x1 + 1) / 2f;
                    if (bh <= 36 * k && bw >= 2 * bh && bw >= 12 * k && cx < m.W / 2f)
                        hits.Add($"{name} {bw}x{bh} @ x {cx:0}");

                    void Visit(int q)
                    {
                        if (mask[q] && !seen[q]) { seen[q] = true; stack.Push(q); }
                    }
                }
            }
            return hits;
        }
    }
}
