using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Flat ground apron around a food pile's footprint ellipse (Phase 8 D14 "slow-band marking"): the band a chicken is
    /// pile-slowed in (0.8x within <c>FoodPile.CollectReach</c> of the surface). Built in WORLD METRES in the pile's local
    /// axes (local +X = the footprint's first component, the same frame as <c>PileFootprintShape</c>), so the band is the same
    /// width on every side of an oval; the visuals counter-scale the mesh against the pile root's X,Z footprint scale.
    /// </summary>
    /// <remarks>
    /// Rings run from just inside the footprint (hidden under the mound) to exactly <c>reach</c> past it, offset along the
    /// ellipse's outward normal (a true parallel curve, not a scaled copy). Vertex alpha carries the soft fade, so the outer
    /// edge is zero at the real slow-band limit. Pure and allocation-free after the first call (it refills the mesh).
    /// </remarks>
    public static class PileBandMesh
    {
        public const int Segments = 64;

        /// <summary>Ring offsets as fractions of the reach: a hair inside the footprint, then out to the band limit.</summary>
        public static readonly float[] OffsetFractions = { -0.05f, 0.5f, 0.85f, 1f };

        /// <summary>Vertex alpha at each of <see cref="OffsetFractions"/>: zero exactly at the band limit.</summary>
        public static readonly float[] Alphas = { 1f, 0.85f, 0.35f, 0f };

        /// <summary>Point on the ellipse (semi-axes a, b) at parameter t, pushed <paramref name="offset"/> along its outward normal.</summary>
        public static Vector2 OffsetPoint(float a, float b, float t, float offset)
        {
            float c = Mathf.Cos(t), s = Mathf.Sin(t);
            var n = new Vector2(b * c, a * s);
            float len = n.magnitude;
            n = len > 1e-6f ? n / len : new Vector2(c, s);
            return new Vector2(a * c, b * s) + n * offset;
        }

        /// <summary>(Re)fills <paramref name="mesh"/> for a footprint of world size <paramref name="footprint"/> (X, Z full extents) and a band of <paramref name="reach"/> metres.</summary>
        public static void Fill(Mesh mesh, Vector2 footprint, float reach)
        {
            int rings = OffsetFractions.Length, n = Segments, stride = n + 1;
            float a = Mathf.Max(footprint.x * 0.5f, 0.01f), b = Mathf.Max(footprint.y * 0.5f, 0.01f);
            var v = new Vector3[rings * stride];
            var nrm = new Vector3[v.Length];
            var col = new Color[v.Length];
            for (int r = 0; r < rings; r++)
            for (int i = 0; i <= n; i++)
            {
                int k = r * stride + i;
                var p = OffsetPoint(a, b, 2f * Mathf.PI * i / n, OffsetFractions[r] * reach);
                v[k] = new Vector3(p.x, 0f, p.y);
                nrm[k] = Vector3.up;
                col[k] = new Color(1f, 1f, 1f, Alphas[r]);
            }

            var tris = new int[(rings - 1) * n * 6];
            int t = 0;
            for (int r = 0; r < rings - 1; r++)
            for (int i = 0; i < n; i++)
            {
                int p0 = r * stride + i, p1 = p0 + 1, p2 = p0 + stride, p3 = p2 + 1;
                // Facing +Y (clockwise seen from above, Unity's front face).
                tris[t++] = p0; tris[t++] = p1; tris[t++] = p2;
                tris[t++] = p1; tris[t++] = p3; tris[t++] = p2;
            }

            mesh.Clear();
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.colors = col;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }
    }
}
