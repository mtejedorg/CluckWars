using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A8 "Ambient + background": pins the match scene's environment lighting and the
    /// MatchCamera clear colour to the values the art pass agreed on.
    /// </summary>
    /// <remarks>
    /// Ambient is per-scene RenderSettings, read from the ACTIVE scene. In a match that is
    /// Game.unity (SceneLoader loads it Single; MapGenerator adds Map.unity additively without
    /// SetActiveScene), so Game.unity's RenderSettings are the single source of truth — no script
    /// writes <c>RenderSettings.ambient*</c> at runtime, and nothing is quality-tier specific.
    /// The old values were Unity's default Skybox ambient over the default skybox, which read dark
    /// and blue; MatchCamera cleared to near-black navy around the arena.
    /// </remarks>
    public class Phase8ChunkA8Tests
    {
        private const string GameScene = "Assets/_Game/Scenes/Game.unity";
        private const float Tolerance = 0.01f;

        private static readonly Color ExpectedSky        = new Color(0.55f, 0.60f, 0.68f, 1f);
        private static readonly Color ExpectedEquator    = new Color(0.42f, 0.40f, 0.33f, 1f);
        private static readonly Color ExpectedGround     = new Color(0.20f, 0.17f, 0.12f, 1f);
        private static readonly Color ExpectedBackground = new Color(0.45f, 0.58f, 0.66f, 1f);

        private string _originalScenePath;

        [SetUp]
        public void CaptureOpenScene()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
        }

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                return;
            }
            EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        [Test]
        public void GameScene_AmbientIsWarmTrilight()
        {
            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode,
                "Game.unity ambient must be Trilight (gradient) — Skybox mode samples the default skybox and reads dark/blue.");
            AssertColor(ExpectedSky,     RenderSettings.ambientSkyColor,     "ambient sky");
            AssertColor(ExpectedEquator, RenderSettings.ambientEquatorColor, "ambient equator");
            AssertColor(ExpectedGround,  RenderSettings.ambientGroundColor,  "ambient ground");
        }

        [Test]
        public void GameScene_MatchCameraBackgroundIsMutedDaytime()
        {
            var cam = Object.FindFirstObjectByType<MatchCamera>(FindObjectsInactive.Include);
            Assert.IsNotNull(cam, "Game.unity must contain a MatchCamera.");

            var so = new SerializedObject(cam);
            var useSolid = so.FindProperty("_useSolidColor");
            var bg = so.FindProperty("_backgroundColor");
            Assert.IsNotNull(bg, "MatchCamera._backgroundColor was renamed — update this test.");
            Assert.IsTrue(useSolid != null && useSolid.boolValue,
                "MatchCamera must force a solid clear, or _backgroundColor is never applied.");
            AssertColor(ExpectedBackground, bg.colorValue, "MatchCamera._backgroundColor (Game.unity)");
        }

        [Test]
        public void MatchCamera_CodeDefaultAgreesWithScene()
        {
            // A fresh AddComponent shows the field initialiser — keeps code default and scene in step.
            var go = new GameObject("A8_TempMatchCamera");
            go.SetActive(false); // no Awake/OnEnable side effects
            try
            {
                var cam = go.AddComponent<MatchCamera>();
                var bg = new SerializedObject(cam).FindProperty("_backgroundColor");
                AssertColor(ExpectedBackground, bg.colorValue, "MatchCamera._backgroundColor code default");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void AssertColor(Color expected, Color actual, string what)
        {
            bool ok = Mathf.Abs(expected.r - actual.r) <= Tolerance
                   && Mathf.Abs(expected.g - actual.g) <= Tolerance
                   && Mathf.Abs(expected.b - actual.b) <= Tolerance;
            Assert.IsTrue(ok, $"{what}: expected {expected}, got {actual} (tolerance {Tolerance}).");
        }
    }
}
