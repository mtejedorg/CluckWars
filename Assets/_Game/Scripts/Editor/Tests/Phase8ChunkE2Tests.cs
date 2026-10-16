using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 E2 (decision a): three FIXED see-through-fence targets for the local player — the own deposit pad and its
    /// two home-side piles — on top of D4's chicken holes. A target opens only when an OUTER fence covers it AND the local
    /// chicken carries food, is within ~8 m, or the round is in GET READY; it eases at the chicken holes' rate; it has its
    /// own shader array so it can never starve a rival's slot; inner arm walls and coops stay local-bird-only; nothing
    /// is networked.
    /// </summary>
    public class Phase8ChunkE2Tests
    {
        private const string HlslPath = "Assets/_Game/Art/Shaders/CWFenceFade.hlsl";
        private const string DriverPath = "Assets/_Game/Scripts/Visuals/FenceFadeDriver.cs";
        private const string TargetsPath = "Assets/_Game/Scripts/Visuals/FenceFixedTargets.cs";
        private const string SwapPath = "Assets/_Game/Scripts/Visuals/FencePerformanceSwap.cs";
        private const string MapScene = "Assets/_Game/Scenes/Map.unity";
        private const float Dt = 1f / 60f;

        // The match camera: 30 degrees down, looking along the diagonal from the seat's corner toward the centre.
        private static readonly Vector3 TowardCamera = new Vector3(0f, 0.5f, -0.866f).normalized;
        // An outer fence panel south of the target, between it and the camera.
        private static readonly Bounds Panel = new Bounds(new Vector3(0f, 1f, -4f), new Vector3(6f, 2f, 0.4f));
        private static readonly List<Bounds> Fence = new List<Bounds> { Panel };

        // ---- gating: carrying / distance / GET READY / not occluded ------------------------------------------

        [TestCase(true, true, 50f, false, true, TestName = "occluded + carrying, far away")]
        [TestCase(true, false, 50f, true, true, TestName = "occluded + GET READY, far away")]
        [TestCase(true, false, 7.9f, false, true, TestName = "occluded + empty-handed, 7.9 m")]
        [TestCase(true, false, 8f, false, true, TestName = "occluded + empty-handed, exactly 8 m")]
        [TestCase(true, false, 8.1f, false, false, TestName = "occluded + empty-handed, 8.1 m")]
        [TestCase(true, false, 40f, false, false, TestName = "occluded + empty-handed, far = closed")]
        [TestCase(false, true, 0f, true, false, TestName = "NOT occluded never opens, even carrying on the pad in GET READY")]
        [TestCase(false, false, 1f, false, false, TestName = "not occluded, near")]
        public void Gating_NeedsCoverAndOneOfCarryNearGetReady(bool occluded, bool carrying, float distance, bool getReady, bool expected)
        {
            Assert.AreEqual(expected, FenceFadeRules.FixedTargetWanted(occluded, carrying, distance, getReady));
        }

        [Test]
        public void InRange_IsTheCheapHalf_OfWanted()
        {
            foreach (bool carrying in new[] { false, true })
            foreach (bool ready in new[] { false, true })
            foreach (float d in new[] { 0f, 7.99f, 8f, 8.01f, 100f })
            {
                bool inRange = FenceFadeRules.FixedTargetInRange(carrying, d, ready);
                Assert.AreEqual(inRange, FenceFadeRules.FixedTargetWanted(true, carrying, d, ready));
                Assert.IsFalse(FenceFadeRules.FixedTargetWanted(false, carrying, d, ready));
            }
        }

        [Test]
        public void Easing_IsTheChickenHolesRate_BothWays_AndGatesCloseAgain()
        {
            // Opens in FadeSeconds (0.15 s) once wanted ...
            float s = 0f;
            int frames = 0;
            while (s < 1f && frames < 600) { s = FenceFadeRules.StepStrengthToward(s, 1f, Dt); frames++; }
            Assert.AreEqual(Mathf.CeilToInt(FenceFadeRules.FadeSeconds / Dt), frames, 1, "frames to fully open");
            Assert.AreEqual(0.15f, FenceFadeRules.FadeSeconds, 1e-6f);
            // ... and is part-way after a third of that, so it is an ease and not a pop.
            Assert.AreEqual(1f / 3f, FenceFadeRules.StepStrengthToward(0f, 1f, FenceFadeRules.FadeSeconds / 3f), 1e-4f);

            // The player walks out of range: wanted -> false, the hole closes in the same time.
            s = 1f;
            frames = 0;
            while (s > 0f && frames < 600)
            {
                bool wanted = FenceFadeRules.FixedTargetWanted(true, false, 12f, false);
                s = FenceFadeRules.StepStrengthToward(s, wanted ? 1f : 0f, Dt);
                frames++;
            }
            Assert.AreEqual(Mathf.CeilToInt(FenceFadeRules.FadeSeconds / Dt), frames, 1, "frames to fully close");
        }

        // ---- occlusion: the near rim counts, not only the centre ---------------------------------------------

        [Test]
        public void TargetOccluded_CentreClearButNearRimCovered_IsCovered()
        {
            // A pile 3.6 m inside the panel: rays from its centre climb over the 2 m panel (the camera looks down 30 degrees),
            // but a probe on the near rim, 2.1 m closer to the panel, passes through it.
            var centre = new Vector3(0f, 0f, -0.2f);
            Assert.IsFalse(FenceFadeRules.IsOccluded(centre, 1.1f, TowardCamera, 40f, Fence), "centre alone is clear (the premise)");
            Assert.IsTrue(FenceFadeRules.IsTargetOccluded(centre, 2.5f, 1.1f, TowardCamera, 40f, Fence), "near-rim probe sees the fence");
        }

        [Test]
        public void TargetOccluded_ClearOfTheFence_IsNotCovered()
        {
            Assert.IsFalse(FenceFadeRules.IsTargetOccluded(new Vector3(0f, 0f, 8f), 2.5f, 0.1f, TowardCamera, 40f, Fence));
            Assert.IsFalse(FenceFadeRules.IsTargetOccluded(Vector3.zero, 2.5f, 0.1f, TowardCamera, 40f, new List<Bounds>()));
            Assert.IsFalse(FenceFadeRules.IsTargetOccluded(Vector3.zero, 2.5f, 0.1f, TowardCamera, 40f, null));
        }

        [Test]
        public void TargetOccluded_BehindTheFenceFromTheCamera_IsCovered_CentreOnly()
        {
            Assert.IsTrue(FenceFadeRules.IsTargetOccluded(new Vector3(0f, 0f, -3f), 0f, 0.1f, TowardCamera, 40f, Fence));
        }

        // ---- which piles: the two home-side ones, derived from data ------------------------------------------

        [Test]
        public void PickHomePiles_SkipsPersonalIsland_IgnoresCentre_TakesTheNextTwo()
        {
            var pad = new Vector3(20f, 0f, 20f);
            var piles = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),        // 0 centre (nearest by chance is not the point)
                new Vector3(-15f, 0f, -15f),    // 1 a rival's personal island, across the arena
                new Vector3(15f, 0f, 15f),      // 2 own personal (nearest non-centre)
                new Vector3(0f, 0f, 19f),       // 3 contested A
                new Vector3(19f, 0f, 0f),       // 4 contested B
                new Vector3(-20f, 0f, 0f),      // 5 far
            };
            var centre = new List<bool> { true, false, false, false, false, false };
            var chosen = new int[2];
            Assert.AreEqual(2, FenceFadeRules.PickHomePiles(pad, piles, centre, chosen));
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, chosen);
        }

        [Test]
        public void PickHomePiles_FewerPilesThanSlots_ReturnsWhatExists_NullsAreSafe()
        {
            var chosen = new int[2];
            Assert.AreEqual(0, FenceFadeRules.PickHomePiles(Vector3.zero, new List<Vector3>(), null, chosen));
            Assert.AreEqual(0, FenceFadeRules.PickHomePiles(Vector3.zero, new List<Vector3> { Vector3.one }, null, chosen), "the only pile is the personal island");
            Assert.AreEqual(1, FenceFadeRules.PickHomePiles(Vector3.zero, new List<Vector3> { Vector3.one, Vector3.one * 5f }, null, chosen));
            Assert.AreEqual(0, FenceFadeRules.PickHomePiles(Vector3.zero, null, null, chosen));
            Assert.AreEqual(0, FenceFadeRules.PickHomePiles(Vector3.zero, new List<Vector3> { Vector3.one }, null, null));
        }

        [Test]
        public void PickHomePiles_OnTheShippedLayout_IsTheTwoContestedPilesBesideEachBase()
        {
            var piles = ShippedMap.ShippedPiles(Vector2.zero, Vector2.zero);
            var positions = piles.Select(p => p.Pos).ToList();
            var isCentre = piles.Select(p => p.Name == "Center").ToList();
            var corners = ShippedMap.Corners();
            for (int seat = 0; seat < 4; seat++)
            {
                var pad = MapGenerator.SpawnPointNominal(corners[seat]);
                var chosen = new int[FenceFadeRules.HomePileCount];
                Assert.AreEqual(2, FenceFadeRules.PickHomePiles(pad, positions, isCentre, chosen), $"seat {seat}");
                var names = chosen.Select(i => piles[i].Name).OrderBy(n => n).ToArray();
                // Contested i sits on the edge between corner i and corner i + 1: the pad's own two edges are i and i - 1.
                var expected = new[] { $"Contested{seat}", $"Contested{(seat + 3) % 4}" }.OrderBy(n => n).ToArray();
                CollectionAssert.AreEqual(expected, names, $"seat {seat}");
            }
        }

        // ---- the hole: bigger than a chicken's, derived from the target ---------------------------------------

        [Test]
        public void TargetHole_ForThePad_IsMuchBiggerThanAChickens_AndCoversTheRing()
        {
            const float viewHeight = 11.2f; // match camera: orthographic size 5.6
            float pad = FenceFadeRules.TargetInnerRadius(2.5f, viewHeight);
            Assert.AreEqual((2.5f + FenceFadeRules.FixedTargetMarginMetres) / viewHeight, pad, 1e-6f);
            Assert.Greater(pad, 2f * FenceFadeRules.InnerRadius - 0.01f, "at least ~2x a chicken-sized hole");
            Assert.GreaterOrEqual(pad * viewHeight, 2.5f + 0.4f, "the fully-clear core reaches past the 2.5 m deposit ring");
            Assert.AreEqual(pad - FenceFadeRules.InnerRadius, FenceFadeRules.TargetRadiusShift(2.5f, viewHeight), 1e-6f);
        }

        [Test]
        public void TargetHole_Radius_IsClamped_AndNeverBelowAChickens()
        {
            Assert.AreEqual(FenceFadeRules.InnerRadius, FenceFadeRules.TargetInnerRadius(0.1f, 11.2f), 1e-6f);
            Assert.AreEqual(FenceFadeRules.FixedTargetMaxInnerRadius, FenceFadeRules.TargetInnerRadius(50f, 11.2f), 1e-6f);
            Assert.AreEqual(FenceFadeRules.InnerRadius, FenceFadeRules.TargetInnerRadius(2.5f, 0f), 1e-6f, "degenerate view height");
            Assert.GreaterOrEqual(FenceFadeRules.TargetRadiusShift(0.1f, 11.2f), 0f);
        }

        [Test]
        public void ShiftedHole_IsTheChickenHoleMovedOutward_Exactly()
        {
            const float front = 10f, target = 14f;
            float shift = FenceFadeRules.TargetRadiusShift(2.5f, 11.2f);
            Assert.Greater(shift, 0.1f);
            // No shift = a chicken's hole.
            Assert.AreEqual(FenceFadeRules.FragmentFade(1f, 0.1f, front, target), FenceFadeRules.ShiftedFragmentFade(1f, 0.1f, 0f, front, target), 1e-7f);
            // At a pad-sized radius the fence is fully open where a chicken hole would already be closed.
            float d = FenceFadeRules.OuterRadius + 0.5f * shift;
            Assert.AreEqual(0f, FenceFadeRules.FragmentFade(1f, d, front, target), 1e-6f, "a chicken's hole is closed here");
            Assert.AreEqual(FenceFadeRules.MaxFade, FenceFadeRules.ShiftedFragmentFade(1f, d, shift, front, target), 1e-6f, "the pad's is wide open");
            // The whole profile moves rigidly: the edge band keeps its width.
            Assert.AreEqual(FenceFadeRules.FragmentFade(1f, 0.135f, front, target),
                FenceFadeRules.ShiftedFragmentFade(1f, 0.135f + shift, shift, front, target), 1e-6f);
            // Still gated to fragments in front of the target and scaled by strength.
            Assert.AreEqual(0f, FenceFadeRules.ShiftedFragmentFade(1f, 0f, shift, 20f, target), 1e-6f, "fence BEHIND the target stays solid");
            Assert.AreEqual(0f, FenceFadeRules.ShiftedFragmentFade(0f, 0f, shift, front, target), 1e-6f);
        }

        [Test]
        public void TargetHoleOnScreen_UsesItsOwnWiderMargin_AndTheAspectOnX()
        {
            var justOff = new Vector3(-FenceFadeRules.OuterRadius - 0.05f, 0.5f, 10f);
            Assert.IsFalse(FenceFadeRules.HoleOnScreen(justOff));
            Assert.IsFalse(FenceFadeRules.TargetHoleOnScreen(justOff, 0f, 1f));
            Assert.IsTrue(FenceFadeRules.TargetHoleOnScreen(justOff, 0.15f, 1f), "a wider hole reaches the screen from further out");
            Assert.IsFalse(FenceFadeRules.TargetHoleOnScreen(new Vector3(0.5f, 0.5f, -1f), 0.15f, 1.78f), "behind the camera");

            // 16:9: a margin of 0.3 screen HEIGHTS is 0.169 of the width, not 0.3 of it.
            Assert.IsTrue(FenceFadeRules.TargetHoleOnScreen(new Vector3(1.15f, 0.5f, 10f), 0.15f, 1.78f), "inside the hole's reach");
            Assert.IsFalse(FenceFadeRules.TargetHoleOnScreen(new Vector3(1.25f, 0.5f, 10f), 0.15f, 1.78f), "a pad 14 m off to the side cannot touch the screen");
            Assert.IsTrue(FenceFadeRules.TargetHoleOnScreen(new Vector3(1.25f, 0.5f, 10f), 0.15f, 1f), "square screen: same 0.3");
            Assert.IsFalse(FenceFadeRules.TargetHoleOnScreen(new Vector3(0.5f, 1.35f, 10f), 0.15f, 1.78f), "y margin is in heights already");
        }

        // ---- slot priority: fixed targets cannot starve rivals, and vice versa --------------------------------

        [Test]
        public void FixedTargets_HaveTheirOwnSlots_RivalsKeepAllEight()
        {
            Assert.AreEqual(3, FenceFadeRules.MaxFixedTargets);
            Assert.AreEqual(2, FenceFadeRules.HomePileCount);
            Assert.AreEqual(8, FenceFadeRules.MaxBirdSlots, "the chicken slots did not shrink to make room");

            // 11 strong chicken candidates still fill exactly the 8 chicken slots; selection never sees the targets.
            var strengths = Enumerable.Repeat(1f, 11).ToList();
            var chosen = new int[FenceFadeRules.MaxBirdSlots];
            Assert.AreEqual(8, FenceFadeRules.SelectSlots(strengths, null, strengths.Count, chosen, FenceFadeRules.MaxBirdSlots));

            string hlsl = File.ReadAllText(HlslPath);
            Assert.AreEqual(FenceFadeRules.MaxBirdSlots, int.Parse(Regex.Match(hlsl, @"#define\s+CW_FENCE_MAX_BIRDS\s+(\d+)").Groups[1].Value));
            Assert.AreEqual(FenceFadeRules.MaxFixedTargets, int.Parse(Regex.Match(hlsl, @"#define\s+CW_FENCE_MAX_TARGETS\s+(\d+)").Groups[1].Value));
            StringAssert.Contains("_CW_FenceBirds[CW_FENCE_MAX_BIRDS]", hlsl);
            StringAssert.Contains("_CW_FenceTargets[CW_FENCE_MAX_TARGETS]", hlsl);
            StringAssert.Contains("_CW_FenceTargetShift", hlsl);
            StringAssert.Contains("_CW_FenceTargetCount", hlsl);

            string driver = File.ReadAllText(TargetsPath);
            foreach (var id in new[] { "_CW_FenceTargets", "_CW_FenceTargetShift", "_CW_FenceTargetCount" })
                StringAssert.Contains("\"" + id + "\"", driver);
            StringAssert.DoesNotContain("_CW_FenceBirds", driver, "targets never write into the chicken array");
        }

        [Test]
        public void Keyword_FollowsTheTargetsToo()
        {
            // The driver feeds max(local, strongest chicken slot, strongest fixed target) to KeywordToggle.
            Assert.IsTrue(FenceFadeRules.KeywordToggle(false, 0.3f, out bool on) && on, "a target alone turns the variant on");
            Assert.IsTrue(FenceFadeRules.KeywordToggle(true, 0f, out bool off) && !off, "all closed turns it off");
            StringAssert.Contains("_fixedTargets.MaxVisibleStrength", File.ReadAllText(DriverPath));
        }

        // ---- shader + inner walls untouched ------------------------------------------------------------------

        [Test]
        public void Shader_TargetLoop_LivesInsideTheOuterFenceKeywordBlock_AndKeepsOneExit()
        {
            string src = File.ReadAllText(HlslPath);
            int loop = src.IndexOf("_CW_FenceTargets[t]", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(loop, 0, "target loop");
            // Walk back to the nearest preprocessor opener: it must be the outer-fence keyword (inner walls / coops compile without it).
            var before = src.Substring(0, loop);
            int open = before.LastIndexOf("#if defined(_CW_FENCE_ALL_BIRDS)", System.StringComparison.Ordinal);
            int close = before.LastIndexOf("#endif", System.StringComparison.Ordinal);
            Assert.Greater(open, close, "the fixed-target loop must be inside #if defined(_CW_FENCE_ALL_BIRDS) so inner arm walls and coops never open for it");
            var m = Regex.Match(src, @"float CW_FenceFadeClipRim\(float4 positionCS\)\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
            Assert.AreEqual(1, Regex.Matches(m.Groups["body"].Value, @"\breturn\b").Count, "one exit point (C16)");
        }

        [Test]
        public void ShippedMap_InnerWallsAndCoops_DoNotReadTheTargets()
        {
            // The targets are read only where _CW_FENCE_ALL_BIRDS is on; D4 already pins that to the outer runs. Re-pin it here so
            // an E2 edit that flips a material cannot open an inner wall.
            var inner = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Prop_wall_segment_Inner.mat");
            var coop = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Prop_chicken_coop.mat");
            var outer = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Prop_wall_segment.mat");
            Assert.IsFalse(FenceFadeRules.FadesForAllBirds(inner));
            Assert.IsFalse(FenceFadeRules.FadesForAllBirds(coop));
            Assert.IsTrue(FenceFadeRules.FadesForAllBirds(outer));
        }

        [Test]
        public void PerformanceModeTwin_SharesTheFenceMaterials_SoItOpensTheSameWay()
        {
            string swap = File.ReadAllText(SwapPath);
            StringAssert.Contains("twin.sharedMaterials = original.sharedMaterials", swap);
        }

        // ---- local, visual only -----------------------------------------------------------------------------

        [Test]
        public void NothingNetworked_NoRpc_NoStateWrites_NoDecoyOrAuthorityBranches()
        {
            foreach (var path in new[] { TargetsPath, DriverPath })
            {
                string code = Regex.Replace(File.ReadAllText(path), @"///[^\n]*|//[^\n]*", "");
                foreach (var token in new[] { "[Networked]", "[Rpc", "RPC_", ".RPC", "NetworkBehaviour", "HasStateAuthority", "HasInputAuthority",
                                              "IsDecoy", "Doppelganger", "IsBot", "PlayerRef", "Runner.", "Debug.Log" })
                    Assert.IsFalse(code.Contains(token), $"{Path.GetFileName(path)} must not contain {token}");
                // Reads of replicated state are fine; writes are not.
                Assert.IsFalse(Regex.IsMatch(code, @"\.(Cargo|Amount|FoodTotal|VisualOpacity)\s*(=|\+=|-=)[^=]"), $"{Path.GetFileName(path)} writes replicated state");
            }
        }

        // ---- the shipped map: every seat's pad and both home piles ARE behind the near fence ------------------

        [Test]
        public void ShippedMap_EverySeat_PadAndBothHomePilesAreCovered_PersonalIslandIsNot()
        {
            var scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);
            try
            {
                var outer = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
                    .Where(r => r.sharedMaterial != null && r.sharedMaterial.shader != null
                                && r.sharedMaterial.shader.name == FenceFadeRules.ShaderName && FenceFadeRules.FadesForAllBirds(r.sharedMaterial))
                    .Select(r => r.bounds).ToList();
                Assert.GreaterOrEqual(outer.Count, 40, "outer fence renderers");

                float padRadius = ShippedMap.BaseDepositRadius;
                var piles = ShippedMap.ShippedPiles(Vector2.zero, Vector2.zero);
                var positions = piles.Select(p => p.Pos).ToList();
                var isCentre = piles.Select(p => p.Name == "Center").ToList();
                var corners = ShippedMap.Corners();
                var problems = new List<string>();
                for (int seat = 0; seat < 4; seat++)
                {
                    var pad = MapGenerator.SpawnPointNominal(corners[seat]);
                    // The match camera yaws to face the seat's own corner (30 deg down): toward-camera = outward along the diagonal, up.
                    var outward = new Vector3(pad.x, 0f, pad.z).normalized;
                    var toward = (outward * Mathf.Cos(30f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized;

                    if (!FenceFadeRules.IsTargetOccluded(pad, padRadius, FenceFadeRules.PadProbeHeight, toward, 60f, outer))
                        problems.Add($"seat {seat}: own pad is not covered by an outer fence");

                    var chosen = new int[FenceFadeRules.HomePileCount];
                    FenceFadeRules.PickHomePiles(pad, positions, isCentre, chosen);
                    foreach (int i in chosen)
                        if (!Covered(piles[i], toward, outer))
                            problems.Add($"seat {seat}: home pile {piles[i].Name} is not covered");

                    var personal = piles.First(p => p.Name == $"Personal{seat}");
                    if (Covered(personal, toward, outer))
                        problems.Add($"seat {seat}: the personal island is covered (it was skipped as 'never behind a fence')");
                }
                Assert.IsEmpty(problems, string.Join("\n", problems));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool Covered(ShippedMap.PileSpec pile, Vector3 toward, IReadOnlyList<Bounds> outer)
        {
            var flat = new Vector2(toward.x, toward.z);
            float rim = PileFootprintShape.RadiusToward(flat, pile.Yaw, pile.Footprint);
            return FenceFadeRules.IsTargetOccluded(pile.Pos, rim, FenceFadeRules.PileProbeHeight, toward, 60f, outer);
        }
    }
}
