using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C11 "Per-quadrant prop kits": the arena dressing is 4-fold symmetric in TYPE as well as in
    /// position, so no quadrant looks (or plays) different. Arm k of the pinwheel lies at 22.5 + 45k degrees
    /// (counter-clockwise from +X, seen from above); a +90 degree turn about the arena centre maps arm k onto
    /// arm k+2 (mod 8), so even arms share one kit and odd arms another. Base furniture and scarecrows are
    /// held to the same rule.
    /// </summary>
    /// <remarks>
    /// For every prop in <c>MapProps</c> (fence and arm panels excluded) the prop turned +90 degrees about the
    /// origin (Unity yaw -90) must land on another prop with the SAME mesh, the same yaw and within 5 cm.
    /// Props are identified by mesh, not name.
    /// </remarks>
    public class Phase8ChunkC11Tests
    {
        private const float PositionTolerance = 0.05f;
        private const float YawTolerance = 1f;

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
        public void ShippedMap_ArmDressing_ArmKPlus2_IsArmK_Rotated90()
        {
            var map = Phase8ChunkA5Tests.LoadMap();
            var props = map.PropRoots
                .Where(p => p.IsChildOf(map.Props))
                .Select(p => (T: p, Mesh: Phase8ChunkA5Tests.MeshOf(p)))
                .Where(p => !p.Mesh.StartsWith("wall_segment"))
                .ToList();
            Assert.GreaterOrEqual(props.Count, 60, "Expected the arm dressing, fence dressing and base furniture.");

            // Arm k at 22.5 + 45k degrees: the pinwheel itself must be 4-fold for the rule to mean anything.
            var rot = Quaternion.Euler(0f, -90f, 0f); // +90 degrees counter-clockwise seen from above
            foreach (var arm in map.Arms)
            {
                var twin = map.Arms.Single(a => a.Index == (arm.Index + 2) % 8);
                Assert.Less(Vector3.Distance(rot * arm.Tip, twin.Tip), PositionTolerance, $"Arm {arm.Index} turned 90 degrees is not arm {twin.Index}.");
                Assert.Less(Vector3.Distance(rot * arm.Hub, twin.Hub), PositionTolerance, $"Arm {arm.Index} turned 90 degrees is not arm {twin.Index}.");
            }

            var offenders = new List<string>();
            foreach (var p in props)
            {
                Vector3 target = rot * p.T.position;
                float targetYaw = p.T.eulerAngles.y - 90f;
                bool sameType = props.Any(o => o.T != p.T && o.Mesh == p.Mesh && Near(o.T, target, targetYaw));
                if (sameType) continue;
                var other = props.FirstOrDefault(o => o.T != p.T && Near(o.T, target, targetYaw));
                offenders.Add(other.T != null
                    ? $"{p.T.name} ({p.Mesh}) {Phase8ChunkA5Tests.Fmt(p.T.position)}: its 90-degree twin {other.T.name} is a {other.Mesh}"
                    : $"{p.T.name} ({p.Mesh}) {Phase8ChunkA5Tests.Fmt(p.T.position)}: nothing at {Phase8ChunkA5Tests.Fmt(target)} yaw {Mathf.Repeat(targetYaw, 360f):F1}");
            }
            Assert.IsEmpty(offenders, "Dressing is not 4-fold in type:\n" + string.Join("\n", offenders));
        }

        private static bool Near(Transform t, Vector3 pos, float yaw) =>
            Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(pos.x, pos.z)) < PositionTolerance
            && Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, yaw)) < YawTolerance;
    }
}
