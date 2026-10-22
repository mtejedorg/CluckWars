using System.IO;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// WP6 / PORTRAIT_AND_LANDSCAPE_SPEC §2.3 with Maestro's C1/C2: landscape keeps the shipped camera at every aspect
    /// (s = 5.6, near −6, chicken centred); portrait holds a 5.6 m lateral half-width capped at s = 12.5, puts the
    /// chicken at the clear band's centre and pulls the near clip back so trees are not sliced.
    /// </summary>
    public sealed class OrientationFramingTests
    {
        // MatchCamera's authored values (Game.unity; pinned against the scene in the fixture below).
        private const float S0 = 5.6f, Near0 = -6f, Distance = 15f, Pitch = 30f;
        private const float Eps = 0.01f;

        private static OrientationFraming.Result F(float aspect) => OrientationFraming.Compute(aspect, S0, Near0, Distance, Pitch);

        // ---- The spec table, rule as decided ------------------------------------------------------------------------

        //          aspect W/H          s       L       D       G
        [TestCase(16f / 9f,           5.60f,  9.96f, 11.20f, 446.0f, TestName = "SpecTable 16x9 landscape")]
        [TestCase(2400f / 1080f,      5.60f, 12.44f, 11.20f, 557.5f, TestName = "SpecTable 20x9 landscape keeps 5.6 - C2 no equal-area zoom")]
        [TestCase(21f / 9f,           5.60f, 13.07f, 11.20f, 585.4f, TestName = "SpecTable 21x9 landscape keeps 5.6")]
        [TestCase(4f / 3f,            5.60f,  7.47f, 11.20f, 334.5f, TestName = "SpecTable 4x3 landscape keeps 5.6")]
        [TestCase(9f / 16f,           9.96f,  5.60f, 19.91f, 446.0f, TestName = "SpecTable 9x16 portrait")]
        [TestCase(1080f / 2400f,     12.44f,  5.60f, 24.89f, 557.5f, TestName = "SpecTable 9x20 portrait")]
        [TestCase(1080f / 2410f,     12.50f,  5.60f, 24.99f, 559.8f, TestName = "SpecTable Pixel 10 Pro portrait")]
        [TestCase(9f / 21f,          12.50f,  5.36f, 25.00f, 535.7f, TestName = "SpecTable 9x21 portrait hits the 12.5 cap")]
        public void SpecTable_Row(float aspect, float s, float lateral, float depth, float area)
        {
            var f = F(aspect);
            Assert.AreEqual(aspect < 1f, f.Portrait, "orientation");
            Assert.AreEqual(s, f.OrthoSize, Eps, "ortho size s");
            Assert.AreEqual(lateral, f.LateralHalfExtent, Eps, "lateral half-extent L = s·a");
            Assert.AreEqual(depth, f.DepthHalfExtent, Eps, "depth half-extent D = 2s");
            Assert.AreEqual(area, f.GroundArea, 0.5f, "ground area G = 4LD");
        }

        [TestCase(9f / 16f)]
        [TestCase(1080f / 2400f)]
        [TestCase(1080f / 2410f)]
        [TestCase(0.75f)]
        public void Portrait_KeepsAtLeastTheLongestReach_ToEachSide_UpToTheCap(float aspect)
        {
            var f = F(aspect);
            if (f.OrthoSize < OrientationFraming.PortraitMaxOrthoSize - 1e-4f)
                Assert.AreEqual(OrientationFraming.PortraitLateralHalfWidth, f.LateralHalfExtent, 1e-3f);
            Assert.GreaterOrEqual(f.LateralHalfExtent, 5.4f, "every ability target in range (5.4 m) must be on screen");
        }

        [Test]
        public void Portrait_IsCappedAt12_5_EvenOnAVeryTallScreen()
        {
            Assert.AreEqual(12.5f, F(0.3f).OrthoSize, 1e-4f);
            Assert.AreEqual(12.5f, F(9f / 21f).OrthoSize, 1e-4f);
        }

        // ---- Focus offset (chicken at fy = 0.5 + (B − T)/2) --------------------------------------------------------

        [Test]
        public void Portrait_ChickenSitsAtTheClearBandCentre_055()
        {
            Assert.AreEqual(0.55f, OrientationFraming.PortraitFocusViewportY, 1e-5f, "T 0.15, B 0.25");
            var f = F(1080f / 2400f);
            Assert.AreEqual(0.55f, f.FocusViewportY, 1e-5f);
            Assert.AreEqual(0.2f * f.OrthoSize, f.FocusBackOffset, 1e-3f, "(fy − 0.5)·4s at 30°");
        }

        [Test]
        public void Portrait_9x20_ClearBandReaches15mAheadAndBehind()
        {
            var f = F(1080f / 2400f);
            float groundPerScreen = 4f * f.OrthoSize;   // 2s / sin30
            float ahead = (1f - OrientationFraming.PortraitTopBandFraction - f.FocusViewportY) * groundPerScreen;
            float behind = (f.FocusViewportY - OrientationFraming.PortraitBottomBandFraction) * groundPerScreen;
            Assert.AreEqual(14.9f, ahead, 0.1f);
            Assert.AreEqual(14.9f, behind, 0.1f);
        }

        [TestCase(9f / 16f)]
        [TestCase(1080f / 2400f)]
        [TestCase(9f / 21f)]
        public void Portrait_FocusOffset_DrawsTheChickenAtFy_OnARealCamera(float aspect)
        {
            var f = F(aspect);
            var go = new GameObject("OrientationFramingProbe");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = f.OrthoSize;
                cam.aspect = aspect;
                cam.nearClipPlane = f.NearClip;
                cam.farClipPlane = 200f;
                const float yaw = 225f;
                var chicken = new Vector3(3f, 0f, -7f);
                // MatchCamera.ApplyTransform, spelled out: focus behind the chicken along the ground-forward.
                var rot = Quaternion.Euler(Pitch, yaw, 0f);
                var focus = chicken - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * f.FocusBackOffset;
                go.transform.SetPositionAndRotation(focus - rot * Vector3.forward * Distance, rot);

                var vp = cam.WorldToViewportPoint(chicken);
                Assert.AreEqual(0.5f, vp.x, 1e-3f, "laterally centred");
                Assert.AreEqual(f.FocusViewportY, vp.y, 1e-3f, "drawn at fy, above centre");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FocusOffset_IsContinuousThroughSquare_NoCameraJump()
        {
            // A Game View or a foldable passing through square used to jump the follow point 0 -> ~1.12 m.
            Assert.AreEqual(0f, F(1f).FocusBackOffset, 0f, "square is landscape: no offset");
            Assert.AreEqual(0f, F(0.99999f).FocusBackOffset, 1e-3f, "just below square: still ~0");
            float prev = F(1f).FocusBackOffset;
            for (float a = 1f; a >= 0.5f; a -= 0.001f)
            {
                float back = F(a).FocusBackOffset;
                Assert.Less(Mathf.Abs(back - prev), 0.03f, $"aspect {a}: focus offset jumps {prev:F3} -> {back:F3} m");
                prev = back;
            }
        }

        [Test]
        public void FocusBlend_EndsAboveEveryPhonePortrait()
        {
            float fullAt = 1f - OrientationFraming.FocusBlendAspectBand;
            Assert.GreaterOrEqual(fullAt, 0.75f, "3:4 tablets and every phone get the full fy");
            Assert.AreEqual(0.55f, OrientationFraming.PortraitFocusViewportYAt(fullAt), 1e-5f);
            Assert.AreEqual(0.55f, OrientationFraming.PortraitFocusViewportYAt(0.75f), 1e-5f);
            Assert.AreEqual(0.5f, OrientationFraming.PortraitFocusViewportYAt(1f), 1e-5f);
            Assert.AreEqual(0.525f, OrientationFraming.PortraitFocusViewportYAt(1f - OrientationFraming.FocusBlendAspectBand / 2f), 1e-4f, "halfway");
        }

        // ---- Edit-time framing (MatchCamera.OnValidate) --------------------------------------------------------------

        [Test]
        public void OutsidePlayMode_TheFramingIsAlwaysLandscape()
        {
            Assert.AreEqual(OrientationFraming.EditTimeAspect, OrientationFraming.FramingAspect(false, 1080f / 2400f));
            Assert.AreEqual(1080f / 2400f, OrientationFraming.FramingAspect(true, 1080f / 2400f));
            Assert.IsFalse(OrientationFraming.Compute(OrientationFraming.EditTimeAspect, S0, Near0, Distance, Pitch).Portrait);
        }

        [Test]
        public void MatchCamera_OnValidate_WithAPortraitCamera_WritesTheLandscapeFraming()
        {
            // The Editor path qa-reviewer flagged: a portrait Game View must never put portrait values on the scene camera.
            var go = new GameObject("OrientationFramingOnValidateProbe");
            try
            {
                var mc = go.AddComponent<MatchCamera>();   // [RequireComponent(Camera)] adds the Camera
                var cam = go.GetComponent<Camera>();
                cam.aspect = 1080f / 2400f;
                var onValidate = typeof(MatchCamera).GetMethod("OnValidate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(onValidate, "MatchCamera.OnValidate moved; re-point this test.");
                onValidate.Invoke(mc, null);

                var so = new SerializedObject(mc);
                float ortho = so.FindProperty("_orthoSize").floatValue, near = so.FindProperty("_nearClipPlane").floatValue;
                float distance = so.FindProperty("_distance").floatValue;
                Vector3 focus = so.FindProperty("_focusPoint").vector3Value;
                Assert.AreEqual(ortho, cam.orthographicSize, 1e-5f, "landscape ortho size");
                Assert.AreEqual(near, cam.nearClipPlane, 1e-5f, "landscape near clip");
                Vector3 expected = focus - go.transform.forward * distance;   // no portrait focus offset
                Assert.Less((go.transform.position - expected).magnitude, 1e-4f, "camera sits on the un-offset focus");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---- Near clip per orientation -------------------------------------------------------------------------------

        //          aspect          near
        [TestCase(9f / 16f,    -13.24f, TestName = "PortraitNear 9x16")]
        [TestCase(1080f / 2400f, -17.56f, TestName = "PortraitNear 9x20")]
        [TestCase(9f / 21f,    -17.65f, TestName = "PortraitNear cap")]
        public void Portrait_NearClip_IsPulledBack(float aspect, float near)
        {
            Assert.AreEqual(near, F(aspect).NearClip, Eps);
        }

        [TestCase(0.99f)]
        [TestCase(9f / 16f)]
        [TestCase(1080f / 2400f)]
        [TestCase(9f / 21f)]
        [TestCase(0.3f)]
        public void Portrait_NearClip_DoesNotSliceTheSurroundTreesAtTheBottomEdge(float aspect)
        {
            // Phase8ChunkA6Tests' landscape inequality, applied to the portrait framing: a point z above the focus stays
            // in front of the near plane at the bottom edge iff z/sin(p) + s/tan(p) <= distance − near.
            var f = F(aspect);
            float p = Pitch * Mathf.Deg2Rad;
            float clearance = (Distance - f.NearClip - f.OrthoSize / Mathf.Tan(p)) * Mathf.Sin(p);
            Assert.GreaterOrEqual(clearance, OrientationFraming.TallestSceneryAboveFocus - 1e-4f,
                $"aspect {aspect}: s {f.OrthoSize:F2}, near {f.NearClip:F2} leaves only {clearance:F2} m above the focus.");
        }

        [Test]
        public void NearClip_IsContinuousAcrossSquare()
        {
            Assert.AreEqual(Near0, F(1f).NearClip, 1e-5f);
            Assert.AreEqual(Near0, F(0.9999f).NearClip, 0.01f);
        }

        // ---- Guard: no landscape aspect changes the camera vs today --------------------------------------------------

        [Test]
        public void EveryLandscapeAspect_IsTodaysCamera_Exactly()
        {
            for (float a = 1f; a <= 4f; a += 0.01f)
            {
                var f = F(a);
                Assert.IsFalse(f.Portrait, $"aspect {a}");
                Assert.AreEqual(S0, f.OrthoSize, 0f, $"aspect {a}: orthoSize");
                Assert.AreEqual(Near0, f.NearClip, 0f, $"aspect {a}: near clip");
                Assert.AreEqual(0f, f.FocusBackOffset, 0f, $"aspect {a}: focus offset");
                Assert.AreEqual(0.5f, f.FocusViewportY, 0f, $"aspect {a}: chicken centred");
            }
        }

        [Test]
        public void Landscape_ReturnsTheAuthoredValues_WhateverTheyAre()
        {
            // The inspector still tunes landscape: the helper passes _orthoSize / _nearClipPlane through untouched.
            var f = OrientationFraming.Compute(16f / 9f, 7.25f, -3.5f, 20f, 35f);
            Assert.AreEqual(7.25f, f.OrthoSize, 0f);
            Assert.AreEqual(-3.5f, f.NearClip, 0f);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void UnusableAspect_IsTreatedAs16x9(float aspect)
        {
            var f = F(aspect);
            Assert.IsFalse(f.Portrait);
            Assert.AreEqual(S0, f.OrthoSize, 0f);
        }

        [Test]
        public void MatchCamera_ReadsTheFramingEveryFrame_FromTheLiveAspect()
        {
            string src = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Assets/_Game/Scripts/Visuals/MatchCamera.cs"));
            StringAssert.Contains("OrientationFraming.Compute(OrientationFraming.FramingAspect(Application.isPlaying, _camera.aspect),", src);
            StringAssert.Contains("_orthoSize, _nearClipPlane, _distance, _pitch)", src);
            StringAssert.Contains("_framing.OrthoSize", src);
            StringAssert.Contains("_framing.NearClip", src);
            StringAssert.Contains("_framing.FocusBackOffset", src);
            StringAssert.Contains("VisibleGround", src);
        }
    }

    /// <summary>The landscape guard against the real scene: Game.unity's MatchCamera values give today's camera.</summary>
    public sealed class OrientationFramingSceneTests
    {
        private const string GameScene = "Assets/_Game/Scenes/Game.unity";
        private string _originalScenePath;
        private float _distance, _pitch, _orthoSize, _near;

        [SetUp]
        public void LoadMatchCamera()
        {
            _originalScenePath = SceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var cam = Object.FindFirstObjectByType<MatchCamera>(FindObjectsInactive.Include);
            Assert.IsNotNull(cam, "Game.unity must contain a MatchCamera.");
            var so = new SerializedObject(cam);
            _distance  = so.FindProperty("_distance").floatValue;
            _pitch     = so.FindProperty("_pitch").floatValue;
            _orthoSize = so.FindProperty("_orthoSize").floatValue;
            _near      = so.FindProperty("_nearClipPlane").floatValue;
        }

        [TearDown]
        public void RestoreOpenScene()
        {
            if (string.IsNullOrEmpty(_originalScenePath))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
        }

        [Test]
        public void SceneValues_AreTheOnesTheSpecTableAssumes()
        {
            Assert.AreEqual(5.6f, _orthoSize, 1e-4f, "C2: landscape keeps orthoSize 5.6");
            Assert.AreEqual(-6f, _near, 1e-4f, "landscape near clip stays -6");
            Assert.AreEqual(15f, _distance, 1e-4f);
            Assert.AreEqual(30f, _pitch, 1e-4f);
        }

        [TestCase(16f / 9f)]
        [TestCase(2400f / 1080f)]
        [TestCase(2410f / 1080f)]
        [TestCase(21f / 9f)]
        [TestCase(4f / 3f)]
        [TestCase(1f)]
        public void SceneCamera_InLandscape_IsUnchanged(float aspect)
        {
            var f = OrientationFraming.Compute(aspect, _orthoSize, _near, _distance, _pitch);
            Assert.AreEqual(_orthoSize, f.OrthoSize, 0f);
            Assert.AreEqual(_near, f.NearClip, 0f);
            Assert.AreEqual(0f, f.FocusBackOffset, 0f);
        }
    }
}
