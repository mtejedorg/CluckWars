using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Balance;
using CluckWars.Gameplay;
using CluckWars.Input;
using CluckWars.Logging;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// BL-212 (the Peck hex fills over the cast) and BL-213 (a cut cast puffs and flashes). The fill and the flash are pure rules; the networked
    /// counter and the observer run without a runner; what needs a live Fusion peer (every cutter really bumping the counter) is pinned
    /// structurally, like the BL-206 cut itself (<see cref="PeckCastTests"/>).
    /// </summary>
    public sealed class PeckCastCuesTests
    {
        private const string Scripts = "Assets/_Game/Scripts/";
        private const string ControllerPath = Scripts + "Gameplay/AbilityController.cs";
        private const string HudPath = Scripts + "Input/TouchControlsController.Hud.cs";
        private const string VfxPath = Scripts + "Visuals/ChickenVFX.cs";

        private sealed class SilentLog : ILogService
        {
            public LogLevel MinLevel { get; set; }
            public bool IsEnabled(LogLevel level) => false;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, Exception exception = null) { }
        }

        private static string Body(string path, string signature) => AssassinExecuteCreditTests.MethodBody(path, signature);

        private static string Code(string path) =>
            string.Join("\n", File.ReadAllLines(path).Select(l =>
            {
                int slash = l.IndexOf("//", StringComparison.Ordinal);
                return slash >= 0 ? l.Substring(0, slash) : l;
            }));

        private static PeckAbilitySO PeckAsset() =>
            (PeckAbilitySO)TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).Find(a => a is PeckAbilitySO);

        private static ChickenStatsSO[] Foragers()
        {
            var abilities = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            var peck = abilities.Find(a => a is PeckAbilitySO);
            var passives = abilities.OfType<PassiveAbilitySO>().ToList();
            var result = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir)
                .Where(s => Enum.TryParse<ChickenClass>(s.DisplayName, out var cls) &&
                            PreEquippedLoadout.ClassForages(cls, passives, abilities, peck)).ToArray();
            Assert.IsNotEmpty(result, "no forager class");
            return result;
        }

        /// <summary>What the HUD does each frame: the slot's remaining fraction (replicated timer over the resolved cast), through the rule.</summary>
        private static float FillAt(float elapsed, float cast) => PeckCastHexRules.Fill01(true, (cast - elapsed) / cast);

        // ---- BL-212: the fill ------------------------------------------------------------------------------------

        [Test]
        public void Fill_IsElapsedOverTheEffectiveCast_ForEveryForager_AndFollowsABalanceDelta()
        {
            var peck = PeckAsset();
            Assert.IsNotNull(peck);
            foreach (var stats in Foragers())
            {
                float cast = PeckCastRules.CastSeconds(stats, peck.Duration);
                foreach (float k in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                    Assert.AreEqual(k, FillAt(cast * k, cast), 1e-4f, $"{stats.name} at {k} of the cast");

                var range = typeof(ChickenStatsSO).GetField(nameof(ChickenStatsSO.PeckCastSeconds)).GetCustomAttribute<UnityEngine.RangeAttribute>();
                float v = (stats.PeckCastSeconds + range.max) * 0.5f;
                Assert.AreNotEqual(cast, v);
                string json = "{\"schema\":1,\"assets\":{\"" + stats.name + "\":{\"PeckCastSeconds\":" + v.ToString("R", CultureInfo.InvariantCulture) + "}}}";
                using var book = new BalanceBook(new SilentLog(),
                    TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath),
                    TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath),
                    TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath));
                var verdict = book.Evaluate(json, "test");
                Assert.IsTrue(verdict.IsAccepted, verdict.Reason);
                var changed = book.Preview(stats, verdict);
                try
                {
                    float served = PeckCastRules.CastSeconds(changed, peck.Duration);
                    Assert.AreEqual(v, served, 1e-4f, "the served cast is the delta's");
                    // The same moment fills a different fraction once the cast is longer: the fill follows the served cast, not the shipped one.
                    float t = cast * 0.5f;
                    Assert.AreEqual(t / served, FillAt(t, served), 1e-4f, $"{stats.name}: fill follows the changed cast");
                    Assert.AreNotEqual(FillAt(t, cast), FillAt(t, served), $"{stats.name}: same moment, different cast, different fill");
                    Assert.AreEqual(1f, FillAt(served, served), 1e-4f, "full exactly at the end of the changed cast");
                }
                finally { UnityEngine.Object.DestroyImmediate(changed); }
            }
        }

        [Test]
        public void Fill_IsGone_WhenTheCastEndsOrIsCut_AndNeverShowsOffAPeckHex()
        {
            Assert.AreEqual(0f, PeckCastHexRules.Fill01(false, 0f), "the slot ended (landed or cut): the cooldown sweep carries on alone");
            Assert.AreEqual(0f, PeckCastHexRules.Fill01(false, 0.4f));
            Assert.IsFalse(PeckCastHexRules.FillShown(true, false), "no cast running: no fill");
            Assert.IsFalse(PeckCastHexRules.FillShown(false, true), "another ability running on a non-Peck hex: no fill");
            Assert.IsTrue(PeckCastHexRules.FillShown(true, true));
            Assert.AreEqual(0f, PeckCastHexRules.Fill01(true, 1.5f), "clamped");
            Assert.AreEqual(1f, PeckCastHexRules.Fill01(true, -0.5f), "clamped");
        }

        [Test]
        public void Hud_ReadsTheFillFromTheBuffRingsFraction_WithNoSecondTimerOrDuration()
        {
            string body = Body(HudPath, "private void RefreshPeckCastFill(");
            StringAssert.Contains("abilities.ActiveRemaining01For(slot)", body);
            StringAssert.Contains("PeckCastHexRules.Fill01(", body);
            StringAssert.DoesNotContain("ResolveActiveDuration", body);
            StringAssert.DoesNotContain("Time.", body, "no clock of its own");
            StringAssert.Contains("FeedbackTuning.PeckCastFillAlpha", body);
            StringAssert.Contains("RefreshPeckCastFill(slot, abilities, equipped);", Body(HudPath, "private void RefreshSlot("));
        }

        [Test]
        public void TheHexLayer_ExistsOnAllFourSlots_AndTheStylesheetCarriesNoColourLiteral()
        {
            string uxml = File.ReadAllText("Assets/UI/TouchControls.uxml");
            for (int n = 1; n <= 4; n++)
            {
                StringAssert.Contains($"name=\"PeckCast{n}\" class=\"cw-hex-peckcast\"", uxml);
                StringAssert.Contains($"name=\"PeckCastFill{n}\" class=\"cw-hex-peckcast-fill\"", uxml);
            }
            string uss = File.ReadAllText("Assets/UI/Styles/TouchControls.uss");
            foreach (var selector in new[] { ".cw-hex-peckcast", ".cw-hex-peckcast-fill", ".cw-hex-peckcut" })
            {
                var m = Regex.Match(uss, @"(?m)^" + Regex.Escape(selector) + @"\s*\{([^}]*)\}");
                Assert.IsTrue(m.Success, selector + " rule missing");
                StringAssert.DoesNotContain("rgb", m.Groups[1].Value, selector + ": the tint comes from the palette in code");
                StringAssert.DoesNotContain("#", m.Groups[1].Value, selector);
            }
            StringAssert.Contains("display: none", Regex.Match(uss, @"(?m)^\.cw-hex-peckcast\s*\{([^}]*)\}").Groups[1].Value, "hidden until a Peck cast runs");
        }

        // ---- BL-213: the cut counter -----------------------------------------------------------------------------

        [Test]
        public void PeckCutEventId_IsBumpedOnlyOnTheAuthority_ByTheTwoCutPaths_AndNeverByALandingOrAnOwnMove()
        {
            string code = Code(ControllerPath);
            Assert.IsTrue(Regex.IsMatch(code, @"\[Networked\] public byte PeckCutEventId \{ get; private set; \}"));
            StringAssert.Contains("private void NotePeckCut() { if (HasStateAuthority) PeckCutEventId++; }", code);
            Assert.AreEqual(2, Regex.Matches(code, @"\bNotePeckCut\(\);").Count, "exactly two bump sites: the interrupt and the execution");

            string interrupt = Body(ControllerPath, "private void CutPeckIfInterrupted(");
            int gate = interrupt.IndexOf("if (!PeckCastRules.IsCut(stunned, knocked, targetGone)) return;", StringComparison.Ordinal);
            int end = interrupt.IndexOf("Deactivate(peck);", StringComparison.Ordinal);
            int bump = interrupt.IndexOf("NotePeckCut();", StringComparison.Ordinal);
            Assert.GreaterOrEqual(gate, 0);
            Assert.Greater(end, gate, "the stun, the shove and the emptied pile all pass the one IsCut gate before the cut");
            Assert.Greater(bump, end, "one bump per cut, after the slot ended");
            Assert.AreEqual(1, Regex.Matches(interrupt, "NotePeckCut\\(\\)").Count);

            string removal = Body(ControllerPath, "public void EndAllForRemoval(");
            StringAssert.Contains("bool peckRunning = peck != InvalidSlot && IsSlotActive(peck);", removal);
            StringAssert.Contains("if (peckRunning) NotePeckCut();", removal, "an execution bumps only when a Peck was actually running");

            foreach (var other in new[]
            {
                "private void Deactivate(", "private void DeactivateAll(", "public void EndAllForReset(",
                "private void EndStealthAndPeckForCast(", "private void OnAuthorityGained(",
            })
                StringAssert.DoesNotContain("NotePeckCut", Body(ControllerPath, other),
                    other + ": a landing, the chicken's own next move, a reset or a loadout swap is no cut");

            StringAssert.DoesNotContain("NotePeckCut", File.ReadAllText(Scripts + "Abilities/PeckAbilitySO.cs"), "the landing path never bumps the cut counter");
            StringAssert.DoesNotContain("NotePeckLanded", interrupt, "a cut never bumps the landed counter");
        }

        // ---- BL-213: the observer and the two cues ---------------------------------------------------------------

        [Test]
        public void TheObserver_RaisesPeckCut_OnAProxy_FromTheReplicatedId_OncePerChange()
        {
            var events = new ChickenLocalEvents();
            int cut = 0, landed = 0;
            events.PeckCut += () => cut++;
            events.PeckLanded += () => landed++;
            var s = new ChickenEventSnapshot { HasAbilities = true, PeckCutEventId = 250, PeckLandedEventId = 3 };
            events.Observe(in s);
            Assert.AreEqual(0, cut, "the first observation only seeds (a late joiner replays nothing)");

            events.Observe(in s);
            Assert.AreEqual(0, cut, "the same id is no event");

            s.PeckCutEventId = 251;
            events.Observe(in s);
            Assert.AreEqual(1, cut);
            Assert.AreEqual(0, landed, "a cut is not a landing");

            s.PeckCutEventId = 255; events.Observe(in s);
            s.PeckCutEventId = 0; events.Observe(in s);
            Assert.AreEqual(3, cut, "the wrap from 255 to 0 is a change");

            s.PeckLandedEventId = 4;
            events.Observe(in s);
            Assert.AreEqual(3, cut, "a landed cast never shows the cut cue");
            Assert.AreEqual(1, landed);

            s.PeckCutEventId = 5; s.PeckLandedEventId = 5;
            events.Observe(in s);
            Assert.AreEqual(4, cut);
            Assert.AreEqual(2, landed);

            events.Reset();
            events.Observe(in s);
            Assert.AreEqual(4, cut, "a reset re-seeds");
        }

        [Test]
        public void TheObserverSnapshot_FillsTheCutIdFromTheAbilityController()
        {
            string src = File.ReadAllText(Scripts + "Gameplay/ChickenLocalEvents.cs");
            StringAssert.Contains("s.PeckCutEventId = _abilities.PeckCutEventId;", src);
            StringAssert.Contains("_peckCut.Observe(s.PeckCutEventId)", src);
            StringAssert.Contains("_peckCut.Reset();", src);
        }

        [Test]
        public void BothCues_AreLocal_DrivenByTheObserver_NeverByAnRpc()
        {
            string vfx = File.ReadAllText(VfxPath);
            StringAssert.Contains("_events.PeckCut   += OnPeckCut;", vfx);
            StringAssert.Contains("_events.PeckCut   -= OnPeckCut;", vfx);
            string puff = Body(VfxPath, "private void EmitPeckCutPuff(");
            StringAssert.Contains("FoodChunkFlight.IsQuiet(_controller)", puff, "nothing on a hidden or faded bird");
            StringAssert.Contains("PlayerPreferences.ReducedMotionEnabled", puff);
            StringAssert.Contains("_peckPuff", puff, "the existing Peck puff is reused, no new emitter");
            StringAssert.DoesNotContain("Rpc", Code(VfxPath));

            string hud = File.ReadAllText(HudPath);
            StringAssert.Contains("_stunCastEvents.PeckCut += OnLocalPeckCut;", hud);
            StringAssert.Contains("_stunCastEvents.PeckCut -= OnLocalPeckCut;", hud);
            string flash = Body(HudPath, "private void UpdatePeckCutFlash(");
            StringAssert.Contains("PeckCastHexRules.FlashOpacity(", flash);
            StringAssert.Contains("FeedbackTuning.PeckCutFlashSeconds", flash);
            Assert.Greater(FeedbackTuning.PeckCutFlashSeconds, 0f);
        }

        [Test]
        public void FlashOpacity_FallsFromFullToNothingOverTheFlash_AndIsZeroOutsideIt()
        {
            float s = FeedbackTuning.PeckCutFlashSeconds;
            Assert.AreEqual(1f, PeckCastHexRules.FlashOpacity(0f, s), 1e-5f);
            Assert.AreEqual(0.5f, PeckCastHexRules.FlashOpacity(s * 0.5f, s), 1e-5f);
            Assert.AreEqual(0f, PeckCastHexRules.FlashOpacity(s, s));
            Assert.AreEqual(0f, PeckCastHexRules.FlashOpacity(s * 2f, s));
            Assert.AreEqual(0f, PeckCastHexRules.FlashOpacity(-0.1f, s));
            Assert.AreEqual(0f, PeckCastHexRules.FlashOpacity(0.1f, 0f));
        }
    }
}
