using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D4 "all OUTER fences fade when a chicken is behind them" (Maestro). Besides the local bird's hole
    /// (every fence material, unchanged), every other chicken — rivals and Doppelganger decoys through one identical
    /// path — covered by an OUTER fence gets its own hole of the same shape and timing, up to
    /// <see cref="FenceFadeRules.MaxBirdSlots"/>, scaled by its networked visual opacity. The inner arm walls
    /// (<c>Prop_wall_segment_Inner.mat</c>) and the coops have no <c>_CW_FENCE_ALL_BIRDS</c>, so they never open
    /// for a rival. The global <c>_CW_FENCE_FADE</c> keyword stays off whenever no hole is open.
    /// </summary>
    public class Phase8ChunkD4Tests
    {
        private const string ShaderPath = "Assets/_Game/Art/Shaders/FenceFadeLit.shader";
        private const string HlslPath = "Assets/_Game/Art/Shaders/CWFenceFade.hlsl";
        private const string DriverPath = "Assets/_Game/Scripts/Visuals/FenceFadeDriver.cs";
        private const string OuterMat = "Assets/_Game/Art/Materials/Prop_wall_segment.mat";
        private const string InnerMat = "Assets/_Game/Art/Materials/Prop_wall_segment_Inner.mat";
        private const string CoopMat = "Assets/_Game/Art/Materials/Prop_chicken_coop.mat";
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const float Dt = 1f / 60f;

        // The match camera looks down the -Z/-Y diagonal; for the ray test only "toward the camera" matters.
        private static readonly Vector3 TowardCamera = new Vector3(0f, 0.6f, -0.8f).normalized;
        // An outer fence panel south of the arena (between the bird and the camera) and an inner arm panel.
        private static readonly Bounds OuterPanel = new Bounds(new Vector3(0f, 0.7f, -1f), new Vector3(2f, 1.4f, 0.3f));
        private static readonly Bounds InnerPanel = new Bounds(new Vector3(10f, 0.7f, -1f), new Vector3(2f, 1.4f, 0.3f));
        private static readonly List<Bounds> OuterOnly = new List<Bounds> { OuterPanel };

        /// <summary>The driver's per-chicken pipeline for a non-local chicken, frame by frame (pure twin).</summary>
        private static float RunOtherBird(Vector3 feet, float visualOpacity, IReadOnlyList<Bounds> outer, int frames, float start = 0f)
        {
            float s = start;
            for (int f = 0; f < frames; f++)
            {
                bool covered = FenceFadeRules.IsOccluded(feet, 1f, TowardCamera, 30f, outer);
                s = FenceFadeRules.StepStrengthToward(s, FenceFadeRules.TargetStrength(covered, visualOpacity), Dt);
            }
            return s;
        }

        // ---- rival behind an outer fence -> slot + keyword -------------------------------------------------

        [Test]
        public void RivalBehindAnOuterFence_GetsASlot_AndTheKeywordTurnsOn()
        {
            float rival = RunOtherBird(new Vector3(0f, 0f, 0f), 1f, OuterOnly, 30);
            Assert.AreEqual(1f, rival, 1e-5f, "covered rival eases to full strength");

            var chosen = new int[FenceFadeRules.MaxBirdSlots];
            int n = FenceFadeRules.SelectSlots(new[] { rival }, new[] { 0f }, 1, chosen, FenceFadeRules.MaxBirdSlots);
            Assert.AreEqual(1, n);
            Assert.AreEqual(0, chosen[0]);

            float localStrength = 0f; // the local bird is out in the open
            Assert.IsTrue(FenceFadeRules.KeywordToggle(false, Mathf.Max(localStrength, rival), out bool want));
            Assert.IsTrue(want, "a rival's hole alone turns the fade variant on");
        }

        [Test]
        public void RivalBehindAnOuterFence_OpensTheFence_InFrontOfItOnly()
        {
            var rivalHole = new[] { new FenceFadeRules.HoleSample(1f, 0f, 10f) };
            var noLocal = new FenceFadeRules.HoleSample(0f, 0f, 10f);
            Assert.AreEqual(FenceFadeRules.MaxFade, FenceFadeRules.CombinedFragmentFade(true, 8f, noLocal, rivalHole), 1e-5f,
                "outer fence in front of the rival: full peak fade");
            Assert.AreEqual(0f, FenceFadeRules.CombinedFragmentFade(true, 12f, noLocal, rivalHole), 1e-6f,
                "fence behind the rival stays solid (same depth gate as the local hole)");
        }

        [Test]
        public void NobodyBehindAFence_NoSlots_AndTheKeywordTurnsOff()
        {
            float rival = RunOtherBird(new Vector3(0f, 0f, 5f), 1f, OuterOnly, 30, start: 1f); // walked out
            Assert.AreEqual(0f, rival, "the hole closes once nobody is covered");

            var chosen = new int[FenceFadeRules.MaxBirdSlots];
            Assert.AreEqual(0, FenceFadeRules.SelectSlots(new[] { rival, 0f }, new[] { 0f, 0f }, 2, chosen, FenceFadeRules.MaxBirdSlots));
            Assert.IsTrue(FenceFadeRules.KeywordToggle(true, Mathf.Max(0f, rival), out bool want));
            Assert.IsFalse(want, "no hole open anywhere -> back on the plain Lit variant");
        }

        [Test]
        public void KeywordFollowsTheStrongestHole_LocalOrOther_WithoutChurn()
        {
            bool on = false;
            int toggles = 0;
            float local = 0f, rival = 0f;
            for (int frame = 0; frame < 120; frame++)
            {
                local = FenceFadeRules.StepStrength(local, frame >= 10 && frame < 40, Dt);
                rival = FenceFadeRules.StepStrengthToward(rival, frame >= 30 && frame < 70 ? 1f : 0f, Dt);
                float strongest = Mathf.Max(local, rival);
                if (FenceFadeRules.KeywordToggle(on, strongest, out bool want)) { on = want; toggles++; }
                Assert.AreEqual(strongest > 0f, on, $"frame {frame}");
            }
            Assert.AreEqual(2, toggles, "overlapping local + rival holes: one enable, one disable");
        }

        // ---- inner walls ----------------------------------------------------------------------------------

        [Test]
        public void InnerWall_RivalBehindIt_DoesNotFade()
        {
            var rivalHole = new[] { new FenceFadeRules.HoleSample(1f, 0f, 10f) };
            var noLocal = new FenceFadeRules.HoleSample(0f, 0f, 10f);
            Assert.AreEqual(0f, FenceFadeRules.CombinedFragmentFade(false, 8f, noLocal, rivalHole), 1e-6f,
                "inner arm wall (no _CW_FENCE_ALL_BIRDS) ignores every other chicken's hole");

            // Selection: the driver tests other chickens against the outer panels only.
            Assert.IsFalse(FenceFadeRules.IsOccluded(new Vector3(10f, 0f, 0f), 1f, TowardCamera, 30f, OuterOnly),
                "a rival behind an inner panel is not covered by any OUTER panel");
            Assert.IsTrue(FenceFadeRules.IsOccluded(new Vector3(10f, 0f, 0f), 1f, TowardCamera, 30f, new List<Bounds> { InnerPanel }),
                "(sanity: the same rival IS behind the inner panel)");
        }

        [Test]
        public void LocalBird_OnInnerAndOuterWalls_IsUnchanged()
        {
            var others = new[] { new FenceFadeRules.HoleSample(0f, 0f, 10f) };
            foreach (float strength in new[] { 0f, 0.4f, 1f })
            foreach (float dist in new[] { 0f, 0.1f, 0.13f, 0.2f })
            foreach (float frag in new[] { 8f, 9.9f, 12f })
            {
                var local = new FenceFadeRules.HoleSample(strength, dist, 10f);
                float single = FenceFadeRules.FragmentFade(strength, dist, frag, 10f);
                Assert.AreEqual(single, FenceFadeRules.CombinedFragmentFade(false, frag, local, others), 1e-6f, "inner");
                Assert.AreEqual(single, FenceFadeRules.CombinedFragmentFade(true, frag, local, others), 1e-6f, "outer, no other hole");
            }
            Assert.AreEqual(FenceFadeRules.StepStrength(0.3f, true, Dt), FenceFadeRules.StepStrengthToward(0.3f, 1f, Dt), 1e-7f,
                "the local bird's easing is the same rate as before");
        }

        // ---- decoys = rivals ------------------------------------------------------------------------------

        [Test]
        public void Decoy_IsTreatedExactlyLikeARival()
        {
            // Same position, same networked opacity -> identical strength on every frame.
            var feet = new Vector3(0.3f, 0f, 0.1f);
            for (int frames = 1; frames <= 20; frames++)
                Assert.AreEqual(RunOtherBird(feet, 1f, OuterOnly, frames), RunOtherBird(feet, 1f, OuterOnly, frames), 0f);

            // And nothing in the driver can tell them apart: no decoy / owner / authority branches.
            string src = File.ReadAllText(DriverPath);
            string code = Regex.Replace(src, @"///[^\n]*|//[^\n]*", "");
            foreach (var token in new[] { "IsDecoy", "Doppelganger", "HasInputAuthority", "HasStateAuthority", "IsBot", "PlayerRef" })
                Assert.IsFalse(code.Contains(token), $"FenceFadeDriver code must not branch on {token} (decoys must look like rivals)");
        }

        [TestCase(false, 1f, 0f)]
        [TestCase(true, 1f, 1f)]
        [TestCase(true, 0f, 1f)]    // never replicated yet -> fully visible (ChickenVisuals' rule)
        [TestCase(true, 0.2f, 0.2f)] // Invisibility / Smoke Roost ghost -> ghost-faint hole
        [TestCase(true, 0.55f, 0.55f)]
        [TestCase(false, 0.2f, 0f)]
        public void TargetStrength_IsTheVisualOpacity_WhileCovered(bool covered, float opacity, float expected)
        {
            Assert.AreEqual(expected, FenceFadeRules.TargetStrength(covered, opacity), 1e-6f);
        }

        [Test]
        public void GhostedRival_HoleNeverClearsMoreThanTheGhostShows()
        {
            float s = RunOtherBird(Vector3.zero, 0.2f, OuterOnly, 30);
            Assert.AreEqual(0.2f, s, 1e-5f);
            float cleared = FenceFadeRules.FragmentFade(s, 0f, 8f, 10f);
            Assert.LessOrEqual(cleared, 0.2f, "a 0.2 ghost opens at most a 20 % screen-door in the fence");
        }

        // ---- slots ----------------------------------------------------------------------------------------

        [Test]
        public void SlotCap_IsRespected_StrongestFirst_TiesToTheScreenCentre()
        {
            const int count = 12;
            var strengths = new float[count];
            var priority = new float[count];
            for (int i = 0; i < count; i++) { strengths[i] = i < 6 ? 1f : 0.05f * i; priority[i] = count - i; }
            strengths[11] = 0f; // not covered: never gets a slot
            var chosen = new int[FenceFadeRules.MaxBirdSlots];
            int n = FenceFadeRules.SelectSlots(strengths, priority, count, chosen, FenceFadeRules.MaxBirdSlots);
            Assert.AreEqual(FenceFadeRules.MaxBirdSlots, n);
            // Six full-strength ties, ordered by priority (lower = nearer the screen centre): 5,4,3,2,1,0; then 0.5 (i=10), 0.45 (i=9).
            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2, 1, 0, 10, 9 }, chosen.Take(n).ToArray());
            CollectionAssert.DoesNotContain(chosen.Take(n), 11);
        }

        [TestCase(0.5f, 0.5f, 10f, true)]
        [TestCase(-0.1f, 0.5f, 10f, true)]   // centre just off the left edge: the hole still reaches the screen
        [TestCase(1.1f, 1.1f, 10f, true)]
        [TestCase(-0.3f, 0.5f, 10f, false)]  // a rival behind the outer fence across the arena
        [TestCase(0.5f, 1.4f, 10f, false)]
        [TestCase(0.5f, 0.5f, -1f, false)]   // behind the camera
        public void OffScreenHoles_TakeNoSlot(float x, float y, float z, bool expected)
        {
            Assert.AreEqual(expected, FenceFadeRules.HoleOnScreen(new Vector3(x, y, z)));
        }

        [Test]
        public void SlotCap_SmallerOutputArray_IsNeverOverrun()
        {
            var chosen = new int[2];
            Assert.AreEqual(2, FenceFadeRules.SelectSlots(new[] { 1f, 1f, 1f }, null, 3, chosen, FenceFadeRules.MaxBirdSlots));
        }

        // ---- shader + materials ---------------------------------------------------------------------------

        [Test]
        public void ShaderSlotCount_MatchesTheRules()
        {
            string src = File.ReadAllText(HlslPath);
            var m = Regex.Match(src, @"#define\s+CW_FENCE_MAX_BIRDS\s+(\d+)");
            Assert.IsTrue(m.Success, "CW_FENCE_MAX_BIRDS define");
            Assert.AreEqual(FenceFadeRules.MaxBirdSlots, int.Parse(m.Groups[1].Value));
            StringAssert.Contains("_CW_FenceBirds[CW_FENCE_MAX_BIRDS]", src);
            StringAssert.Contains("_CW_FenceBirdCount", src);
        }

        [Test]
        public void Shader_DeclaresTheAllBirdsFeature_InEveryClippingPass()
        {
            string src = File.ReadAllText(ShaderPath);
            int fade = Regex.Matches(src, @"#pragma\s+multi_compile_fragment\s+_\s+" + FenceFadeRules.KeywordName + @"\b").Count;
            int all = Regex.Matches(src, @"#pragma\s+shader_feature_local_fragment\s+_\s+" + FenceFadeRules.AllBirdsKeywordName + @"\b").Count;
            Assert.AreEqual(4, fade);
            Assert.AreEqual(fade, all, "every pass that clips must know whether it is an outer fence");
        }

        [Test]
        public void Materials_OuterFadesForAllBirds_InnerAndCoopDoNot()
        {
            var outer = AssetDatabase.LoadAssetAtPath<Material>(OuterMat);
            var inner = AssetDatabase.LoadAssetAtPath<Material>(InnerMat);
            var coop = AssetDatabase.LoadAssetAtPath<Material>(CoopMat);
            Assert.IsNotNull(inner, InnerMat);
            Assert.IsTrue(FenceFadeRules.FadesForAllBirds(outer), "Prop_wall_segment.mat (boundary runs + prefab default)");
            Assert.IsFalse(FenceFadeRules.FadesForAllBirds(inner), "Prop_wall_segment_Inner.mat (arm panels)");
            Assert.IsFalse(FenceFadeRules.FadesForAllBirds(coop), "Prop_chicken_coop.mat");
            // The inner twin is the outer material minus the keyword: same shader, textures, colour, instancing.
            Assert.AreEqual(outer.shader, inner.shader);
            Assert.AreEqual(outer.mainTexture, inner.mainTexture);
            Assert.AreEqual(outer.GetColor("_BaseColor"), inner.GetColor("_BaseColor"));
            Assert.AreEqual(outer.GetFloat("_Smoothness"), inner.GetFloat("_Smoothness"));
            Assert.IsTrue(inner.enableInstancing && outer.enableInstancing);
            CollectionAssert.AreEquivalent(
                outer.shaderKeywords.Where(k => k != FenceFadeRules.AllBirdsKeywordName), inner.shaderKeywords);
        }

        [Test]
        public void ShippedMap_OuterRunsFadeForAllBirds_ArmPanelsAndCoopsDoNot()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);
            try
            {
                var outerName = new Regex(@"^Wall_[NSEW]_[0-9]+$");
                var innerName = new Regex(@"^InnerWall_[0-9]+_[0-9]+$");
                var coopName = new Regex(@"^Base[0-9]+_Coop$");
                int outer = 0, inner = 0, coops = 0;
                var wrong = new List<string>();
                foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
                {
                    string owner = r.transform.parent != null ? r.transform.parent.name : r.name;
                    if (!outerName.IsMatch(owner) && outerName.IsMatch(r.name)) owner = r.name;
                    if (!innerName.IsMatch(owner) && innerName.IsMatch(r.name)) owner = r.name;
                    bool all = FenceFadeRules.FadesForAllBirds(r.sharedMaterial);
                    if (outerName.IsMatch(owner)) { outer++; if (!all) wrong.Add(owner + " (outer, should fade for all)"); }
                    else if (innerName.IsMatch(owner)) { inner++; if (all) wrong.Add(owner + " (inner, must stay local-only)"); }
                    else if (coopName.IsMatch(owner)) { coops++; if (all) wrong.Add(owner + " (coop, local-only)"); }
                    else if (all) wrong.Add(owner + "/" + r.name + " (not a fence)");
                }
                Assert.GreaterOrEqual(outer, 40, "outer fence renderers found");
                Assert.GreaterOrEqual(inner, 32, "arm panel renderers found");
                Assert.AreEqual(4, coops, "coop renderers found");
                Assert.IsEmpty(wrong, string.Join("\n", wrong));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void AllBirdsVariant_CompilesWithoutWarnings_OnGles3VulkanAndD3D()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            var sub = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            string[] passes = { "ForwardLit", "GBuffer", "DepthOnly", "DepthNormals" };
            string[] kw = { FenceFadeRules.KeywordName, FenceFadeRules.AllBirdsKeywordName, "_SPECULARHIGHLIGHTS_OFF" };
            var problems = new StringBuilder();
            int compiled = 0;
            for (int p = 0; p < sub.PassCount; p++)
            {
                var pass = sub.GetPass(p);
                if (!passes.Contains(pass.Name)) continue;
                foreach (var plat in new[] { ShaderCompilerPlatform.GLES3x, ShaderCompilerPlatform.Vulkan, ShaderCompilerPlatform.D3D })
                {
                    if (pass.Name == "GBuffer" && plat == ShaderCompilerPlatform.GLES3x) continue; // excluded by design
                    var target = plat == ShaderCompilerPlatform.D3D ? BuildTarget.StandaloneWindows64 : BuildTarget.Android;
                    var r = pass.CompileVariant(ShaderType.Fragment, kw, plat, target);
                    compiled++;
                    if (!r.Success) problems.AppendLine($"{pass.Name} {plat}: failed");
                    foreach (var msg in r.Messages)
                        problems.AppendLine($"{pass.Name} {plat}: {msg.severity} {msg.message} ({msg.file}:{msg.line})");
                }
            }
            Assert.Greater(compiled, 0, "no fence-fade passes compiled");
            Assert.IsEmpty(problems.ToString(), "shader compile messages");
        }
    }
}
