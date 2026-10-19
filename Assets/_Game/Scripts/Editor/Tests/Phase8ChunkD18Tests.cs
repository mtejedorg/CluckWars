using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D18: <c>PC_RPAsset</c> light layers, reflection-probe blending and box projection are OFF (Mobile already was,
    /// C7), plus a non-zeroed sanity guard for both RP assets.
    /// </summary>
    /// <remarks>
    /// Nothing in the project uses the features: no ReflectionProbe component in any scene or prefab (the ambient reflection is
    /// the skybox default, which blending / box projection do not touch), every light's rendering-layer mask and every
    /// renderer's layer mask is the default (1), no Decal feature on either renderer. Turning them off drops the
    /// SUPPORT_LIGHT_LAYERS / _REFLECTION_PROBE_BLENDING / _REFLECTION_PROBE_BOX_PROJECTION variants from PC player builds.
    /// If a future feature needs one, flip the setting and this test together.
    ///
    /// The sanity guard mirrors the Mobile_RPAsset corruption (2026-09-15: all bytes zeroed, a deserialised-to-null pipeline,
    /// pink frame). A zeroed asset still loads as a URP asset in some paths, so it also pins the non-zero core settings.
    /// Read through SerializedObject (raw values). Map.unity is binary and carries no lights (guarded elsewhere), so the text
    /// scenes and prefabs are scanned here.
    /// </remarks>
    public class Phase8ChunkD18Tests
    {
        private const string MobileRpAsset = "Assets/Settings/Mobile_RPAsset.asset";
        private const string PcRpAsset     = "Assets/Settings/PC_RPAsset.asset";

        [TestCase(MobileRpAsset)]
        [TestCase(PcRpAsset)]
        public void LightLayersAndReflectionProbeExtras_AreOff_OnBothTiers(string path)
        {
            Assert.IsFalse(Prop(path, "m_SupportsLightLayers").boolValue, $"{path} light layers");
            Assert.IsFalse(Prop(path, "m_ReflectionProbeBlending").boolValue, $"{path} reflection-probe blending");
            Assert.IsFalse(Prop(path, "m_ReflectionProbeBoxProjection").boolValue, $"{path} reflection-probe box projection");
        }

        [Test]
        public void ProjectUsesNone_OfThePcFeaturesTurnedOff()
        {
            var offenders = new System.Collections.Generic.List<string>();

            // Text scenes (Map.unity is binary, no lights, no probes - Phase8ChunkA-series lighting guard).
            foreach (var scene in new[] { "Bootstrap", "Game", "AbilityLab" })
            {
                string p = $"Assets/_Game/Scenes/{scene}.unity";
                string text = File.ReadAllText(p);
                if (Regex.IsMatch(text, @"^--- !u!215 ", RegexOptions.Multiline)) offenders.Add($"{p}: ReflectionProbe");
                foreach (Match m in Regex.Matches(text, @"m_LightLayerMask: (\d+)"))
                    if (m.Groups[1].Value != "1") offenders.Add($"{p}: light layer mask {m.Groups[1].Value}");
            }

            // Every prefab under Assets/_Game, through the object model (nested + variant safe).
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) continue;
                if (go.GetComponentsInChildren<ReflectionProbe>(true).Length > 0) offenders.Add($"{p}: ReflectionProbe");
                foreach (var l in go.GetComponentsInChildren<Light>(true))
                {
                    var d = l.GetComponent<UniversalAdditionalLightData>();
                    if (d != null && d.renderingLayers != 1u) offenders.Add($"{p}: light '{l.name}' layers {d.renderingLayers}");
                }
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r.renderingLayerMask != 1u) offenders.Add($"{p}: renderer '{r.name}' layer mask {r.renderingLayerMask}");
            }

            Assert.IsEmpty(offenders,
                "D18 turned off PC light layers + reflection-probe blending / box projection because the project uses none of them. " +
                "Re-enable them in PC_RPAsset (and flip LightLayersAndReflectionProbeExtras_AreOff_OnBothTiers) if you add one.");
        }

        [TestCase(MobileRpAsset, 0, 2, 30)]
        [TestCase(PcRpAsset,     1, 4, 35)]
        public void RpAsset_IsNotZeroed_KeepsRendererAndCoreSettings(string path, int hdr, int msaa, int shadowDistance)
        {
            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(a, $"{path} failed to load as a URP asset (missing or zeroed).");

            var list = Prop(path, "m_RendererDataList");
            Assert.GreaterOrEqual(list.arraySize, 1, $"{path} renderer list is empty (zeroed asset?)");
            for (int i = 0; i < list.arraySize; i++)
                Assert.IsNotNull(list.GetArrayElementAtIndex(i).objectReferenceValue, $"{path} renderer {i} reference is null");

            Assert.AreEqual(hdr == 1, Prop(path, "m_SupportsHDR").boolValue, $"{path} HDR");
            Assert.AreEqual(msaa, Prop(path, "m_MSAA").intValue, $"{path} MSAA");
            Assert.AreEqual((float)shadowDistance, Prop(path, "m_ShadowDistance").floatValue, 0.001f, $"{path} shadow distance");
            Assert.Greater(Prop(path, "m_RenderScale").floatValue, 0f, $"{path} render scale");
            Assert.Greater(Prop(path, "m_MainLightShadowmapResolution").intValue, 0, $"{path} main-light shadow map resolution");
            Assert.IsTrue(Prop(path, "m_MainLightShadowsSupported").boolValue, $"{path} main-light shadows");
            Assert.IsTrue(Prop(path, "m_UseSRPBatcher").boolValue, $"{path} SRP batcher");
            Assert.IsNotNull(Prop(path, "m_VolumeProfile").objectReferenceValue, $"{path} default volume profile");
        }

        private static SerializedProperty Prop(string path, string name)
        {
            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(a, $"{path} failed to load as a URP asset (missing, or a corrupted / zeroed .asset).");
            var prop = new SerializedObject(a).FindProperty(name);
            Assert.IsNotNull(prop, $"{path}.{name} not found - field renamed? Update this test.");
            return prop;
        }
    }
}
