using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// WP3 (decisions 2026-10-21 B1-B3, B5-B7, B9; Fable plan 2026-10-22): ability effects have TYPES - Static AoE and
    /// Aura - authored as data on the ability asset, and one <see cref="AbilityZone"/> carries both, anchored to the
    /// world or to its caster. These tests pin the type model, the anchored centre, the channels and despawn rules, the
    /// decoy rule (B7, CW-232) and the World-hash split.
    /// </summary>
    public sealed class AreaEffectTypeTests
    {
        private const string ZonePath       = "Assets/_Game/Scripts/Gameplay/AbilityZone.cs";
        private const string ControllerPath = "Assets/_Game/Scripts/Gameplay/ChickenController.cs";
        private const string SpawnerPath    = "Assets/_Game/Scripts/Abilities/AreaEffectSpawner.cs";
        private const string AbilityDir     = "Assets/_Game/Scripts/Abilities";
        private const string ZonePrefabPath = TestAssets.PrefabsDir + "/Abilities/AbilityZone.prefab";

        private static List<AbilityBaseSO> AllAbilityAssets() =>
            TestAssets.LoadAllIn<ScriptableObject>(TestAssets.AbilitiesDir).OfType<AbilityBaseSO>().ToList();

        private static T Asset<T>() where T : AbilityBaseSO => AllAbilityAssets().OfType<T>().Single();

        private static string Src(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        // ---- 1. Ordinals (append-only, persisted in .asset files / on the wire) -------------------------------------

        [Test]
        public void AreaEffectKind_Ordinals_ArePinned()
        {
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(AreaEffectKind)));
            Assert.AreEqual(0, (int)AreaEffectKind.None);
            Assert.AreEqual(1, (int)AreaEffectKind.StaticAoE);
            Assert.AreEqual(2, (int)AreaEffectKind.Aura);
            Assert.AreEqual(3, Enum.GetValues(typeof(AreaEffectKind)).Length, "append a member, never renumber one");
        }

        [Test]
        public void AreaEffectChannel_And_ZoneAnchor_Ordinals_ArePinned()
        {
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(AreaEffectChannel)), "a [Networked] byte on AbilityZone");
            Assert.IsTrue(typeof(AreaEffectChannel).IsDefined(typeof(FlagsAttribute), false));
            Assert.AreEqual(1, (int)AreaEffectChannel.OnEnter);
            Assert.AreEqual(2, (int)AreaEffectChannel.WhileInside);
            Assert.AreEqual(4, (int)AreaEffectChannel.Periodic);

            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(ZoneAnchor)), "a [Networked] byte on AbilityZone");
            Assert.AreEqual(0, (int)ZoneAnchor.World, "the zero default (only the spawner creates zones and it always stamps Anchor)");
            Assert.AreEqual(1, (int)ZoneAnchor.Caster);
        }

        [Test]
        public void AbilityZone_WP3Fields_AreNetworked_WithWeaverSafeTypes()
        {
            // Fusion's weaver only runs inside Unity; pin the exact types so a scratch build cannot drift them.
            var expected = new Dictionary<string, Type>
            {
                { nameof(AbilityZone.Anchor), typeof(ZoneAnchor) },
                { nameof(AbilityZone.SourceSlot), typeof(byte) },
                { nameof(AbilityZone.Channels), typeof(AreaEffectChannel) },
            };
            foreach (var kv in expected)
            {
                var prop = typeof(AbilityZone).GetProperty(kv.Key);
                Assert.IsNotNull(prop, kv.Key);
                Assert.AreEqual(kv.Value, prop.PropertyType, kv.Key);
                Assert.IsNotNull(prop.GetCustomAttributes(typeof(Fusion.NetworkedAttribute), false).FirstOrDefault(), $"{kv.Key} must be [Networked]");
            }
        }

        // ---- 2. The type is data, and agrees with everything derived from it --------------------------------------

        [Test]
        public void EveryRegistryAbility_AreaKind_AgreesWithAimShape_PlacesZone_AndItsSpec()
        {
            var assets = AllAbilityAssets();
            Assert.Greater(assets.Count, 20, "the asset sweep went blind");
            int areas = 0;
            foreach (var a in assets)
            {
                bool hasSpec = a.TryGetAreaEffect(out var spec);
                Assert.AreEqual(a.AreaKind != AreaEffectKind.None, hasSpec,
                    $"{a.name}: AreaKind={a.AreaKind} but TryGetAreaEffect={hasSpec}. An authored area needs a spec, and a spec needs its type authored.");
                if (hasSpec) { areas++; Assert.AreEqual(a.AreaKind, spec.Kind, $"{a.name}: spec kind"); }

                Assert.AreEqual(a.AreaKind == AreaEffectKind.Aura, a.AimShape == AbilityAimShape.Aura,
                    $"{a.name}: an Aura draws the Aura shape (persistent circle that follows), and only an Aura does");
                if (a.AreaKind == AreaEffectKind.StaticAoE)
                    Assert.That(a.AimShape, Is.EqualTo(AbilityAimShape.ForwardCircle).Or.EqualTo(AbilityAimShape.SelfCircle),
                        $"{a.name}: a Static AoE's telegraph is its placed footprint");
                Assert.AreEqual(a.AreaKind != AreaEffectKind.None, a.PlacesZone,
                    $"{a.name}: PlacesZone is derived from AreaKind (every area spawns a zone since WP4, Aura included)");
            }
            Assert.AreEqual(4, areas, "Feather Trap, Root Egg, Smoke Roost, Feather Aura");
        }

        [TestCase("FeatherTrap", AreaEffectKind.StaticAoE)]
        [TestCase("RootEgg", AreaEffectKind.StaticAoE)]
        [TestCase("SmokeRoost", AreaEffectKind.StaticAoE)]
        [TestCase("FeatherAura", AreaEffectKind.Aura)]
        public void TheFourAreaAssets_AuthorTheirKind_AndFreshInstancesAgree(string asset, AreaEffectKind kind)
        {
            string path = $"{TestAssets.AbilitiesDir}/{asset}.asset";
            StringAssert.Contains($"\n  AreaKind: {(int)kind}\n", Src(path), $"{asset}.asset must author its AreaKind line (data, not code)");
            var loaded = TestAssets.Load<AbilityBaseSO>(path);
            Assert.AreEqual(kind, loaded.AreaKind);
            var fresh = (AbilityBaseSO)ScriptableObject.CreateInstance(loaded.GetType());
            try { Assert.AreEqual(kind, fresh.AreaKind, "the class default matches, so probes built with CreateInstance agree"); }
            finally { UnityEngine.Object.DestroyImmediate(fresh); }
        }

        [Test]
        public void AreaAbilities_TryGetAreaEffect_MatchesTheirFields()
        {
            // Phase 1 moves no number: every spec value is read off the field the asset already serializes.
            var trap = Asset<FeatherTrapAbilitySO>();
            Assert.IsTrue(trap.TryGetAreaEffect(out var t));
            Assert.AreEqual(ZoneEffect.Slow, t.Effect);
            Assert.AreEqual(AreaEffectChannel.WhileInside, t.Channels);
            Assert.AreEqual(trap.ZoneRadius, t.Radius);
            Assert.AreEqual(trap.ZoneDuration, t.Lifetime);
            Assert.AreEqual(trap.SlowFactor, t.SlowFactor);
            Assert.AreEqual(trap.ForwardOffset, t.ForwardOffset);
            Assert.IsFalse(t.ConsumeOnTrigger);
            Assert.AreEqual(ZoneAnchor.World, t.Anchor);

            var egg = Asset<RootEggAbilitySO>();
            Assert.IsTrue(egg.TryGetAreaEffect(out var e));
            Assert.AreEqual(ZoneEffect.Root, e.Effect);
            Assert.AreEqual(AreaEffectChannel.OnEnter, e.Channels);
            Assert.AreEqual(egg.EggRadius, e.Radius);
            Assert.AreEqual(egg.EggLifetime, e.Lifetime);
            Assert.AreEqual(egg.RootDuration, e.RootDuration);
            Assert.AreEqual(0f, e.ForwardOffset, "at the caster's feet");
            Assert.IsTrue(e.ConsumeOnTrigger);
            Assert.AreEqual(egg.EmittedControl.Seconds, e.RootDuration, "the F4 bound and the zone agree");

            var smoke = Asset<SmokeRoostAbilitySO>();
            Assert.IsTrue(smoke.TryGetAreaEffect(out var s));
            Assert.AreEqual(ZoneEffect.Slow, s.Effect);
            Assert.AreEqual(smoke.ZoneRadius, s.Radius);
            Assert.AreEqual(smoke.ZoneDuration, s.Lifetime);
            Assert.AreEqual(smoke.SlowFactor, s.SlowFactor);
            Assert.AreEqual(0f, s.ForwardOffset);

            var aura = Asset<FeatherAuraAbilitySO>();
            Assert.IsTrue(aura.TryGetAreaEffect(out var f));
            Assert.AreEqual(ZoneAnchor.Caster, f.Anchor);
            Assert.AreEqual(aura.AuraRadius, f.Radius);
            Assert.AreEqual(aura.AuraSlowFactor, f.SlowFactor);
            Assert.AreEqual(aura.Duration, f.Lifetime, "Duration is the aura's backstop lifetime");
            Assert.IsTrue(f.EndsWithAbility);
            Assert.AreEqual(ZoneEffect.Slow, f.Effect);
            Assert.AreEqual(AreaEffectChannel.WhileInside | AreaEffectChannel.OnEnter, f.Channels,
                "WP4: the slow is WhileInside; OnEnter confirms each rival once, the ones inside at the cast included");
        }

        [Test]
        public void SmokeRoost_IsAStaticAoE()
        {
            // Maestro, 2026-10-21: Smoke Roost stays a Static AoE. Making it follow its caster is a deliberate change.
            var smoke = Asset<SmokeRoostAbilitySO>();
            Assert.AreEqual(AreaEffectKind.StaticAoE, smoke.AreaKind);
            Assert.IsTrue(smoke.TryGetAreaEffect(out var spec));
            Assert.AreEqual(ZoneAnchor.World, spec.Anchor);
            Assert.IsFalse(spec.EndsWithAbility, "the cloud outlives the fade");
        }

        [Test]
        public void PeriodicInterval_IsFlooredAtAQuarterSecond_AndOnlyExistsOnThePeriodicChannel()
        {
            var p = new AreaEffectSpec(AreaEffectKind.Aura, AreaEffectChannel.Periodic, ZoneEffect.Slow, 1.2f, 3f,
                periodicInterval: 1f / 32f, periodicAmountPerSecond: 2f);
            Assert.AreEqual(AreaEffectSpec.MinPeriodicInterval, p.PeriodicInterval, "never ticks with the 32 Hz simulation");
            Assert.AreEqual(0.25f, AreaEffectSpec.MinPeriodicInterval);
            Assert.AreEqual(2f, p.PeriodicAmountPerSecond);
            var q = new AreaEffectSpec(AreaEffectKind.StaticAoE, AreaEffectChannel.WhileInside, ZoneEffect.Slow, 1f, 1f,
                periodicInterval: 1f, periodicAmountPerSecond: 9f);
            Assert.AreEqual(0f, q.PeriodicInterval);
            Assert.AreEqual(0f, q.PeriodicAmountPerSecond);
        }

        // ---- 3. One spawner, byte-identical stamp ------------------------------------------------------------------

        [Test]
        public void Spawner_AcceptsEveryShippedSpec_AndRefusesTheOnesAZoneCannotHonour()
        {
            foreach (var a in AllAbilityAssets())
            {
                if (!a.TryGetAreaEffect(out var spec)) continue;
                Assert.IsNull(AreaEffectSpawner.Refusal(spec, slot: 2), $"{a.name}: its shipped spec is refused");
            }

            Assert.IsNotNull(AreaEffectSpawner.Refusal(default, 0), "Kind None");
            Assert.IsNotNull(AreaEffectSpawner.Refusal(new AreaEffectSpec(AreaEffectKind.Aura,
                AreaEffectChannel.Periodic, ZoneEffect.Slow, 1.2f, 3f, periodicInterval: 0.5f), 1), "Periodic has no RPC yet (WP5)");
            Assert.IsNotNull(AreaEffectSpawner.Refusal(new AreaEffectSpec(AreaEffectKind.StaticAoE,
                AreaEffectChannel.OnEnter, ZoneEffect.Slow, 1f, 1f), 0), "a slow on OnEnter alone would affect nobody");
            Assert.IsNull(AreaEffectSpawner.Refusal(new AreaEffectSpec(AreaEffectKind.StaticAoE,
                AreaEffectChannel.WhileInside | AreaEffectChannel.OnEnter, ZoneEffect.Slow, 1f, 1f), 0),
                "a slow may add OnEnter (per-chicken entry confirmation, WP4)");
            Assert.IsNotNull(AreaEffectSpawner.Refusal(new AreaEffectSpec(AreaEffectKind.StaticAoE,
                AreaEffectChannel.WhileInside | AreaEffectChannel.OnEnter, ZoneEffect.Root, 1f, 1f, rootDuration: 1f,
                consumeOnTrigger: true), 0), "a root is OnEnter only: AbilityZone has no WhileInside root");
            Assert.IsNotNull(AreaEffectSpawner.Refusal(new AreaEffectSpec(AreaEffectKind.StaticAoE,
                AreaEffectChannel.OnEnter, ZoneEffect.Root, 1f, 1f, rootDuration: 1f), 0), "a root zone the zone would not consume");
            Assert.IsNotNull(AreaEffectSpawner.Refusal(AreaEffectSpec.AuraSlow(5f, 3f, 0.5f), -1), "ends with a slot it does not know");
            Assert.IsNotNull(AreaEffectSpawner.Refusal(AreaEffectSpec.AuraSlow(5f, 3f, 0.5f), AreaEffectRules.NoSourceSlot));
            Assert.IsNotNull(AreaEffectSpawner.Refusal(new AreaEffectSpec(AreaEffectKind.StaticAoE,
                AreaEffectChannel.WhileInside, ZoneEffect.Slow, 1f, 1f, endsWithAbility: true), 1), "EndsWithAbility is an Aura's rule");
        }

        [Test]
        public void Spawner_StampsTheOriginalFieldsInTheOriginalOrder_ThenTheThreeNewOnes()
        {
            string body = AssassinExecuteCreditTests.MethodBody(SpawnerPath, "public static bool Spawn(");
            string[] order =
            {
                "zone.SpawnPosition    = capPos;",
                "zone.HasSpawnPosition = true;",
                "zone.Effect          = capSpec.Effect;",
                "zone.OwnerChicken    = capOwner;",
                "zone.SlowFactor   = capSpec.SlowFactor;",
                "zone.RootDuration = capSpec.RootDuration;",
                "zone.NetworkedRadius = capSpec.Radius;",
                "zone.LifetimeTimer   = TickTimer.CreateFromSeconds(runner, capSpec.Lifetime);",
                "zone.Anchor          = capSpec.Anchor;",
                "zone.SourceSlot      = capSlot;",
                "zone.Channels        = capSpec.Channels;",
            };
            int last = -1;
            foreach (var line in order)
            {
                int at = body.IndexOf(line, StringComparison.Ordinal);
                Assert.Greater(at, last, $"stamp line missing or out of order: {line}");
                last = at;
            }
            StringAssert.Contains("leaked a zone", body, "the abandoned-spawn Error is kept (CONVENTIONS corollary 2)");
        }

        [Test]
        public void Placement_ReadsTheSpecsForwardOffset_OnTheCastersGround()
        {
            var at = new Vector3(3f, 0.4f, -2f);
            var fwd = new Vector3(0.6f, 0.8f, 0f);   // tilted: the height must still be the caster's
            var trap = AreaEffectSpec.StaticSlow(3.6f, 5f, 0.45f, 4.5f);
            var p = AreaEffectSpawner.Placement(at, fwd, trap);
            Assert.AreEqual(5.7f, p.x, 1e-4f);
            Assert.AreEqual(0.4f, p.y, 0f, "on the caster's ground plane, whatever the forward's pitch");
            Assert.AreEqual(-2f, p.z, 1e-4f);
            var egg = Asset<RootEggAbilitySO>();
            Assert.IsTrue(egg.TryGetAreaEffect(out var e));
            Assert.AreEqual(at, AreaEffectSpawner.Placement(at, fwd, e), "Root Egg: exactly the caster's feet");

            foreach (var file in new[] { "FeatherTrapAbilitySO.cs", "RootEggAbilitySO.cs", "SmokeRoostAbilitySO.cs", "FeatherAuraAbilitySO.cs" })
                StringAssert.Contains("AreaEffectSpawner.Placement(", Src(Path.Combine(AbilityDir, file)),
                    $"{file}: the spec's ForwardOffset is the one placement (it cannot disagree with the cast)");
        }

        [Test]
        public void TheFourZoneAbilities_SpawnOnlyThroughTheSpawner()
        {
            foreach (var file in new[] { "FeatherTrapAbilitySO.cs", "RootEggAbilitySO.cs", "SmokeRoostAbilitySO.cs", "FeatherAuraAbilitySO.cs" })
            {
                string src = Src(Path.Combine(AbilityDir, file));
                StringAssert.DoesNotContain("Runner.Spawn(", src, $"{file}: the spawn block lives in AreaEffectSpawner now");
                StringAssert.DoesNotContain("onBeforeSpawned", src, file);
                StringAssert.Contains("AreaEffectSpawner.Spawn(ctx, spec,", src, file);
                StringAssert.Contains("ctx.CanSpawnZone(", src, $"{file}: the pre-commitment wiring gate stays");
            }
        }

        // ---- 4. The anchored centre --------------------------------------------------------------------------------

        [Test]
        public void ZoneCenter_World_IsTheStamp_Caster_IsTheOwner_AndNeverThePrefabRoot()
        {
            var stamp = new Vector3(4f, 0f, 2f);
            var root = new Vector3(9.47f, 0f, 9.38f);
            var owner = new Vector3(-3f, 0.4f, 7f);

            Assert.IsTrue(AreaEffectRules.TryResolveCenter(ZoneAnchor.World, true, stamp, root, false, default, out var c));
            Assert.AreEqual(stamp, c, "World: the stamp, whatever the transform says");
            Assert.IsTrue(AreaEffectRules.TryResolveCenter(ZoneAnchor.World, false, default, stamp, false, default, out c));
            Assert.AreEqual(stamp, c, "World before its stamp (the authority, inside Spawned): the transform");

            Assert.IsTrue(AreaEffectRules.TryResolveCenter(ZoneAnchor.Caster, true, stamp, root, true, owner, out c));
            Assert.AreEqual(owner, c, "Caster: the owner as this peer sees it, not where it was cast");
            Assert.IsFalse(AreaEffectRules.TryResolveCenter(ZoneAnchor.Caster, true, stamp, root, false, owner, out c),
                "Caster with no resolvable owner: no centre (inert, hidden) - never the prefab root, never the cast spot");
        }

        [Test]
        public void DespawnRules_Lifetime_Consumed_OwnerGone_AbilityEnded()
        {
            const byte slot = 2, none = AreaEffectRules.NoSourceSlot;
            Assert.AreEqual(ZoneEnd.None, AreaEffectRules.Despawn(false, false, ZoneAnchor.World, false, none, false),
                "a World zone ignores its owner: a placed trap acts while its owner is elsewhere, or gone");
            Assert.AreEqual(ZoneEnd.Lifetime, AreaEffectRules.Despawn(true, false, ZoneAnchor.World, true, none, true));
            Assert.AreEqual(ZoneEnd.Consumed, AreaEffectRules.Despawn(false, true, ZoneAnchor.World, true, none, true));
            Assert.AreEqual(ZoneEnd.OwnerGone, AreaEffectRules.Despawn(false, false, ZoneAnchor.Caster, false, slot, true), "B5");
            Assert.AreEqual(ZoneEnd.OwnerGone, AreaEffectRules.Despawn(false, false, ZoneAnchor.Caster, false, none, false), "B5");
            Assert.AreEqual(ZoneEnd.AbilityEnded, AreaEffectRules.Despawn(false, false, ZoneAnchor.Caster, true, slot, false));
            Assert.AreEqual(ZoneEnd.None, AreaEffectRules.Despawn(false, false, ZoneAnchor.Caster, true, slot, true));
            Assert.AreEqual(ZoneEnd.None, AreaEffectRules.Despawn(false, false, ZoneAnchor.Caster, true, none, false),
                "a Caster zone that does not end with its ability lives its lifetime");
            Assert.AreEqual(ZoneEnd.Lifetime, AreaEffectRules.Despawn(true, false, ZoneAnchor.Caster, true, slot, true), "the backstop");
        }

        [Test]
        public void AbilityZone_DespawnsByTheRules_BehindTheAuthorityGate_AfterTheResetCheck()
        {
            string fun = AssassinExecuteCreditTests.MethodBody(ZonePath, "public override void FixedUpdateNetwork()");
            int gate  = fun.IndexOf("if (!HasStateAuthority) return;", StringComparison.Ordinal);
            int reset = fun.IndexOf("RoundResetObservation.OutlivedReset(", StringComparison.Ordinal);
            int rules = fun.IndexOf("AreaEffectRules.Despawn(", StringComparison.Ordinal);
            int scan  = fun.IndexOf("ScanOnEnter();", StringComparison.Ordinal);
            Assert.GreaterOrEqual(gate, 0);
            Assert.Greater(reset, gate);
            Assert.Greater(rules, reset, "B10 first, then the life rules");
            Assert.Greater(scan, rules, "a despawning zone fires on nobody");
            StringAssert.Contains("owner.Abilities.IsSlotActive(SourceSlot)", fun, "EndsWithAbility reads the caster's own slot");
            StringAssert.Contains("HasChannel(AreaEffectChannel.OnEnter)", fun);
        }

        [Test]
        public void OnEnter_FiresOncePerChickenPerLife_IncludingChickensAlreadyInside()
        {
            // No runner in EditMode: pin the shape. The set is re-armed per spawn (pooled) and checked before the RPC, and
            // there is no "entered since the last tick" edge, so chickens inside on the first authority tick fire too.
            StringAssert.Contains("_entered.Clear();", AssassinExecuteCreditTests.MethodBody(ZonePath, "public override void Spawned()"));
            string scan = AssassinExecuteCreditTests.MethodBody(ZonePath, "private void ScanOnEnter()");
            int add = scan.IndexOf("if (!_entered.Add(chicken.Id)) continue;", StringComparison.Ordinal);
            int rpc = scan.IndexOf("chicken.RPC_ApplyRoot(RootDuration, OwnerChicken);", StringComparison.Ordinal);
            Assert.GreaterOrEqual(add, 0);
            Assert.Greater(rpc, add);
            StringAssert.Contains("Consumed = true;", scan, "Root Egg is still spent on its first catch");
        }

        [Test]
        public void NoSimulationRead_OfZoneTransform()
        {
            // A Caster zone's transform is only moved in Render: any simulation or feedback read of it is a frame stale.
            // Chicken positions (chicken.transform / owner.transform) are fine; the zone's own transform is not.
            var zoneOwnTransform = new Regex(@"(?<![\w\.])transform\.position(?!\s*=[^=])");
            foreach (var method in new[]
                     {
                         "private void ScanOnEnter()", "private void NotifyCaughtVictims()", "private bool AnyRivalInside()",
                         "private bool IsCaught(", "private void RefreshRootGeometry()", "public override void FixedUpdateNetwork()",
                         "private void ObserveRootEvidence()", "private void ConfirmEachFirstEntry()",
                     })
            {
                string body = AssassinExecuteCreditTests.MethodBody(ZonePath, method);
                Assert.IsFalse(zoneOwnTransform.IsMatch(body), $"AbilityZone.{method} reads the zone's transform; use TryGetCenter");
            }

            // The one place it is read is the resolver's World fallback (the authority before its self-stamp).
            StringAssert.Contains("AreaEffectRules.TryResolveCenter(Anchor, HasSpawnPosition, SpawnPosition, transform.position,",
                AssassinExecuteCreditTests.MethodBody(ZonePath, "public bool TryGetCenter("));

            string slow = AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void CheckAbilityZoneSlow()");
            StringAssert.Contains("zone.TryGetCenter(out Vector3 center)", slow);
            foreach (var path in Directory.GetFiles("Assets/_Game/Scripts/Gameplay", "*.cs"))
                StringAssert.DoesNotContain("zone.transform", Src(path), $"{Path.GetFileName(path)} reads a zone's transform");
        }

        [Test]
        public void AbilityZonePrefab_HasNoNetworkTransform()
        {
            // F2 / C2: an aura costs no bandwidth after its spawn - every peer evaluates it from the caster's replicated
            // position. A NetworkTransform here would add a second interpolated transform that drifts from the caster.
            var prefab = TestAssets.Load<GameObject>(ZonePrefabPath);
            Assert.IsNotNull(prefab.GetComponent<AbilityZone>());
            Assert.IsNull(prefab.GetComponentInChildren<Fusion.NetworkTransform>(true),
                "AbilityZone.prefab must not carry a NetworkTransform (decision F2): Caster zones follow in Render");
        }

        // ---- 5. B7: one "inside", decoys ignored --------------------------------------------------------------------

        [Test]
        public void ZonesIgnoreDecoys()
        {
            // B7 (2026-10-21). CW-232 "Doppelganger fidelity" may later reverse this on purpose (a perfect copy is slowed
            // and rooted visibly); until then no zone channel touches a decoy, on any peer.
            Assert.IsTrue(AreaEffectRules.IgnoresChicken(isOwner: false, isDecoy: true), "decoys are ignored");
            Assert.IsTrue(AreaEffectRules.IgnoresChicken(isOwner: true, isDecoy: false), "zones never affect their caster");
            Assert.IsFalse(AreaEffectRules.IgnoresChicken(isOwner: false, isDecoy: false), "a rival is affected");

            const string rule = "AreaEffectRules.IgnoresChicken(";
            StringAssert.Contains(rule, AssassinExecuteCreditTests.MethodBody(ZonePath, "private void ScanOnEnter()"), "OnEnter (root)");
            StringAssert.Contains(rule, AssassinExecuteCreditTests.MethodBody(ZonePath, "private bool IsCaught("), "cast confirmation");
            StringAssert.Contains(rule + "zone.OwnerChicken == this.Id, IsDecoy)",
                AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void CheckAbilityZoneSlow()"), "WhileInside (slow)");
            StringAssert.Contains("CW-232", Src("Assets/_Game/Scripts/Gameplay/AreaEffectRules.cs"), "the fidelity ticket is named next to the rule");
        }

        [Test]
        public void SlowZonesAndAura_UsePlanarDistance_LikeRootAimAndRing()
        {
            // B7: a chicken 1.5 m above the centre at 2.9 m planar from it is inside a 3 m ring (3D said 3.26 m: outside).
            var center = Vector3.zero;
            var onTheMound = new Vector3(2.9f, 1.5f, 0f);
            Assert.Greater(onTheMound.magnitude, 3f, "fixture: the old 3D metric left it out");
            Assert.IsTrue(AreaEffectRules.IsInside(center, 3f, onTheMound));
            Assert.IsFalse(AreaEffectRules.IsInside(center, 3f, new Vector3(3.01f, 0f, 0f)));

            // WP4: Feather Aura is a zone, so CheckAbilityZoneSlow is the one slow-zone test (CheckAuraSlow is gone).
            StringAssert.Contains("AreaEffectRules.IsInside(center, zone.TriggerRadius, transform.position)",
                AssassinExecuteCreditTests.MethodBody(ControllerPath, "private void CheckAbilityZoneSlow()"));
            StringAssert.DoesNotContain("CheckAuraSlow", Src(ControllerPath));
        }

        // ---- 6. World hash: auras leave static ---------------------------------------------------------------------

        private static (uint stat, int ns, uint aura, int na) Hash(IEnumerable<RoundInvariants.WorldZone> zones)
        {
            var statics = new List<(string, Vector2)>();
            var auras = new List<(uint, int, byte, byte)>();
            RoundInvariants.SplitZones(zones, statics, auras);
            return (RoundInvariants.WorldHash(statics, out int ns), ns, RoundInvariants.AuraHash(auras, out int na), na);
        }

        private static RoundInvariants.WorldZone Trap(Vector2 at) => new RoundInvariants.WorldZone
        {
            CasterAnchored = false, Position = at, OwnerRaw = 7, Radius = 3.6f, Effect = 0, Channels = 2,
        };

        private static RoundInvariants.WorldZone Aura(Vector2 casterSeenAt, float radius = 5.4f) => new RoundInvariants.WorldZone
        {
            CasterAnchored = true, Position = casterSeenAt, OwnerRaw = 42, Radius = radius, Effect = 0, Channels = 2,
        };

        [Test]
        public void WorldHash_CasterZones_AreExcludedFromStatic()
        {
            // Two peers see the caster ~3 m apart (interpolation): static and auras must still agree.
            var peerA = Hash(new[] { Trap(new Vector2(4f, 2f)), Aura(new Vector2(10f, 10f)) });
            var peerB = Hash(new[] { Trap(new Vector2(4f, 2f)), Aura(new Vector2(12.1f, 12.2f)) });
            Assert.AreEqual(peerA.stat, peerB.stat, "a moving aura never trips the static hash");
            Assert.AreEqual(1, peerA.ns, "only the placed trap is static");
            Assert.AreEqual(peerA.aura, peerB.aura, "the aura hash ignores position");
            Assert.AreEqual(1, peerA.na);

            var missing = Hash(new[] { Trap(new Vector2(4f, 2f)) });
            Assert.AreEqual(peerA.stat, missing.stat);
            Assert.AreNotEqual(peerA.aura, missing.aura, "a peer missing the aura is caught on auras=");
            Assert.AreNotEqual(peerA.aura, Hash(new[] { Aura(Vector2.zero, radius: 4.0f) }).aura, "a wrong radius is caught");
        }

        [Test]
        public void InvariantProbe_SplitsZonesByAnchor_AndPrintsAuras()
        {
            string probe = Src("Assets/_Game/Scripts/Diagnostics/InvariantProbe.cs");
            StringAssert.Contains("CasterAnchored = z.Anchor == ZoneAnchor.Caster,", probe);
            StringAssert.Contains("RoundInvariants.SplitZones(_zoneScan, _zones, _auras);", probe);
            StringAssert.Contains("auras={ha:X8}/{na}", probe);
            StringAssert.Contains("if (live[i].Anchor == ZoneAnchor.Caster) auras++;", probe, "round start: an aura zone counts as au=, not zn=");
            string merge = Src("tools/test-session/merge.ps1");
            StringAssert.Contains("Test-WorldParity 'auras'", merge, "merge.ps1 runs the 2-heartbeat parity rule on auras=");
            StringAssert.Contains("Test-WorldParity 'static'", merge);
        }
    }
}
