using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

// NUnit and UnityEngine both export RangeAttribute (CS0104). This test reads the
// [Range] ceilings off serialized fields, so it is always UnityEngine's. Same alias
// pattern CONVENTIONS.md prescribes for the Fusion/CluckWars LogLevel collision.
using RangeAttribute = UnityEngine.RangeAttribute;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the two invariants the Drag/Snare split rests on: that every authored slow is
    /// harsh enough to actually raise <see cref="ControlVfx.Slowed"/>, and that
    /// <see cref="ControlVfx.Snared"/> is a distinct bit that only ever appears alongside it.
    /// </summary>
    /// <remarks>
    /// <b>The landmine this exists to defuse.</b> Slow composes by <see cref="Mathf.Min"/> in
    /// <see cref="ChickenController.ApplySlow"/>, never by a product, so
    /// <c>SlowMultiplier</c> is always <i>exactly one</i> authored factor. Four separate
    /// thresholds read it — <see cref="ChickenController.SlowVfxFlagThreshold"/> (0.92),
    /// <c>CurrentControlState</c>'s 0.99, and two 0.999s in the nameplate and the overlays —
    /// and they cannot currently disagree only because every shipping factor is ≤ 0.80,
    /// comfortably below all of them. The band between 0.92 and 1.0 is unreachable <i>today</i>.
    /// <para>
    /// A slow authored inside that band would be a real, gameplay-affecting slow that never
    /// raises the replicated VFX bit — so it would be invisible on every remote peer, and
    /// nothing anywhere would fail. These tests convert that into a red test.
    /// </para>
    /// <b>Ceilings, not values.</b> The <c>[Range]</c> attribute is what actually stops a
    /// designer typing a number into the inspector, so it — not whatever is authored in the
    /// <c>.asset</c> today — is the real bound. Same reasoning (and the same technique) as
    /// <c>BaseDepositRulesTests.MaxDepositRateMultiplier_MatchesTheInspectorCeiling</c>, and
    /// it has the useful side effect of not going red every time the ability assets are
    /// re-authored.
    /// </remarks>
    public sealed class SlowSourceTests
    {
        /// <summary>
        /// A slow factor whose inspector ceiling reaches into the invisible band, kept here as
        /// an explicit, named gap rather than as a silent hole in the loop below.
        /// <para>
        /// <c>DustKickAbilitySO.SlowFactor</c> is <c>[Range(0.1f, 1f)]</c> where every other
        /// slow factor in the project is <c>[Range(0.1f, 0.9f)]</c>, so its ceiling admits e.g.
        /// 0.95 — a genuine 5% slow that no remote peer would ever see. The authored value is
        /// 0.55 and is pinned by <see cref="EveryAuthoredSlowFactor_StaysBelowTheVfxFlagThreshold"/>
        /// below, so nothing is broken today. The fix is a one-word change to that
        /// <c>[Range]</c>; <see cref="KnownWideCeilings_AreStillReal"/> deletes this exemption
        /// automatically when it lands.
        /// </para>
        /// </summary>
        private static readonly string[] KnownWideCeilings = { "DustKickAbilitySO.SlowFactor" };

        // ---- Discovery --------------------------------------------------------

        private readonly struct SlowField
        {
            public readonly System.Type    Owner;
            public readonly FieldInfo      Field;
            public readonly RangeAttribute Range;

            public SlowField(System.Type owner, FieldInfo field, RangeAttribute range)
            {
                Owner = owner; Field = field; Range = range;
            }

            public string Key => $"{Owner.Name}.{Field.Name}";
        }

        /// <summary>
        /// Every <c>[Range]</c>d float on an ability SO whose name ends in "SlowFactor" — i.e.
        /// every knob a designer can turn that feeds <see cref="SlowSource.Ability"/>.
        /// Discovered rather than listed so a new slow ability joins these tests by existing.
        /// </summary>
        private static List<SlowField> DiscoverSlowFactorFields()
        {
            return typeof(AbilityBaseSO).Assembly
                .GetTypes()
                .Where(t => typeof(AbilityBaseSO).IsAssignableFrom(t))
                .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.FieldType == typeof(float) && f.Name.EndsWith("SlowFactor"))
                    .Select(f => new SlowField(t, f,
                        f.GetCustomAttributes(typeof(RangeAttribute), false)
                         .Cast<RangeAttribute>().FirstOrDefault())))
                .OrderBy(sf => sf.Key)
                .ToList();
        }

        [Test]
        public void SlowFactorFields_AreStillDiscoverable()
        {
            var found = DiscoverSlowFactorFields();

            // Without this, a rename ("SlowFactor" → "SpeedScale") would empty the loops below
            // and every ceiling test would pass by testing nothing at all.
            Assert.GreaterOrEqual(found.Count, 4,
                "Expected at least the four shipped ability slow factors (Feather Trap, Feather " +
                "Aura, Dust Kick, Smoke Roost) but found " +
                $"{found.Count}: [{string.Join(", ", found.Select(f => f.Key))}]. The discovery " +
                "predicate matches public float fields named '*SlowFactor' on AbilityBaseSO " +
                "subclasses — if one was renamed, update the predicate, not this number.");

            foreach (var sf in found)
                Assert.IsNotNull(sf.Range,
                    $"{sf.Key} has no [Range]. Nothing now caps what a designer can type in, so " +
                    "there is no ceiling to check against ChickenController.SlowVfxFlagThreshold.");
        }

        // ---- The invisible band ----------------------------------------------

        [Test]
        public void EverySlowFactorInspectorCeiling_StaysBelowTheVfxFlagThreshold()
        {
            foreach (var sf in DiscoverSlowFactorFields())
            {
                if (KnownWideCeilings.Contains(sf.Key)) continue;   // see the field's remarks
                if (sf.Range == null) continue;                     // reported by the test above

                Assert.Less(sf.Range.max, ChickenController.SlowVfxFlagThreshold,
                    $"{sf.Key} lets a designer author up to {sf.Range.max}, at or above " +
                    $"ChickenController.SlowVfxFlagThreshold ({ChickenController.SlowVfxFlagThreshold}). " +
                    "A slow authored in that band is a real slow that silently stops raising " +
                    "ControlVfx.Slowed, so the state becomes invisible on every remote peer — no " +
                    "ring, no streaks, no badge, and nothing fails. Lower the [Range] ceiling.");
            }
        }

        [Test]
        public void KnownWideCeilings_AreStillReal()
        {
            // An exemption that outlives the problem is how a test quietly stops covering
            // something. When the [Range] is narrowed, this goes red and the entry gets deleted.
            var byKey = DiscoverSlowFactorFields().ToDictionary(sf => sf.Key, sf => sf);

            foreach (var key in KnownWideCeilings)
            {
                Assert.IsTrue(byKey.TryGetValue(key, out var sf),
                    $"'{key}' is exempted from the ceiling check but no longer exists. Delete it " +
                    "from KnownWideCeilings.");
                Assert.IsNotNull(sf.Range, $"'{key}' lost its [Range] entirely — that is worse, not better.");
                Assert.GreaterOrEqual(sf.Range.max, ChickenController.SlowVfxFlagThreshold,
                    $"'{key}' now has a safe ceiling of {sf.Range.max}. Remove it from " +
                    "KnownWideCeilings so it is covered by the real check.");
            }
        }

        [Test]
        public void EveryAuthoredSlowFactor_StaysBelowTheVfxFlagThreshold()
        {
            // Covers what the ceiling check cannot: the value actually shipped in each .asset,
            // including DustKick's, whose inspector ceiling is exempted above.
            foreach (var ability in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir))
            foreach (var sf in DiscoverSlowFactorFields().Where(f => f.Owner.IsInstanceOfType(ability)))
            {
                float authored = (float)sf.Field.GetValue(ability);
                Assert.Less(authored, ChickenController.SlowVfxFlagThreshold,
                    $"{ability.name}.{sf.Field.Name} is authored at {authored}, at or above " +
                    $"ChickenController.SlowVfxFlagThreshold ({ChickenController.SlowVfxFlagThreshold}). " +
                    "It would slow the victim for real while raising no replicated VFX bit, so the " +
                    "state would be invisible to every peer but its owner.");
            }
        }

        [Test]
        public void TheAmbientSlowConstants_StayBelowTheVfxFlagThreshold()
        {
            // Pile and collision are compile-time constants rather than authored assets, so they
            // are read straight off the type. These are the two DRAG sources — the ones that
            // deliberately get no ring and no impact beat — which makes it doubly important that
            // they still raise ControlVfx.Slowed: the streaks and the joystick tint are all the
            // feedback a Drag has left.
            foreach (var name in new[] { "PileSlowFactor", "CollisionSlowFactor" })
            {
                var field = typeof(ChickenController).GetField(
                    name, BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(field, $"ChickenController.{name} was renamed or removed.");

                float value = (float)field.GetRawConstantValue();
                Assert.Less(value, ChickenController.SlowVfxFlagThreshold,
                    $"ChickenController.{name} is {value}, at or above SlowVfxFlagThreshold " +
                    $"({ChickenController.SlowVfxFlagThreshold}). A Drag that raises no Slowed bit " +
                    "has no feedback left at all — it lost its ring and its impact beat by design.");
            }
        }

        // ---- Flag algebra -----------------------------------------------------

        [Test]
        public void Snared_IsItsOwnBit()
        {
            Assert.AreEqual(1 << 3, (int)ControlVfx.Snared);

            foreach (var other in new[] { ControlVfx.Slowed, ControlVfx.Rooted, ControlVfx.Stunned })
                Assert.AreEqual(ControlVfx.None, ControlVfx.Snared & other,
                    $"ControlVfx.Snared collides with {other}. ControlFlags is a single replicated " +
                    "byte, so a collision would make one state silently read as another on every peer.");
        }

        [Test]
        public void SnaredImpliesSlowed_HoldsForTheFlagAlgebra()
        {
            // The invariant is ENFORCED at the single publish site in
            // ChickenController.FixedUpdateNetwork, where Snared is only ever OR-ed in from
            // inside the `SlowMultiplier < SlowVfxFlagThreshold` branch. That site needs a live
            // NetworkRunner to exercise, so what is pinned here is the consumer contract that
            // depends on it: given the invariant, "Drag" is exactly `Slowed && !Snared`, and
            // every consumer (ControlStateVFX's ring, HitFeedback's impact edge,
            // ChickenStateOverlays' pulse) tests one bit without re-deriving the other.
            const ControlVfx drag  = ControlVfx.Slowed;
            const ControlVfx snare = ControlVfx.Slowed | ControlVfx.Snared;

            Assert.IsTrue((drag & ControlVfx.Slowed) != 0);
            Assert.IsTrue((drag & ControlVfx.Snared) == 0, "A Drag must never carry the Snared bit.");

            Assert.IsTrue((snare & ControlVfx.Slowed) != 0,
                "A Snare must also read as Slowed, so the shared 'you are slowed' channels " +
                "(streaks, status badge, joystick tint) light for both.");
            Assert.IsTrue((snare & ControlVfx.Snared) != 0);

            Assert.AreNotEqual(drag, snare, "The two must be distinguishable from ControlFlags alone.");
        }

        [Test]
        public void OnlyAbilitySlow_CountsAsEnemyAgency()
        {
            // Snared is published from (_activeSlowSources & SlowSource.Ability). This pins the
            // membership that decision rests on: Pile and Collision are ambient friction with
            // nobody to blame, Ability is the one source an opponent chose to apply.
            Assert.AreEqual(SlowSource.None, SlowSource.Ability & (SlowSource.Pile | SlowSource.Collision),
                "SlowSource.Ability must stay disjoint from the two ambient sources, or a food " +
                "pile would start drawing a snare ring and an impact beat with no attacker.");
        }
    }
}
