using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C5: the ground and the 13 zone discs (4 base, 9 pile) are shadow receivers only. A 2 cm
    /// decal and a 51 m slab have no shadow worth rendering, and each cost a caster draw in the
    /// main-light shadow pass.
    /// </summary>
    public class Phase8ChunkC5Tests
    {
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";

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
        public void ShippedMap_GroundAndZoneDiscs_ReceiveButNeverCastShadows()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            var geometry = scene.GetRootGameObjects().First(g => g.name == "GeneratedMapGeometry").transform;

            var ground = geometry.Find("GeneratedGround");
            Assert.IsNotNull(ground, "Map.unity has no GeneratedGround.");
            var zones = geometry.Cast<Transform>()
                .Where(t => t.name.StartsWith("PileZone_") || t.name.StartsWith("BaseZone_")).ToList();
            Assert.AreEqual(13, zones.Count, "1 centre + 4 personal + 4 contested pile zones + 4 base zones.");

            foreach (var t in zones.Prepend(ground))
            {
                var r = t.GetComponent<MeshRenderer>();
                Assert.IsNotNull(r, t.name);
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, $"{t.name}: castShadows");
                Assert.IsTrue(r.receiveShadows, $"{t.name}: receiveShadows");
            }
        }

        [Test]
        public void MapGenerator_SetsShadowCastingOff_ForGroundAndZoneMarkers()
        {
            var src = System.IO.File.ReadAllText("Assets/_Game/Scripts/Gameplay/MapGenerator.cs");
            Assert.AreEqual(2,
                Regex.Matches(src, @"shadowCastingMode\s*=\s*UnityEngine\.Rendering\.ShadowCastingMode\.Off").Count,
                "BuildPlane and AddZoneMarker must both switch shadow casting off.");
        }
    }
}
