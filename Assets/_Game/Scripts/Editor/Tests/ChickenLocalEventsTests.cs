using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// A12 (architecture audit 2026-11-12, decision D2a): one local observer of a chicken's replicated one-shot
    /// counters. The edge rules and the observer are exercised for real over plain snapshots (no runner needed); that
    /// no presentation component kept a cursor of its own is a source guard.
    /// </summary>
    public sealed class ChickenLocalEventsTests
    {
        private const string ScriptsRoot = "Assets/_Game/Scripts";

        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        private T Ability<T>(CastArchetype motion = CastArchetype.Lunge) where T : AbilityBaseSO
        {
            var a = ScriptableObject.CreateInstance<T>();
            a.CastMotion = motion;
            _made.Add(a);
            return a;
        }

        // ---- EventEdge: the cursor rules ---------------------------------------------------------------------------

        [Test]
        public void TheFirstObservation_Seeds_AndFiresNothing_WhateverTheId()
        {
            foreach (byte inherited in new byte[] { 0, 1, 37, 255 })
            {
                var edge = new EventEdge();
                Assert.IsFalse(edge.Seeded);
                Assert.IsFalse(edge.WouldFire(inherited));
                Assert.IsFalse(edge.Observe(inherited), $"a late joiner inheriting id {inherited} must not replay it");
                Assert.IsTrue(edge.Seeded);
                Assert.IsFalse(edge.Observe(inherited), "an unchanged id never fires");
            }
        }

        [Test]
        public void ABump_FiresOnce()
        {
            var edge = new EventEdge();
            edge.Observe(4);
            Assert.IsTrue(edge.WouldFire(5));
            Assert.IsTrue(edge.Observe(5));
            Assert.IsFalse(edge.WouldFire(5));
            Assert.IsFalse(edge.Observe(5), "the same bump is not delivered twice");
            Assert.IsFalse(edge.Observe(5));
        }

        [Test]
        public void TheByteWrap_255To0_FiresOnce()
        {
            var edge = new EventEdge();
            edge.Observe(255);
            Assert.IsTrue(edge.Observe(unchecked((byte)(255 + 1))), "255 -> 0 is a change like any other");
            Assert.IsFalse(edge.Observe(0));
        }

        [Test]
        public void SeveralBumps_BetweenTwoObservations_FireOnce()
        {
            // The rule: the fields written beside the id describe only the latest moment, so one event carries it.
            var edge = new EventEdge();
            edge.Observe(10);
            Assert.IsTrue(edge.Observe(13), "three bumps, one observation: one event");
            Assert.IsFalse(edge.Observe(13));
        }

        [Test]
        public void Reset_ReSeeds_SoARecycledObjectNeverReplaysItsPreviousLife()
        {
            var edge = new EventEdge();
            edge.Observe(3);
            edge.Reset();
            Assert.IsFalse(edge.Seeded);
            Assert.IsFalse(edge.Observe(9), "the first observation after a reset seeds again");
            Assert.IsTrue(edge.Observe(10));
        }

        // ---- ChickenLocalEvents: the observer over snapshots ---------------------------------------------------------

        private sealed class Counts
        {
            public int Cast, Knockback, SelfImpulse, Jump, Stolen;
            public CastEvent LastCast;
            public StealEvent LastSteal;

            public Counts(ChickenLocalEvents e)
            {
                e.Cast += c => { Cast++; LastCast = c; };
                e.Knockback += () => Knockback++;
                e.SelfImpulse += () => SelfImpulse++;
                e.Jump += () => Jump++;
                e.Stolen += s => { Stolen++; LastSteal = s; };
            }

            public int Total => Cast + Knockback + SelfImpulse + Jump + Stolen;
        }

        private static ChickenEventSnapshot Snapshot(byte cast = 0, byte knock = 0, byte self = 0, byte jump = 0, byte steal = 0) =>
            new ChickenEventSnapshot
            {
                HasAbilities = true, CastEventId = cast, CastSlot = AbilityController.InvalidSlot,
                KnockbackEventId = knock, SelfImpulseEventId = self, JumpEventId = jump,
                HasCargo = true, StealEventId = steal,
            };

        [Test]
        public void ALateJoiner_InheritingNonZeroIds_RaisesNothing()
        {
            var events = new ChickenLocalEvents();
            var seen = new Counts(events);
            var s = Snapshot(cast: 200, knock: 7, self: 3, jump: 12, steal: 91);
            events.Observe(in s);
            events.Observe(in s);
            Assert.AreEqual(0, seen.Total);
        }

        [Test]
        public void EachCounter_RaisesItsOwnEvent_AndOnlyItsOwn()
        {
            var events = new ChickenLocalEvents();
            var seen = new Counts(events);
            events.Observe(Snapshot());

            events.Observe(Snapshot(knock: 1));
            Assert.AreEqual((0, 1, 0, 0, 0), (seen.Cast, seen.Knockback, seen.SelfImpulse, seen.Jump, seen.Stolen));

            events.Observe(Snapshot(knock: 1, self: 1));
            Assert.AreEqual((0, 1, 1, 0, 0), (seen.Cast, seen.Knockback, seen.SelfImpulse, seen.Jump, seen.Stolen),
                "a self-applied dodge is not a knockback");

            events.Observe(Snapshot(knock: 1, self: 1, jump: 1));
            events.Observe(Snapshot(knock: 1, self: 1, jump: 1, steal: 1));
            events.Observe(Snapshot(cast: 1, knock: 1, self: 1, jump: 1, steal: 1));
            Assert.AreEqual((1, 1, 1, 1, 1), (seen.Cast, seen.Knockback, seen.SelfImpulse, seen.Jump, seen.Stolen));

            events.Observe(Snapshot(cast: 1, knock: 1, self: 1, jump: 1, steal: 1));
            Assert.AreEqual(5, seen.Total, "an unchanged snapshot raises nothing");
        }

        [Test]
        public void ManyObservationsInOneFrame_DeliverABumpOnce_ToEveryListener()
        {
            // Listeners observe at their own point in the frame; the shared cursor means the first call raises the
            // event for all of them and the later calls raise nothing.
            var events = new ChickenLocalEvents();
            var a = new Counts(events);
            var b = new Counts(events);
            events.Observe(Snapshot());
            var bumped = Snapshot(cast: 1);
            for (int i = 0; i < 6; i++) events.Observe(in bumped);
            Assert.AreEqual(1, a.Cast);
            Assert.AreEqual(1, b.Cast);
        }

        [Test]
        public void TheCastEvent_CarriesIdsAndNumbers_ReadForThatCast()
        {
            var stun = Ability<WingSlamAbilitySO>(CastArchetype.Slam);
            var events = new ChickenLocalEvents();
            var seen = new Counts(events);
            events.Observe(Snapshot());

            var s = Snapshot(cast: 1);
            s.CastSlot = 2;
            s.CastAbility = stun;
            s.CastHitCount = 3;
            events.Observe(in s);

            Assert.AreEqual(1, seen.Cast);
            Assert.AreEqual(2, seen.LastCast.Slot);
            Assert.AreSame(stun, seen.LastCast.Ability);
            Assert.AreSame(stun, seen.LastCast.AimedAbility);
            Assert.AreEqual(3, seen.LastCast.HitCount);
            Assert.AreEqual(CastArchetype.Slam, seen.LastCast.Archetype);
            Assert.IsFalse(seen.LastCast.IsPeck);
        }

        [Test]
        public void APeckCast_IsFlaggedAsPeck()
        {
            var events = new ChickenLocalEvents();
            var seen = new Counts(events);
            events.Observe(Snapshot());
            var s = Snapshot(cast: 1);
            s.CastAbility = Ability<PeckAbilitySO>();
            events.Observe(in s);
            Assert.IsTrue(seen.LastCast.IsPeck);
        }

        [Test]
        public void AnUnresolvedCast_FallsBackToTheAbilityLastSeenCharging()
        {
            // TryActivate clears the charge in the tick it fires; a peer that has not resolved the slot still knows
            // what it saw being aimed.
            var aimed = Ability<CluckShockAbilitySO>(CastArchetype.Flare);
            var events = new ChickenLocalEvents();
            var seen = new Counts(events);

            var charging = Snapshot();
            charging.ChargingAbility = aimed;
            events.Observe(in charging);

            events.Observe(Snapshot(cast: 1));   // no CastAbility, charge already cleared
            Assert.IsNull(seen.LastCast.Ability);
            Assert.AreSame(aimed, seen.LastCast.AimedAbility);
            Assert.AreEqual(CastArchetype.Flare, seen.LastCast.Archetype);
        }

        [Test]
        public void ACastNobodyResolved_StillRaises_WithNoArchetype()
        {
            var events = new ChickenLocalEvents();
            var seen = new Counts(events);
            events.Observe(Snapshot());
            events.Observe(Snapshot(cast: 1));
            Assert.AreEqual(1, seen.Cast);
            Assert.IsNull(seen.LastCast.AimedAbility);
            Assert.AreEqual(CastArchetype.None, seen.LastCast.Archetype);
        }

        [Test]
        public void AChickenWithoutCargo_OrAbilities_SkipsThoseCounters()
        {
            // A Doppelganger decoy has no ChickenCargo.
            var events = new ChickenLocalEvents();
            var seen = new Counts(events);
            var s = new ChickenEventSnapshot();
            events.Observe(in s);
            s.StealEventId = 5;
            s.CastEventId = 5;
            s.KnockbackEventId = 1;
            events.Observe(in s);
            Assert.AreEqual((0, 1, 0), (seen.Cast, seen.Knockback, seen.Stolen));
        }

        [Test]
        public void Reset_ReSeedsEveryCounter_AndForgetsTheChargedAbility()
        {
            var aimed = Ability<CluckShockAbilitySO>();
            var events = new ChickenLocalEvents();
            var seen = new Counts(events);
            var charging = Snapshot(cast: 4, knock: 4, self: 4, jump: 4, steal: 4);
            charging.ChargingAbility = aimed;
            events.Observe(in charging);

            events.Reset();   // respawn / pool reuse
            events.Observe(Snapshot(cast: 9, knock: 9, self: 9, jump: 9, steal: 9));
            Assert.AreEqual(0, seen.Total, "the next life's first observation only seeds");

            events.Observe(Snapshot(cast: 10, knock: 9, self: 9, jump: 9, steal: 9));
            Assert.AreEqual(1, seen.Cast);
            Assert.IsNull(seen.LastCast.AimedAbility, "the previous life's charge is not this cast's ability");
        }

        [Test]
        public void AListenerThatObservesFromInsideACallback_DoesNotReenter()
        {
            var events = new ChickenLocalEvents();
            int casts = 0;
            var bumped = Snapshot(cast: 1, knock: 1);
            events.Cast += _ => { casts++; events.Observe(in bumped); };
            int knocks = 0;
            events.Knockback += () => knocks++;
            events.Observe(Snapshot());
            events.Observe(in bumped);
            Assert.AreEqual(1, casts);
            Assert.AreEqual(1, knocks, "the outer pass still delivers the rest");
        }

        [Test]
        public void AListenerThatThrows_IsNotSwallowed_AndTheObserverRecovers()
        {
            var events = new ChickenLocalEvents();
            bool fail = true;
            int casts = 0;
            events.Cast += _ => { casts++; if (fail) throw new System.InvalidOperationException("listener"); };
            events.Observe(Snapshot());
            Assert.Throws<System.InvalidOperationException>(() => events.Observe(Snapshot(cast: 1)));
            fail = false;
            events.Observe(Snapshot(cast: 2));
            Assert.AreEqual(2, casts, "the observer is usable after a listener threw");
        }

        // ---- Source guards (what no test can run without a live runner) ---------------------------------------------

        private static readonly string[] Watchers =
        {
            "Visuals/ChickenVFX.cs", "Visuals/HitFeedback.cs", "Visuals/ControlStateVFX.cs",
            "Visuals/AbilityRangeIndicator.cs", "Visuals/ChickenAnimator.cs",
            "Input/TouchControlsController.cs", "AbilityLab/AbilityLabHud.cs",
        };

        private static string Code(string relative)
        {
            string src = File.ReadAllText($"{ScriptsRoot}/{relative}");
            src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(src, @"//[^\n]*", string.Empty);
        }

        [Test]
        public void NoPresentationComponent_ReadsAOneShotCounterItself()
        {
            // Source guard. The counters are read in exactly one place outside gameplay: ChickenLocalEvents.
            var counter = new Regex(@"\b(LastCastEventId|KnockbackEventId|SelfImpulseEventId|JumpEventId|StealEventId)\b");
            foreach (string folder in new[] { "Visuals", "UI", "Input", "AbilityLab", "Audio" })
                foreach (string path in Directory.GetFiles($"{ScriptsRoot}/{folder}", "*.cs", SearchOption.AllDirectories))
                {
                    string code = Code(path.Substring(ScriptsRoot.Length + 1).Replace('\\', '/'));
                    var m = counter.Match(code);
                    Assert.IsFalse(m.Success, $"{path} reads {m.Value} itself. Subscribe to ChickenController.LocalEvents instead: " +
                        "one observer per chicken, no private cursor.");
                }
        }

        [Test]
        public void EveryWatcher_PairsItsSubscriptions()
        {
            // Source guard: each `+=` on a ChickenLocalEvents event has its `-=`.
            foreach (string file in Watchers)
            {
                string code = Code(file);
                var subs = Regex.Matches(code, @"\b(\w*[eE]vents)\.(Cast|Knockback|SelfImpulse|Jump|Stolen)\s*\+=\s*(\w+)");
                Assert.Greater(subs.Count, 0, $"{file} no longer subscribes to ChickenLocalEvents");
                foreach (Match m in subs)
                    StringAssert.IsMatch($@"\.{m.Groups[2].Value}\s*-=\s*{m.Groups[3].Value}\b", code,
                        $"{file} subscribes {m.Groups[3].Value} to {m.Groups[2].Value} and never unsubscribes it");
            }
        }

        [Test]
        public void TheObserver_IsGameplaySide_AndKnowsNoPresentationType()
        {
            foreach (string file in new[] { "Gameplay/ChickenLocalEvents.cs", "Gameplay/EventEdge.cs" })
            {
                string code = Code(file);
                foreach (string ns in new[] { "CluckWars.Visuals", "CluckWars.UI", "CluckWars.Audio", "UnityEngine.UI" })
                    StringAssert.DoesNotContain(ns, code, $"{file} must not depend on {ns}: Visuals depends on it, not the reverse");
                StringAssert.DoesNotContain("AudioClip", code, "payloads carry ids, never clips");
                StringAssert.DoesNotContain("string ", code, "payloads carry ids and keys, never display strings");
            }
        }

        [Test]
        public void TheController_ObservesInRender_AndReSeedsOnSpawnAndDespawn()
        {
            string code = Code("Gameplay/ChickenController.cs");
            StringAssert.IsMatch(@"public override void Render\(\)\s*=>\s*LocalEvents\.Observe\(\);", code);
            string spawned = AssassinExecuteCreditTests.MethodBody($"{ScriptsRoot}/Gameplay/ChickenController.cs", "public override void Spawned()");
            StringAssert.Contains("LocalEvents.Reset()", spawned);
            string despawned = AssassinExecuteCreditTests.MethodBody($"{ScriptsRoot}/Gameplay/ChickenController.cs", "public override void Despawned(");
            StringAssert.Contains("_localEvents?.Reset()", despawned);
        }
    }
}
