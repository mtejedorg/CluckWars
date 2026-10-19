using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D12 "Overlaps": nothing placed outside the fence sits in the hedge, and a scarecrow keeps clear of its base's
    /// coop. Both are measured on the shipped <c>Map.unity</c> renderer bounds (what the camera sees), not on the bakers' tables.
    /// </summary>
    public class Phase8ChunkD12Tests
    {
        /// <summary>Every prop in the fence-hedge gap stays this far (XZ, m) from the HedgeStrip bounds.</summary>
        private const float HedgeMargin = 0.2f;
        /// <summary>A scarecrow's renderer bounds stay this far (XZ, m) from its coop's renderer bounds.</summary>
        private const float CoopMargin = 0.3f;

        private string _originalScenePath;

        [SetUp]
        public void CaptureOpenScene() => _originalScenePath = SceneManager.GetActiveScene().path;

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        private static Bounds Grown(Bounds b, float margin)
        {
            b.Expand(new Vector3(margin * 2f, 0f, margin * 2f));
            return b;
        }

        /// <summary>True when the boxes overlap in XZ (height is ignored: a hedge crown still hides what stands in it).</summary>
        private static bool OverlapsXZ(Bounds a, Bounds b) =>
            a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;

        [Test]
        public void ShippedMap_OutsideProps_AreClearOfTheHedgeStrips()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var hedges = map.Surroundings.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.sharedMesh.name == "HedgeStrip")
                .Select(f => (f.name, Bounds: f.GetComponent<Renderer>().bounds)).ToList();
            Assert.AreEqual(4, hedges.Count, "Expected the four HedgeStrip renderers (Hedge_N/S/E/W).");

            var offenders = new List<string>();
            int checkedCount = 0;
            foreach (var f in new[] { map.Props, map.Surroundings }.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)))
            {
                var mesh = f.sharedMesh;
                var rend = f.GetComponent<Renderer>();
                if (mesh == null || rend == null || !rend.enabled) continue;
                if (mesh.name == "HedgeStrip" || mesh.name.StartsWith("GroundDecal")) continue; // the hedge itself; flat alpha decals
                var b = rend.bounds;
                // Ground / surround planes are not props; only things that can stand in the gap are checked.
                if (Mathf.Max(b.size.x, b.size.z) > map.ArenaHalf) continue;
                // Outside the fence: any part of it past the arena.
                if (Mathf.Max(Mathf.Abs(b.max.x), Mathf.Abs(b.min.x), Mathf.Abs(b.max.z), Mathf.Abs(b.min.z)) <= map.ArenaHalf) continue;
                checkedCount++;
                foreach (var h in hedges)
                    if (OverlapsXZ(Grown(b, HedgeMargin), h.Bounds))
                        offenders.Add($"{f.transform.parent.name}/{f.name} ({mesh.name}) bounds {b.min:F2}..{b.max:F2} is within {HedgeMargin} m of {h.name} {h.Bounds.min:F2}..{h.Bounds.max:F2}");
            }
            Assert.Greater(checkedCount, 30, "Expected the outside vignettes, silos and barn to be checked.");
            Assert.IsEmpty(offenders, "Outside props clip into the hedge:\n" + string.Join("\n", offenders));
        }

        [Test]
        public void ShippedMap_Scarecrows_DoNotTouchTheirCoop()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var crows = map.PropRoots.Where(p => Phase8ChunkA5Tests.MeshOf(p) == "scarecrow").ToList();
            var coops = map.PropRoots.Where(p => Phase8ChunkA5Tests.MeshOf(p) == "chicken_coop").ToList();
            Assert.AreEqual(4, crows.Count, "One scarecrow per base.");
            Assert.AreEqual(4, coops.Count, "One coop per base.");
            foreach (var s in crows)
            {
                var sb = s.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                foreach (var c in coops)
                {
                    var cb = c.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                    Assert.IsFalse(OverlapsXZ(Grown(sb, CoopMargin), cb),
                        $"{s.name} bounds {sb.min:F2}..{sb.max:F2} are within {CoopMargin} m of {c.name} {cb.min:F2}..{cb.max:F2}.");
                }
            }
        }
    }
}
