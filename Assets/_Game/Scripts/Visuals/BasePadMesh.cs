using UnityEngine;

namespace CluckWars.Visuals
{
    /// <summary>
    /// Flat meshes for a player base's pad (Phase 8 C S2 "D3 base kit"): the straw floor disc and the
    /// player-colour ring around it, both single-sided, facing +Y, at y = 0 in the base's local space.
    /// </summary>
    /// <remarks>
    /// The floor's UVs are in world metres / <see cref="TileMetres"/>, so the tileable straw texture repeats
    /// every 2 m at material tiling 1 whatever the pad's size. The ring straddles the deposit edge
    /// (<see cref="RingInner"/> .. <see cref="RingOuter"/> around <see cref="FloorRadius"/> = the deposit radius),
    /// so its centre line is the honest "you can bank from here" boundary. Baked once to the asset paths below
    /// (the prefab references the assets); the builders are kept so tests can check the assets still match.
    /// </remarks>
    public static class BasePadMesh
    {
        public const string FloorAssetPath = "Assets/_Game/Art/Meshes/BasePadFloor.asset";
        public const string RingAssetPath = "Assets/_Game/Art/Meshes/BasePadRing.asset";
        public const int Segments = 64;

        /// <summary>Straw floor radius: the deposit radius (<c>PlayerBase._depositRadius</c>, 2.5 m).</summary>
        public const float FloorRadius = 2.5f;
        /// <summary>Metres covered by one repeat of <c>straw_floor_tile.png</c>.</summary>
        public const float TileMetres = 2f;
        public const float RingInner = 2.35f;
        public const float RingOuter = 2.65f;

        public static Mesh BuildFloor()
        {
            int n = Segments;
            var v = new Vector3[n + 1];
            var nrm = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            var tris = new int[n * 3];

            v[0] = Vector3.zero; nrm[0] = Vector3.up; uv[0] = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                var p = new Vector3(Mathf.Cos(a) * FloorRadius, 0f, Mathf.Sin(a) * FloorRadius);
                v[1 + i] = p; nrm[1 + i] = Vector3.up; uv[1 + i] = new Vector2(p.x, p.z) / TileMetres;
            }
            int t = 0;
            for (int i = 0; i < n; i++)
            {
                // Clockwise seen from above = facing +Y in Unity.
                tris[t++] = 0; tris[t++] = 1 + (i + 1) % n; tris[t++] = 1 + i;
            }
            return Finish("BasePadFloor", v, nrm, uv, tris);
        }

        public static Mesh BuildRing()
        {
            int n = Segments, stride = n + 1;
            var v = new Vector3[2 * stride];
            var nrm = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            var tris = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                float a = 2f * Mathf.PI * i / n, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i] = new Vector3(c * RingInner, 0f, s * RingInner);
                v[stride + i] = new Vector3(c * RingOuter, 0f, s * RingOuter);
                nrm[i] = nrm[stride + i] = Vector3.up;
                uv[i] = new Vector2((float)i / n, 0f);
                uv[stride + i] = new Vector2((float)i / n, 1f);
            }
            int t = 0;
            for (int i = 0; i < n; i++)
            {
                int a = i, b = i + 1, c = stride + i, d = c + 1;
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
            return Finish("BasePadRing", v, nrm, uv, tris);
        }

        private static Mesh Finish(string name, Vector3[] v, Vector3[] nrm, Vector2[] uv, int[] tris)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
