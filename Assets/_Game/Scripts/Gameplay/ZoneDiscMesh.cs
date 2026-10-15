using UnityEngine;

namespace CluckWars.Gameplay
{
    /// <summary>
    /// Round mesh for the flat pile / base zone markers: the same shape and size as Unity's
    /// built-in Cylinder (radius 0.5, height 2, so <c>MapGenerator.AddZoneMarker</c>'s scale maths is
    /// unchanged) but with <see cref="Segments"/> sides instead of 20, so a 3-5 m disc reads as a
    /// circle rather than a polygon from the match camera.
    /// </summary>
    public static class ZoneDiscMesh
    {
        public const int Segments = 64;

        private static Mesh _shared;

        /// <summary>Lazily built, process-wide instance (rebuilt if Unity unloaded it).</summary>
        public static Mesh Shared
        {
            get
            {
                if (_shared == null)
                {
                    _shared = Build(Segments);
                    _shared.hideFlags = HideFlags.HideAndDontSave;
                }
                return _shared;
            }
        }

        /// <summary>Builds a capped cylinder, radius 0.5, y in [-1, 1], with hard-edged side faces.</summary>
        public static Mesh Build(int segments)
        {
            const float radius = 0.5f;
            const float halfHeight = 1f;
            int n = Mathf.Max(3, segments);

            // Layout: [top centre][top ring n][bottom centre][bottom ring n][side top n+1][side bottom n+1]
            var v = new Vector3[2 * (n + 1) + 2 * (n + 1)];
            var nrm = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            var tris = new int[(n * 3) * 2 + n * 6];

            int top = 0, bot = n + 1, sideTop = 2 * (n + 1), sideBot = sideTop + n + 1;
            v[top] = new Vector3(0f, halfHeight, 0f); nrm[top] = Vector3.up; uv[top] = new Vector2(0.5f, 0.5f);
            v[bot] = new Vector3(0f, -halfHeight, 0f); nrm[bot] = Vector3.down; uv[bot] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i <= n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                var rim = new Vector3(c * radius, 0f, s * radius);
                var cap = new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f);
                var outward = new Vector3(c, 0f, s);

                if (i < n)
                {
                    v[top + 1 + i] = rim + Vector3.up * halfHeight; nrm[top + 1 + i] = Vector3.up; uv[top + 1 + i] = cap;
                    v[bot + 1 + i] = rim + Vector3.down * halfHeight; nrm[bot + 1 + i] = Vector3.down; uv[bot + 1 + i] = cap;
                }
                v[sideTop + i] = rim + Vector3.up * halfHeight; nrm[sideTop + i] = outward; uv[sideTop + i] = new Vector2((float)i / n, 1f);
                v[sideBot + i] = rim + Vector3.down * halfHeight; nrm[sideBot + i] = outward; uv[sideBot + i] = new Vector2((float)i / n, 0f);
            }

            int t = 0;
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n;
                // Top cap: clockwise seen from above (+Y). Bottom cap: the reverse.
                tris[t++] = top; tris[t++] = top + b; tris[t++] = top + a;
                tris[t++] = bot; tris[t++] = bot + a; tris[t++] = bot + b;
                // Side quad, outward facing.
                tris[t++] = sideTop + i; tris[t++] = sideTop + i + 1; tris[t++] = sideBot + i;
                tris[t++] = sideBot + i; tris[t++] = sideTop + i + 1; tris[t++] = sideBot + i + 1;
            }

            var mesh = new Mesh { name = $"ZoneDisc{n}" };
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
