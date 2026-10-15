using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 C6 "soft shadows per tier" + C7 "unused pipeline features off".
    /// </summary>
    /// <remarks>
    /// C6: both tiers filter the sun's shadow (soft), Mobile at Low (cheapest PCF), PC at Medium.
    ///
    /// C7, Mobile tier: nothing in the project uses light layers, reflection probes (none in any scene, prefab or
    /// Map.unity) or per-frame volume evaluation (no Volume components anywhere; the only volume input is the RP
    /// asset's default profile, and the Mobile tier never renders post - <see cref="CluckWars.Visuals.RenderTierPolicy"/>),
    /// so those are off / ViaScripting. Additional lights stay per-pixel with a per-object limit of 3, not 2 or
    /// Disabled: the menu's <c>MenuChickenStage</c> studio rig is four directional lights (key becomes the main
    /// light, fill / rim / front are additional), and a lower limit would drop one of them on Android. No match
    /// object has a Light besides the sun, so the limit costs nothing in a match.
    ///
    /// C7, PC tier: no additional light casts shadows, so that shadow atlas path is off. The PC tier keeps
    /// volume update EveryFrame: D7 turns PC post-processing on through the default volume profile.
    ///
    /// Read through both SerializedObject (raw values; Mobile_RPAsset has a corruption history - see
    /// ProjectConfigTests) and the typed URP API (pins the enum meaning of the raw ints).
    /// </remarks>
    public class Phase8ChunkC6C7Tests
    {
        private const string MobileRpAsset = "Assets/Settings/Mobile_RPAsset.asset";
        private const string PcRpAsset     = "Assets/Settings/PC_RPAsset.asset";

        // ---- C6 ----

        [Test]
        public void Mobile_SoftShadowsOn_QualityLow()
        {
            var a = Urp(MobileRpAsset);
            Assert.IsTrue(a.supportsSoftShadows, "Mobile tier soft shadows");
            Assert.AreEqual(SoftShadowQuality.Low, (SoftShadowQuality)Prop(MobileRpAsset, "m_SoftShadowQuality").intValue,
                "Mobile tier soft-shadow quality");
        }

        [Test]
        public void Pc_SoftShadowsOn_QualityMedium()
        {
            var a = Urp(PcRpAsset);
            Assert.IsTrue(a.supportsSoftShadows, "PC tier soft shadows");
            Assert.AreEqual(SoftShadowQuality.Medium, (SoftShadowQuality)Prop(PcRpAsset, "m_SoftShadowQuality").intValue,
                "PC tier soft-shadow quality");
        }

        [TestCase(MobileRpAsset)]
        [TestCase(PcRpAsset)]
        public void MainLightShadowsStillOn(string path)
        {
            Assert.IsTrue(Urp(path).supportsMainLightShadows, $"{path} main-light shadows");
        }

        // ---- C7 ----

        [Test]
        public void Mobile_VolumeUpdateViaScripting()
        {
            Assert.AreEqual(VolumeFrameworkUpdateMode.ViaScripting, Urp(MobileRpAsset).volumeFrameworkUpdateMode);
        }

        [Test]
        public void Pc_VolumeUpdateEveryFrame_ForD7Post()
        {
            Assert.AreEqual(VolumeFrameworkUpdateMode.EveryFrame, Urp(PcRpAsset).volumeFrameworkUpdateMode,
                "PC tier post-processing (D7) reads the volume stack every frame.");
        }

        [Test]
        public void Mobile_LightLayersAndReflectionProbeExtrasOff()
        {
            Assert.IsFalse(Prop(MobileRpAsset, "m_SupportsLightLayers").boolValue, "Mobile light layers");
            Assert.IsFalse(Prop(MobileRpAsset, "m_ReflectionProbeBlending").boolValue, "Mobile reflection-probe blending");
            Assert.IsFalse(Prop(MobileRpAsset, "m_ReflectionProbeBoxProjection").boolValue, "Mobile reflection-probe box projection");
        }

        [Test]
        public void Mobile_AdditionalLightsPerPixel_LimitFitsMenuStudioRig()
        {
            var a = Urp(MobileRpAsset);
            Assert.AreEqual(LightRenderingMode.PerPixel, a.additionalLightsRenderingMode,
                "Mobile additional lights must stay on: the menu chicken stage lights with 3 additional directionals.");
            Assert.AreEqual(3, a.maxAdditionalLightsCount, "Mobile additional lights per-object limit");
        }

        [Test]
        public void Pc_AdditionalLightShadowsOff()
        {
            Assert.IsFalse(Urp(PcRpAsset).supportsAdditionalLightShadows, "PC additional-light shadows");
        }

        private static UniversalRenderPipelineAsset Urp(string path)
        {
            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(a, $"{path} failed to load as a URP asset (missing, or a corrupted / zeroed .asset).");
            return a;
        }

        private static SerializedProperty Prop(string path, string name)
        {
            var so = new SerializedObject(Urp(path));
            var prop = so.FindProperty(name);
            Assert.IsNotNull(prop, $"{path}.{name} not found - field renamed? Update this test.");
            return prop;
        }
    }
}
