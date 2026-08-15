using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Settings;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the always-on ability reach overlay (<see cref="AbilitySlotOverlay"/>): the
    /// alpha hierarchy that keeps an ambient outline quieter than every transient cue, the
    /// deliberate <c>AimShape.None</c> divergence from the telegraph, the slot-count bug the
    /// retired range ring shipped with, and the prefab wiring the feature is invisible without.
    /// </summary>
    /// <remarks>
    /// Assertions are written as RELATIONSHIPS against the real constants and the real shipped
    /// assets, never as restated literals. Two separate incidents on this project (the
    /// BalanceOracle drift, the lobby copy drift) came from a test that restated the value it
    /// was meant to be guarding and therefore agreed with any future change to it.
    /// </remarks>
    public sealed class AbilitySlotOverlayTests
    {
        /// <summary>
        /// Adds the overlay to <paramref name="go"/> and makes sure its geometry has actually
        /// been built. Edit Mode does not deliver <c>Awake</c> to ordinary (non-<c>ExecuteAlways</c>)
        /// MonoBehaviours the way Play Mode does, so it is driven explicitly — but only when it
        /// has demonstrably not already run, or the outlines would be built twice and the count
        /// assertion would be meaningless.
        /// </summary>
        private static AbilitySlotOverlay NewProbeOverlay(GameObject go)
        {
            var overlay = go.AddComponent<AbilitySlotOverlay>();

            if (go.GetComponentsInChildren<LineRenderer>(true).Length == 0)
            {
                var awake = typeof(AbilitySlotOverlay).GetMethod(
                    "Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(awake, "AbilitySlotOverlay no longer has an Awake to build its geometry in.");
                awake.Invoke(overlay, null);
            }

            return overlay;
        }

        // ---- Alpha / weight hierarchy --------------------------------------

        [Test]
        public void AlphaTiers_AreStrictlyOrdered_AndAllStayUnderTheAimPreview()
        {
            // The whole design rests on this ladder. A future re-tune that inverted any pair
            // would silently break the grammar: an ambient outline louder than the live aim
            // preview would read as "I am aiming this", and a suppressed slot louder than a
            // cooling one would tell the player the wrong story about why a slot is quiet.
            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaSuppressed,
                Is.LessThan(FeedbackTuning.AmbientSlotOutlineAlphaCooldown),
                "\"suppressed by someone else's aim\" must never read louder than \"on cooldown\".");

            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaCooldown,
                Is.LessThan(FeedbackTuning.AmbientSlotOutlineAlphaReady),
                "a cooling slot must recede below a ready one…");

            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaCooldown, Is.GreaterThan(0f),
                "…but must NOT vanish. Losing the spatial reference for an ability you are " +
                "waiting on is worst exactly mid-fight, which is when cooldowns are running.");

            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaReady,
                Is.LessThan(FeedbackTuning.AmbientSlotOutlineAlphaHot),
                "a slot with a rival actually inside it is the decision-relevant one and must " +
                "be the brightest ambient tier.");

            Assert.That(FeedbackTuning.AmbientSlotOutlineAlphaHot,
                Is.LessThan(FeedbackTuning.TelegraphPreviewAlpha),
                "the brightest ambient tier must stay clearly under the live aim preview, or " +
                "a permanent reference becomes mistakable for an active aim.");
        }

        [Test]
        public void CooldownTier_RecedesInTheSameProportionTheHudHexDoes()
        {
            // The cooldown alpha is not an independent number: it is the HUD hex's own
            // ready→cooldown ratio (HexAlphaCooldown / HexAlphaReady) applied to the ambient
            // ready tier, so the world and the HUD tell the same story about the same slot.
            // Asserted as a ratio band rather than a literal so both pairs can be re-tuned
            // together but not drift apart.
            float hexRatio     = FeedbackTuning.HexAlphaCooldown / FeedbackTuning.HexAlphaReady;
            float ambientRatio = FeedbackTuning.AmbientSlotOutlineAlphaCooldown /
                                 FeedbackTuning.AmbientSlotOutlineAlphaReady;

            Assert.That(ambientRatio, Is.EqualTo(hexRatio).Within(0.10f),
                $"The ambient cooldown tier recedes by {ambientRatio:0.00}x but the HUD hex " +
                $"recedes by {hexRatio:0.00}x. They describe the same slot in the same state; " +
                "re-tune both or neither.");
        }

        [Test]
        public void OutlineStroke_IsThinnerThanEveryPersistentCueItSharesTheGroundWith()
        {
            // Weight-by-duration is the failure mode this guards. The overlay is on screen for
            // 100% of the match on up to four shapes at once, so any stroke competitive with a
            // transient cue wins the player's attention by lasting longer, not by mattering more.
            Assert.That(FeedbackTuning.AmbientSlotOutlineWidth, Is.GreaterThan(0f),
                "a zero-width line renders nothing at all.");

            Assert.That(FeedbackTuning.AmbientSlotOutlineWidth,
                Is.LessThan(FeedbackTuning.RivalRingWidth),
                "the rival ring (itself already chosen to stay under the transient status ring) " +
                "marks ONE thing per rival; the ambient overlay draws four at once and must be " +
                "thinner still.");
        }

        [Test]
        public void KillSwitch_IsNotCompileTimeFolded()
        {
            // static readonly, not const: a const bool folds at compile time and turns
            // AbilitySlotOverlay.Awake's guard into a CS0162 unreachable-code warning. Same
            // reasoning (and same test shape) as RivalIndicatorsEnabled.
            var field = typeof(FeedbackTuning).GetField(
                nameof(FeedbackTuning.AbilitySlotOverlayEnabled),
                BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(field, "FeedbackTuning.AbilitySlotOverlayEnabled is gone.");
            Assert.IsFalse(field.IsLiteral,
                "AbilitySlotOverlayEnabled must be `static readonly`, not `const` — a folded " +
                "const bool makes the guard that reads it an unreachable-code warning.");
        }

        // ---- AimShape.None draws nothing (the deliberate divergence) --------

        [Test]
        public void AbilitiesWithNoAimShape_ProduceNoAmbientOutline()
        {
            // Read against the REAL shipped assets, not a hand-written list of names.
            var noShape = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir)
                .Where(a => a.AimShape == AbilityAimShape.None)
                .ToList();

            Assert.IsNotEmpty(noShape,
                "No shipped ability has AimShape.None any more — if that is a real change, this " +
                "test has nothing left to guard and the None branch in TryResolveAmbientShape " +
                "should be re-justified rather than left as dead code.");

            foreach (var a in noShape)
            {
                Assert.IsFalse(
                    AbilitySlotOverlay.TryResolveAmbientShape(a, out _, out _, out _, out _),
                    $"{a.name} has no aim shape, so the ambient overlay must draw nothing for it. " +
                    "Degrading it to a self-ring (which TelegraphShapes.Resolve does, correctly, " +
                    "for the telegraph) would paint a permanent circle at the caster's feet at " +
                    "the same radius as the stun/root/slow status ring.");
            }
        }

        [Test]
        public void TheNoneCase_DivergesFromTheTelegraphOnPurpose()
        {
            // Pins the divergence itself, not just its effect: Resolve DOES hand back a
            // drawable self-ring for a None ability, and the overlay deliberately refuses it.
            // Without this, someone "unifying" the two would delete the short-circuit, see the
            // test above still pass through Resolve's own output, and ship the duplicate ring.
            var selfBuff = ScriptableObject.CreateInstance<TurtleModeAbilitySO>();
            try
            {
                Assert.AreEqual(AbilityAimShape.None, selfBuff.AimShape, "precondition");

                TelegraphShapes.Resolve(selfBuff, out var telegraphShape, out float telegraphRadius,
                                        out _, out _);
                Assert.That(telegraphRadius, Is.EqualTo(FeedbackTuning.SelfRingRadius).Within(1e-5f),
                    "precondition: the telegraph degrades a None ability to a self-ring, which " +
                    "is correct for a transient 'armed' pulse during a hold.");
                Assert.AreEqual(AbilityAimShape.None, telegraphShape, "precondition");

                Assert.IsFalse(
                    AbilitySlotOverlay.TryResolveAmbientShape(selfBuff, out _, out _, out _, out _),
                    "The ALWAYS-ON overlay must not inherit that degradation. SelfRingRadius is " +
                    "exactly ControlStateVFX's status-ring radius, so a permanent copy would " +
                    "collide with the real stun/root/slow ring the moment it lit up.");
            }
            finally { Object.DestroyImmediate(selfBuff); }
        }

        [Test]
        public void EveryAbilityWithARealAimShape_ProducesADrawableOutline()
        {
            // The converse. An ability that declares a shape but resolves to something the
            // overlay silently skips would be invisible in exactly the feature meant to teach
            // it — the same class of silent gap as the slot-3 bug below.
            foreach (var a in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir)
                                        .Where(a => a.AimShape != AbilityAimShape.None))
            {
                Assert.IsTrue(
                    AbilitySlotOverlay.TryResolveAmbientShape(a, out var shape, out float radius,
                                                              out _, out _),
                    $"{a.name} declares AimShape.{a.AimShape} but the ambient overlay draws " +
                    "nothing for it.");
                Assert.AreEqual(a.AimShape, shape, $"{a.name}: the overlay must draw the ability's own shape.");
                Assert.Greater(radius, 0f, $"{a.name}: a zero-radius outline is invisible.");
            }
        }

        [Test]
        public void JumpAbilities_KeepResolvesLandingRadiusOverride()
        {
            // The None short-circuit bypasses Resolve; Jump must NOT. Its ring is the fixed
            // JumpLandingRadius, not AimRadius — and it is the one shape where an always-on
            // outline teaches something otherwise completely invisible.
            var jumps = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir)
                .Where(a => a.AimShape == AbilityAimShape.Jump)
                .ToList();

            Assert.IsNotEmpty(jumps, "No shipped ability declares AimShape.Jump — Shadowstep changed shape?");

            foreach (var a in jumps)
            {
                Assert.IsTrue(
                    AbilitySlotOverlay.TryResolveAmbientShape(a, out _, out float radius,
                                                              out float forwardOffset, out _));
                Assert.That(radius, Is.EqualTo(AbilityBaseSO.JumpLandingRadius).Within(1e-5f),
                    $"{a.name}: the ambient landing ring must keep TelegraphShapes.Resolve's " +
                    "fixed-radius override, not the ability's AimRadius.");
                Assert.That(forwardOffset, Is.EqualTo(a.AimForwardOffset).Within(1e-5f),
                    $"{a.name}: the ring must sit at the jump's own length.");
            }
        }

        // ---- Slot coverage (the bug the retired range ring shipped with) ----

        [Test]
        public void Overlay_BuildsOneOutlinePerEquippedSlot_NotAHardCodedThree()
        {
            // The bug this exists for: AbilityRangeIndicator.UpdateRangeRing looped
            // `slot < 3` and its private SlotAbility() handled only 0/1/2, so after the roster
            // went to four slots, slot 3's range was never drawn on any chicken, ever. Asserted
            // against AbilityController.SlotCount rather than the number 4, so raising the slot
            // count fails here instead of silently dropping a slot again.
            if (!FeedbackTuning.AbilitySlotOverlayEnabled)
            {
                Assert.Ignore("AbilitySlotOverlayEnabled is off — Awake builds no geometry by design.");
            }

            var go = new GameObject("AbilitySlotOverlayTests_Probe");
            try
            {
                NewProbeOverlay(go);

                var lines = go.GetComponentsInChildren<LineRenderer>(true);
                Assert.AreEqual(AbilityController.SlotCount, lines.Length,
                    $"The overlay built {lines.Length} outlines for {AbilityController.SlotCount} " +
                    "equipped slots. Every slot gets one, or the missing one is invisible forever.");

                for (int slot = 0; slot < AbilityController.SlotCount; slot++)
                {
                    string expected = $"AbilitySlotOverlay{slot}";
                    Assert.IsTrue(lines.Any(l => l.name == expected),
                        $"No outline named '{expected}'. The lines must be indexed by slot so " +
                        "each one tracks its own ability.");
                }

                foreach (var lr in lines)
                {
                    Assert.That(lr.widthMultiplier,
                        Is.EqualTo(FeedbackTuning.AmbientSlotOutlineWidth).Within(1e-5f),
                        $"{lr.name}: width is the one channel that must NOT vary — only alpha " +
                        "encodes state.");
                    Assert.IsFalse(lr.enabled,
                        $"{lr.name}: outlines start hidden and are only enabled once the local " +
                        "player and a drawable shape are both confirmed.");
                }

                // One shared material across all four lines: BuildLineMaterial does
                // `new Material(...)` per call, so four calls would mean four unbatchable
                // SetPass calls permanently on the local chicken.
                var materials = lines.Select(l => l.sharedMaterial).Distinct().ToList();
                Assert.AreEqual(1, materials.Count,
                    $"The overlay is using {materials.Count} distinct materials for " +
                    $"{lines.Length} identical unlit lines. Build one in Awake and share it.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AbilityController_ExposesGetSlotPublicly_SoNoConsumerReimplementsTheMapping()
        {
            // Root cause of the slot-3 bug: GetSlot(int) was private, so AbilityRangeIndicator
            // wrote its own slot→ability ternary and that copy went stale. The class of bug is
            // fixed by there being exactly one mapping and it being reachable.
            var getSlot = typeof(AbilityController).GetMethod(
                nameof(AbilityController.GetSlot),
                BindingFlags.Public | BindingFlags.Instance,
                binder: null, types: new[] { typeof(int) }, modifiers: null);

            Assert.IsNotNull(getSlot,
                "AbilityController.GetSlot(int) must stay public. Making it private again forces " +
                "every consumer to re-derive the slot→ability mapping, which is exactly how " +
                "slot 3 stopped being drawn.");

            Assert.IsNull(
                typeof(AbilityRangeIndicator).GetMethod("SlotAbility",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
                "AbilityRangeIndicator.SlotAbility is back. It is the stale duplicate of " +
                "AbilityController.GetSlot that only ever handled slots 0/1/2.");
        }

        // ---- Player-facing toggle -------------------------------------------

        [Test]
        public void AbilityRangeGuides_DefaultToOn_WhenThePlayerHasNeverChosen()
        {
            // The feature exists because new players cannot tell what an ability reaches, so
            // it has to be on before anyone has been near a settings screen. Restores whatever
            // the developer running the suite had set, so the test cannot change their machine.
            bool hadKey = PlayerPrefs.HasKey(PlayerPreferences.AbilityRangeGuidesKey);
            int  saved  = PlayerPrefs.GetInt(PlayerPreferences.AbilityRangeGuidesKey, 1);
            try
            {
                PlayerPrefs.DeleteKey(PlayerPreferences.AbilityRangeGuidesKey);
                PlayerPreferences.ResetCache();

                Assert.IsTrue(PlayerPreferences.AbilityRangeGuidesEnabled,
                    "With no stored preference the reach overlay must default to ON.");

                PlayerPreferences.AbilityRangeGuidesEnabled = false;
                PlayerPreferences.ResetCache();
                Assert.IsFalse(PlayerPreferences.AbilityRangeGuidesEnabled,
                    "An explicit 'off' must survive a cache drop, i.e. it really reached PlayerPrefs.");
            }
            finally
            {
                if (hadKey) PlayerPrefs.SetInt(PlayerPreferences.AbilityRangeGuidesKey, saved);
                else PlayerPrefs.DeleteKey(PlayerPreferences.AbilityRangeGuidesKey);
                PlayerPrefs.Save();
                PlayerPreferences.ResetCache();
            }
        }

        // ---- Prefab wiring ---------------------------------------------------

        [Test]
        public void ChickenPrefab_CarriesTheAbilitySlotOverlay()
        {
            // Prefab wiring is a silent-failure surface on this project: the component simply
            // does not exist at runtime and the feature is invisible with no error anywhere.
            var chicken = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);

            Assert.IsNotNull(chicken.GetComponentInChildren<AbilitySlotOverlay>(true),
                "Chicken.prefab is missing AbilitySlotOverlay. Nothing logs when it is absent — " +
                "the reach guides are just silently gone for every player.");

            Assert.AreEqual(1, chicken.GetComponentsInChildren<AbilitySlotOverlay>(true).Length,
                "Duplicate AbilitySlotOverlay on Chicken.prefab: each copy builds its own four " +
                "LineRenderers and they would z-fight at doubled apparent alpha.");
        }

        [Test]
        public void DoppelgangerPrefab_InheritsTheOverlayFromItsBase()
        {
            // The decoy is a variant of Chicken.prefab, so it inherits the component. It is
            // never the local player's own chicken, so ShouldDraw's HasInputAuthority gate keeps
            // it dark — this only asserts the variant did not get the component stripped, which
            // would mean the base prefab's structure had been edited in a way variants dropped.
            var decoy = TestAssets.Load<GameObject>(TestAssets.DoppelgangerPrefabPath);

            Assert.IsNotNull(decoy.GetComponentInChildren<AbilitySlotOverlay>(true),
                "Doppelganger.prefab lost AbilitySlotOverlay, so it is no longer inheriting " +
                "Chicken.prefab's component set the way CONVENTIONS.md relies on.");
        }
    }
}
