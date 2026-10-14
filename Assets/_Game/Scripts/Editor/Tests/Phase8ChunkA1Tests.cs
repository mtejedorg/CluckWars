using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A1 "One sun": with the shipped Game + Map scenes loaded together (as they are in
    /// a match) there is exactly one enabled directional light, and it lives in Game.unity.
    /// </summary>
    /// <remarks>
    /// Map.unity used to carry its own enabled Directional Light — a leftover of the baker's
    /// first-bake clone of Game.unity — so the arena was lit by two suns and read washed out.
    /// <c>RenderSettings.sun</c> pointed at only one of them, so nothing complained. The map
    /// scene is always loaded additively with Game; Game's sun is the single light source.
    /// </remarks>
    public class Phase8ChunkA1Tests
    {
        private const string GameScene = "Assets/_Game/Scenes/Game.unity";
        private const string MapScene  = "Assets/_Game/Scenes/Map.unity";

        private string _originalScenePath;

        [SetUp]
        public void CaptureOpenScene()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
        }

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return;
            }

            // Always reopen Single: this also unloads the additive Map scene we loaded.
            EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        [Test]
        public void GameAndMapLoaded_HaveExactlyOneEnabledDirectionalLight_InGameScene()
        {
            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);

            var suns = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(l => l.enabled && l.type == LightType.Directional)
                .ToArray();

            string Describe() => string.Join(", ",
                suns.Select(l => $"'{l.name}' in {l.gameObject.scene.path} (intensity {l.intensity})"));

            Assert.AreEqual(1, suns.Length,
                $"Expected exactly one enabled directional light with Game+Map loaded; found {suns.Length}: {Describe()}. " +
                "Map.unity must not carry its own sun — it is lit by Game.unity's.");
            Assert.AreEqual(GameScene, suns[0].gameObject.scene.path,
                $"The single sun must live in Game.unity, not {suns[0].gameObject.scene.path}.");
        }
    }
}
