using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C14 "Mesh holes": the AI-generated barrel / feed_silo / bush_round / hay_bale are soups of overlapping
    /// shells; cracks between the shells showed the grass behind them. The FBXs were welded (1 mm), their small boundary
    /// loops filled, faces only ever seen from behind were turned outward, and a closed inner liner (textured from the
    /// outer surface it sits behind) was added so any crack that remains shows prop colour instead of the ground.
    /// Object / mesh / material / UV names, pivot and the outer silhouette are unchanged (so fileIDs and .meta survive).
    /// These tests pin that: bounds, the open-edge count (welded by position, as in Phase8ChunkC1Tests) never rising
    /// back to the pre-fix value, and a tri budget so the liner cannot quietly grow.
    /// </summary>
    public class Phase8ChunkC14Tests
    {
        // name, bounds min, bounds max (Unity space, measured after the fix; the silhouette is the pre-fix one within 1 mm),
        // tris after, open edges after, open edges before the fix (welded by position at 1e-4).
        private static readonly (string name, Vector3 min, Vector3 max, int tris, int open, int openBefore)[] Props =
        {
            ("barrel",     new Vector3(-0.2187f, -0.2153f, 0.0017f), new Vector3(0.2148f, 0.2164f, 0.4844f), 1438, 1290, 1415),
            ("feed_silo",  new Vector3(-0.4210f, -0.5212f, 0.0151f), new Vector3(0.7059f, 0.5272f, 1.9977f), 1578, 1426, 1651),
            ("bush_round", new Vector3(-0.2630f, -0.2720f, 0.0027f), new Vector3(0.2697f, 0.2754f, 0.4079f), 2519, 2395, 2545),
            ("hay_bale",   new Vector3(-0.1902f, -0.1951f, 0.0000f), new Vector3(0.1897f, 0.1958f, 0.3085f), 1517, 1387, 1492),
        };
        private static readonly int[] TrisBefore = { 1233, 1311, 2297, 1350 };

        private static Mesh Load(string name) =>
            AssetDatabase.LoadAllAssetsAtPath($"Assets/Generated/{name}.fbx").OfType<Mesh>().First();

        [Test]
        public void Props_KeepMeshName_AndBounds()
        {
            foreach (var p in Props)
            {
                var mesh = Load(p.name);
                Assert.AreEqual(p.name, mesh.name, $"{p.name}: mesh name changed (prefab fileIDs depend on it).");
                var b = mesh.bounds;
                for (int a = 0; a < 3; a++)
                {
                    Assert.AreEqual(p.min[a], b.min[a], 0.0015f, $"{p.name}: bounds min axis {a}");
                    Assert.AreEqual(p.max[a], b.max[a], 0.0015f, $"{p.name}: bounds max axis {a}");
                }
            }
        }

        [Test]
        public void Props_OpenEdgesStayBelowTheBrokenMeshes()
        {
            foreach (var p in Props)
            {
                int open = CountOpenEdges(Load(p.name));
                Assert.Less(open, p.openBefore, $"{p.name}: {open} open edges, the cracked mesh had {p.openBefore}.");
                Assert.LessOrEqual(open, (int)(p.open * 1.03f) + 5, $"{p.name}: {open} open edges (fixed mesh {p.open}). Was the FBX re-exported without the C14 weld/fill?");
            }
        }

        [Test]
        public void Props_TriangleBudget_StaysCloseToTheFixedMesh()
        {
            for (int i = 0; i < Props.Length; i++)
            {
                var p = Props[i];
                int tris = Load(p.name).triangles.Length / 3;
                Assert.GreaterOrEqual(tris, TrisBefore[i], $"{p.name}: {tris} tris is below the pre-fix {TrisBefore[i]} (a decimation would reopen cracks).");
                Assert.LessOrEqual(tris, (int)(p.tris * 1.05f), $"{p.name}: {tris} tris (fixed mesh {p.tris}, +{p.tris - TrisBefore[i]} over the original).");
            }
        }

        [Test]
        public void PropPrefabs_ResolveMeshAndMaterial()
        {
            foreach (var p in Props)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Prefabs/Props/Prop_{p.name}.prefab");
                Assert.IsNotNull(prefab, $"Prop_{p.name}.prefab missing");
                var mf = prefab.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null && f.sharedMesh.name == p.name);
                Assert.IsNotNull(mf, $"Prop_{p.name}.prefab lost its {p.name} mesh reference");
                var mr = mf.GetComponent<MeshRenderer>();
                Assert.IsNotNull(mr);
                Assert.IsNotNull(mr.sharedMaterial, $"Prop_{p.name}.prefab lost its material");
            }
        }

        private static int CountOpenEdges(Mesh m)
        {
            var v = m.vertices; var tr = m.triangles;
            var weld = new Dictionary<(int, int, int), int>();
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = (Mathf.RoundToInt(v[i].x * 1e4f), Mathf.RoundToInt(v[i].y * 1e4f), Mathf.RoundToInt(v[i].z * 1e4f));
                if (!weld.TryGetValue(k, out int w)) { w = weld.Count; weld[k] = w; }
                id[i] = w;
            }
            var edges = new Dictionary<long, int>();
            for (int t = 0; t < tr.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = id[tr[t + e]], b = id[tr[t + (e + 1) % 3]];
                    if (a == b) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    edges[key] = edges.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            return edges.Values.Count(c => c == 1);
        }
    }
}
