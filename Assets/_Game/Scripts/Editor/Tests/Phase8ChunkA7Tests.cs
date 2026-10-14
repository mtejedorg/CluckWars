using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using CluckWars.Services;
using CluckWars.Visuals;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 A7 "AA + pipeline waste": per-tier MSAA, no unused camera depth / opaque copies, LDR on the Mobile
    /// tier, the match volume profile under its real name, and the per-platform frame pacing rules.
    /// </summary>
    /// <remarks>
    /// The RP assets are read through SerializedObject: Mobile_RPAsset has a corruption history (see
    /// ProjectConfigTests), and a zeroed asset fails loudly here too. Nothing in the project samples
    /// <c>_CameraDepthTexture</c> / <c>_CameraOpaqueTexture</c> (no SceneDepth / SceneColor ShaderGraph nodes, no
    /// renderer feature or camera asking for them; PC_Renderer's SSAO requests its own depth-normals input), so
    /// requiring them only paid for a depth prepass/copy and an opaque copy every frame.
    /// </remarks>
    public class Phase8ChunkA7Tests
    {
        private const string GameScene     = "Assets/_Game/Scenes/Game.unity";
        private const string MobileRpAsset = "Assets/Settings/Mobile_RPAsset.asset";
        private const string PcRpAsset     = "Assets/Settings/PC_RPAsset.asset";
        private const string ProfilePath   = "Assets/Settings/CW_MatchProfile.asset";
        private const string OldProfilePath = "Assets/Settings/SampleSceneProfile.asset";
        // The GUID the RP assets (and SampleScene.unity) reference: the rename must not re-key it.
        private const string ProfileGuid   = "10fc4df2da32a41aaa32d77bc913491c";

        [TestCase(PcRpAsset, 4)]
        [TestCase(MobileRpAsset, 2)]
        public void RpAsset_MsaaSampleCount(string path, int samples)
        {
            Assert.AreEqual(samples, Prop(RpAsset(path), "m_MSAA").intValue, $"{path} MSAA sample count");
        }

        [TestCase(PcRpAsset)]
        [TestCase(MobileRpAsset)]
        public void RpAsset_DoesNotRequireDepthOrOpaqueTexture(string path)
        {
            var so = RpAsset(path);
            Assert.IsFalse(Prop(so, "m_RequireDepthTexture").boolValue,
                $"{path} requires the camera depth texture but nothing samples it (extra depth prepass / copy per frame).");
            Assert.IsFalse(Prop(so, "m_RequireOpaqueTexture").boolValue,
                $"{path} requires the camera opaque texture but nothing samples it (full-screen copy per frame).");
        }

        [Test]
        public void MobileRpAsset_HdrOff()
        {
            Assert.IsFalse(Prop(RpAsset(MobileRpAsset), "m_SupportsHDR").boolValue,
                "Mobile tier renders LDR: no post-processing runs there, so an HDR colour buffer is pure bandwidth.");
        }

        [Test]
        public void MatchProfile_RenamedKeepingItsGuid()
        {
            Assert.AreEqual(ProfileGuid, AssetDatabase.AssetPathToGUID(ProfilePath),
                $"{ProfilePath} missing, or re-keyed (the RP assets reference GUID {ProfileGuid}).");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath), $"{ProfilePath} is not a VolumeProfile.");
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(OldProfilePath), $"{OldProfilePath} still exists.");
        }

        [TestCase(PcRpAsset)]
        [TestCase(MobileRpAsset)]
        public void RpAsset_DefaultVolumeProfile_IsTheMatchProfile(string path)
        {
            var profile = Prop(RpAsset(path), "m_VolumeProfile").objectReferenceValue;
            Assert.IsNotNull(profile, $"{path} has no default volume profile.");
            Assert.AreEqual(ProfilePath, AssetDatabase.GetAssetPath(profile), $"{path} default volume profile");
        }

        [Test]
        public void FrameRatePolicy_Android30_OtherwiseDefault()
        {
            Assert.AreEqual(30, FrameRatePolicy.TargetFor(RuntimePlatform.Android));
            foreach (var p in new[] { RuntimePlatform.WindowsPlayer, RuntimePlatform.WindowsEditor,
                                      RuntimePlatform.OSXEditor, RuntimePlatform.IPhonePlayer, RuntimePlatform.LinuxPlayer })
                Assert.AreEqual(FrameRatePolicy.LeaveDefault, FrameRatePolicy.TargetFor(p), $"{p} must not be capped");
        }

        [Test]
        public void QualityLevels_PcVSyncOn_MobileVSyncOffSoTargetFrameRateApplies()
        {
            // vSyncCount > 0 makes Unity ignore Application.targetFrameRate, so the Mobile level must stay 0 or the
            // Android 30 fps cap silently stops applying.
            Assert.AreEqual(1, QualityLevelInt("PC", "vSyncCount"), "PC quality level vSyncCount");
            Assert.AreEqual(0, QualityLevelInt("Mobile", "vSyncCount"), "Mobile quality level vSyncCount");
        }

        [Test]
        public void RenderTierPolicy_MobileNeverPost_PcFollowsTheFlag()
        {
            var pc = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PcRpAsset);
            var mobile = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobileRpAsset);
            Assert.AreEqual(RenderTierPolicy.PcRpAssetName, pc.name, "RenderTierPolicy keys on the PC asset's name");
            Assert.AreEqual(RenderTierPolicy.PcPostProcessingEnabled, RenderTierPolicy.PostProcessingFor(pc));
            Assert.IsFalse(RenderTierPolicy.PostProcessingFor(mobile), "Mobile tier must never render post-processing");
            Assert.IsFalse(RenderTierPolicy.PostProcessingFor((RenderPipelineAsset)null));
        }

        [Test]
        public void MatchCamera_AllowsMsaa_RendersToScreen_PostOffByDefault()
        {
            string original = SceneManager.GetActiveScene().path;
            try
            {
                EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
                var mc = Object.FindFirstObjectByType<MatchCamera>(FindObjectsInactive.Include);
                Assert.IsNotNull(mc, "Game.unity must contain a MatchCamera.");
                var cam = mc.GetComponent<Camera>();
                Assert.IsTrue(cam.allowMSAA, "MatchCamera.allowMSAA off defeats the RP assets' MSAA.");
                Assert.IsNull(cam.targetTexture, "A target texture would take its sample count from the RT, not the RP asset.");
                var urp = mc.GetComponent<UniversalAdditionalCameraData>();
                Assert.IsNotNull(urp);
                Assert.AreEqual(CameraRenderType.Base, urp.renderType);
                Assert.AreEqual(0, urp.cameraStack.Count, "No overlay cameras expected on the match camera.");
                Assert.IsFalse(urp.renderPostProcessing,
                    "Serialized default must be post OFF (the cheap Mobile path); MatchCamera turns it on per tier at runtime.");
            }
            finally
            {
                if (string.IsNullOrEmpty(original)) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
            }
        }

        private static int QualityLevelInt(string levelName, string field)
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            Assert.IsNotEmpty(qs, "QualitySettings.asset failed to load.");
            var levels = new SerializedObject(qs[0]).FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                if (level.FindPropertyRelative("name").stringValue == levelName)
                    return level.FindPropertyRelative(field).intValue;
            }
            Assert.Fail($"No quality level named '{levelName}'.");
            return -1;
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
