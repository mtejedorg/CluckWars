using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C1 "Fence mesh regression": B2 decimated the fence panel 4000 -> 1642 tris and signed it off from an
    /// overview; at match-camera distance the post caps were hollow and the planks showed see-through slits. C1
    /// restored the 4000-tri mesh (the decimated candidates did not pass the match-camera see-through guardrail).
    /// These tests keep a lossy re-decimation from sneaking back in: tri-count floor, the pivot/bounds the map
    /// layout depends on, and a topology check (open-edge count on the welded mesh) that cracks at post caps would
    /// push up.
    /// </summary>
    public class Phase8ChunkC1Tests
    {
        private const string SourceFbx = "Assets/Generated/wall_segment.fbx";
        private const string InnerMesh = "Assets/_Game/Art/Meshes/wall_segment_inner.asset";

        private static Mesh Source() => AssetDatabase.LoadAllAssetsAtPath(SourceFbx).OfType<Mesh>().First();

        [Test]
        public void FenceMesh_IsTheFullDetailMesh()
        {
            var mesh = Source();
            int tris = mesh.triangles.Length / 3;
            StringAssert.StartsWith("wall_segment", mesh.name, "Unexpected mesh name " + mesh.name);
            Assert.GreaterOrEqual(tris, 3900,
                $"The fence mesh has {tris} tris. Decimating it again (B2: 1642 tris) opened hollow post caps and slits at match distance; " +
                "only ship a lighter mesh after the C1 match-camera guardrail (tools/ui_capture/capture_c1_fence.py) shows no added see-through.");
        }

        [Test]
        public void FenceMesh_KeepsPivotAndBounds()
        {
            var b = Source().bounds;
            Assert.AreEqual(-0.353f, b.min.x, 0.01f); Assert.AreEqual(0.352f, b.max.x, 0.01f);
            Assert.AreEqual(-1.001f, b.min.y, 0.01f); Assert.AreEqual(0.994f, b.max.y, 0.01f);
            Assert.AreEqual(-0.022f, b.min.z, 0.01f); Assert.AreEqual(1.463f, b.max.z, 0.01f);
        }

        [Test]
        public void FenceMesh_OpenEdgesPerTriangle_StayAtTheFullDetailLevel()
        {
            var mesh = Source();
            int open = CountOpenEdges(mesh);
            float perTri = open / (float)(mesh.triangles.Length / 3);
            // The AI-generated panel has interpenetrating parts, so it is not watertight: the 4000-tri original has about
            // 1.11 single-triangle edges per triangle (welded by position). The cracked 1642-tri decimation had 1.30.
            Assert.LessOrEqual(perTri, 1.2f, $"Open edges per triangle {perTri:0.00} ({open} open edges): the mesh looks cracked.");
        }

        [Test]
        public void InnerMesh_IsDerivedFromTheShippedFenceMesh()
        {
            var inner = AssetDatabase.LoadAssetAtPath<Mesh>(InnerMesh);
            var src = Source();
            Assert.AreEqual(src.vertexCount, inner.vertexCount, "wall_segment_inner must be regenerated whenever the fence mesh changes.");
            CollectionAssert.AreEqual(src.triangles, inner.triangles);
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
