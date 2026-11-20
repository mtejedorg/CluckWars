using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Input;
using CluckWars.Localization;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Long press on the Peck slot toggles Auto-Peck, a tap still pecks (Maestro, 2026-11-20): the pure press rules, the one
    /// threshold constant, the AUTO tag and the never-hidden Peck hex, plus the source guards that the networked input path and
    /// the HUD really use them.
    /// </summary>
    public class PeckLongPressTests
    {
        private const string Service = "Assets/_Game/Scripts/App/FusionNetworkService.cs";
        private const string Hud     = "Assets/_Game/Scripts/Input/TouchControlsController.Hud.cs";
        private const string Touch   = "Assets/_Game/Scripts/Input/TouchControlsController.cs";
        private const string Uss     = "Assets/UI/Styles/TouchControls.uss";

        private const float Frame = 1f / 60f;

        /// <summary>Runs <paramref name="frames"/> frames of "down" and returns every non-None action it produced.</summary>
        private static System.Collections.Generic.List<PeckPressAction> Hold(ref PeckHoldState state, int frames, float dt = Frame, bool peck = true)
        {
            var actions = new System.Collections.Generic.List<PeckPressAction>();
            for (int i = 0; i < frames; i++)
            {
                var a = PeckLongPressRules.Step(ref state, peck, down: true, cancel: false, dt);
                if (a != PeckPressAction.None) actions.Add(a);
            }
            return actions;
        }

        private static PeckPressAction Release(ref PeckHoldState state, bool cancel = false) =>
            PeckLongPressRules.Step(ref state, true, down: false, cancel, Frame);

        [Test]
        public void Threshold_IsHalfASecond_AndHasOneHome()
        {
            Assert.AreEqual(0.5f, PeckLongPressRules.PeckLongPressSeconds);
            foreach (string path in new[] { Service, Hud, Touch })
                StringAssert.DoesNotContain("PeckLongPressSeconds =", File.ReadAllText(path), $"{path} must not redeclare the threshold");
            Assert.AreEqual(1, Regex.Matches(File.ReadAllText("Assets/_Game/Scripts/Input/PeckLongPressRules.cs"), @"PeckLongPressSeconds\s*=").Count);
        }

        [Test]
        public void Tap_FiresOnceOnRelease_AndNotBefore()
        {
            var s = default(PeckHoldState);
            Assert.IsEmpty(Hold(ref s, 6), "nothing is sent while a tap is still down");
            Assert.AreEqual(PeckPressAction.FirePeck, Release(ref s));
            Assert.AreEqual(PeckPressAction.None, Release(ref s), "one release, one peck");
        }

        [Test]
        public void ASingleFramePress_StillPecksOnTheNextFrame()
        {
            var s = default(PeckHoldState);
            Assert.AreEqual(PeckPressAction.None, PeckLongPressRules.Step(ref s, true, true, false, Frame));
            Assert.AreEqual(PeckPressAction.FirePeck, Release(ref s));
        }

        [Test]
        public void ReleaseJustBeforeTheThreshold_IsStillATap()
        {
            var s = default(PeckHoldState);
            PeckLongPressRules.Step(ref s, true, true, false, 0f);
            Assert.IsEmpty(Hold(ref s, 1, 0.4999f));
            Assert.AreEqual(PeckPressAction.FirePeck, Release(ref s));
        }

        [Test]
        public void CrossingTheThreshold_TogglesOnce_WhileStillHolding()
        {
            var s = default(PeckHoldState);
            PeckLongPressRules.Step(ref s, true, true, false, 0f);   // the press frame
            var actions = Hold(ref s, 60, Frame);                    // a full second of holding
            Assert.AreEqual(new[] { PeckPressAction.ToggleAutoPeck }, actions.ToArray(), "one flip, however long it is held");
        }

        [Test]
        public void ExactlyAtTheThreshold_IsALongPress()
        {
            var s = default(PeckHoldState);
            PeckLongPressRules.Step(ref s, true, true, false, 0f);
            Assert.AreEqual(PeckPressAction.None, PeckLongPressRules.Step(ref s, true, true, false, 0.25f));
            Assert.AreEqual(PeckPressAction.ToggleAutoPeck, PeckLongPressRules.Step(ref s, true, true, false, 0.25f), "0.25 + 0.25 == 0.5 exactly");
        }

        [Test]
        public void ReleaseAfterAToggle_FiresNoPeck_AndTheNextTapPecksAgain()
        {
            var s = default(PeckHoldState);
            PeckLongPressRules.Step(ref s, true, true, false, 0f);
            Hold(ref s, 40);
            Assert.AreEqual(PeckPressAction.None, Release(ref s), "the release that follows a flip pecks nothing");

            Hold(ref s, 3);
            Assert.AreEqual(PeckPressAction.FirePeck, Release(ref s), "the state is clean for the next press");
        }

        [Test]
        public void ACancelBeforeTheThreshold_KillsThePress_NoPeckAndNoFlip()
        {
            var s = default(PeckHoldState);
            PeckLongPressRules.Step(ref s, true, true, false, 0f);
            PeckLongPressRules.Step(ref s, true, true, true, Frame);                 // Esc / L3 / right mouse mid-hold
            Assert.IsEmpty(Hold(ref s, 60), "still down past the threshold: no flip once cancelled");
            Assert.AreEqual(PeckPressAction.None, Release(ref s));
        }

        [Test]
        public void ACancelOnTheReleaseFrame_IsTheTouchEdgeBand_NoPeck()
        {
            var s = default(PeckHoldState);
            PeckLongPressRules.Step(ref s, true, true, false, 0f);
            Hold(ref s, 3);
            Assert.AreEqual(PeckPressAction.None, Release(ref s, cancel: true), "the lift in the edge band cancels");
        }

        [Test]
        public void ACancelAfterTheToggle_ChangesNothing()
        {
            var s = default(PeckHoldState);
            PeckLongPressRules.Step(ref s, true, true, false, 0f);
            Assert.AreEqual(1, Hold(ref s, 40).Count);
            Assert.AreEqual(PeckPressAction.None, Release(ref s, cancel: true));
        }

        [Test]
        public void ACancelWithNothingHeld_IsIgnored()
        {
            var s = default(PeckHoldState);
            Assert.AreEqual(PeckPressAction.None, Release(ref s, cancel: true));
            Hold(ref s, 3);
            Assert.AreEqual(PeckPressAction.FirePeck, Release(ref s), "a stray right click did not poison the next tap");
        }

        [Test]
        public void ASlotThatIsNotPeck_IsNeverTouched()
        {
            var s = default(PeckHoldState);
            Assert.IsEmpty(Hold(ref s, 120, Frame, peck: false), "no flip, no peck on a slot without Peck");
            Assert.AreEqual(PeckPressAction.None, PeckLongPressRules.Step(ref s, false, false, false, Frame));
            Assert.IsFalse(s.Active);
        }

        [Test]
        public void TheLoadoutChangingMidPress_DropsThePress()
        {
            var s = default(PeckHoldState);
            PeckLongPressRules.Step(ref s, true, true, false, 0f);
            Hold(ref s, 5);
            Assert.AreEqual(PeckPressAction.None, PeckLongPressRules.Step(ref s, false, false, false, Frame));
            Assert.AreEqual(PeckPressAction.None, Release(ref s), "nothing is left pending after the slot lost its Peck");
        }

        [Test]
        public void AutoTag_ShowsOnlyOnAPeckSlot_WhileAutoPeckIsOn()
        {
            Assert.IsTrue(PeckLongPressRules.AutoPeckBadgeVisible(true, true));
            Assert.IsFalse(PeckLongPressRules.AutoPeckBadgeVisible(true, false));
            Assert.IsFalse(PeckLongPressRules.AutoPeckBadgeVisible(false, true), "a slot without Peck never wears AUTO");
            Assert.IsFalse(PeckLongPressRules.AutoPeckBadgeVisible(false, false));
        }

        [Test]
        public void ThePeckHex_NeverHides_InAnySlot()
        {
            var peck = ScriptableObject.CreateInstance<PeckAbilitySO>();
            try
            {
                for (int slot = 0; slot < 4; slot++)
                    Assert.IsFalse(PeckLongPressRules.HexHidden(slot, peck), $"slot {slot} holding Peck must stay visible");
            }
            finally { Object.DestroyImmediate(peck); }

            Assert.IsTrue(PeckLongPressRules.HexHidden(2, null), "an empty third slot still hides, as before");
            Assert.IsFalse(PeckLongPressRules.HexHidden(0, null));
            Assert.IsFalse(PeckLongPressRules.HexHidden(1, null));
            Assert.IsFalse(PeckLongPressRules.HexHidden(3, null));
        }

        [Test]
        public void TheHud_HidesAHexOnlyThroughTheRule_AndDrivesTheAutoTag()
        {
            string hud = File.ReadAllText(Hud);
            StringAssert.Contains("PeckLongPressRules.HexHidden(slot, equipped)", hud);
            StringAssert.Contains("PeckLongPressRules.AutoPeckBadgeVisible(equipped.Is(AbilityJob.Peck), PlayerPreferences.AutoPeckEnabled)", hud);
            StringAssert.Contains("UiText.Get(UiKeys.HudAutoPeckTag)", hud);
            Assert.IsFalse(Regex.IsMatch(hud, @"refs\.Hex\.style\.display\s*="), "no other path may hide a hex");
        }

        [Test]
        public void TheHoldHint_IgnoresAHoldOnThePeckHex()
        {
            StringAssert.Contains("IsHeldVisual(i) && !_appliedSlots[i].Is(AbilityJob.Peck)", File.ReadAllText(Touch));
        }

        [Test]
        public void TheAutoTagText_ComesFromTheDictionary()
        {
            Assert.AreEqual("AUTO", UiText.Get(UiKeys.HudAutoPeckTag));
            Assert.AreEqual("hud.autoPeck.tag", UiKeys.HudAutoPeckTag);
        }

        [Test]
        public void TheAutoTagClass_HasARuleInTheStylesheet_AtTheHudTextFloor()
        {
            string uss = File.ReadAllText(Uss);
            var rule = Regex.Match(uss, @"\." + Regex.Escape(PeckLongPressRules.AutoTagClass) + @"\s*\{([^}]*)\}");
            Assert.IsTrue(rule.Success, $"no rule for .{PeckLongPressRules.AutoTagClass} in TouchControls.uss");
            var size = Regex.Match(rule.Groups[1].Value, @"font-size:\s*(\d+)px");
            Assert.IsTrue(size.Success && int.Parse(size.Groups[1].Value) >= 28, "AUTO is HUD text: 28 px floor");
        }

        [Test]
        public void TheNetworkInputPath_RoutesThePeckSlotThroughTheGate()
        {
            string svc = File.ReadAllText(Service);
            StringAssert.Contains("PeckLongPressRules.Step(ref _peckHold", svc);
            StringAssert.Contains("abilities.FindPeckSlot()", svc);
            // The Peck slot's raw press and hold bit never reach the buffer.
            for (int n = 0; n < 4; n++)
            {
                StringAssert.Contains($"(live{n + 1} && peckSlot != {n})", svc);
                StringAssert.Contains($"GetAbilityHeld({n}) && peckSlot != {n}", svc);
            }
            StringAssert.Contains("AutoPeckEnabled = on", svc);
        }
    }
}
