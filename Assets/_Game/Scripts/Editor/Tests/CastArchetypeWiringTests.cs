using System.Collections.Generic;
using System.Linq;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the cast-archetype chain end to end: every ability declares an archetype, every
    /// archetype has a state in the controller, every state is reachable, and every class supplies
    /// a clip for each one.
    /// </summary>
    /// <remarks>
    /// <b>Every link here fails silently when it breaks.</b> That is the whole reason the fixture
    /// exists. An ability with no archetype casts as <see cref="CastArchetype.Lunge"/> because Lunge
    /// is the zero value; a state whose Any State transition is ordered behind the generic Cast
    /// fallback never runs, and the chicken plays a cast, just the wrong one; a class missing an
    /// archetype clip falls through to the empty placeholder and freezes for the length of the beat.
    /// None of those throw, none log, and all three look approximately like working animation. The
    /// workstream that produced this file began with a shipped field
    /// (<c>AbilityBaseSO.AbilityAnimationClip</c>) that had been declared and unread since Phase 6
    /// without anything noticing.
    ///
    /// Assertions are relationships against the real assets and the real enum rather than restated
    /// literals — a test that hardcodes the ability list agrees with any future change to it, which
    /// is how the BalanceOracle drift on this project went green while it was wrong.
    /// </remarks>
    public sealed class CastArchetypeWiringTests
    {
        private const string ControllerPath = "Assets/_Game/Art/Animations/Chicken.controller";
        private const string RegistryPath   = "Assets/_Game/Data/ChickenClassRegistry.asset";
        private const string AbilitiesDir   = "Assets/_Game/Data/Abilities";

        /// <summary>Archetypes that are real motions — everything except <see cref="CastArchetype.None"/>.</summary>
        private static IEnumerable<CastArchetype> Motions =>
            System.Enum.GetValues(typeof(CastArchetype))
                       .Cast<CastArchetype>()
                       .Where(a => a != CastArchetype.None);

        private static IEnumerable<AbilityBaseSO> AllAbilities =>
            AssetDatabase.FindAssets("t:AbilityBaseSO", new[] { AbilitiesDir })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Select(AssetDatabase.LoadAssetAtPath<AbilityBaseSO>)
                         .Where(a => a != null);

        private static AnimatorController Controller =>
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        private static string StateName(CastArchetype a) => "Cast_" + a;

        // ---------------------------------------------------------------- assets

        [Test]
        public void EveryActivatableAbility_DeclaresACastMotionThatIsARealArchetype()
        {
            // Peck is excluded by design: it owns a dedicated state and trigger because foraging is
            // frequent enough that sharing a combat beat reads as a bug.
            var offenders = AllAbilities
                .Where(a => a is not PeckAbilitySO && a is not PassiveAbilitySO)
                .Where(a => a.CastMotion == CastArchetype.None)
                .Select(a => a.name)
                .ToArray();

            Assert.IsEmpty(offenders,
                "These activatable abilities declare CastArchetype.None, which marks an ability " +
                "that is never cast. They will activate with no cast animation and nothing will " +
                "report it at runtime beyond a single warning: " + string.Join(", ", offenders));
        }

        [Test]
        public void EverySpecialization_DeclaresNone_RatherThanInheritingTheZeroDefault()
        {
            // A passive "expresses itself through hooks, never through OnActivate", so an archetype
            // is meaningless for it. The point of asserting it is the zero default: before None
            // existed, all eight of these silently read as Lunge, and would have animated as a lunge
            // the day one of them was made activatable.
            var offenders = AllAbilities
                .OfType<PassiveAbilitySO>()
                .Where(p => p.CastMotion != CastArchetype.None)
                .Select(p => $"{p.name}={p.CastMotion}")
                .ToArray();

            Assert.IsEmpty(offenders,
                "These specializations carry a cast archetype. A passive is never cast, so the " +
                "value is meaningless and — because Lunge is the zero value — indistinguishable " +
                "from having been left unset: " + string.Join(", ", offenders));
        }

        [Test]
        public void EveryArchetype_IsUsedByAtLeastOneAbility()
        {
            // Not a style rule. An unused archetype means four generated clips per class that
            // nothing can ever play, and it usually means an ability was meant to reach it and was
            // mapped somewhere else by mistake.
            var used = AllAbilities
                .Where(a => a is not PassiveAbilitySO)
                .Select(a => a.CastMotion)
                .ToHashSet();

            var unused = Motions.Where(a => !used.Contains(a)).ToArray();

            Assert.IsEmpty(unused,
                "No ability routes to these archetypes, so their clips are unreachable: " +
                string.Join(", ", unused));
        }

        // ---------------------------------------------------------------- controller

        [Test]
        public void Controller_HasAStateForEveryArchetype()
        {
            var states = Controller.layers[0].stateMachine.states.Select(s => s.state.name).ToHashSet();
            var missing = Motions.Where(a => !states.Contains(StateName(a))).ToArray();

            Assert.IsEmpty(missing,
                "Chicken.controller has no state for these archetypes, so an ability declaring one " +
                "falls through to the generic Cast fallback and plays the wrong motion with no " +
                "error: " + string.Join(", ", missing.Select(StateName)));
        }

        [Test]
        public void Controller_HasTheCastArchetypeParameter()
        {
            Assert.IsTrue(
                Controller.parameters.Any(p => p.name == "CastArchetype" &&
                                               p.type == AnimatorControllerParameterType.Int),
                "Chicken.controller is missing the int parameter 'CastArchetype'. " +
                "ChickenAnimator.TriggerAbilityCast writes it to select the state; without it " +
                "Unity logs a warning per cast and every archetype falls to the fallback.");
        }

        [Test]
        public void EveryArchetypeState_IsReachableFromAnyState_OnItsOwnOrdinal()
        {
            var anyState = Controller.layers[0].stateMachine.anyStateTransitions;

            foreach (var archetype in Motions)
            {
                var t = anyState.FirstOrDefault(x => x.destinationState != null &&
                                                     x.destinationState.name == StateName(archetype));
                Assert.IsNotNull(t,
                    $"No Any State transition reaches {StateName(archetype)}, so that archetype " +
                    "can never play.");

                Assert.IsTrue(
                    t.conditions.Any(c => c.parameter == "CastArchetype" &&
                                          c.mode == AnimatorConditionMode.Equals &&
                                          Mathf.RoundToInt(c.threshold) == (int)archetype),
                    $"{StateName(archetype)} is not gated on CastArchetype == {(int)archetype}. " +
                    "Its transition would then fire for the wrong archetypes, or for all of them.");

                Assert.IsTrue(
                    t.conditions.Any(c => c.parameter == "AbilityCast"),
                    $"{StateName(archetype)} does not require the AbilityCast trigger, so it can " +
                    "fire from a stale CastArchetype value with no cast having been requested.");
            }
        }

        [Test]
        public void ArchetypeTransitions_AreOrderedAheadOfTheGenericCastFallback()
        {
            // Unity takes the FIRST Any State transition whose conditions pass. The generic Cast
            // transition tests only the AbilityCast trigger, so anywhere it sits ahead of the
            // archetype transitions it matches first and every archetype plays the same clip. That
            // failure looks exactly like success: a cast animation does play.
            var anyState = Controller.layers[0].stateMachine.anyStateTransitions;

            int generic = System.Array.FindIndex(anyState, t =>
                t.destinationState != null &&
                t.destinationState.name == "Cast" &&
                t.conditions.All(c => c.parameter != "CastArchetype"));

            if (generic < 0) return;   // fallback removed entirely: nothing can shadow the archetypes

            foreach (var archetype in Motions)
            {
                int idx = System.Array.FindIndex(anyState, t =>
                    t.destinationState != null && t.destinationState.name == StateName(archetype));

                Assert.Less(idx, generic,
                    $"{StateName(archetype)} is ordered behind the generic Cast fallback " +
                    $"(index {idx} vs {generic}). The fallback matches on the AbilityCast trigger " +
                    "alone, so it wins and this archetype never plays.");
            }
        }

        [Test]
        public void StunAndHit_StillOutrankEveryCast()
        {
            // Pre-existing priority, asserted because inserting the archetype transitions is exactly
            // the kind of edit that would quietly reorder it. A cast must not be able to override
            // being stunned.
            var anyState = Controller.layers[0].stateMachine.anyStateTransitions;

            int Index(string state) => System.Array.FindIndex(anyState, t =>
                t.destinationState != null && t.destinationState.name == state);

            int firstCast = Motions.Select(a => Index(StateName(a))).Where(i => i >= 0).Min();

            Assert.Less(Index("Stunned"), firstCast,
                "A cast transition now outranks Stunned, so casting would interrupt a stun.");
            Assert.Less(Index("Hit"), firstCast,
                "A cast transition now outranks Hit, so casting would swallow the hit reaction.");
        }

        // ---------------------------------------------------------------- clips

        [Test]
        public void EveryClass_SuppliesAClipForEveryArchetype()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(RegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(registry.TryGet(cls, out var entry), $"No registry entry for {cls}.");

                foreach (var archetype in Motions)
                {
                    Assert.IsNotNull(entry.Clips.CastFor(archetype),
                        $"{cls} has no clip for {archetype}. The state falls back to its empty " +
                        "placeholder, so every ability with that archetype freezes the chicken for " +
                        "the length of the cast without logging anything.");
                }

                Assert.IsNull(entry.Clips.CastFor(CastArchetype.None),
                    $"{cls} has a clip bound to CastArchetype.None, which is not a motion.");
            }
        }

        [Test]
        public void ArchetypeClips_AreOneShot_NotLooping()
        {
            // Loop flags are importer state and default off, but these clips are written by a
            // generator: a regression there would loop the cast forever rather than returning to
            // Idle, since the exit transition waits on normalized time.
            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(RegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                registry.TryGet(cls, out var entry);
                foreach (var archetype in Motions)
                {
                    var clip = entry.Clips.CastFor(archetype);
                    if (clip == null) continue;   // reported by EveryClass_SuppliesAClipForEveryArchetype
                    Assert.IsFalse(clip.isLooping,
                        $"{cls}/{archetype} ({clip.name}) is looping. A cast beat is one-shot; a " +
                        "looping clip never satisfies the exit transition and the chicken stays " +
                        "in the cast state.");
                }
            }
        }

        [Test]
        public void ArchetypeClips_DoNotKeyTheModelRootTransform()
        {
            // Mirrors the existing DataIntegrityTests rule for the five shipped clips. The model
            // root belongs to ChickenAnimator's lean/bank write, and a clip that keys it fights
            // that every frame.
            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(RegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                registry.TryGet(cls, out var entry);
                foreach (var archetype in Motions)
                {
                    var clip = entry.Clips.CastFor(archetype);
                    if (clip == null) continue;

                    var rootBindings = AnimationUtility.GetCurveBindings(clip)
                        .Where(b => string.IsNullOrEmpty(b.path))
                        .Select(b => b.propertyName)
                        .ToArray();

                    Assert.IsEmpty(rootBindings,
                        $"{cls}/{archetype} keys the model root ({string.Join(", ", rootBindings)}). " +
                        "ChickenAnimator drives that transform, so the two would fight.");
                }
            }
        }

        [Test]
        public void ClassClips_ReportIncompleteWhenAnArchetypeIsMissing()
        {
            // Pins the guard itself. IsComplete is what ChickenController consults before binding
            // the override controller, so if it ignored the archetype set a half-filled registry
            // would be reported as fine.
            var registry = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(RegistryPath);
            registry.TryGet(ChickenClass.Warrior, out var entry);

            var clips = entry.Clips;
            Assert.IsTrue(clips.IsComplete, "Warrior's clip set should be complete to start from.");

            clips.CastByArchetype = clips.CastByArchetype.ToArray();   // copy; do not disturb the asset
            clips.CastByArchetype[(int)CastArchetype.Flare] = null;

            Assert.IsFalse(clips.IsComplete,
                "IsComplete still reports true with an archetype clip missing, so a partially " +
                "wired registry would bind silently and freeze on that archetype.");
            Assert.AreEqual("Cast_" + CastArchetype.Flare, clips.FirstMissing,
                "FirstMissing does not name the absent archetype, so the error message would not " +
                "say which clip to fix.");
        }
    }
}
