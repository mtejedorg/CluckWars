using NUnit.Framework;
using CluckWars.Gameplay;
using CluckWars.UI;

namespace CluckWars.Tests
{
    /// <summary>
    /// Pins the pure mapping layer Stage 5 (v0.6 aftermath/refusal HUD, FEEDBACK.md §5.2
    /// and §6) factored out of <c>TouchControlsController</c> so it could be tested
    /// without a <c>NetworkRunner</c> or a live <c>UIDocument</c>:
    /// <see cref="HudFeedbackStyle"/>. These are the exact branches the controller takes —
    /// it queries this class and then writes the resulting class name / mark / row set
    /// straight to the panel, so nothing here is a parallel reimplementation.
    ///
    /// The load-bearing property is that ART §6.6's layer 9 never has to arbitrate: the
    /// refusal reasons are made mutually exclusive by <see cref="AbilityRefusalRules"/>
    /// (pinned separately in <c>AbilityActivationRulesTests</c>), which is exactly why
    /// four distinguishable refusal states fit on nine layers instead of ten.
    /// </summary>
    public sealed class HudFeedbackStyleTests
    {
        // ---- Hex refusal -> USS class -------------------------------------------

        [Test]
        public void HexClass_None_HasNoModifier()
        {
            Assert.IsNull(HudFeedbackStyle.HexClass(AbilityRefusal.None));
        }

        [Test]
        public void HexClass_SlotUnavailable_HasNoModifier()
        {
            // The hex is display:none'd entirely (ART §6.6 slot-3 visibility rule), so
            // there is nothing left to paint — a class here would be dead styling.
            Assert.IsNull(HudFeedbackStyle.HexClass(AbilityRefusal.SlotUnavailable));
        }

        [Test]
        public void HexClass_EveryVisibleRefusal_MapsToItsOwnClass()
        {
            Assert.AreEqual(HudFeedbackStyle.HexCooldownClass,
                HudFeedbackStyle.HexClass(AbilityRefusal.Cooldown));
            Assert.AreEqual(HudFeedbackStyle.HexNoTargetClass,
                HudFeedbackStyle.HexClass(AbilityRefusal.NoTarget));
            Assert.AreEqual(HudFeedbackStyle.HexStunnedClass,
                HudFeedbackStyle.HexClass(AbilityRefusal.Stunned));
            Assert.AreEqual(HudFeedbackStyle.HexOtherActiveClass,
                HudFeedbackStyle.HexClass(AbilityRefusal.OtherAbilityActive));
        }

        [Test]
        public void HexClass_TheFourVisibleRefusals_AreAllDistinct()
        {
            // §6's whole point: four refusals that currently look identical must become
            // four different reads. If any two ever collapsed onto one class the player
            // would be back to guessing.
            var classes = new[]
            {
                HudFeedbackStyle.HexClass(AbilityRefusal.Cooldown),
                HudFeedbackStyle.HexClass(AbilityRefusal.NoTarget),
                HudFeedbackStyle.HexClass(AbilityRefusal.Stunned),
                HudFeedbackStyle.HexClass(AbilityRefusal.OtherAbilityActive),
            };
            CollectionAssert.AllItemsAreUnique(classes);
            CollectionAssert.AllItemsAreNotNull(classes);
        }

        // ---- Hex refusal -> ART §6.6 layer-9 mark --------------------------------

        [Test]
        public void CenterMark_Cooldown_KeepsTheShippedSecondsReadout()
        {
            Assert.AreEqual(HexCenterMark.CooldownSeconds,
                HudFeedbackStyle.CenterMark(AbilityRefusal.Cooldown));
        }

        [Test]
        public void CenterMark_NoTargetAndStunned_GetDistinctMarks()
        {
            Assert.AreEqual(HexCenterMark.NoTarget,
                HudFeedbackStyle.CenterMark(AbilityRefusal.NoTarget));
            Assert.AreEqual(HexCenterMark.StunnedCross,
                HudFeedbackStyle.CenterMark(AbilityRefusal.Stunned));
        }

        [Test]
        public void CenterMark_OtherAbilityActive_ShowsNothingCentred()
        {
            // Its signal is the top-down accent drain on the slot that is actually
            // running; stamping a mark on every suppressed hex would say "four things
            // are wrong" when one thing is happening.
            Assert.AreEqual(HexCenterMark.None,
                HudFeedbackStyle.CenterMark(AbilityRefusal.OtherAbilityActive));
        }

        [Test]
        public void CenterMark_NoneAndSlotUnavailable_ShowNothingCentred()
        {
            Assert.AreEqual(HexCenterMark.None, HudFeedbackStyle.CenterMark(AbilityRefusal.None));
            Assert.AreEqual(HexCenterMark.None, HudFeedbackStyle.CenterMark(AbilityRefusal.SlotUnavailable));
        }

