using System.Collections.Generic;
using Fusion;
using NUnit.Framework;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using UnityEngine;

// Fusion ships its own Assert, so importing it alongside NUnit is ambiguous — the same
// footgun docs/CONVENTIONS.md records for LogLevel, with the same fix.
using Assert = NUnit.Framework.Assert;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the pre-commitment wiring gate: <see cref="AbilityBaseSO.CanActivate"/> and the
    /// four spawning abilities that override it — the three zone placers, plus Doppelganger,
    /// which spawns a NetworkObject without placing a zone. Before the gate existed, a missing
    /// <c>PrefabRegistrySO.AbilityZone</c> cost the player the whole cast <i>and</i> its
    /// cooldown, because the guard sat inside <c>OnActivate</c> — after
    /// <c>AbilityController.TryActivate</c> had already written <c>ActiveSlot</c>, the
    /// activation timer and the cooldown.
    /// </summary>
    /// <remarks>
    /// Testable without Play Mode because <see cref="AbilityContext"/> is a plain class and
    /// <see cref="AbilityContext.CanSpawnZone"/> only reads two fields off it. The zone prefab
    /// is the real shipped asset; the <c>NetworkRunner</c> is a stand-in on an <b>inactive</b>
    /// GameObject, which suppresses Awake/OnEnable so the test gets a non-null reference of the
    /// right type without booting any Fusion machinery.
    ///
    /// <see cref="EverySpawningAbility_OverridesCanActivate"/> is the load-bearing one — it is
    /// what catches the <i>next</i> spawning ability added without a guard, and
    /// <see cref="SpawnerKey_CoversEveryAbilityThatSpawns"/> is what keeps <i>its</i> key honest.
    /// </remarks>
    public sealed class AbilityWiringGateTests
    {
        /// <summary>Everything created for one test, torn down together.</summary>
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            }
            _spawned.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            _spawned.Add(obj);
            return obj;
        }

        /// <summary>
        /// A non-null <c>NetworkRunner</c> reference that never wakes up. The GameObject is
        /// deactivated before the component is added, which suppresses Awake/OnEnable — the
        /// gate only needs the reference to be non-null, not a running Fusion session.
        /// </summary>
        private NetworkRunner InertRunner()
        {
            var go = Track(new GameObject("TestRunner"));
            go.SetActive(false);
            return go.AddComponent<NetworkRunner>();
        }

        private AbilityContext ContextWith(NetworkRunner runner, PrefabRegistrySO registry)
        {
            // Controller is null throughout: none of CanSpawnZone, and none of the three
            // CanActivate overrides, touch it. Log is null too, so the Error lines those
            // overrides emit no-op via ctx.Log?. rather than needing a fake logger.
            return new AbilityContext(null) { Runner = runner, PrefabRegistry = registry };
        }

        private PrefabRegistrySO RegistryWithZone()
        {
            var registry = Track(ScriptableObject.CreateInstance<PrefabRegistrySO>());
            // The real shipped prefab rather than a synthesised NetworkObject: this is the
            // reference the game actually wires, and loading it costs nothing here.
            registry.AbilityZone = TestAssets.Load<NetworkObject>(
                TestAssets.PrefabsDir + "/Abilities/AbilityZone.prefab");
            return registry;
        }

        /// <summary>The three abilities that place an <c>AbilityZone</c>, as live instances.</summary>
        private IEnumerable<AbilityBaseSO> ZoneAbilities()
        {
            yield return Track(ScriptableObject.CreateInstance<FeatherTrapAbilitySO>());
            yield return Track(ScriptableObject.CreateInstance<RootEggAbilitySO>());
            yield return Track(ScriptableObject.CreateInstance<SmokeRoostAbilitySO>());
        }

        // ---- The three ways the wiring can be broken ----------------------------

        [Test]
        public void ZoneAbility_RefusesWhenRunnerIsNull()
        {
            var ctx = ContextWith(null, RegistryWithZone());
            foreach (var ability in ZoneAbilities())
            {
                Assert.IsFalse(ability.CanActivate(ctx),
                    $"{ability.GetType().Name} must refuse before the cast is committed when the " +
                    "context carries no NetworkRunner — there is nothing to Spawn into.");
            }
        }

        [Test]
        public void ZoneAbility_RefusesWhenPrefabRegistryIsNull()
        {
            var ctx = ContextWith(InertRunner(), null);
            foreach (var ability in ZoneAbilities())
            {
                Assert.IsFalse(ability.CanActivate(ctx),
                    $"{ability.GetType().Name} must refuse when no PrefabRegistrySO was injected " +
                    "into AbilityController.");
            }
        }

        [Test]
        public void ZoneAbility_RefusesWhenAbilityZonePrefabIsUnassigned()
        {
            // The failure that actually happens in practice: the registry asset exists and is
            // injected, but its AbilityZone slot was never filled in the inspector.
            var registry = Track(ScriptableObject.CreateInstance<PrefabRegistrySO>());
            var ctx = ContextWith(InertRunner(), registry);

            foreach (var ability in ZoneAbilities())
            {
                Assert.IsFalse(ability.CanActivate(ctx),
                    $"{ability.GetType().Name} must refuse when PrefabRegistrySO.AbilityZone is " +
                    "unassigned — this is the wiring bug the gate exists for.");
            }
        }

        [Test]
        public void ZoneAbility_AllowsWhenFullyWired()
        {
            var ctx = ContextWith(InertRunner(), RegistryWithZone());
            foreach (var ability in ZoneAbilities())
            {
                Assert.IsTrue(ability.CanActivate(ctx),
                    $"{ability.GetType().Name} must allow the cast once Runner, PrefabRegistry and " +
                    "AbilityZone are all present. A gate that refuses a correctly wired ability " +
                    "would break every zone cast in the game.");
            }
        }

        // ---- The base default ---------------------------------------------------

        [Test]
        public void NonZoneAbility_UsesPermissiveBaseDefault()
        {
            // CanActivate had to be non-breaking by construction: the other 34 shipped abilities
            // never opted in, so the base must let them through even on a context with nothing in
            // it. Asserted against a real shipped asset, not a fresh CreateInstance, so an asset
            // that somehow carried an override would still be caught.
            var shock = TestAssets.Load<AbilityBaseSO>(TestAssets.AbilitiesDir + "/CluckShock.asset");
            Assert.IsFalse(shock.PlacesZone, "Cluck Shock is the non-zone control in this test — " +
                "if it started placing a zone, pick a different ability here.");

            Assert.IsTrue(shock.CanActivate(ContextWith(null, null)),
                "AbilityBaseSO.CanActivate must default to true. An ability that never opted into " +
                "the wiring gate must not be refused by it.");
        }

        // ---- The component the caster must actually have ------------------------

        /// <summary>
        /// Receiver expressions that denote the <b>casting chicken</b> inside an ability SO.
        /// A component reached for through one of these has to exist on Chicken.prefab, or the
        /// cast fails at the point of use — after <c>AbilityController.TryActivate</c> has
        /// already written <c>ActiveSlot</c> and charged the cooldown, which is precisely the
        /// pre-commitment failure the rest of this fixture guards.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT match <c>networkObject.GetComponent&lt;…&gt;</c>: those read a
        /// prefab the ability just spawned (AbilityZone, Doppelganger), not the caster, and
        /// asserting them against Chicken.prefab would fail for the wrong reason. The receiver
        /// is the whole discriminator, so it is what the pattern keys on.
        ///
        /// Covers the null-conditional and <c>x != null ? x.GetComponent…</c> spellings, both
        /// of which reduce to the same receiver token.
        /// </remarks>
        private const string CasterReceiverPattern =
            @"\b(?:caster|ctx\.Controller)\s*\??\s*\.\s*GetComponent\s*<\s*([A-Za-z_]\w*)\s*>";

        /// <summary>
        /// Resolves a simple type name written in an ability SO to the <c>Component</c> type it
        /// names. Ability scripts all sit in Assembly-CSharp and import
        /// <c>CluckWars.Gameplay</c>, so the simple name is unambiguous.
        /// </summary>
        private static System.Type ResolveComponentType(string simpleName)
        {
            foreach (var type in typeof(ChickenController).Assembly.GetTypes())
            {
                if (type.Name == simpleName && typeof(Component).IsAssignableFrom(type)) return type;
            }
            return null;
        }

        /// <summary>
        /// The gate that would have caught Mark/Kill being inert in the shipped game
        /// (found 2026-09-04 by the Ability Lab's first full-registry sweep).
        /// <c>MarkKillAbilitySO</c> reached for <c>AssassinExecute</c> on the caster; the
        /// component was on no prefab and nothing added it at runtime, so every press logged an
        /// Error and burned its cooldown with nothing marked.
        /// </summary>
        /// <remarks>
        /// Source-derived on purpose. <c>ProjectConfigTests.ChickenPrefab_HasEveryComponentTheGameplayLoopResolves</c>
        /// asserts the same class of fact from a hand-written <c>Require&lt;T&gt;</c> list — and
        /// sat green through this exact bug, because nobody remembered to add a line to it when
        /// <c>AssassinExecute</c> was written. Reading the requirement out of the ability source
        /// is what makes the <i>next</i> one self-registering: an ability that reaches for a new
        /// component on the caster is covered the moment it is written, with no list to update.
        /// </remarks>
        [Test]
        public void EveryComponentAbilitiesResolveOnTheCaster_IsOnTheChickenPrefab()
        {
            var abilityScriptDir = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "Assets/_Game/Scripts/Abilities");
            Assert.IsTrue(System.IO.Directory.Exists(abilityScriptDir),
                $"Ability scripts are not at '{abilityScriptDir}'. docs/CONVENTIONS.md pins them there; " +
                "if they moved, update this path AND every other consumer of that convention.");

            var chicken = TestAssets.Load<GameObject>(TestAssets.ChickenPrefabPath);
            var networkObject = chicken.GetComponent<NetworkObject>();
            Assert.IsNotNull(networkObject,
                "Chicken.prefab has no NetworkObject, so nothing about the chicken replicates. " +
                "Every other assertion here is moot until that is fixed.");

            var regex = new System.Text.RegularExpressions.Regex(CasterReceiverPattern);
            int required = 0;

            foreach (var file in System.IO.Directory.GetFiles(abilityScriptDir, "*.cs"))
            {
                string abilityName = System.IO.Path.GetFileNameWithoutExtension(file);
                foreach (System.Text.RegularExpressions.Match match
                         in regex.Matches(System.IO.File.ReadAllText(file)))
                {
                    string simpleName = match.Groups[1].Value;
                    var type = ResolveComponentType(simpleName);
                    Assert.IsNotNull(type,
                        $"{abilityName} resolves '{simpleName}' off the caster, but no Component type " +
                        "by that name exists in Assembly-CSharp. Either it was renamed and the ability " +
                        "was not updated, or it lives in an assembly this sweep cannot see.");

                    required++;

                    Assert.IsNotNull(chicken.GetComponent(type),
                        $"{abilityName} calls caster.GetComponent<{simpleName}>(), but Chicken.prefab has " +
                        $"no {simpleName}. The ability commits the cast first — AbilityController.TryActivate " +
                        "writes ActiveSlot and starts the cooldown before OnActivate runs — so the player " +
                        "pays for a press that can do nothing. This is the Mark/Kill defect verbatim: add " +
                        $"{simpleName} to Chicken.prefab (Doppelganger.prefab is a variant and inherits it).");

                    // A NetworkBehaviour that is present but never baked into NetworkObject's
                    // list is the quieter half of the same bug: GetComponent finds it, so the
                    // Error above never fires, but none of its [Networked] state replicates.
                    if (!typeof(NetworkBehaviour).IsAssignableFrom(type)) continue;

                    Assert.Contains(chicken.GetComponent(type), networkObject.NetworkedBehaviours,
                        $"{simpleName} is on Chicken.prefab but missing from NetworkObject.NetworkedBehaviours, " +
                        $"so its [Networked] state never replicates — {abilityName} would work on the caster's " +
                        "own peer and silently do nothing everywhere else. Re-save the prefab in the Editor to " +
                        "let Fusion's baker rebuild the list.");
                }
            }

            Assert.GreaterOrEqual(required, 1,
                "Found no caster-side GetComponent<…> call in any ability SO. Mark/Kill has two, so zero " +
                $"matches means {nameof(CasterReceiverPattern)} stopped matching the source — the sweep is " +
                "passing vacuously, not because the wiring is clean.");
        }

        // ---- The one that catches the next spawning ability ---------------------

        /// <summary>
        /// Ability types that spawn a <c>NetworkObject</c> without placing an
        /// <c>AbilityZone</c>, and so are invisible to <see cref="AbilityBaseSO.PlacesZone"/>.
        /// </summary>
        /// <remarks>
        /// TODO — replace this set with <c>public virtual bool SpawnsNetworkObject => false;</c>
        /// on <see cref="AbilityBaseSO"/>, overridden <c>=> true</c> by each spawning ability,
        /// and key <see cref="EverySpawningAbility_OverridesCanActivate"/> off that instead.
        /// That is the self-registering form: a new spawning ability would opt in where it is
        /// written rather than needing someone to remember this list.
        ///
        /// <see cref="AbilityBaseSO.PlacesZone"/> is NOT that flag and must not be reused as
        /// one — it is a feedback flag (it gates <c>ReportsCastHits</c>). Keying the sweep off
        /// it is exactly why Doppelganger, the fourth <c>onBeforeSpawned</c> call site, went
        /// unguarded while a test written to catch that class of bug sat green.
        ///
        /// Until the virtual lands, <see cref="SpawnerKey_CoversEveryAbilityThatSpawns"/> is
        /// what keeps this list honest.
        /// </remarks>
        private static readonly HashSet<System.Type> NonZoneSpawnerTypes = new HashSet<System.Type>
        {
            typeof(DoppelgangerAbilitySO),
        };

        private static bool SpawnsNetworkObject(AbilityBaseSO ability) =>
            ability.PlacesZone || NonZoneSpawnerTypes.Contains(ability.GetType());

        [Test]
        public void EverySpawningAbility_OverridesCanActivate()
        {
            // Sweeps the whole shipped pool, subfolders included (Passives/ lives under
            // AbilitiesDir), so a new spawning ability dropped in tomorrow is covered without
            // anyone remembering to extend this file.
            var all = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir);
            Assert.IsNotEmpty(all, $"No ability assets found under {TestAssets.AbilitiesDir}.");

            int spawnerCount = 0;
            foreach (var ability in all)
            {
                if (!SpawnsNetworkObject(ability)) continue;
                spawnerCount++;

                var declaring = ability.GetType().GetMethod(nameof(AbilityBaseSO.CanActivate)).DeclaringType;
                Assert.AreNotEqual(typeof(AbilityBaseSO), declaring,
                    $"{ability.name} ({ability.GetType().Name}) spawns a NetworkObject but does not " +
                    "override CanActivate, so unassigned wiring (PrefabRegistrySO.AbilityZone, or the " +
                    "ability's own prefab field) would burn the player's cast and its cooldown before " +
                    "failing. Add a CanActivate that returns false and logs an Error naming the missing " +
                    "reference — ctx.CanSpawnZone(Source, \"" + ability.DisplayName + "\") for a zone " +
                    "ability, or a hand-written check for an ability with its own prefab field.");
            }

            Assert.GreaterOrEqual(spawnerCount, 4,
                "Expected at least the four known spawning abilities (Feather Trap, Root Egg, Smoke " +
                "Roost, Doppelganger). Fewer means the sweep stopped seeing assets, not that the " +
                "guards are gone.");
        }

        [Test]
        public void SpawnerKey_CoversEveryAbilityThatSpawns()
        {
            // The sweep above can only be as good as its key, and until SpawnsNetworkObject
            // exists that key is partly a hand-maintained list. This is the backstop: it finds
            // spawning abilities the way a human would — by looking for the runner.Spawn
            // callback in the source — and fails if the key does not already know about one.
            var abilityScriptDir = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "Assets/_Game/Scripts/Abilities");
            Assert.IsTrue(System.IO.Directory.Exists(abilityScriptDir),
                $"Ability scripts are not at '{abilityScriptDir}'. docs/CONVENTIONS.md pins them there; " +
                "if they moved, update this path AND every other consumer of that convention.");

            // Types the sweep will actually visit, resolved without needing an authored asset:
            // a spawning ability written today but not yet given a .asset is still a real gap.
            var covered = new HashSet<string>();
            foreach (var type in typeof(AbilityBaseSO).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(AbilityBaseSO).IsAssignableFrom(type)) continue;
                var probe = Track(ScriptableObject.CreateInstance(type) as AbilityBaseSO);
                if (probe != null && SpawnsNetworkObject(probe)) covered.Add(type.Name);
            }

            foreach (var file in System.IO.Directory.GetFiles(abilityScriptDir, "*.cs"))
            {
                if (!System.IO.File.ReadAllText(file).Contains("onBeforeSpawned")) continue;

                var typeName = System.IO.Path.GetFileNameWithoutExtension(file);
                Assert.IsTrue(covered.Contains(typeName),
                    $"{typeName} calls runner.Spawn(onBeforeSpawned: …) but is invisible to " +
                    $"{nameof(EverySpawningAbility_OverridesCanActivate)}, so nothing checks that it " +
                    "refuses the press before the cooldown is charged when its wiring is missing. Add it " +
                    $"to {nameof(NonZoneSpawnerTypes)} — or, better, add the SpawnsNetworkObject virtual " +
                    "to AbilityBaseSO that the TODO there describes and key the sweep off that.");
            }
        }
    }
}
