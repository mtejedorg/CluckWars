using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Flat ground halo for the centre food pile (Phase 8 C S1 "warm glow"): an annulus in the XZ plane,
    /// in the pile's UNIT footprint space (the footprint oval is radius 0.5), whose vertex alpha carries
    /// the soft radial falloff. Drawn with an alpha-blended unlit particle material, so it needs no texture
    /// and, because it only ever blends toward a colour &lt;= 1, it can never push a pixel above the PC bloom
    /// threshold (1.0) or brighter than the glow colour itself on Mobile (no post there).
    /// </summary>
    /// <remarks>
    /// The inner edge is hidden under the mound (its outline is 0.41-0.495 of the footprint); the peak sits on
    /// the mound's foot and the pile disc's rim, and the falloff spreads onto the ground around it.
    /// Baked once to <see cref="AssetPath"/> (the prefab references the asset); <see cref="Build"/> is kept so
    /// the tests can check the asset still matches the profile.
    /// </remarks>
    public static class PileGlowRingMesh
    {
        public const string AssetPath = "Assets/_Game/Art/Meshes/PileGlowRing.asset";
        public const int Segments = 64;

        /// <summary>Ring radii (unit footprint space; 0.5 = the footprint oval / collider edge).</summary>
        public static readonly float[] Radii = { 0.38f, 0.45f, 0.50f, 0.56f, 0.63f, 0.71f, 0.80f };

        /// <summary>Vertex alpha at each of <see cref="Radii"/>: zero at both edges, peak at the mound's foot.</summary>
        public static readonly float[] Alphas = { 0f, 1f, 0.92f, 0.62f, 0.32f, 0.11f, 0f };

        public static Mesh Build()
        {
            int rings = Radii.Length, n = Segments, stride = n + 1;
            var v = new Vector3[rings * stride];
            var nrm = new Vector3[v.Length];
            var col = new Color[v.Length];
            var uv = new Vector2[v.Length];
            var tris = new int[(rings - 1) * n * 6];

            for (int r = 0; r < rings; r++)
            for (int i = 0; i <= n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                int k = r * stride + i;
                v[k] = new Vector3(Mathf.Cos(a) * Radii[r], 0f, Mathf.Sin(a) * Radii[r]);
                nrm[k] = Vector3.up;
                col[k] = new Color(1f, 1f, 1f, Alphas[r]);
                uv[k] = new Vector2((float)i / n, (float)r / (rings - 1));
            }

            int t = 0;
            for (int r = 0; r < rings - 1; r++)
            for (int i = 0; i < n; i++)
            {
                int a = r * stride + i, b = a + 1, c = a + stride, d = c + 1;
                // Facing +Y (clockwise seen from above, Unity's front face).
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }

            var mesh = new Mesh { name = "PileGlowRing" };
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.colors = col;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