        // ---- Control state -> joystick class -------------------------------------

        [Test]
        public void JoystickClass_Free_HasNoModifier()
        {
            Assert.IsNull(HudFeedbackStyle.JoystickClass(ControlState.Free));
        }

        [Test]
        public void JoystickClass_EachBlockedOrDegradedState_IsDistinct()
        {
            Assert.AreEqual(HudFeedbackStyle.JoyStunnedClass, HudFeedbackStyle.JoystickClass(ControlState.Stunned));
            Assert.AreEqual(HudFeedbackStyle.JoyRootedClass,  HudFeedbackStyle.JoystickClass(ControlState.Rooted));
            Assert.AreEqual(HudFeedbackStyle.JoySlowedClass,  HudFeedbackStyle.JoystickClass(ControlState.Slowed));
            CollectionAssert.AllItemsAreUnique(new[]
            {
                HudFeedbackStyle.JoyStunnedClass,
                HudFeedbackStyle.JoyRootedClass,
                HudFeedbackStyle.JoySlowedClass,
            });
        }

        [Test]
        public void JoystickClass_MarksExactlyTheStatesThatBlockMovement()
        {
            // §5.2's table is meant to *teach* ControlRules by playing. A state the rules
            // let you move in must not be greyed, and a state they block must not look
            // free — so "has a class" has to line up with the rules, not drift from them.
            foreach (ControlState s in System.Enum.GetValues(typeof(ControlState)))
            {
                bool marked = HudFeedbackStyle.JoystickClass(s) != null;
                bool degraded = !ControlRules.CanMove(s) || s == ControlState.Slowed;
                Assert.AreEqual(degraded, marked, $"Joystick marking disagrees with ControlRules for {s}.");
            }
        }

        // ---- Status strip rows ----------------------------------------------------

        private static HudStatusRow[] Buffer() => new HudStatusRow[3];

        [Test]
        public void CollectStatusRows_Free_ProducesNoRows()
        {
            var buf = Buffer();
            int n = HudFeedbackStyle.CollectStatusRows(false, 0f, false, 0f, 1f, buf);
            Assert.AreEqual(0, n);
        }

        [Test]
        public void CollectStatusRows_AllThreeAtOnce_ReportsAllThreeInSeverityOrder()
        {
            // Stun/root/slow are independent bits, not a ladder — the strip shows every
            // active one (§5.1 "one small icon per active status"), unlike the single
            // dominant CurrentControlState that drives the stick.
            var buf = Buffer();
            int n = HudFeedbackStyle.CollectStatusRows(true, 1.5f, true, 2.0f, 0.45f, buf);
            Assert.AreEqual(3, n);
            Assert.AreEqual(HudStatusKind.Stun, buf[0].Kind);
            Assert.AreEqual(HudStatusKind.Root, buf[1].Kind);
            Assert.AreEqual(HudStatusKind.Slow, buf[2].Kind);
        }

        [Test]
        public void CollectStatusRows_CarriesSecondsForTimedStatesAndMagnitudeForSlow()
        {
            var buf = Buffer();
            HudFeedbackStyle.CollectStatusRows(true, 1.5f, true, 2.0f, 0.45f, buf);
            Assert.AreEqual(1.5f, buf[0].Value, 1e-4f);
            Assert.AreEqual(2.0f, buf[1].Value, 1e-4f);
            Assert.AreEqual(0.45f, buf[2].Value, 1e-4f, "Slow carries its multiplier, not a countdown.");
        }

        [Test]
        public void CollectStatusRows_RootOnly_StillOccupiesTheFirstRow()
        {
            var buf = Buffer();
            int n = HudFeedbackStyle.CollectStatusRows(false, 0f, true, 2.4f, 1f, buf);
            Assert.AreEqual(1, n);
            Assert.AreEqual(HudStatusKind.Root, buf[0].Kind);
        }

        [Test]
        public void CollectStatusRows_UnslowedMultiplier_ProducesNoSlowRow()
        {
            var buf = Buffer();
            Assert.AreEqual(0, HudFeedbackStyle.CollectStatusRows(false, 0f, false, 0f, 1f, buf));
            Assert.AreEqual(0, HudFeedbackStyle.CollectStatusRows(false, 0f, false, 0f, 0.995f, buf),
                "Floating-point noise just under 1 must not raise a SLOW row.");
        }

