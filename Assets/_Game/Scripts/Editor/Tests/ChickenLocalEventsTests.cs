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

        /// <summary>An observer whose registry holds exactly <paramref name="abilities"/> (codes 1, 2, ... in that order).</summary>
        private ChickenLocalEvents EventsWith(params AbilityBaseSO[] abilities)
        {
            var registry = ScriptableObject.CreateInstance<AbilityRegistrySO>();
            registry.All = abilities;
            _made.Add(registry);
            return new ChickenLocalEvents { Registry = registry };
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
            var events = EventsWith(Ability<CluckShockAbilitySO>(), stun);   // stun is the registry's second entry: code 2
            var seen = new Counts(events);
            events.Observe(Snapshot());

            var s = Snapshot(cast: 1);
            s.CastSlot = 2;
            s.CastAbility = stun;
            s.CastHitCount = 3;
            events.Observe(in s);

            Assert.AreEqual(1, seen.Cast);
            Assert.AreEqual(2, seen.LastCast.Slot);
            Assert.AreEqual(2, seen.LastCast.AbilityCode, "the event carries the registry code, not the asset (S-5)");
            Assert.AreEqual(2, seen.LastCast.AimedAbilityCode);
            Assert.AreSame(stun, events.AbilityOf(seen.LastCast.AimedAbilityCode), "a listener resolves the asset through the registry");
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
            var events = EventsWith(aimed);
            var seen = new Counts(events);

            var charging = Snapshot();
            charging.ChargingAbility = aimed;
            events.Observe(in charging);

            events.Observe(Snapshot(cast: 1));   // no CastAbility, charge already cleared
            Assert.AreEqual(AbilityRegistrySO.NoAbilityCode, seen.LastCast.AbilityCode);
            Assert.AreSame(aimed, events.AbilityOf(seen.LastCast.AimedAbilityCode));
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
            Assert.AreEqual(AbilityRegistrySO.NoAbilityCode, seen.LastCast.AimedAbilityCode);
            Assert.IsNull(events.AbilityOf(seen.LastCast.AimedAbilityCode));
            Assert.AreEqual(CastArchetype.None, seen.LastCast.Archetype);
        }

        [Test]
        public void TheCastEvent_HoldsNoAssetReference_OnlyIdsEnumsAndNumbers()
        {
            // S-5, Maestro's rule: ids only. Checked on the type itself, so a field added later cannot bring one back.
            foreach (var field in typeof(CastEvent).GetFields(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                Assert.IsTrue(field.FieldType.IsPrimitive || field.FieldType.IsEnum,
                    $"CastEvent.{field.Name} is a {field.FieldType.Name}: a cast event carries ids, enums and numbers only");
            }
        }

        [Test]
        public void AnAbilityOutsideTheRegistry_HasNoCode_ButItsArchetypeStillTravels()
        {
            // Every shipped ability is in the registry (AbilitySystemTests pins it); an asset that is not has no id to
            // carry, so a listener that needs the asset gets none, while the animation beat is unaffected.
            var events = EventsWith(Ability<CluckShockAbilitySO>());
            var seen = new Counts(events);
            events.Observe(Snapshot());
            var s = Snapshot(cast: 1);
            s.CastAbility = Ability<WingSlamAbilitySO>(CastArchetype.Slam);
            events.Observe(in s);
            Assert.AreEqual(AbilityRegistrySO.NoAbilityCode, seen.LastCast.AimedAbilityCode);
            Assert.AreEqual(CastArchetype.Slam, seen.LastCast.Archetype);
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
            var events = EventsWith(aimed);
            var seen = new Counts(events);
            var charging = Snapshot(cast: 4, knock: 4, self: 4, jump: 4, steal: 4);
            charging.ChargingAbility = aimed;
            events.Observe(in charging);

            events.Reset();   // respawn / pool reuse
            events.Observe(Snapshot(cast: 9, knock: 9, self: 9, jump: 9, steal: 9));
            Assert.AreEqual(0, seen.Total, "the next life's first observation only seeds");

            events.Observe(Snapshot(cast: 10, knock: 9, self: 9, jump: 9, steal: 9));
            Assert.AreEqual(1, seen.Cast);
            Assert.AreEqual(AbilityRegistrySO.NoAbilityCode, seen.LastCast.AimedAbilityCode, "the previous life's charge is not this cast's ability");
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

        private sealed class ErrorLog : CluckWars.Logging.ILogService
        {
            public readonly List<(string message, System.Exception exception)> Errors = new List<(string, System.Exception)>();
            public CluckWars.Logging.LogLevel MinLevel { get; set; }
            public bool IsEnabled(CluckWars.Logging.LogLevel level) => true;
            public void Verbose(string source, string message) { }
            public void Debug(string source, string message) { }
            public void Info(string source, string message) { }
            public void Warn(string source, string message) { }
            public void Error(string source, string message, System.Exception exception = null) => Errors.Add(($"[{source}] {message}", exception));
        }

        private sealed class BrokenCastListener
        {
            public int Calls;
            public void OnCast(CastEvent e) { Calls++; throw new System.InvalidOperationException("listener"); }
        }

        [Test]
        public void AListenerThatThrows_IsReported_AndEveryOtherListenerStillGetsTheBeat()
        {
            // S-1: the edge has already advanced when the listeners run, so a throw that stopped the raise would
            // cost every later subscriber the beat for good and unwind into whoever observed (ChickenCargo.Render).
            var log = new ErrorLog();
            var events = new ChickenLocalEvents { Log = log };
            var broken = new BrokenCastListener();
            int before = 0, after = 0, knocks = 0;
            events.Cast += _ => before++;
            events.Cast += broken.OnCast;
            events.Cast += _ => after++;
            events.Knockback += () => knocks++;
            events.Observe(Snapshot());

            Assert.DoesNotThrow(() => events.Observe(Snapshot(cast: 1, knock: 1)));

            Assert.AreEqual((1, 1, 1), (before, broken.Calls, after), "the subscribers either side of the broken one ran");
            Assert.AreEqual(1, knocks, "the rest of the observation ran: the next event of the same pass is delivered");
            Assert.AreEqual(1, log.Errors.Count, "reported, not swallowed");
            StringAssert.Contains("[ChickenEvents] A Cast listener (BrokenCastListener.OnCast) threw", log.Errors[0].message);
            Assert.IsInstanceOf<System.InvalidOperationException>(log.Errors[0].exception);

            // Rate-limited per event and listener type: a listener that throws on every cast does not flood the log.
            events.Observe(Snapshot(cast: 2, knock: 1));
            Assert.AreEqual((2, 2, 2), (before, broken.Calls, after), "the observer keeps working after a listener threw");
            Assert.AreEqual(1, log.Errors.Count, "the second failure inside the interval is counted, not logged");
        }

        [Test]
        public void ALocalRaise_SurvivesAThrowingListenerToo()
        {
            var log = new ErrorLog();
            var events = new ChickenLocalEvents { Log = log };
            int seen = 0;
            events.CargoChanged += _ => throw new System.ArgumentException("cargo listener");
            events.CargoChanged += _ => seen++;

            Assert.DoesNotThrow(() => events.RaiseCargoChanged(2f, -1));

            Assert.AreEqual(1, seen);
            Assert.AreEqual(1, log.Errors.Count);
            StringAssert.Contains("A CargoChanged listener", log.Errors[0].message);
        }

        [Test]
        public void Subscriptions_BehaveLikeDelegates()
        {
            var events = new ChickenLocalEvents();
            int a = 0, b = 0;
            System.Action onA = () => a++;
            System.Action onB = null;
            onB = () => { b++; events.Knockback -= onB; };   // unsubscribing mid-raise does not disturb the raise
            events.Knockback += onA;
            events.Knockback += onB;
            events.Knockback += onA;
            events.Observe(Snapshot());

            events.Observe(Snapshot(knock: 1));
            Assert.AreEqual((2, 1), (a, b), "a delegate subscribed twice runs twice");

            events.Knockback -= onA;                        // removes one subscription, as -= does
            events.Observe(Snapshot(knock: 2));
            Assert.AreEqual((3, 1), (a, b));
        }

        [Test]
        public void TheListenerList_OnlyBuildsANewArrayWhenASubscriptionChanges()
        {
            // What a raise iterates: the same array instance until someone subscribes or unsubscribes, so raising
            // allocates nothing and a raise in progress is never disturbed.
            var list = new ListenerList<System.Action>();
            System.Action one = () => { }, two = () => { };
            list.Add(one);
            list.Add(two);
            var items = list.Items;
            Assert.AreSame(items, list.Items);
            CollectionAssert.AreEqual(new[] { one, two }, items);

            list.Remove(one);
            Assert.AreNotSame(items, list.Items);
            CollectionAssert.AreEqual(new[] { one, two }, items, "the array a raise already holds is untouched");
            CollectionAssert.AreEqual(new[] { two }, list.Items);

            list.Remove(one);                               // not subscribed: nothing changes
            list.Add(null);
            Assert.AreEqual(1, list.Count);
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
            string src = TestSources.ReadClass($"{ScriptsRoot}/{relative}");
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
