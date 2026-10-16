using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Derives an arm-wall ("inner") fence mesh from a fence panel mesh with the Phase 8 B4 UV rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The 80 <c>InnerWall_*</c> arm walls are the fence panel with its mesh child squashed to
    /// <see cref="ThicknessScale"/> in X, which squashes the texture on every face that spans the thickness
    /// (post and rail tops, bottoms, ends). The inner mesh is the SAME vertices and triangles with each UV island
    /// made of (almost) only such faces compressed by <see cref="ThicknessScale"/> along its own thickness
    /// direction in UV space, so those faces sample the atlas at the same density as the rest. Object scale and
    /// vertices are untouched. The rule is byte-for-byte what produced the shipped
    /// <c>wall_segment_inner.asset</c> (<c>Phase8ChunkS5Tests</c> pins that), so the Performance Mode fence
    /// (Phase 8 S5, <c>wall_segment_perf.fbx</c> -> <c>wall_segment_inner_perf.asset</c>) gets the identical rule.
    /// </para>
    /// </remarks>
    public static class FenceInnerMeshBuilder
    {
        /// <summary>The arm walls' mesh-child X scale in <c>Prop_wall_segment.prefab</c>.</summary>
        public const float ThicknessScale = 0.35f;

        /// <summary>An island is compressed when this fraction of its area faces away from the X axis.</summary>
        public const float SpanFraction = 0.9f;

        /// <summary>A face "spans the thickness" when |normal.x| is below this.</summary>
        public const float MaxNormalX = 0.35f;

        public const string DefaultSource = "Assets/Generated/wall_segment.fbx";
        public const string DefaultInner = "Assets/_Game/Art/Meshes/wall_segment_inner.asset";
        public const string PerfSource = "Assets/Generated/wall_segment_perf.fbx";
        public const string PerfInner = "Assets/_Game/Art/Meshes/wall_segment_inner_perf.asset";

        [MenuItem("Cluck Wars/Map/Rebuild Fence Inner Meshes (default + Performance Mode)", priority = 24)]
        public static void RebuildAll()
        {
            Write(DefaultSource, DefaultInner);
            Write(PerfSource, PerfInner);
        }

        /// <summary>Rebuilds <paramref name="dstAsset"/> (created when missing, overwritten in place otherwise so
        /// every reference to it survives) from the first mesh of <paramref name="srcFbx"/>.</summary>
        public static Mesh Write(string srcFbx, string dstAsset)
        {
            var src = AssetDatabase.LoadAllAssetsAtPath(srcFbx).OfType<Mesh>().FirstOrDefault();
            if (src == null) throw new System.InvalidOperationException("No mesh in " + srcFbx);

            var built = Build(src);
            built.name = System.IO.Path.GetFileNameWithoutExtension(dstAsset);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(dstAsset);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(built, dstAsset);
                existing = built;
            }
            else
            {
                EditorUtility.CopySerialized(built, existing);
                existing.name = built.name;
                Object.DestroyImmediate(built);
                EditorUtility.SetDirty(existing);
            }
            AssetDatabase.SaveAssets();
            return existing;
        }

        /// <summary>The inner mesh for <paramref name="src"/>: same geometry, thickness-spanning UV islands compressed.</summary>
        public static Mesh Build(Mesh src) => Build(src, SpanFraction, MaxNormalX, out _, out _);

        public static Mesh Build(Mesh src, float spanFrac, float maxNx, out int islandsCompressed, out int islands)
        {
            var v = src.vertices; var uv = src.uv; var tr = src.triangles; int nT = tr.Length / 3;

            // Weld by position + uv, then union by triangle: one component per UV island.
            var key = new Dictionary<(int, int, int, int, int), int>(); var vid = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = (Mathf.RoundToInt(v[i].x * 1e4f), Mathf.RoundToInt(v[i].y * 1e4f), Mathf.RoundToInt(v[i].z * 1e4f),
                         Mathf.RoundToInt(uv[i].x * 1e5f), Mathf.RoundToInt(uv[i].y * 1e5f));
                if (!key.TryGetValue(k, out int id)) { id = key.Count; key[k] = id; }
                vid[i] = id;
            }
            var par = Enumerable.Range(0, key.Count).ToArray();
            for (int t = 0; t < nT; t++)
            {
                int a = Find(par, vid[tr[t * 3]]), b = Find(par, vid[tr[t * 3 + 1]]), c = Find(par, vid[tr[t * 3 + 2]]);
                par[b] = a; par[Find(par, c)] = Find(par, a);
            }
            var faces = new Dictionary<int, List<int>>();
            for (int t = 0; t < nT; t++)
            {
                int r = Find(par, vid[tr[t * 3]]);
                if (!faces.TryGetValue(r, out var list)) faces[r] = list = new List<int>();
                list.Add(t);
            }

            var newUv = (Vector2[])uv.Clone(); islandsCompressed = 0; islands = faces.Count;
            foreach (var kv in faces)
            {
                float span = 0, tot = 0;
                foreach (int t in kv.Value)
                {
                    var cr = Vector3.Cross(v[tr[t * 3 + 1]] - v[tr[t * 3]], v[tr[t * 3 + 2]] - v[tr[t * 3]]);
                    float area = cr.magnitude * 0.5f; tot += area;
                    if (Mathf.Abs(cr.normalized.x) < maxNx) span += area;
                }
                if (span < spanFrac * tot) continue;

                var vs = new HashSet<int>();
                foreach (int t in kv.Value) { vs.Add(tr[t * 3]); vs.Add(tr[t * 3 + 1]); vs.Add(tr[t * 3 + 2]); }
                var idx = vs.ToList(); int n = idx.Count; if (n < 3) continue;

                // Least squares uv = A p + b: the UV image of the X axis is A's first column.
                Vector3 pc = Vector3.zero; Vector2 uc = Vector2.zero;
                foreach (int i in idx) { pc += v[i]; uc += uv[i]; }
                pc /= n; uc /= n;
                var m = new float[3, 3]; float[] bu = new float[3], bv = new float[3];
                foreach (int i in idx)
                {
                    var p = v[i] - pc; var q = uv[i] - uc; float[] pp = { p.x, p.y, p.z };
                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = 0; c < 3; c++) m[r, c] += pp[r] * pp[c];
                        bu[r] += pp[r] * q.x; bv[r] += pp[r] * q.y;
                    }
                }
                for (int r = 0; r < 3; r++) m[r, r] += 1e-9f;
                var au = Solve(m, bu); var av = Solve(m, bv); if (au == null) continue;
                var d = new Vector2(au[0], av[0]); if (d.magnitude < 1e-7f) continue; d.Normalize();

                foreach (int i in idx)
                {
                    var q = uv[i] - uc; float al = Vector2.Dot(q, d);
                    newUv[i] = uc + q - (1f - ThicknessScale) * al * d;
                }
                islandsCompressed++;
            }

            // Vertices split off the welded id (same position + uv) follow their twin.
            var moved = new Dictionary<int, Vector2>();
            for (int i = 0; i < v.Length; i++) if (newUv[i] != uv[i]) moved[vid[i]] = newUv[i];
            for (int i = 0; i < v.Length; i++) if (moved.TryGetValue(vid[i], out var nu)) newUv[i] = nu;

            var result = Object.Instantiate(src);
            result.uv = newUv;
            return result;
        }

        private static int Find(int[] p, int i) { while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; } return i; }

        private static float[] Solve(float[,] m, float[] b)
        {
            var a = new float[3, 4];
            for (int r = 0; r < 3; r++) { for (int c = 0; c < 3; c++) a[r, c] = m[r, c]; a[r, 3] = b[r]; }
            for (int i = 0; i < 3; i++)
            {
                int p = i;
                for (int r = i + 1; r < 3; r++) if (Mathf.Abs(a[r, i]) > Mathf.Abs(a[p, i])) p = r;
                if (Mathf.Abs(a[p, i]) < 1e-12f) return null;
                for (int c = 0; c < 4; c++) { var t = a[i, c]; a[i, c] = a[p, c]; a[p, c] = t; }
                for (int r = 0; r < 3; r++)
                    if (r != i) { float f = a[r, i] / a[i, i]; for (int c = i; c < 4; c++) a[r, c] -= f * a[i, c]; }
            }
            return new[] { a[0, 3] / a[0, 0], a[1, 3] / a[1, 1], a[2, 3] / a[2, 2] };
        }
    }
}
