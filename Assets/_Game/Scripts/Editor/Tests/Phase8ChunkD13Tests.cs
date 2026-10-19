using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CluckWars.EditorTools;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D13 "Rim paths": a base's rim path leaves the base 2+ m off both home fences, so the owner sees it past the near
    /// fence from the match camera, and only swings back into the rim gap (between the arm rim tip and the E fence) further
    /// down. Measured on the centreline the baker lays down (corner 0; the other three are rotated copies, see S3 4-fold test).
    /// </summary>
    public class Phase8ChunkD13Tests
    {
        /// <summary>The first stretch stays at least this far from each home fence face.</summary>
        private const float MinOffHomeFence = 2.0f;
        /// <summary>The first stretch is the part of the path this far (z, corner 0) above arm 0's rim tip.</summary>
        private const float FirstStretchAboveTip = 3.0f;
        /// <summary>Base pad radius (D17: the straw pad is 2.5 m); the path starts outside it.</summary>
        private const float PadRadius = 2.5f;

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

        [Test]
        public void RimPath_FirstStretch_StaysOffTheHomeFences_AndOutOfPadAndPiles()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            // Fence inner faces: the E wall's min x and the N wall's min z.
            var walls = map.Props.Cast<Transform>().Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^Wall_[NE]_\d+$")).ToList();
            float eInner = walls.Where(t => t.name.StartsWith("Wall_E_")).SelectMany(t => t.GetComponentsInChildren<Renderer>()).Min(r => r.bounds.min.x);
            float nInner = walls.Where(t => t.name.StartsWith("Wall_N_")).SelectMany(t => t.GetComponentsInChildren<Renderer>()).Min(r => r.bounds.min.z);

            var arm0 = map.Arms.First(a => a.Index == 0);
            float tipZ = arm0.Tip.z;
            var centre = GroundDressingBaker.CatmullRom(GroundDressingBaker.RoutePoints(map.Geometry, "Rim"), 0.2f);
            var first = centre.Where(p => p.y >= tipZ + FirstStretchAboveTip).ToList();
            Assert.Greater(first.Count, 15, "Expected a first stretch of several metres above the arm tip.");
            foreach (var p in first)
            {
                Assert.GreaterOrEqual(eInner - p.x, MinOffHomeFence, $"rim path ({p.x:F2}, {p.y:F2}) is {(eInner - p.x):F2} m off the E fence.");
                Assert.GreaterOrEqual(nInner - p.y, MinOffHomeFence, $"rim path ({p.x:F2}, {p.y:F2}) is {(nInner - p.y):F2} m off the N fence.");
            }

            // It still reaches the rim gap: at the tip's z it is between the tip and the fence.
            var atTip = centre.OrderBy(p => Mathf.Abs(p.y - tipZ)).First();
            Assert.Greater(atTip.x, arm0.Tip.x + 0.5f, $"rim path at the tip ({atTip.x:F2}, {atTip.y:F2}) is not in the gap beside arm 0's rim end ({arm0.Tip.x:F2}).");
            Assert.Less(atTip.x, eInner - 0.5f, $"rim path at the tip ({atTip.x:F2}) touches the fence ({eInner:F2}).");

            // Pad and piles (D6 rule kept): centreline outside the pad, and outside every pile oval.
            var baseT = map.Geometry.Find("BaseZone_0");
            var b = new Vector2(baseT.position.x, baseT.position.z);
            foreach (var p in centre)
                Assert.GreaterOrEqual(Vector2.Distance(p, b), PadRadius - 0.01f, $"rim path ({p.x:F2}, {p.y:F2}) is inside the straw pad.");
            foreach (Transform pile in map.Geometry)
            {
                if (!pile.name.StartsWith("PileZone_")) continue;
                var pc = new Vector2(pile.position.x, pile.position.z);
                foreach (var p in centre)
                {
                    var d = p - pc;
                    if (d.sqrMagnitude < 1e-6f) continue;
                    // The route ends PileEndGap outside its own pile's oval, never inside any.
                    Assert.GreaterOrEqual(d.magnitude, GroundDressingBaker.OvalRadius(pile, d.normalized) + GroundDressingBaker.PileEndGap - 0.02f,
                        $"rim path ({p.x:F2}, {p.y:F2}) is inside {pile.name}.");
                }
            }
        }

        [Test]
        public void BakedPathMesh_MatchesTheBaker_AndHasNoColliders()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var ground = EditorSceneManager.GetActiveScene().GetRootGameObjects().First(g => g.name == GroundDressingBaker.GroundRootName);
            var filters = ground.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name.StartsWith("DirtPaths_Base")).ToList();
            Assert.AreEqual(4, filters.Count);
            var fresh = GroundDressingBaker.BuildPathMesh(map.Geometry);
            foreach (var f in filters)
            {
                Assert.AreEqual(fresh.vertexCount, f.sharedMesh.vertexCount, $"{f.name}: baked path mesh is stale - re-run the Bake Paths + Decals + Vignettes menu.");
                Assert.IsEmpty(f.GetComponentsInChildren<Collider>(true), $"{f.name} has a collider: paths are visual only, the NavMesh must not see them.");
            }
        }
    }
}
