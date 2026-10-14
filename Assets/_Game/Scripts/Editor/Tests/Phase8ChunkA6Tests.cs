using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A6 "Shadows that show": the match camera must sit close enough that the whole
    /// visible ground is inside every quality tier's shadow distance, and the shadow cascades are
    /// pinned per tier.
    /// </summary>
    /// <remarks>
    /// The bug class: MatchCamera pulled back 30 m (harmless for ortho framing) while
    /// Mobile_RPAsset.shadowDistance was 26, so every pixel on screen was beyond the shadow range
    /// and the Mobile tier rendered no shadows at all. Nothing failed — URP just skips them.
    /// URP fades main-light shadows by Euclidean distance from the camera POSITION, so the test
    /// checks the farthest visible ground point (top screen corner on a 20:9 phone), not just
    /// the focus distance. The RP assets are read through SerializedObject — Mobile_RPAsset has
    /// a corruption history (see ProjectConfigTests), and a zeroed asset fails loudly here too.
    /// </remarks>
    public class Phase8ChunkA6Tests
    {
        private const string GameScene      = "Assets/_Game/Scenes/Game.unity";
        private const string MobileRpAsset  = "Assets/Settings/Mobile_RPAsset.asset";
        private const string PcRpAsset      = "Assets/Settings/PC_RPAsset.asset";
        private const float  WidestAspect   = 2400f / 1080f; // 20:9 phone, the widest target
        // Surround trees reach ~6.2 m world height; the camera focus is the chicken pivot (~0.9 m).
        private const float  TallestPropAboveFocus = 5.5f;
        private const float  Tolerance = 0.001f;

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
            _distance  = Prop(so, "_distance").floatValue;
            _pitch     = Prop(so, "_pitch").floatValue;
            _orthoSize = Prop(so, "_orthoSize").floatValue;
            _near      = Prop(so, "_nearClipPlane").floatValue;
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

        [TestCase(MobileRpAsset)]
        [TestCase(PcRpAsset)]
        public void FarthestVisibleGround_IsInsideShadowDistance(string rpAssetPath)
        {
            float shadowDistance = Prop(RpAsset(rpAssetPath), "m_ShadowDistance").floatValue;

            Assert.Less(_distance, shadowDistance,
                $"MatchCamera._distance ({_distance}) is beyond {rpAssetPath} shadowDistance ({shadowDistance}): " +
                "the whole view renders without shadows.");

            // Top screen edge meets the ground at view depth distance + orthoSize / tan(pitch);
            // add the half-height and the 20:9 half-width for the corner's Euclidean distance.
            float p = _pitch * Mathf.Deg2Rad;
            float depth = _distance + _orthoSize / Mathf.Tan(p);
            float halfW = _orthoSize * WidestAspect;
            float corner = Mathf.Sqrt(depth * depth + _orthoSize * _orthoSize + halfW * halfW);
            Assert.Less(corner, shadowDistance,
                $"The farthest visible ground corner is {corner:F1} m from the MatchCamera but {rpAssetPath} " +
                $"shadowDistance is {shadowDistance}: the top of the screen loses its shadows. Lower " +
                "MatchCamera._distance (it does not change ortho framing) rather than raising shadowDistance.");
        }

        [Test]
        public void NearPlane_DoesNotSliceTallPropsAtTheBottomEdge()
        {
            // A point z m above the focus stays in front of the near plane at the bottom screen edge
            // iff z / sin(pitch) + orthoSize / tan(pitch) <= distance - near.
            float p = _pitch * Mathf.Deg2Rad;
            float clearance = (_distance - _near - _orthoSize / Mathf.Tan(p)) * Mathf.Sin(p);
            Assert.GreaterOrEqual(clearance, TallestPropAboveFocus,
                $"With _distance {_distance} and _nearClipPlane {_near} the near plane slices anything taller than " +
                $"{clearance:F2} m above the focus at the bottom screen edge (surround trees reach ~6.2 m). " +
                "Pull the near plane further negative (legal for an orthographic camera).");
        }

        [Test]
        public void MobileRpAsset_SingleCascade_30m()
        {
            var so = RpAsset(MobileRpAsset);
            Assert.AreEqual(1, Prop(so, "m_ShadowCascadeCount").intValue, "Mobile shadow cascades");
            Assert.AreEqual(30f, Prop(so, "m_ShadowDistance").floatValue, Tolerance, "Mobile shadowDistance");
            Assert.AreEqual(0.1f, Prop(so, "m_CascadeBorder").floatValue, Tolerance, "Mobile cascade border");
        }

        [Test]
        public void PcRpAsset_TwoCascades_35m()
        {
            var so = RpAsset(PcRpAsset);
            Assert.AreEqual(2, Prop(so, "m_ShadowCascadeCount").intValue, "PC shadow cascades");
            Assert.AreEqual(35f, Prop(so, "m_ShadowDistance").floatValue, Tolerance, "PC shadowDistance");
            Assert.AreEqual(0.55f, Prop(so, "m_Cascade2Split").floatValue, Tolerance, "PC 2-cascade split");
            Assert.AreEqual(0.1f, Prop(so, "m_CascadeBorder").floatValue, Tolerance, "PC cascade border");
        }

        private static SerializedObject RpAsset(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.IsNotNull(asset, $"{path} failed to load (missing, or a corrupted / zeroed .asset).");
            return new SerializedObject(asset);
        }

        private static SerializedProperty Prop(SerializedObject so, string name)
        {
            var prop = so.FindProperty(name);
            Assert.IsNotNull(prop, $"{so.targetObject.name}.{name} not found — field renamed? Update this test.");
            return prop;
        }
    }
}
