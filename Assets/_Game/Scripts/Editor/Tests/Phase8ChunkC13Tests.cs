using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.EditorTools;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C13 "Arm wall joints": neighbouring arm wall panels (<c>MapProps/InnerWall_{arm}_*</c>)
    /// share a post instead of standing two posts side by side with rail stubs between them, and
    /// no run's art reaches more than 0.1 m past its collider's ends.
    /// </summary>
    /// <remarks>
    /// The panel mesh's posts stand ~14 cm in from its ends; the rails run on past them as stubs.
    /// Posts are measured here from the panels' world-space vertices (the only geometry in the top
    /// 8 % of the panel's height), independently of <see cref="MapPropsRescaler"/>'s own measuring.
    /// </remarks>
    public class Phase8ChunkC13Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const float PostCentreTolerance = 0.02f;
        private const float PostOverlapTolerance = 0.01f;
        private const float EndOverhang = 0.1f;

        private static readonly Regex ArmCollider = new Regex(@"^Terrain_Standard_([0-9]+)$");
        private static readonly Regex ArmPanel = new Regex(@"^InnerWall_([0-9]+)_[0-9]+$");

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
        public void SolveArmPanels_SharedPosts_CoverTheLengthExactly_WithTheLeastStretch()
        {
            // Shipped arm: 14.7634 m collider, posts 1.40 m apart, 1.85 m from stub middle to stub middle.
            var (count, stretch) = MapPropsRescaler.SolveArmPanels(14.7634f, 1.40f, 1.85f);
            Assert.AreEqual(10, count, "10 panels (stretch ~1.02) beats 9 (~1.13) and 11 (~0.93).");
            Assert.That(stretch * ((count - 1) * 1.40f + 1.85f), Is.EqualTo(14.7634f).Within(1e-4f));

            var (one, _) = MapPropsRescaler.SolveArmPanels(0.5f, 1.40f, 1.85f);
            Assert.AreEqual(1, one, "Never zero panels.");

            // Butted panels are the degenerate case and keep the A4 answer.
            Assert.AreEqual(MapPropsRescaler.SolveArmPanels(14.7634f, 1.9957f),
                MapPropsRescaler.SolveArmPanels(14.7634f, 1.9957f, 1.9957f));
        }

        [Test]
        public void ShippedMap_NeighbouringArmPanels_ShareOnePost_AndRunsStopAtTheColliderEnds()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var geometry = roots.First(g => g.name == "GeneratedMapGeometry");
            var props = roots.First(g => g.name == "MapProps");

            var colliders = geometry.transform.Cast<Transform>()
                .Select(t => (t, m: ArmCollider.Match(t.name)))
                .Where(x => x.m.Success)
                .ToDictionary(x => int.Parse(x.m.Groups[1].Value), x => x.t);
            Assert.AreEqual(8, colliders.Count, "The pinwheel has 8 arm colliders.");

            var panelsByArm = props.transform.Cast<Transform>()
                .Select(t => (t, m: ArmPanel.Match(t.name)))
                .Where(x => x.m.Success)
                .GroupBy(x => int.Parse(x.m.Groups[1].Value), x => x.t)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (arm, collider) in colliders.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)))
            {
                Assert.IsTrue(panelsByArm.ContainsKey(arm), $"Arm {arm} has no InnerWall panels.");
                var box = collider.GetComponent<BoxCollider>();
                Vector3 size = Vector3.Scale(box.size, collider.lossyScale);
                bool alongX = size.x >= size.z;
                Vector3 axis = alongX ? collider.right : collider.forward;
                axis.y = 0f;
                axis.Normalize();
                float half = (alongX ? size.x : size.z) * 0.5f;
                float c = Vector3.Dot(collider.TransformPoint(box.center), axis);
                float colMin = c - half, colMax = c + half;

                var spans = panelsByArm[arm].Select(p => Measure(p, axis)).OrderBy(s => s.Min).ToList();
                Assert.GreaterOrEqual(spans.Count, 2, $"Arm {arm}: expected a run of panels.");

                for (int i = 1; i < spans.Count; i++)
                {
                    var a = spans[i - 1].FarPost;
                    var b = spans[i].NearPost;
                    float ca = (a.x + a.y) * 0.5f, cb = (b.x + b.y) * 0.5f;
                    Assert.That(cb, Is.EqualTo(ca).Within(PostCentreTolerance),
                        $"Arm {arm}: {spans[i - 1].Name}'s far post ({ca:F3}) and {spans[i].Name}'s near post ({cb:F3}) " +
                        "should be one shared post, not a doubled one.");
                    float overlap = Mathf.Min(a.y, b.y) - Mathf.Max(a.x, b.x);
                    float width = Mathf.Min(a.y - a.x, b.y - b.x);
                    Assert.GreaterOrEqual(overlap, width - PostOverlapTolerance,
                        $"Arm {arm}: {spans[i - 1].Name} and {spans[i].Name} overlap by {overlap:F3} m, not one post width ({width:F3} m).");
                }

                Assert.GreaterOrEqual(spans.Min(s => s.Min), colMin - EndOverhang,
                    $"Arm {arm}: art reaches {colMin - spans.Min(s => s.Min):F3} m past the collider's hub end.");
                Assert.LessOrEqual(spans.Max(s => s.Max), colMax + EndOverhang,
                    $"Arm {arm}: art reaches {spans.Max(s => s.Max) - colMax:F3} m past the collider's outer end.");
                // ...and the end posts stand at the ends, so the stubs are not hiding an invisible wall.
                Assert.LessOrEqual(spans[0].NearPost.x - colMin, EndOverhang,
                    $"Arm {arm}: the hub-end post stands {spans[0].NearPost.x - colMin:F3} m inside the collider end.");
                Assert.LessOrEqual(colMax - spans[^1].FarPost.y, EndOverhang,
                    $"Arm {arm}: the outer-end post stands {colMax - spans[^1].FarPost.y:F3} m inside the collider end.");
            }
        }

        private struct PanelSpan
        {
            public string Name;
            public float Min, Max;
            public Vector2 NearPost, FarPost;
        }

        /// <summary>World extent of one panel along <paramref name="axis"/>, and its two posts' extents.</summary>
        private static PanelSpan Measure(Transform panel, Vector3 axis)
        {
            var points = new List<Vector3>();
            foreach (var mf in panel.GetComponentsInChildren<MeshFilter>())
            {
                Assert.IsNotNull(mf.sharedMesh, $"{panel.name} has a MeshFilter with no mesh.");
                var m = mf.transform.localToWorldMatrix;
                points.AddRange(mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)));
            }
            Assert.IsNotEmpty(points, $"{panel.name} has no mesh.");

            var along = points.Select(p => Vector3.Dot(p, axis)).ToList();
            float min = along.Min(), max = along.Max(), mid = (min + max) * 0.5f;
            float bottom = points.Min(p => p.y), top = points.Max(p => p.y);
            float capLine = top - (top - bottom) * 0.08f;
            var caps = points.Where(p => p.y >= capLine).Select(p => Vector3.Dot(p, axis)).ToList();
            var near = caps.Where(a => a < mid).ToList();
            var far = caps.Where(a => a >= mid).ToList();
            Assert.IsNotEmpty(near, $"{panel.name}: no post found on its hub side.");
            Assert.IsNotEmpty(far, $"{panel.name}: no post found on its outer side.");
            return new PanelSpan
            {
                Name = panel.name, Min = min, Max = max,
                NearPost = new Vector2(near.Min(), near.Max()), FarPost = new Vector2(far.Min(), far.Max()),
            };
        }
    }
}
