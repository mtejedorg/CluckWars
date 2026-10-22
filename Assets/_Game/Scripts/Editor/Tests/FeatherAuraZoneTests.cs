using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.Diagnostics;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// WP4 (decisions B3, B4, B5, B6, F2; Fable plan 2026-10-22): Feather Aura is a Caster-anchored <see cref="AbilityZone"/>
    /// spawned through <see cref="AreaEffectSpawner"/>, and the <c>ChickenController</c> emitter triplet, the effect-stack
    /// aura channel and the three visual reads of the emitter flag are gone (the self-snail-badge bug, proposal §3.1).
    /// </summary>
    /// <remarks>
    /// <b>Which ones fail on the pre-WP4 build:</b> the removed-member scan, the visual reads, the spawn / channels / no
    /// cast-time hits / OnEnter-confirmation / bot / no-overlap tests, and everything on the probe and <c>cw_autoaura</c>
    /// (new code: they do not compile there). <b>WP3 rule tests that already passed before WP4</b>, kept here because
    /// the aura now depends on them: <see cref="AuraZone_FollowsTheOwner_PureRule"/>,
    /// <see cref="AuraZone_Despawns_WhenTheSlotDeactivates_OrTheOwnerIsGone"/> and
    /// <see cref="FeatherAura_KeepsItsGameplayNumbers"/> (a no-drift pin, true before and after).
    /// </remarks>
    public sealed class FeatherAuraZoneTests
    {
        private const string ScriptsDir     = "Assets/_Game/Scripts";
        private const string AuraPath       = ScriptsDir + "/Abilities/FeatherAuraAbilitySO.cs";
        private const string ZonePath       = ScriptsDir + "/Gameplay/AbilityZone.cs";
        private const string ControllerPath = ScriptsDir + "/Gameplay/ChickenController.cs";
        private const string AbilityCtlPath = ScriptsDir + "/Gameplay/AbilityController.cs";
        private const string BotPath        = ScriptsDir + "/Gameplay/BotController.cs";
        private const string ProbePath      = ScriptsDir + "/Diagnostics/InvariantProbe.cs";

        private static string Src(string path) => File.ReadAllText(path).Replace("\r\n", "\n");
        private static string Body(string path, string signature) => AssassinExecuteCreditTests.MethodBody(path, signature);

        private static FeatherAuraAbilitySO Shipped() =>
            TestAssets.Load<FeatherAuraAbilitySO>(TestAssets.AbilitiesDir + "/FeatherAura.asset");

        // ---- The triplet is gone, and so is every read of it ---------------------------------------------------------

        [Test]
        public void NothingReads_AuraSlowActive()
        {
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            foreach (var name in new[] { "AuraSlowActive", "AuraSlowRadius", "AuraSlowFactor" })
                Assert.IsNull(typeof(ChickenController).GetProperty(name, any),
                    $"ChickenController.{name} is a removed [Networked] field (WP4 protocol change): Feather Aura is a zone");
            Assert.IsNull(typeof(ChickenController).GetMethod("CheckAuraSlow", any), "the aura slows through CheckAbilityZoneSlow");
            Assert.IsNull(typeof(ChickenController).GetField("AuraSlowSearchRadius", any));
            foreach (var name in new[] { "SetAura", "RemoveAura", "TryGetAura" })
                Assert.IsNull(typeof(AbilityEffectStack).GetMethod(name, any), $"AbilityEffectStack.{name}: the aura is not a stacked effect");
            Assert.IsNull(typeof(AbilityEffectStack).GetProperty("AuraActive", any));

            // Production code only (tests may name the removed members to pin their absence). The emitter flag was read
            // by ChickenNameplate.StateIcon and ChickenStateOverlays.IsSlowed/IsSnared as "this chicken is slowed",
            // which badged the CASTER with the snail (proposal §3.1).
            var banned = new Regex(@"\b(AuraSlowActive|AuraSlowRadius|AuraSlowSearchRadius|CheckAuraSlow|SetAura|RemoveAura|TryGetAura)\b");
            int scanned = 0;
            foreach (var file in Directory.GetFiles(ScriptsDir, "*.cs", SearchOption.AllDirectories))
            {
                string norm = file.Replace('\\', '/');
                if (norm.Contains("/Editor/Tests/")) continue;
                scanned++;
                var lines = Src(file).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var m = banned.Match(lines[i]);
                    Assert.IsFalse(m.Success, $"{norm}:{i + 1} still names '{m.Value}', a removed aura member (WP4)");
                }
            }
            Assert.Greater(scanned, 100, "the scan went blind");
        }

        [Test]
        public void TheVisualSlowReads_UseOnlyReplicatedVictimState()
        {
            string overlays = ScriptsDir + "/Visuals/ChickenStateOverlays.cs";
            string snared = Body(overlays, "private bool IsSnared(ControlVfx flags)");
            StringAssert.Contains("return (flags & ControlVfx.Snared) != 0;", snared, "the replicated bit, set by the VICTIM's authority");
            StringAssert.DoesNotContain("_controller.", snared, "no caster-side flag can make a chicken look snared");
            StringAssert.DoesNotContain("Aura", Body(overlays, "private bool IsSlowed(ControlVfx flags)"));
            StringAssert.DoesNotContain("Aura", Body(ScriptsDir + "/Visuals/ChickenNameplate.cs", "private string StateIcon()"));
        }

        // ---- Feather Aura spawns a Caster zone, numbers unchanged ------------------------------------------------------

        [Test]
        public void FeatherAura_KeepsItsGameplayNumbers()
        {
            var a = Shipped();
            Assert.AreEqual(0.55f, a.AuraSlowFactor, 1e-6f, "slow factor (WP4 moves no number)");
            Assert.AreEqual(5.4f, a.AuraRadius, 1e-6f, "radius");
            Assert.AreEqual(3f, a.Duration, 1e-6f, "duration");
            Assert.AreEqual(AreaEffectKind.Aura, a.AreaKind);
        }

        [Test]
        public void FeatherAura_SpawnsACasterZone_ThatEndsWithItsSlot()
        {
            var a = Shipped();
            Assert.IsTrue(a.TryGetAreaEffect(out var spec));
            Assert.AreEqual(AreaEffectKind.Aura, spec.Kind);
            Assert.AreEqual(ZoneAnchor.Caster, spec.Anchor, "centred on the caster as each peer sees them (no NetworkTransform, F2)");
            Assert.AreEqual(ZoneEffect.Slow, spec.Effect);
            Assert.AreEqual(AreaEffectChannel.WhileInside | AreaEffectChannel.OnEnter, spec.Channels);
            Assert.AreEqual(a.AuraRadius, spec.Radius);
            Assert.AreEqual(a.AuraSlowFactor, spec.SlowFactor);
            Assert.AreEqual(a.Duration, spec.Lifetime, "Duration is the backstop lifetime");
            Assert.IsTrue(spec.EndsWithAbility, "the zone ends with the casting slot");
            Assert.IsFalse(spec.ConsumeOnTrigger);
            Assert.IsNull(AreaEffectSpawner.Refusal(spec, 1), "the spawner accepts the shipped aura");

            string activate = Body(AuraPath, "public override void OnActivate(AbilityContext ctx)");
            StringAssert.Contains("AreaEffectSpawner.Spawn(ctx, spec,", activate);
            StringAssert.DoesNotContain("Effects.", activate, "no effect-stack channel any more");
            string deactivate = Body(AuraPath, "public override void OnDeactivate(AbilityContext ctx)");
            StringAssert.DoesNotContain("Effects.", deactivate);
            StringAssert.DoesNotContain("Despawn", deactivate, "the zone despawns itself (slot-inactive rule)");
            string gate = Body(AuraPath, "public override bool CanActivate(AbilityContext ctx)");
            StringAssert.Contains("if (!ctx.CanSpawnZone(Source, \"Feather Aura\")) return false;", gate,
                "refuses before the cast and its cooldown when PrefabRegistrySO.AbilityZone is unassigned");
            StringAssert.Contains("AreaEffectSpawner.Refusal(spec, ctx.Slot)", gate,
                "the spawner's one failure (a refused spec) is checked BEFORE the cooldown is charged, so OnActivate's spawn cannot fail");

            // The stamp the despawn rule reads: SourceSlot is the casting slot for an EndsWithAbility spec.
            StringAssert.Contains("byte  capSlot  = spec.EndsWithAbility ? (byte)ctx.Slot : AreaEffectRules.NoSourceSlot;",
                Src(ScriptsDir + "/Abilities/AreaEffectSpawner.cs"));
        }

        [Test]
        public void AnAura_ReportsNoCastTimeHits_TheZoneConfirmsInstead()
        {
            var a = Shipped();
            Assert.IsTrue(a.PlacesZone, "PlacesZone covers Aura since WP4");
            Assert.IsFalse(a.ReportsCastHits, "Maestro: the OnEnter confirmation in AbilityZone.Render replaces the cast-time sparks");
            Assert.IsFalse(a.ZeroHitsIsAWhiff, "an aura cast on empty ground is not a whiff");
            foreach (var ability in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir))
                if (ability.AreaKind != AreaEffectKind.None)
                    Assert.IsFalse(ability.ReportsCastHits, $"{ability.name}: no area reports cast-time hits");
        }

        // ---- The zone follows its owner, and ends with it ------------------------------------------------------------

        [Test]
        public void AuraZone_FollowsTheOwner_PureRule()
        {
            var castSpot = new Vector3(0f, 0f, 0f);
            var ownerNow = new Vector3(8f, 0f, 0f);
            var victim = new Vector3(10f, 0f, 0f);      // 10 m from where it was cast, 2 m from the caster now
            const float r = 5.4f;

            Assert.IsTrue(AreaEffectRules.TryResolveCenter(ZoneAnchor.Caster, true, castSpot, castSpot, true, ownerNow, out var c));
            Assert.AreEqual(ownerNow, c, "the centre is the owner's live position, not the cast spot");
            Assert.IsTrue(AreaEffectRules.IsInside(c, r, victim), "a rival the caster ran up to is slowed");
            Assert.IsFalse(AreaEffectRules.IsInside(castSpot, r, victim), "fixture: the cast spot alone would have missed it");
            Assert.IsFalse(AreaEffectRules.TryResolveCenter(ZoneAnchor.Caster, true, castSpot, castSpot, false, ownerNow, out _),
                "owner gone on this peer: no centre, the aura affects nobody here");

            string render = Body(ZonePath, "public override void Render()");
            StringAssert.Contains("if (Anchor == ZoneAnchor.Caster) { if (TryGetCenter(out Vector3 follow)) transform.position = follow; }", render,
                "visuals follow in Render (F2); simulation reads TryGetCenter");
        }

        [Test]
        public void AuraZone_Despawns_WhenTheSlotDeactivates_OrTheOwnerIsGone()
        {
            const byte slot = 1;
            Assert.AreEqual(ZoneEnd.AbilityEnded, AreaEffectRules.Despawn(false, false, ZoneAnchor.Caster, true, slot, false),
                "the aura ends the tick after its slot stops being active");
            Assert.AreEqual(ZoneEnd.None, AreaEffectRules.Despawn(false, false, ZoneAnchor.Caster, true, slot, true));
            Assert.AreEqual(ZoneEnd.OwnerGone, AreaEffectRules.Despawn(false, false, ZoneAnchor.Caster, false, slot, true),
                "B5: owner removed / left -> despawn next tick");
            Assert.AreEqual(ZoneEnd.Lifetime, AreaEffectRules.Despawn(true, false, ZoneAnchor.Caster, true, slot, true), "Duration backstop");

            StringAssert.Contains("return owner.Combat == null || !owner.Combat.IsRemoved;",
                Body(ZonePath, "private bool TryGetAvailableOwner(out ChickenController owner)"), "a removed owner is not available");
            StringAssert.Contains("owner.Abilities.IsSlotActive(SourceSlot)", Body(ZonePath, "public override void FixedUpdateNetwork()"));

            // The slot is active BEFORE OnActivate spawns the zone, so the rule cannot end the aura on its first tick.
            string activate = Body(AbilityCtlPath, "private bool TryActivate(int slot)");
            int begin = activate.IndexOf("ActiveMask = AbilityRunRules.Begin(ActiveMask, slot);", StringComparison.Ordinal);
            int cast = activate.IndexOf("ability.OnActivate(_ctx);", StringComparison.Ordinal);
            Assert.GreaterOrEqual(begin, 0);
            Assert.Greater(cast, begin);
        }

        [Test]
        public void OnEnter_ConfirmsEachRivalOnce_IncludingTheOnesAlreadyInsideAtTheCast()
        {
            // No runner in EditMode: pin the shape. Per-chicken set, re-armed per spawn (pooled), and no previous-frame
            // occupancy edge, so the rivals inside on the zone's first frame confirm (Maestro's rule) and a rival walking
            // in later confirms too.
            StringAssert.Contains("_confirmed.Clear();", Body(ZonePath, "public override void Spawned()"));
            string observe = Body(ZonePath, "private void ObserveSlowZoneEntry()");
            int route = observe.IndexOf("if (HasChannel(AreaEffectChannel.OnEnter)) { ConfirmEachFirstEntry(); return; }", StringComparison.Ordinal);
            int edge = observe.IndexOf("_slowZoneOccupied", StringComparison.Ordinal);
            Assert.GreaterOrEqual(route, 0, "an OnEnter slow zone confirms per chicken");
            Assert.Greater(edge, route, "the occupancy edge stays for the placed slow zones (Feather Trap, Smoke Roost)");

            string confirm = Body(ZonePath, "private void ConfirmEachFirstEntry()");
            StringAssert.Contains("if (!IsCaught(chicken, center, radius) || !_confirmed.Add(chicken.Id)) continue;", confirm);
            StringAssert.Contains("NotifyAttacker(center)", confirm, "B6: the bearing points at the centre = the caster");
            StringAssert.Contains("NotifyOwnerZoneTriggered(Runner)", confirm);
            StringAssert.DoesNotContain("_slowZoneOccupied", confirm);
        }

        // ---- Bots ---------------------------------------------------------------------------------------------------

        [Test]
        public void Bots_NeverCastAnAuraAtNobody_AndCannotStackOne()
        {
            StringAssert.Contains("if (!haveTarget && ability.AreaKind != AreaEffectKind.StaticAoE) continue;",
                Body(BotPath, "private bool TryCast(BotSituation situation, ChickenController target)"),
                "only a placed zone is worth dropping with nobody near (the pre-WP4 Feather Aura rule)");

            // "Aura already up": bots go through the same TryActivate -> CanActivate gate as players (no overlapping
            // auras, see the next test), and also refuse while any of their slots runs.
            StringAssert.Contains("if (AnyAbilityActive) return false;", Body(AbilityCtlPath, "public bool BotTryActivate(int slot)"));
            StringAssert.Contains("TryActivate(slot);", Body(AbilityCtlPath, "public bool BotTryActivate(int slot)"));
        }

        [Test]
        public void ASecondAura_IsRefusedWhileTheFirstLives_EvenAtAQuarterCooldown()
        {
            // The shipped numbers alone (7 s cooldown > 3 s aura) are not the guarantee: a cooldown passive can go to x0.25.
            var a = Shipped();
            var relentless = ScriptableObject.CreateInstance<RelentlessPassiveSO>();
            try
            {
                relentless.CooldownMultiplier = 0.25f;
                float cd = relentless.ModifyCooldown(a.Cooldown, a, null);
                Assert.AreEqual(1.75f, cd, 1e-4f);
                Assert.Less(cd, a.Duration, "fixture: at x0.25 the cooldown is up while the aura still lives");
            }
            finally { UnityEngine.Object.DestroyImmediate(relentless); }

            // So the refusal is explicit: a live aura of this owner that ends with this slot blocks the re-cast.
            Assert.IsTrue(AreaEffectRules.BlocksRecast(ZoneAnchor.Caster, sameOwner: true, zoneSlot: 1, castSlot: 1));
            Assert.IsFalse(AreaEffectRules.BlocksRecast(ZoneAnchor.Caster, sameOwner: true, zoneSlot: 1, castSlot: 2), "another slot");
            Assert.IsFalse(AreaEffectRules.BlocksRecast(ZoneAnchor.Caster, sameOwner: false, zoneSlot: 1, castSlot: 1), "another caster's aura");
            Assert.IsFalse(AreaEffectRules.BlocksRecast(ZoneAnchor.World, sameOwner: true, zoneSlot: 1, castSlot: 1), "a placed zone never blocks");
            Assert.IsFalse(AreaEffectRules.BlocksRecast(ZoneAnchor.Caster, true, AreaEffectRules.NoSourceSlot, AreaEffectRules.NoSourceSlot),
                "a zone that does not end with a slot blocks nothing");

            StringAssert.Contains("return ctx.Controller == null || !AbilityZone.HasLiveAuraFrom(ctx.Controller.Id, ctx.Slot);",
                Body(AuraPath, "public override bool CanActivate(AbilityContext ctx)"));
            StringAssert.Contains("AreaEffectRules.BlocksRecast(z.Anchor, z.OwnerChicken == owner, z.SourceSlot, slot)",
                Body(ZonePath, "public static bool HasLiveAuraFrom(NetworkBehaviourId owner, int slot)"));

            // CanActivate knows the slot: TryActivate sets it before the gate and clears it after.
            string activate = Body(AbilityCtlPath, "private bool TryActivate(int slot)");
            int set = activate.IndexOf("_ctx.Slot = slot;", StringComparison.Ordinal);
            int gate = activate.IndexOf("canActivate = ability.CanActivate(_ctx);", StringComparison.Ordinal);
            Assert.GreaterOrEqual(set, 0);
            Assert.Greater(gate, set, "the slot is set before CanActivate");
            StringAssert.Contains("finally { _ctx.Slot = -1; }", activate.Substring(gate));
        }

        // ---- The remote-victim probe (state, not logs) ---------------------------------------------------------------

        private static RoundInvariants.AuraVictimTally Run(RoundInvariants.AuraVictimTally t, float from, float to, bool deep, bool snared)
        {
            for (float now = from; now < to; now += 1f / 60f) t = RoundInvariants.SampleAuraVictim(t, deep, snared, now);
            return t;
        }

        [Test]
        public void AuraVictim_CountsOnlySettledDeepFrames_AndJudgesTheSnaredShare()
        {
            Assert.IsTrue(RoundInvariants.IsDeepInsideAura(3.2f, 5.4f));
            Assert.IsFalse(RoundInvariants.IsDeepInsideAura(3.3f, 5.4f), "the rim is no evidence (peers see ~3 m apart)");

            var t = Run(default, 0f, RoundInvariants.AuraSettleSeconds - 0.02f, deep: true, snared: false);
            Assert.AreEqual(0, t.Deep, "the settle window covers the zone's replication and the victim's flag coming back");

            var ok = Run(default, 0f, 2f, deep: true, snared: true);
            Assert.Greater(ok.Deep, RoundInvariants.AuraMinSamples);
            Assert.AreEqual(RoundInvariants.AuraVerdict.Ok, RoundInvariants.JudgeAuraVictim(ok));

            var bad = Run(default, 0f, 2f, deep: true, snared: false);
            Assert.AreEqual(RoundInvariants.AuraVerdict.NotSnared, RoundInvariants.JudgeAuraVictim(bad), "CW-217 class: inside, never slowed");

            var brush = Run(default, 0f, 0.45f, deep: true, snared: false);
            Assert.AreEqual(RoundInvariants.AuraVerdict.TooFew, RoundInvariants.JudgeAuraVictim(brush), "a brush is no verdict");

            // Leaving the core restarts the settle window.
            var back = Run(Run(Run(default, 0f, 1f, true, true), 1f, 1.5f, false, false), 1.5f, 1.6f, true, false);
            Assert.AreEqual(Run(default, 0f, 1f, true, true).Deep, back.Deep, "re-entry frames inside the settle window do not count");

            StringAssert.Contains("remote=1", RoundInvariants.AuraVictimLine("P2", "B2", true, ok, 1));
            StringAssert.Contains("verdict=ok", RoundInvariants.AuraVictimLine("P2", "B2", true, ok, 1));
        }

        [Test]
        public void InvariantProbe_SamplesTheVictimsReplicatedSnared_OnEveryPeer_AndCountsAurasByZone()
        {
            string probe = Src(ProbePath);
            string auras = Body(ProbePath, "private void Auras(NetworkRunner runner)");
            StringAssert.Contains("(c.ControlFlags & ControlVfx.Snared) != 0", auras, "the victim's replicated state, not a log line");
            StringAssert.Contains("c.Object.StateAuthority != z.Object.StateAuthority", auras, "remote = the victim's authority is not the aura's");
            StringAssert.Contains("z.TryGetCenter(out Vector3 center)", auras);
            StringAssert.Contains("\"aura-orphan\"", auras, "hostleave: a bot's aura goes with its bot");
            StringAssert.Contains("\"aura-victim-snared\"", Body(ProbePath, "private void FinishAura(uint zoneId)"));
            StringAssert.Contains("Auras(gm.Runner);", probe);
            StringAssert.Contains("if (live[i].Anchor == ZoneAnchor.Caster) auras++;", Body(ProbePath, "private void CheckStart(GameManager gm)"));

            string merge = Src("tools/test-session/merge.ps1");
            StringAssert.Contains("no peer proved a remote victim slowed", merge);
            StringAssert.Contains("aura victims not slowed on", merge);
            string selftest = Src("tools/test-session/selftest.ps1");
            StringAssert.Contains("aura-victim-fixture", selftest);
            StringAssert.Contains("aura-unproven-fixture", selftest);
            StringAssert.Contains("aura-victim-clean-fixture", selftest);
        }

        // ---- cw_autoaura -------------------------------------------------------------------------------------------

        [Test]
        public void AutoAura_Key_ParsesNeedsAutoPlay_AndExcludesAutoZone()
        {
            CollectionAssert.Contains(AutoTestConfig.Keys, AutoTestConfig.AutoAuraKey);
            var cfg = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_autosolo", "-cw_autoplay", "-cw_autoaura" });
            Assert.IsTrue(cfg.AutoAura);
            CollectionAssert.IsEmpty(cfg.Problems);

            var lone = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_autosolo", "-cw_autoaura" });
            Assert.IsFalse(lone.AutoAura, "the walk and the cast are fake-player input");
            Assert.AreEqual(1, lone.Problems.Count);

            var both = AutoTestConfig.FromCommandLine(new[] { "CluckWars.exe", "-cw_autosolo", "-cw_autoplay", "-cw_autozone", "-cw_autoaura" });
            Assert.IsTrue(both.AutoZone);
            Assert.IsFalse(both.AutoAura, "one reserved slot");
            Assert.AreEqual(1, both.Problems.Count);

            StringAssert.Contains("'-cw_autoaura'", Src("tools/test-session/scenario-pc.ps1"), "scenario-pc -AutoAura passes it");
        }

        [Test]
        public void AutoAura_ChasesAfterTheMark_CastsDeepInside_AndPicksTheAuraSlot()
        {
            Assert.IsFalse(AutoAuraRules.ShouldChase(44f));
            Assert.IsTrue(AutoAuraRules.ShouldChase(AutoAuraRules.ChaseAtRemaining));
            Assert.IsTrue(AutoAuraRules.ShouldCast(2.0f, 5.4f, ready: true));
            Assert.IsFalse(AutoAuraRules.ShouldCast(2.0f, 5.4f, ready: false));
            Assert.IsFalse(AutoAuraRules.ShouldCast(3.0f, 5.4f, ready: true), "cast well inside, so the rival stays in the probe's core");
            Assert.LessOrEqual(AutoAuraRules.CastFraction, RoundInvariants.AuraDeepFraction, "the cast distance is inside the deep core");

            var peck = ScriptableObject.CreateInstance<PeckAbilitySO>();
            var trap = ScriptableObject.CreateInstance<FeatherTrapAbilitySO>();
            var aura = ScriptableObject.CreateInstance<FeatherAuraAbilitySO>();
            try
            {
                Assert.AreEqual(2, AutoAuraRules.PickAuraSlot(new AbilityBaseSO[] { peck, trap, aura, null }));
                Assert.AreEqual(-1, AutoAuraRules.PickAuraSlot(new AbilityBaseSO[] { peck, trap, null, null }));
                Assert.AreEqual(-1, AutoAuraRules.PickAuraSlot(null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(peck);
                UnityEngine.Object.DestroyImmediate(trap);
                UnityEngine.Object.DestroyImmediate(aura);
            }
        }
    }
}