        [Test]
        public void CollectStatusRows_SlowThreshold_MatchesTheControlLadder()
        {
            // The strip and the stick must agree: CurrentControlState calls it Slowed
            // below 0.99, so the strip has to raise its row at exactly the same point or
            // the HUD would list a status on a stick it is painting as Free.
            var buf = Buffer();
            Assert.AreEqual(1, HudFeedbackStyle.CollectStatusRows(false, 0f, false, 0f, 0.98f, buf));
            Assert.AreEqual(0, HudFeedbackStyle.CollectStatusRows(false, 0f, false, 0f, HudFeedbackStyle.SlowThreshold, buf));
        }

        [Test]
        public void CollectStatusRows_NegativeOrMissingCountdown_ClampsToZeroNotANegativeBar()
        {
            // A stun observed on the frame its timer has not been read yet must render as
            // a bare row, never as a negative remaining time that would invert the bar.
            var buf = Buffer();
            int n = HudFeedbackStyle.CollectStatusRows(true, -0.2f, false, 0f, 1f, buf);
            Assert.AreEqual(1, n);
            Assert.AreEqual(0f, buf[0].Value, 1e-4f);
        }

        [Test]
        public void CollectStatusRows_NeverOverrunsTheCallersBuffer()
        {
            // The controller sizes its buffer from FeedbackTuning.StatusBadgeMaxRows; if
            // that were ever lowered, collecting must truncate rather than throw.
            var small = new HudStatusRow[1];
            int n = HudFeedbackStyle.CollectStatusRows(true, 1f, true, 1f, 0.5f, small);
            Assert.AreEqual(1, n);
            Assert.AreEqual(HudStatusKind.Stun, small[0].Kind);

            Assert.AreEqual(0, HudFeedbackStyle.CollectStatusRows(true, 1f, true, 1f, 0.5f, null));
            Assert.AreEqual(0, HudFeedbackStyle.CollectStatusRows(true, 1f, true, 1f, 0.5f, new HudStatusRow[0]));
        }

        // ---- Bars, glyphs, names --------------------------------------------------

        [Test]
        public void HasDrainBar_OnlyTheStatesWithARealTimerGetOne()
        {
            // The accepted deferral: SlowMultiplier is re-derived from scratch every tick
            // and has no deadline, so a draining bar there would be a lie. It shows its
            // magnitude instead.
            Assert.IsTrue(HudFeedbackStyle.HasDrainBar(HudStatusKind.Stun));
            Assert.IsTrue(HudFeedbackStyle.HasDrainBar(HudStatusKind.Root));
            Assert.IsFalse(HudFeedbackStyle.HasDrainBar(HudStatusKind.Slow));
            Assert.IsFalse(HudFeedbackStyle.HasDrainBar(HudStatusKind.None));
        }

        [Test]
        public void Glyphs_AreTheExactCodepointsTheWorldSpaceBadgesUse()
        {
            // ChickenStatusBadges aliases these very consts, so this pins the shared
            // vocabulary at the codepoint level: the HUD strip and the badge above the
            // chicken's head can never drift into two different symbols for one state.
            Assert.AreEqual("⚡",      HudFeedbackStyle.StunGlyph, "stun = HIGH VOLTAGE SIGN");
            Assert.AreEqual("⛓",      HudFeedbackStyle.RootGlyph, "root = CHAINS");
            Assert.AreEqual("\U0001F40C",  HudFeedbackStyle.SlowGlyph, "slow = SNAIL");
        }

        [Test]
        public void GlyphAndName_AreNonEmptyForEveryRealStatusAndEmptyForNone()
        {
            foreach (var kind in new[] { HudStatusKind.Stun, HudStatusKind.Root, HudStatusKind.Slow })
            {
                Assert.IsNotEmpty(HudFeedbackStyle.GlyphFor(kind), $"{kind} needs a glyph.");
                Assert.IsNotEmpty(HudFeedbackStyle.NameFor(kind), $"{kind} needs a name.");
            }
            Assert.IsEmpty(HudFeedbackStyle.GlyphFor(HudStatusKind.None));
            Assert.IsEmpty(HudFeedbackStyle.NameFor(HudStatusKind.None));
        }

        [Test]
        public void RefusalMarkGlyphs_AreDefinedEvenThoughShapesAreDrawn()
        {
            // UseDrawnRefusalMarks is true because neither codepoint exists in
            // LilitaOne-Regular.ttf or NotoEmoji-Regular.ttf and TMP Settings has no
            // fallback list. The character forms stay defined so flipping that one switch
            // is genuinely a one-line change if a font that carries them is ever added.
            Assert.AreEqual("⃠", HudFeedbackStyle.NoTargetGlyph);
            Assert.AreEqual("✕", HudFeedbackStyle.StunnedCrossGlyph);
        }
    }
}
