using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A04 (architecture audit 2026-11-12): the cast / peck animation and the cast / deposit sounds are presentation,
    /// played from the replicated event on every peer, never from gameplay on the state authority.
    /// </summary>
    /// <remarks>
    /// The animator half is exercised for real: a <see cref="ChickenAnimator"/> on a chicken with no state authority
    /// (no runner at all) is subscribed the way the game subscribes it and fed the cast event through the chicken's
    /// own <see cref="ChickenLocalEvents"/>. That is exactly a rival on a remote peer. What needs a live runner (the
    /// sound's input-authority gate) is pinned as a pure rule plus source guards, named as such.
    /// </remarks>
    public sealed class CastPresentationTests
    {
        private const string ScriptsRoot = "Assets/_Game/Scripts";
        private const string ControllerPath = "Assets/_Game/Art/Animations/Chicken.controller";

        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private T Ability<T>(CastArchetype motion) where T : AbilityBaseSO
        {
            var a = ScriptableObject.CreateInstance<T>();
            a.CastMotion = motion;
            _made.Add(a);
            return a;
        }

        private static void Call(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        /// <summary>A chicken as a remote peer has it: components present, lifecycle run, no runner, no authority.</summary>
        private ChickenAnimator RivalChicken(out ChickenLocalEvents events, out Animator animator)
        {
            var go = new GameObject("A04_Rival");
            _made.Add(go);
            animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            var chickenAnimator = go.AddComponent<ChickenAnimator>();   // brings ChickenController with it
            // EditMode does not run Awake / OnEnable on a plain MonoBehaviour: run the real ones.
            Call(chickenAnimator, "Awake");
            Call(chickenAnimator, "OnEnable");
            events = go.GetComponent<ChickenController>().LocalEvents;
            events.Observe(Seed());
            return chickenAnimator;
        }

        private static ChickenEventSnapshot Seed() => new ChickenEventSnapshot { HasAbilities = true, CastSlot = AbilityController.InvalidSlot };

        private static ChickenEventSnapshot Cast(byte id, AbilityBaseSO ability) =>
            new ChickenEventSnapshot { HasAbilities = true, CastEventId = id, CastSlot = 1, CastAbility = ability };

        // ---- The animation, on a peer that is not the state authority ------------------------------------------------

        [Test]
        public void ARivalsCast_PlaysItsArchetype_OnAPeerWithNoAuthority()
        {
            var animatorComponent = RivalChicken(out var events, out var animator);
            Assert.AreEqual(0, animatorComponent.CastBeatsPlayed);

            events.Observe(Cast(1, Ability<WingSlamAbilitySO>(CastArchetype.Slam)));

            Assert.AreEqual(1, animatorComponent.CastBeatsPlayed,
                "the cast event reached nobody: rivals do not animate their casts (A04)");
            if (animator.runtimeAnimatorController != null && animator.isInitialized)
            {
                Assert.AreEqual((int)CastArchetype.Slam, animator.GetInteger("CastArchetype"));
                Assert.IsTrue(animator.GetBool("AbilityCast"), "the cast trigger is set");
            }
        }

        [Test]
        public void ARivalsPeck_PlaysThePeckBeat_NotACast()
        {
            var animatorComponent = RivalChicken(out var events, out var animator);
            events.Observe(Cast(1, Ability<PeckAbilitySO>(CastArchetype.Lunge)));

            Assert.AreEqual(1, animatorComponent.CastBeatsPlayed);
            if (animator.runtimeAnimatorController != null && animator.isInitialized)
            {
                Assert.IsTrue(animator.GetBool("Peck"), "the peck trigger is set");
                Assert.IsFalse(animator.GetBool("AbilityCast"), "a peck is not a combat cast");
            }
        }

        [Test]
        public void EveryCast_IsOneBeat_AndTheSameBumpIsNeverPlayedTwice()
        {
            var animatorComponent = RivalChicken(out var events, out _);
            var slam = Ability<WingSlamAbilitySO>(CastArchetype.Slam);
            for (byte id = 1; id <= 3; id++)
            {
                var cast = Cast(id, slam);
                events.Observe(in cast);
                events.Observe(in cast);   // a second observation in the same frame
            }
            Assert.AreEqual(3, animatorComponent.CastBeatsPlayed);
        }

        [Test]
        public void ACastThisPeerCannotResolve_OrAnAbilityThatIsNeverCast_PlaysNothing()
        {
            var animatorComponent = RivalChicken(out var events, out _);
            events.Observe(Cast(1, null));
            events.Observe(Cast(2, Ability<WingSlamAbilitySO>(CastArchetype.None)));
            Assert.AreEqual(0, animatorComponent.CastBeatsPlayed);
        }

        [Test]
        public void ADisabledAnimator_IsUnsubscribed_AndPlaysNothing()
        {
            var animatorComponent = RivalChicken(out var events, out _);
            Call(animatorComponent, "OnDisable");
            events.Observe(Cast(1, Ability<WingSlamAbilitySO>(CastArchetype.Slam)));
            Assert.AreEqual(0, animatorComponent.CastBeatsPlayed);
        }

        // ---- The deposit moment ------------------------------------------------------------------------------------------

        [Test]
        public void TheDepositCounter_RaisesDeposited_OncePerBump_AndNeverOnALateJoin()
        {
            var events = new ChickenLocalEvents();
            int deposits = 0;
            events.Deposited += () => deposits++;

            events.Observe(new ChickenEventSnapshot { HasCargo = true, DepositEventId = 40 });
            Assert.AreEqual(0, deposits, "an inherited id is the baseline");
            events.Observe(new ChickenEventSnapshot { HasCargo = true, DepositEventId = 41 });
            events.Observe(new ChickenEventSnapshot { HasCargo = true, DepositEventId = 41 });
            Assert.AreEqual(1, deposits);
        }

        // ---- The audience rule for the two own sounds --------------------------------------------------------------------

        [TestCase(true, false, false, true, TestName = "The human who drives the chicken hears it")]
        [TestCase(false, false, false, false, TestName = "A rival's is silent here")]
        [TestCase(false, true, false, false, TestName = "A bot's is silent on every peer")]
        [TestCase(true, true, false, false, TestName = "A bot is silent even with input authority")]
        [TestCase(true, false, true, false, TestName = "A decoy shares input authority and stays silent")]
        public void OwnSounds_AreHeardOnlyByTheHumanWhoDrivesTheChicken(bool inputAuthority, bool bot, bool decoy, bool expected) =>
            Assert.AreEqual(expected, OwnSoundRules.IsAudible(inputAuthority, bot, decoy));

        // ---- Source guards (need a live runner to run for real) ----------------------------------------------------------

        private static string Code(string relative)
        {
            string src = File.ReadAllText($"{ScriptsRoot}/{relative}");
            src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(src, @"//[^\n]*", string.Empty);
        }

        [Test]
        public void Gameplay_NoLongerPlaysTheCastAnimationOrSound_SourceGuard()
        {
            string abilities = Code("Gameplay/AbilityController.cs");
            foreach (string gone in new[] { "TriggerPeck", "TriggerAbilityCast", "ChickenAnimator", "AbilityActivate" })
                StringAssert.DoesNotContain(gone, abilities, $"AbilityController still touches {gone}: the cast is played from the cast event on every peer");
            string activate = AssassinExecuteCreditTests.MethodBody($"{ScriptsRoot}/Gameplay/AbilityController.cs", "private bool TryActivate(int slot)");
            StringAssert.Contains("LastCastEventId++", activate);
        }

        [Test]
        public void TheDepositSound_IsRaisedAsAnEvent_NotPlayedFromTheSettleRpc_SourceGuard()
        {
            string cargo = Code("Gameplay/ChickenCargo.cs");
            StringAssert.DoesNotContain("PlaySFX", cargo, "ChickenCargo plays no sound: it bumps DepositEventId");
            StringAssert.DoesNotContain("IAudioService", cargo);
            StringAssert.Contains("[Networked] public byte DepositEventId", cargo);
            string settle = AssassinExecuteCreditTests.MethodBody($"{ScriptsRoot}/Gameplay/ChickenCargo.cs", "public void RPC_SettleDeposit(");
            Assert.AreEqual(1, Regex.Matches(settle, @"DepositEventId\+\+").Count);
        }

        [Test]
        public void TheOwnSounds_AreKeyedById_AndGatedByTheAudienceRule_SourceGuard()
        {
            string feedback = Code("Visuals/HitFeedback.cs");
            StringAssert.Contains("PlayOwnSfx(SfxId.AbilityActivate)", feedback);
            StringAssert.Contains("PlayOwnSfx(SfxId.Deposit)", feedback);
            string play = AssassinExecuteCreditTests.MethodBody($"{ScriptsRoot}/Visuals/HitFeedback.cs", "private void PlayOwnSfx(SfxId id)");
            int gate = play.IndexOf("OwnSoundRules.IsAudible(_controller.HasInputAuthority, _controller.IsBot, _controller.IsDecoy)", System.StringComparison.Ordinal);
            int sound = play.IndexOf("_audio.PlaySFX(id)", System.StringComparison.Ordinal);
            Assert.Greater(gate, 0, "the audience gate is gone");
            Assert.Greater(sound, gate, "the gate comes before the sound");
        }
    }
}
