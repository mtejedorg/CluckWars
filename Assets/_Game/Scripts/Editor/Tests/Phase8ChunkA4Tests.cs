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
    /// Phase 8 A4 "Arms honest": in the shipped <c>Map.unity</c> every pinwheel arm's wall art
    /// (<c>MapProps/InnerWall_{arm}_*</c>) covers exactly its baked collider
    /// (<c>GeneratedMapGeometry/Terrain_Standard_{arm}</c>) — no invisible wall, no see-through gap.
    /// </summary>
    /// <remarks>
    /// The 2026-08-14 ×1.35 rescale moved the art; the 08-19 revert re-baked the colliders but not
    /// the art, leaving 3.6–4.5 m of invisible wall at each arm's hub end and 0.65 m gaps between
    /// panels. The art is now derived from the colliders (<see cref="MapPropsRescaler.RelayArmWalls()"/>).
    /// Coverage is measured from the panels' actual mesh bounds in world space, projected on the
    /// collider's axis — independently of the tool that laid them.
    /// </remarks>
    public class Phase8ChunkA4Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const float EndTolerance = 0.1f;
        private const float MaxGap = 0.05f;
        private const float MaxOffLine = 0.05f;

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
        public void SolveArmPanels_CoversTheLengthExactly_WithTheLeastStretch()
        {
            var (count, stretch) = MapPropsRescaler.SolveArmPanels(14.7634f, 1.9957f);
            Assert.AreEqual(7, count, "7 panels (stretch 1.057) beats 8 (0.925).");
            Assert.That(count * 1.9957f * stretch, Is.EqualTo(14.7634f).Within(1e-4f));

            var (one, s1) = MapPropsRescaler.SolveArmPanels(0.5f, 2f);
            Assert.AreEqual(1, one, "Never zero panels.");
            Assert.That(s1, Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void ShippedMap_EveryArmWall_CoversItsColliderEndToEnd_WithNoGaps()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var geometry = roots.FirstOrDefault(g => g.name == "GeneratedMapGeometry");
            var props = roots.FirstOrDefault(g => g.name == "MapProps");
            Assert.IsNotNull(geometry, "Map.unity has no GeneratedMapGeometry root.");
            Assert.IsNotNull(props, "Map.unity has no MapProps root.");

            var colliders = geometry.transform.Cast<Transform>()
                .Select(t => (t, m: ArmCollider.Match(t.name)))
                .Where(x => x.m.Success)
                .ToDictionary(x => int.Parse(x.m.Groups[1].Value), x => x.t);
            Assert.AreEqual(8, colliders.Count, "The pinwheel has 8 arm colliders (Terrain_Standard_0..7).");

            var panelsByArm = props.transform.Cast<Transform>()
                .Select(t => (t, m: ArmPanel.Match(t.name)))
                .Where(x => x.m.Success)
                .GroupBy(x => int.Parse(x.m.Groups[1].Value), x => x.t)
                .ToDictionary(g => g.Key, g => g.ToList());
            CollectionAssert.AreEquivalent(colliders.Keys, panelsByArm.Keys,
                "Every arm collider needs wall art and every InnerWall run needs a collider.");

            foreach (var (arm, collider) in colliders.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)))
            {
                var box = collider.GetComponent<BoxCollider>();
                Assert.IsNotNull(box, $"{collider.name} has no BoxCollider.");
                Vector3 size = Vector3.Scale(box.size, collider.lossyScale);
                bool alongX = size.x >= size.z;
                Vector3 axis = alongX ? collider.right : collider.forward;
                axis.y = 0f;
                axis.Normalize();
                Vector3 normal = new Vector3(-axis.z, 0f, axis.x);
                Vector3 centre = collider.TransformPoint(box.center);
                float half = (alongX ? size.x : size.z) * 0.5f;
                float colMin = Vector3.Dot(centre, axis) - half, colMax = Vector3.Dot(centre, axis) + half;
                float line = Vector3.Dot(centre, normal);

                var spans = new List<(float Min, float Max, string Name)>();
                foreach (var panel in panelsByArm[arm])
                {
                    float min = float.MaxValue, max = float.MinValue, offMin = float.MaxValue, offMax = float.MinValue;
                    foreach (var mf in panel.GetComponentsInChildren<MeshFilter>())
                    {
                        Assert.IsNotNull(mf.sharedMesh, $"{panel.name} has a MeshFilter with no mesh.");
                        var b = mf.sharedMesh.bounds;
                        var m = mf.transform.localToWorldMatrix;
                        for (int c = 0; c < 8; c++)
                        {
                            var p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(
                                (c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                            float a = Vector3.Dot(p, axis), n = Vector3.Dot(p, normal);
                            min = Mathf.Min(min, a); max = Mathf.Max(max, a);
                            offMin = Mathf.Min(offMin, n); offMax = Mathf.Max(offMax, n);
                        }
                    }
                    Assert.Less(min, max, $"{panel.name} has no renderable mesh.");
                    Assert.That((offMin + offMax) * 0.5f, Is.EqualTo(line).Within(MaxOffLine),
                        $"{panel.name} is off arm {arm}'s centre line.");
                    spans.Add((min, max, panel.name));
                }

                spans.Sort((x, y) => x.Min.CompareTo(y.Min));
                Assert.That(spans[0].Min, Is.EqualTo(colMin).Within(EndTolerance),
                    $"Arm {arm}: art starts at {spans[0].Min:F2} but the collider at {colMin:F2} (along the arm).");
                Assert.That(spans[^1].Max, Is.EqualTo(colMax).Within(EndTolerance),
                    $"Arm {arm}: art ends at {spans[^1].Max:F2} but the collider at {colMax:F2} (along the arm).");

                float reach = spans[0].Max;
                for (int i = 1; i < spans.Count; i++)
                {
                    float gap = spans[i].Min - reach;
                    Assert.LessOrEqual(gap, MaxGap,
                        $"Arm {arm}: {gap:F3} m see-through gap before {spans[i].Name}.");
                    reach = Mathf.Max(reach, spans[i].Max);
                }
            }
        }
    }
}
