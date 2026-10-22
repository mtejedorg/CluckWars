using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// F4 (decisions 2026-10-22): <c>ChickenController.RPC_ApplyStun</c>, <c>RPC_ApplyRoot</c> and
    /// <c>RPC_ApplyAbilitySlow</c> are <c>RpcSources.All</c> and used to honour any caller with any
    /// duration. They now name the casting chicken and are judged by <see cref="ControlClaimRules"/>.
    /// </summary>
    /// <remarks>
    /// Two halves, as <see cref="ExecuteRpcValidationTests"/>: the rule tests call the same
    /// <see cref="ControlClaimRules"/> entry points production calls, with the ceilings derived from
    /// the shipped <c>AbilityRegistry.asset</c> exactly as <c>ChickenController</c> derives them; the
    /// structural tests pin the wiring a live runner would be needed to exercise. The honest-caller
    /// tests are the important ones: a bound that drifts below a real ability's numbers rejects real
    /// stuns at runtime with nothing else going red (the merge.ps1 rule then sees it in the matrix).
    /// </remarks>
    public sealed class ControlRpcValidationTests
    {
        private const string ScriptsRoot     = "Assets/_Game/Scripts";
        private const string AbilityDir      = "Assets/_Game/Scripts/Abilities";
        private const string ControllerPath  = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";

        private static readonly (string Rpc, ControlKind Kind)[] Rpcs =
        {
            ("RPC_ApplyStun", ControlKind.Stun),
            ("RPC_ApplyRoot", ControlKind.Root),
            ("RPC_ApplyAbilitySlow", ControlKind.Slow),
        };

        private static string Src(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        private static AbilityRegistrySO Registry() => TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);

        private static ControlClaimRules.Ceiling Shipped(ControlKind kind) =>
            ControlClaimRules.CeilingFor(Registry().All, kind);

        private static List<AbilityBaseSO> AllAbilityAssets() =>
            TestAssets.LoadAllIn<ScriptableObject>(TestAssets.AbilitiesDir).OfType<AbilityBaseSO>().ToList();

        /// <summary>An honest claim: attributable, live caster, in reach.</summary>
        private static ControlClaimRules.Claim Honest(ControlKind kind, float seconds, float factor = 1f, float distance = 2f) =>
            new ControlClaimRules.Claim
            {
                Kind = kind,
                Seconds = seconds,
                SlowFactor = factor,
                CasterResolved = true,
                SenderSpeaksForCaster = true,
                PlanarDistance = distance,
                RangeMargin = 0f,
            };

        // ---- 1. Honest callers pass ---------------------------------------------------------

        [Test]
        public void EveryShippedEmitter_AtItsOwnNumbers_AndTheEdgeOfItsReach_IsAccepted()
        {
            var emitters = Registry().All.Where(a => a != null && a.EmittedControl.Kind != ControlKind.None).ToList();
            Assert.GreaterOrEqual(emitters.Count, 7, "fewer control emitters in the registry than shipped (Ambush, Wing Slam, " +
                "Belly Flop, Headbutt, Ground Quake, Root Egg, Dust Kick) - the bound has gone blind to some of them");

            foreach (var a in emitters)
            {
                var e = a.EmittedControl;
                var claim = Honest(e.Kind, e.Seconds, e.SlowFactor, ControlClaimRules.Reach(a));
                var verdict = ControlClaimRules.Evaluate(claim, Shipped(e.Kind));
                Assert.AreEqual(ControlClaimRules.Verdict.Accepted, verdict,
                    $"{a.name}: its own {e.Kind} ({e.Seconds:0.00}s, factor {e.SlowFactor:0.00}) at its reach " +
                    $"{ControlClaimRules.Reach(a):0.00} m is refused ({verdict}) - every real cast of it would be rejected at the victim.");
            }
        }

        [Test]
        public void EveryEmitterAssetOnDisk_FitsInsideTheRegistrysCeiling()
        {
            // Recomputed from the assets on disk, as StealRulesTests does, so an ability that exists but
            // is missing from the registry shows up as a ceiling too small for it.
            foreach (var a in AllAbilityAssets())
            {
                var e = a.EmittedControl;
                if (e.Kind == ControlKind.None) continue;
                var c = Shipped(e.Kind);
                Assert.IsTrue(ControlClaimRules.IsPlausibleSeconds(e.Seconds, c), $"{a.name}: {e.Seconds}s is above the registry's {e.Kind} ceiling {c.MaxSeconds}s");
                Assert.LessOrEqual(ControlClaimRules.Reach(a), c.MaxReach + 1e-4f, $"{a.name}: reach above the registry's {e.Kind} reach");
                if (e.Kind == ControlKind.Slow)
                    Assert.IsTrue(ControlClaimRules.IsPlausibleSlowFactor(e.SlowFactor, c), $"{a.name}: factor {e.SlowFactor} stronger than the ceiling");
            }
        }

        [Test]
        public void EveryKind_HasAShippedEmitter_SoItsCeilingIsResolved()
        {
            foreach (var (rpc, kind) in Rpcs)
            {
                var c = Shipped(kind);
                Assert.IsTrue(c.Known && c.MaxSeconds > 0f && c.MaxReach > 0f,
                    $"{rpc}: no {kind} emitter in the registry, so ChickenController falls back to sender-only checks (and logs an Error).");
            }
        }

        [Test]
        public void BellyFlop_ReachIncludesItsJump()
        {
            // It stuns at the landing; the victim's peer may still hold the take-off pose.
            var flop = AllAbilityAssets().OfType<BellyFlopAbilitySO>().Single();
            Assert.AreEqual(flop.ImpactRadius + StealRules.MaxJumpTravel(flop.JumpTier), ControlClaimRules.Reach(flop), 1e-4f);
            Assert.Greater(StealRules.MaxJumpTravel(flop.JumpTier), 0f);
        }

        [Test]
        public void RootEgg_FarFromItsCaster_IsAcceptedOnlyInsideTheCastersZone()
        {
            var egg = AllAbilityAssets().OfType<RootEggAbilitySO>().Single();
            var claim = Honest(ControlKind.Root, egg.RootDuration, distance: 30f);
            Assert.AreEqual(ControlClaimRules.Verdict.OutOfRange, ControlClaimRules.Evaluate(claim, Shipped(ControlKind.Root)));
            claim.InsideCastersRootZone = true;
            Assert.AreEqual(ControlClaimRules.Verdict.Accepted, ControlClaimRules.Evaluate(claim, Shipped(ControlKind.Root)),
                "an egg placed long ago acts while its owner is across the map");
        }

        [Test]
        public void RemoteLatency_WidensTheRangeByTheDrift()
        {
            var c = Shipped(ControlKind.Stun);
            float margin = StealRules.RangeMargin(2f * 8f, ExecuteRules.RemoteLatency(0f) + 2f / 32f);
            var claim = Honest(ControlKind.Stun, c.MaxSeconds, distance: c.MaxReach + ControlClaimRules.RangeSlackMeters + margin - 0.01f);
            claim.RangeMargin = margin;
            Assert.AreEqual(ControlClaimRules.Verdict.Accepted, ControlClaimRules.Evaluate(claim, c));
        }

        // ---- 2. Forged claims are refused -----------------------------------------------------

        [Test]
        public void Forged_DurationAboveTheCeiling_IsRefused()
        {
            foreach (var (_, kind) in Rpcs)
            {
                var c = Shipped(kind);
                var claim = Honest(kind, c.MaxSeconds + 0.05f, kind == ControlKind.Slow ? c.MinSlowFactor : 1f);
                Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleDuration, ControlClaimRules.Evaluate(claim, c), kind.ToString());
            }
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Forged_NonPositiveOrNonFiniteDuration_IsRefused_EvenWithoutABound(float seconds)
        {
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleDuration,
                ControlClaimRules.Evaluate(Honest(ControlKind.Stun, seconds), Shipped(ControlKind.Stun)));
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleDuration,
                ControlClaimRules.Evaluate(Honest(ControlKind.Stun, seconds), ControlClaimRules.Ceiling.Unknown));
        }

        [TestCase(0.05f)]
        [TestCase(0f)]
        [TestCase(1f)]
        [TestCase(float.NaN)]
        public void Forged_SlowFactor_OutsideTheAuthoredRange_IsRefused(float factor)
        {
            var c = Shipped(ControlKind.Slow);
            Assume.That(factor < c.MinSlowFactor - 0.01f || factor >= 1f || float.IsNaN(factor) || factor <= 0f);
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleFactor,
                ControlClaimRules.Evaluate(Honest(ControlKind.Slow, c.MaxSeconds, factor), c));
        }

        [Test]
        public void Forged_FromAPeerThatDoesNotOwnTheCaster_IsRefused()
        {
            var claim = Honest(ControlKind.Stun, 1f);
            claim.SenderSpeaksForCaster = false;
            Assert.AreEqual(ControlClaimRules.Verdict.NotAttributable, ControlClaimRules.Evaluate(claim, Shipped(ControlKind.Stun)));
            // Attribution does not depend on authored data: still refused with no bound.
            Assert.AreEqual(ControlClaimRules.Verdict.NotAttributable, ControlClaimRules.Evaluate(claim, ControlClaimRules.Ceiling.Unknown));
        }

        [Test]
        public void Forged_UnresolvedSelfOrDecoyCaster_IsRefused()
        {
            var c = Shipped(ControlKind.Root);
            var unresolved = Honest(ControlKind.Root, 1f); unresolved.CasterResolved = false;
            var self = Honest(ControlKind.Root, 1f);       self.CasterIsVictim = true;
            var decoy = Honest(ControlKind.Root, 1f);      decoy.CasterIsDecoy = true;
            Assert.AreEqual(ControlClaimRules.Verdict.CasterUnresolved, ControlClaimRules.Evaluate(unresolved, c));
            Assert.AreEqual(ControlClaimRules.Verdict.SelfApplied, ControlClaimRules.Evaluate(self, c));
            Assert.AreEqual(ControlClaimRules.Verdict.DecoyCaster, ControlClaimRules.Evaluate(decoy, c));
        }

        [Test]
        public void Forged_FromAcrossTheArena_IsRefused_AndAZoneFlagDoesNotHelpAStun()
        {
            var c = Shipped(ControlKind.Stun);
            var claim = Honest(ControlKind.Stun, c.MaxSeconds, distance: 40f);
            Assert.AreEqual(ControlClaimRules.Verdict.OutOfRange, ControlClaimRules.Evaluate(claim, c));
            claim.InsideCastersRootZone = true;   // only roots come from zones
            Assert.AreEqual(ControlClaimRules.Verdict.OutOfRange, ControlClaimRules.Evaluate(claim, c));
        }

        [Test]
        public void Ceiling_ForAKindNothingProduces_RefusesEveryDuration()
        {
            var noControl = new AbilityBaseSO[] { null };
            var c = ControlClaimRules.CeilingFor(noControl, ControlKind.Stun);
            Assert.IsTrue(c.Known);
            Assert.AreEqual(ControlClaimRules.Verdict.ImplausibleDuration, ControlClaimRules.Evaluate(Honest(ControlKind.Stun, 0.1f), c));
            Assert.IsFalse(ControlClaimRules.CeilingFor(null, ControlKind.Stun).Known);
        }

        [Test]
        public void IsInsideZone_IsPlanar_AndHonoursTheMargin()
        {
            var center = new Vector3(5f, 0f, 5f);
            Assert.IsTrue(ControlClaimRules.IsInsideZone(center, 1.45f, new Vector3(6.4f, 3f, 5f), 0f), "height is ignored");
            Assert.IsFalse(ControlClaimRules.IsInsideZone(center, 1.45f, new Vector3(7.5f, 0f, 5f), 0f));
            Assert.IsTrue(ControlClaimRules.IsInsideZone(center, 1.45f, new Vector3(7.5f, 0f, 5f), 1.1f));
        }

        // ---- 2b. Dust Kick headroom (qa W3) -------------------------------------------------

        [Test]
        public void DustKick_AtItsEdge_WithASpeedBurstCaster_IsAccepted_ButNotMuchFurther()
        {
            var kick = AllAbilityAssets().OfType<DustKickAbilitySO>().Single();
            var c = Shipped(ControlKind.Slow);
            float edge = ControlClaimRules.Reach(kick) + ControlClaimRules.RangeSlackMeters;
            // A buffed fleeing Speedy outruns the DriftSpeed margin by ~1.3 m (see SlowReachHeadroomMeters).
            var claim = Honest(ControlKind.Slow, kick.SlowSeconds, kick.SlowFactor, edge + 1.3f);
            Assert.AreEqual(ControlClaimRules.Verdict.Accepted, ControlClaimRules.Evaluate(claim, c),
                "before W3 the slow bound had no headroom past Dust Kick's own reach");
            claim.PlanarDistance = edge + ControlClaimRules.SlowReachHeadroomMeters + 0.1f;
            Assert.AreEqual(ControlClaimRules.Verdict.OutOfRange, ControlClaimRules.Evaluate(claim, c));
        }

        [Test]
        public void SlowHeadroom_DoesNotLeakIntoStunOrRoot()
        {
            foreach (var kind in new[] { ControlKind.Stun, ControlKind.Root })
            {
                float poolMax = Registry().All.Where(a => a != null && a.EmittedControl.Kind == kind)
                                              .Max(a => ControlClaimRules.Reach(a));
                Assert.AreEqual(poolMax, Shipped(kind).MaxReach, 1e-4f, $"{kind} reach must be exactly the pool's");
            }
        }

        // ---- 2c. Root-zone evidence (security review) ---------------------------------------

        private static ControlClaimRules.RootZoneEvidence HonestZone(Vector3 center) => new ControlClaimRules.RootZoneEvidence
        {
            SameAuthorityAsCaster = true,
            Center = center,
            Radius = AllAbilityAssets().OfType<RootEggAbilitySO>().Single().EggRadius,
            HasCasterAtSpawn = true,
            CasterAtSpawn = center,          // Root Egg spawns at its caster's feet
            SpawnDrift = 0.5f,
            SecondsSinceSeen = 5f,
        };


        [Test]
        public void RootCeiling_ZoneGeometry_IsReadOffRootEgg()
        {
            var egg = AllAbilityAssets().OfType<RootEggAbilitySO>().Single();
            var c = Shipped(ControlKind.Root);
            Assert.AreEqual(egg.EggRadius, c.MaxZoneRadius, 1e-6f);
            Assert.AreEqual(egg.AimForwardOffset, c.MaxZonePlacement, 1e-6f);
            Assert.AreEqual(egg.EggLifetime, c.MaxZoneLifetime, 1e-6f, "the zone's authored life bounds how long it can vouch");
            Assert.AreEqual(0f, Shipped(ControlKind.Stun).MaxZoneRadius, "no stun comes from a zone");
        }

        [Test]
        public void RootZone_Honest_VouchesForAVictimInside_WhileItsCasterIsFarAway()
        {
            var zone = HonestZone(new Vector3(2f, 0f, 2f));
            Assert.IsTrue(ControlClaimRules.RootZoneVouches(zone, new Vector3(3f, 0f, 2f), 0.5f, Shipped(ControlKind.Root)));
        }

        [Test]
        public void RootZone_InflatedRadius_IsClampedToTheAuthoredOne()
        {
            var zone = HonestZone(new Vector3(2f, 0f, 2f));
            zone.Radius = 30f;   // attacker-written
            var victim = new Vector3(8f, 0f, 2f);
            Assert.IsTrue(ControlClaimRules.IsInsideZone(zone.Center, zone.Radius, victim, 0.5f), "fixture: the old check trusted this radius");
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, Shipped(ControlKind.Root)));
            zone.Radius = float.NaN;
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, new Vector3(3f, 0f, 2f), 0.5f, Shipped(ControlKind.Root)));
        }

        [Test]
        public void RootZone_OfAnotherAuthority_IsRefused()
        {
            var zone = HonestZone(new Vector3(2f, 0f, 2f));
            zone.SameAuthorityAsCaster = false;
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, new Vector3(3f, 0f, 2f), 0.5f, Shipped(ControlKind.Root)));
        }

        [Test]
        public void RootZone_FarFromWhereItsCasterStood_IsRefused()
        {
            var zone = HonestZone(new Vector3(-15f, 0f, -15f));   // dropped on the victim, not at the caster's feet
            zone.CasterAtSpawn = new Vector3(10f, 0f, 10f);
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, new Vector3(-15f, 0f, -14.5f), 0.5f, Shipped(ControlKind.Root)));
        }

        [Test]
        public void RootZone_WithNoSpawnAnchor_IsNotEvidence()
        {
            // Security review (WP3): the old fallback anchored on the caster's CURRENT position plus drift over the
            // zone's whole age, so an attacker's zone gained ~12 m of credible placement per second of age. A peer that
            // never resolved the caster at first sight now gets no evidence (the far root is refused once, logged).
            var zone = HonestZone(new Vector3(2f, 0f, 2f));
            zone.HasCasterAtSpawn = false;
            zone.SecondsSinceSeen = 0.2f;
            var victim = new Vector3(3f, 0f, 2f);
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, Shipped(ControlKind.Root)));
            Assert.IsFalse(ControlClaimRules.IsCredibleZoneCenter(zone, Shipped(ControlKind.Root)));
        }

        [Test]
        public void RootZone_WhoseOwnerOrAuthorityChangedAfterTheSnapshot_IsNotEvidence()
        {
            var zone = HonestZone(new Vector3(2f, 0f, 2f));
            var victim = new Vector3(3f, 0f, 2f);
            Assert.IsTrue(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, Shipped(ControlKind.Root)), "fixture: honest");
            zone.IdentityChanged = true;
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, Shipped(ControlKind.Root)),
                "a zone re-pointed at another owner or authority must stop vouching, not re-anchor");
        }

        [Test]
        public void RootZone_OlderThanItsAuthoredLife_StopsVouching()
        {
            var c = Shipped(ControlKind.Root);
            var zone = HonestZone(new Vector3(2f, 0f, 2f));
            var victim = new Vector3(3f, 0f, 2f);
            zone.SecondsSinceSeen = c.MaxZoneLifetime + ExecuteRules.MaxLatencySeconds - 0.01f;
            Assert.IsTrue(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, c), "inside its life plus the latency cap");
            zone.SecondsSinceSeen = c.MaxZoneLifetime + ExecuteRules.MaxLatencySeconds + 0.01f;
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, c), "an egg kept alive past its life is no evidence");
            zone.SecondsSinceSeen = float.NaN;
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, c));
            zone.SecondsSinceSeen = 1f;
            var noLife = new ControlClaimRules.Ceiling(true, c.MaxSeconds, c.MinSlowFactor, c.MaxReach, c.MaxZoneRadius, c.MaxZonePlacement, 0f);
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, noLife), "no authored lifetime: no zone may vouch");
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, ControlClaimRules.Ceiling.Unknown));
        }

        [Test]
        public void AbilityZone_SnapshotsOwnerAndAuthorityOnce_AndRefreshesOnlyTheGeometry()
        {
            const string zonePath = "Assets/_Game/Scripts/Gameplay/AbilityZone.cs";
            string capture = AssassinExecuteCreditTests.MethodBody(zonePath, "private void CaptureRootEvidence()");
            StringAssert.Contains("_seen.Owner       = OwnerChicken;", capture);
            StringAssert.Contains("_seen.Authority   = Object != null ? Object.StateAuthority : default;", capture);

            string observe = AssassinExecuteCreditTests.MethodBody(zonePath, "private void ObserveRootEvidence()");
            Assert.IsFalse(Regex.IsMatch(observe, @"_seen\.Owner\s*=[^=]"), "Render must never re-anchor the owner");
            // The one authority write in Render is the deferred capture of a None snapshot (qa W2), gated by the pure rule.
            Assert.AreEqual(1, Regex.Matches(observe, @"_seen\.Authority\s*=[^=]").Count);
            int gate = observe.IndexOf("ControlClaimRules.IdentityObservation.CaptureAuthority", StringComparison.Ordinal);
            int write = observe.IndexOf("_seen.Authority = authority;", StringComparison.Ordinal);
            Assert.GreaterOrEqual(gate, 0);
            Assert.Greater(write, gate, "the authority is written only on the CaptureAuthority verdict");
            StringAssert.Contains("snapshotAuthorityKnown: _seen.Authority != PlayerRef.None,", observe);
            StringAssert.Contains("_seen.IdentityChanged = true;", observe, "a change voids the evidence");
            StringAssert.Contains("RefreshRootGeometry();", observe);
            string refresh = AssassinExecuteCreditTests.MethodBody(zonePath, "private void RefreshRootGeometry()");
            Assert.IsFalse(Regex.IsMatch(refresh, @"_seen\.(Owner|Authority|CasterAtSpawn|HasCasterAtSpawn|FirstSeenAt)\s*=[^=]"),
                "only Center and Radius are refreshed");
            StringAssert.Contains("IdentityChanged = s.IdentityChanged,", Src(zonePath), "the void flag reaches the rule");
        }

        [Test]
        public void RootEvidence_ANoneAuthorityAtFirstSight_IsCapturedLater_NotChanged()
        {
            // qa W2: before, a None authority at Spawned marked the evidence changed for good on the next frame.
            Assert.AreEqual(ControlClaimRules.IdentityObservation.CaptureAuthority,
                ControlClaimRules.ObserveIdentity(sameOwner: true, snapshotAuthorityKnown: false, authorityKnown: true, sameAuthority: false));
            Assert.AreEqual(ControlClaimRules.IdentityObservation.Unchanged,
                ControlClaimRules.ObserveIdentity(true, false, false, true), "still None: wait (the evidence cannot vouch meanwhile)");
            Assert.AreEqual(ControlClaimRules.IdentityObservation.Unchanged, ControlClaimRules.ObserveIdentity(true, true, true, true));
            Assert.AreEqual(ControlClaimRules.IdentityObservation.Changed, ControlClaimRules.ObserveIdentity(true, true, true, false),
                "a captured authority that changes still voids the evidence (security review)");
            Assert.AreEqual(ControlClaimRules.IdentityObservation.Changed, ControlClaimRules.ObserveIdentity(true, true, false, false),
                "a captured authority that turns None voids it too");
            Assert.AreEqual(ControlClaimRules.IdentityObservation.Changed, ControlClaimRules.ObserveIdentity(false, false, true, false),
                "an owner change voids it, captured or not");
        }

        /// <summary>A late joiner's view of an honest egg placed <paramref name="age"/> s before it joined: the caster walked off.</summary>
        private static ControlClaimRules.RootZoneEvidence LateJoinerZone(float age, Vector3 eggAt, Vector3 casterAtJoin, ControlClaimRules.Ceiling c)
        {
            var zone = HonestZone(eggAt);
            zone.CasterAtSpawn = casterAtJoin;
            zone.CasterDriftSpeed = 12.15f;
            zone.SpawnDrift = 12.15f * 0.2f;
            zone.InJoinSnapshot = true;
            zone.RemainingAtFirstSight = c.MaxZoneLifetime - age;
            zone.SecondsSinceSeen = 0.3f;
            return zone;
        }

        [Test]
        public void RootZone_InTheJoinSnapshot_AllowsTheCastersDriftOverTheEggsAge()
        {
            // qa W1: the egg was placed 3 s before this peer joined; its caster walked 10 m since. Before, the anchor was
            // where the caster stood at the join with only latency drift, so this honest egg could never vouch here.
            var c = Shipped(ControlKind.Root);
            var egg = new Vector3(2f, 0f, 2f);
            var zone = LateJoinerZone(3f, egg, egg + new Vector3(10f, 0f, 0f), c);
            var victim = egg + new Vector3(0.5f, 0f, 0f);
            Assert.AreEqual(3f, ControlClaimRules.AgeAtFirstSight(zone, c), 1e-4f);
            Assert.IsTrue(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, c), "honest pre-join egg, in-range claim: accepted");

            zone.InJoinSnapshot = false;   // spawned after this peer joined, or a peer already in the match: no extension
            Assert.AreEqual(0f, ControlClaimRules.AgeAtFirstSight(zone, c));
            Assert.AreEqual(0f, ControlClaimRules.LateJoinDrift(zone, c));
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(zone, victim, 0.5f, c));
        }

        [Test]
        public void RootZone_LateJoinDrift_IsCapped_WhateverAgeTheTimerClaims()
        {
            // Security review: the timer is the zone owner's; 7.5 s at 12.15 m/s would have bought 91 m.
            var c = Shipped(ControlKind.Root);
            var egg = new Vector3(2f, 0f, 2f);
            var victim = egg + new Vector3(0.5f, 0f, 0f);
            var inflated = LateJoinerZone(7.5f, egg, egg + new Vector3(30f, 0f, 0f), c);
            Assert.AreEqual(ControlClaimRules.LateJoinDriftCapMeters, ControlClaimRules.LateJoinDrift(inflated, c), 1e-4f);
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(inflated, victim, 0.5f, c), "30 m away: beyond the cap");
            inflated.CasterAtSpawn = egg + new Vector3(ControlClaimRules.LateJoinDriftCapMeters + 0.5f, 0f, 0f);
            Assert.IsTrue(ControlClaimRules.RootZoneVouches(inflated, victim, 0.5f, c), "the most an inflated claim buys is the cap (+ slack, spawn drift)");
            Assert.LessOrEqual(ControlClaimRules.LateJoinDriftCapMeters, 12f, "a quarter of the arena at most; see its remarks");
        }

        [Test]
        public void JoinSnapshot_IsTickBased_OnlyZonesSeenAtOrBeforeTheJoinTick()
        {
            Assert.IsTrue(LocalJoinClock.IsInJoinSnapshot(firstSeenTick: 4000, joinTick: 4000), "delivered with the initial snapshot");
            Assert.IsTrue(LocalJoinClock.IsInJoinSnapshot(3990, 4000));
            Assert.IsFalse(LocalJoinClock.IsInJoinSnapshot(4001, 4000), "spawned after the join: no extension");
            Assert.IsFalse(LocalJoinClock.InJoinSnapshot(null, 0), "no runner: no extension");
        }

        [Test]
        public void RootZone_SeenAtJoin_TheClaimedAgeIsPaidFromTheVouchingWindow_AndUnknownFailsClosed()
        {
            var c = Shipped(ControlKind.Root);
            var egg = new Vector3(2f, 0f, 2f);
            var victim = egg + new Vector3(0.5f, 0f, 0f);

            // An attacker claiming a near-dead egg to buy drift gets almost no window to use it.
            var old = LateJoinerZone(c.MaxZoneLifetime - 0.1f, egg, egg + new Vector3(60f, 0f, 0f), c);
            old.SecondsSinceSeen = 0.7f;
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(old, victim, 0.5f, c), "claimed age + local age past the lifetime");

            // Drift is still bounded by the claimed age: 20 m in 1 s is not credible at 12 m/s.
            var young = LateJoinerZone(1f, egg, egg + new Vector3(20f, 0f, 0f), c);
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(young, victim, 0.5f, c));

            // A timer that claims MORE than the authored lifetime buys nothing.
            var inflated = LateJoinerZone(-5f, egg, egg + new Vector3(20f, 0f, 0f), c);
            Assert.AreEqual(0f, ControlClaimRules.AgeAtFirstSight(inflated, c));
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(inflated, victim, 0.5f, c));

            var unknown = LateJoinerZone(3f, egg, egg + new Vector3(20f, 0f, 0f), c);
            unknown.RemainingAtFirstSight = float.NaN;
            Assert.AreEqual(c.MaxZoneLifetime, ControlClaimRules.AgeAtFirstSight(unknown, c), "unknown remaining claims the whole life");
            Assert.IsFalse(ControlClaimRules.IsCredibleZoneCenter(unknown, c), "an unreadable timer prices no drift: not evidence");
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(unknown, victim, 0.5f, c), "fail closed");
            unknown.CasterAtSpawn = egg;   // even with the caster right on the egg
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(unknown, victim, 0.5f, c));

            var unresolved = LateJoinerZone(3f, egg, egg + new Vector3(20f, 0f, 0f), c);
            unresolved.HasCasterAtSpawn = false;
            Assert.IsFalse(ControlClaimRules.RootZoneVouches(unresolved, victim, 0.5f, c), "no anchor is still no evidence");
        }

        [Test]
        public void AbilityZone_RecordsTheJoinFactsAtFirstSight()
        {
            const string zonePath = "Assets/_Game/Scripts/Gameplay/AbilityZone.cs";
            string capture = AssassinExecuteCreditTests.MethodBody(zonePath, "private void CaptureRootEvidence()");
            StringAssert.Contains("_seen.InJoinSnapshot = LocalJoinClock.InJoinSnapshot(Runner, _spawnTick);", capture,
                "the late-joiner extension keys on this peer's own join tick, never on replicated state");
            string spawned = AssassinExecuteCreditTests.MethodBody(zonePath, "public override void Spawned()");
            Assert.Less(spawned.IndexOf("_spawnTick = Runner.Tick.Raw;", StringComparison.Ordinal),
                        spawned.IndexOf("CaptureRootEvidence();", StringComparison.Ordinal), "the capture reads this life's Spawned tick");
            StringAssert.Contains("LifetimeTimer.RemainingTime(Runner) ?? -1f", capture);
            StringAssert.Contains("_seen.CasterDriftSpeed = ChickenCargo.DriftSpeed(caster);", capture);
            StringAssert.Contains("LocalJoinClock.Note(Runner);", AssassinExecuteCreditTests.MethodBody(zonePath, "public override void Spawned()"));
            StringAssert.Contains("LocalJoinClock.Note(Runner);",
                AssassinExecuteCreditTests.MethodBody("Assets/_Game/Scripts/Gameplay/ChickenController.cs", "public override void Spawned()"));
        }

        [Test]
        public void AbilityZone_RecordsDespawnedRootsFromItsSnapshot_NotFromHasState()
        {
            const string zonePath = "Assets/_Game/Scripts/Gameplay/AbilityZone.cs";
            string despawned = AssassinExecuteCreditTests.MethodBody(zonePath, "public override void Despawned(");
            StringAssert.Contains("if (_seenAsRoot) RememberDespawnedRoot(_seen);", despawned,
                "W1: a remote copy's Despawned may get hasState == false; the record must come from the local snapshot");
            StringAssert.DoesNotContain("hasState && Effect == ZoneEffect.Root) RememberDespawnedRoot", despawned);
            StringAssert.Contains("CaptureRootEvidence();", AssassinExecuteCreditTests.MethodBody(zonePath, "public override void Spawned("));
            StringAssert.Contains("ObserveRootEvidence();", AssassinExecuteCreditTests.MethodBody(zonePath, "public override void Render("));

            string contains = AssassinExecuteCreditTests.MethodBody(zonePath, "public static bool ContainsRootOf(");
            StringAssert.Contains("ControlClaimRules.RootZoneVouches(", contains, "live and recorded zones are judged by the clamped rule");
            StringAssert.DoesNotContain("IsInsideZone(", contains, "the zone's own radius must never be used unclamped");
            StringAssert.Contains("caster.Object.StateAuthority", contains, "the zone must share the caster's state authority");
        }

        // ---- 3. Wiring ------------------------------------------------------------------------

        [Test]
        public void ControlRpcs_NameTheCaster_TakeRpcInfo_AndJudgeBeforeApplying()
        {
            foreach (var (rpc, kind) in Rpcs)
            {
                var method = typeof(ChickenController).GetMethod(rpc);
                Assert.IsNotNull(method, rpc);
                var ps = method.GetParameters();
                Assert.IsTrue(ps.Any(p => p.ParameterType == typeof(Fusion.NetworkBehaviourId)), $"{rpc} must name the casting chicken (F4)");
                Assert.IsTrue(ps.Any(p => p.ParameterType == typeof(Fusion.RpcInfo)), $"{rpc} is RpcSources.All; without RpcInfo it cannot tell who sent it");
                Assert.IsTrue(method.GetCustomAttributes(typeof(Fusion.RpcAttribute), false).Any(), $"{rpc} lost its [Rpc]");

                string body = AssassinExecuteCreditTests.MethodBody(ControllerPath, $"public void {rpc}(");
                int judge   = body.IndexOf($"AcceptControlClaim(nameof({rpc}), ControlKind.{kind},", StringComparison.Ordinal);
                int reject  = body.IndexOf("return;", StringComparison.Ordinal);
                int apply   = body.IndexOf("ApplyPassiveControlDuration(", StringComparison.Ordinal);
                Assert.Greater(judge, 0, $"{rpc} no longer judges its claim (or judges it as the wrong kind)");
                Assert.Greater(reject, judge, $"{rpc}: a rejected claim must return before anything is applied");
                Assert.Greater(apply, reject);
            }

            string judgeBody = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private ControlClaimRules.Verdict JudgeControl(");
            StringAssert.Contains("caster.Object.StateAuthority == info.Source", judgeBody, "off the wire: the caster's state authority");
            StringAssert.Contains("caster.Object.HasStateAuthority", judgeBody, "local invoke (solo, bots, master): simulated here");
            StringAssert.Contains("AbilityZone.ContainsRootOf(caster, v,", judgeBody, "a root from a far caster is judged against its own zones");
            StringAssert.Contains("ControlClaimRules.Evaluate(claim, ceiling)", judgeBody);

            string accept = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private bool AcceptControlClaim(");
            StringAssert.Contains("ControlRejectionLimiter.Allow(", accept, "rejections are rate-limited");
            StringAssert.Contains("_log?.Warn(Source, $\"{name}: rejected {rpc}(", accept,
                "merge.ps1 counts the stable prefix 'rejected RPC_Apply'; keep it");

            string ceilings = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void ResolveControlCeilings(");
            StringAssert.Contains("_abilityRegistry.All", ceilings, "the bounds come from the shipped registry");
        }

        [Test]
        public void EveryCallSite_PassesTheCastingChicken()
        {
            var rx = new Regex(@"\.(RPC_ApplyStun|RPC_ApplyRoot|RPC_ApplyAbilitySlow)\((.*)\);");
            int found = 0;
            foreach (var (path, line) in ProductionCodeLines())
            {
                var m = rx.Match(line);
                if (!m.Success) continue;
                found++;
                string lastArg = m.Groups[2].Value.Split(',').Last().Trim();
                Assert.IsTrue(lastArg == "caster.Id" || lastArg == "OwnerChicken",
                    $"{Path.GetFileName(path)}: {m.Groups[1].Value} is sent with '{lastArg}' as its caster. It must be the " +
                    "casting chicken's ChickenController id (an ability's caster.Id, a zone's OwnerChicken), or the victim refuses it.");
            }
            Assert.GreaterOrEqual(found, 6, "the call-site scan found fewer control RPC calls than ship today - the regex went blind");
        }

        [Test]
        public void TheOnlyNonAbilityCaller_IsTheRootZone()
        {
            var callers = ProductionCodeLines()
                .Where(t => Rpcs.Any(r => t.Line.Contains("." + r.Rpc + "(")))
                .Select(t => t.Path.Replace('\\', '/'))
                .Where(p => !p.Contains(AbilityDir + "/"))
                .Select(Path.GetFileName)
                .Distinct()
                .ToArray();
            CollectionAssert.AreEquivalent(new[] { "AbilityZone.cs" }, callers,
                "A new non-ability caller of the control RPCs has no ability declaring its effect, so nothing puts it inside " +
                "the victim's bound: " + string.Join(", ", callers));
        }

        [Test]
        public void EveryAbilityThatCallsAControlRpc_DeclaresThatEffect()
        {
            // EmittedControl is the ONLY thing that puts an ability inside the victim's bound.
            var assets = AllAbilityAssets();
            var asm = typeof(AbilityBaseSO).Assembly;
            int checkedTypes = 0;
            foreach (var path in Directory.GetFiles(AbilityDir, "*.cs", SearchOption.AllDirectories))
            {
                var code = CodeLines(path);
                foreach (var (rpc, kind) in Rpcs)
                {
                    if (!code.Any(l => l.Contains("." + rpc + "("))) continue;
                    var type = asm.GetType($"{typeof(AbilityBaseSO).Namespace}.{Path.GetFileNameWithoutExtension(path)}");
                    Assert.IsNotNull(type, $"{path} calls {rpc} but its file and class names have drifted apart");
                    var instances = assets.Where(a => type.IsAssignableFrom(a.GetType())).ToList();
                    Assert.IsNotEmpty(instances, $"{type.Name} calls {rpc} but no asset of it ships");
                    foreach (var a in instances)
                    {
                        Assert.AreEqual(kind, a.EmittedControl.Kind,
                            $"{a.name} calls {rpc} but declares EmittedControl={a.EmittedControl.Kind}; its casts would be refused.");
                        Assert.Greater(a.EmittedControl.Seconds, 0f, a.name);
                    }
                    checkedTypes++;
                }
            }
            Assert.GreaterOrEqual(checkedTypes, 5, "the ability scan went blind");

            // Root Egg sends nothing itself (its zone does) but must declare the root it causes.
            var egg = assets.OfType<RootEggAbilitySO>().Single();
            Assert.AreEqual(ControlKind.Root, egg.EmittedControl.Kind);
            Assert.AreEqual(egg.RootDuration, egg.EmittedControl.Seconds, 1e-6f);
        }

        // ---- Helpers ----------------------------------------------------------------------------

        private static IEnumerable<(string Path, string Line)> ProductionCodeLines()
        {
            foreach (var path in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Replace('\\', '/').Contains("/Editor/")) continue;
                foreach (var line in CodeLines(path)) yield return (path, line);
            }
        }

        private static string[] CodeLines(string path) =>
            File.ReadAllLines(path)
                .Select(l =>
                {
                    int slash = l.IndexOf("//", StringComparison.Ordinal);
                    return slash >= 0 ? l.Substring(0, slash) : l;
                })
                .ToArray();
    }
}
