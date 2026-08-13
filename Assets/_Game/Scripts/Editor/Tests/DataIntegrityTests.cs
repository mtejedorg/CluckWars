using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using CluckWars.Abilities;
using CluckWars.Audio;
using CluckWars.Gameplay;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the REAL ScriptableObject assets under <c>Assets/_Game/Data/</c>.
    /// </summary>
    /// <remarks>
    /// This is the category that pays for itself before a playtest: an unassigned
    /// inspector slot compiles clean, passes every logic test, and then crashes (or
    /// worse, silently no-ops) the first time a match spawns. Zenject binds these
    /// SOs <c>FromInstance</c> — see <c>docs/CONVENTIONS.md</c> "Bind by instance" —
    /// so a null field here becomes a null dependency everywhere downstream.
    ///
    /// Everything asserted here is authored data, not code. A failure means "open
    /// the asset in the Inspector and fill it in", not "fix a bug in C#".
    /// </remarks>
    public sealed class DataIntegrityTests
    {
        // ---- ChickenClassRegistrySO — the spawn path's lookup table -------------

        [Test]
        public void ClassRegistry_HasExactlyOneEntry_PerChickenClass()
        {
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out _),
                    $"No registry entry for {cls}. ChickenController.Spawned resolves stats from this " +
                    "registry; a missing entry silently falls back to Warrior (or to the prefab's " +
                    "_fallbackStats, and if that is null the chicken cannot move at all).");
            }
        }

        [Test]
        public void ClassRegistry_EveryEntry_HasStatsAssigned()
        {
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");
                Assert.IsNotNull(entry.Stats,
                    $"{cls}'s registry entry has no ChickenStatsSO. ChickenController would fall through " +
                    "to _fallbackStats; if that is null, _movement is never constructed and the chicken " +
                    "is frozen for the whole match (documented footgun, CONVENTIONS.md).");
            }
        }

        [Test]
        public void ClassRegistry_EveryEntry_HasTheClassPassive_TheGddAssigns()
        {
            // GDD v0.3 §5.2. The passive drives real mechanics (ChickenController's
            // Tough damage bonus, Immovable knockback reduction, Slippery control
            // resistance, and the Combo 3rd-slot gate) — a mis-authored Passive
            // silently gives a class the wrong kit with no error anywhere.
            var expected = new Dictionary<ChickenClass, ChickenPassive>
            {
                { ChickenClass.Warrior,  ChickenPassive.Tough },
                { ChickenClass.Speedy,   ChickenPassive.Slippery },
                { ChickenClass.Fatty,    ChickenPassive.Immovable },
                { ChickenClass.Assassin, ChickenPassive.Combo },
            };

            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            foreach (var kv in expected)
            {
                Assert.IsTrue(reg.TryGet(kv.Key, out var entry), $"No entry for {kv.Key}.");
                Assert.IsNotNull(entry.Stats, $"{kv.Key} has no stats asset.");
                Assert.AreEqual(kv.Value, entry.Stats.Passive,
                    $"{kv.Key} should have the {kv.Value} passive (GDD §5.2) but its stats asset " +
                    $"'{entry.Stats.name}' says {entry.Stats.Passive}.");
            }
        }

        [Test]
        public void ClassRegistry_EveryEntry_HasADistinctVisibleTint()
        {
            // ChickenController.Spawned pushes entry.TintColor straight into
            // ChickenVisuals.ApplyTint → the renderer's _BaseColor. The tint IS the
            // in-world class identity, so it must be distinguishable and not black.
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            var tints = new Dictionary<ChickenClass, Color>();
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");
                tints[cls] = entry.TintColor;

                float luma = entry.TintColor.r + entry.TintColor.g + entry.TintColor.b;
                Assert.Greater(luma, 0.05f,
                    $"{cls}'s TintColor is {entry.TintColor} — effectively black. " +
                    "ChickenVisuals.PushColor writes it to _BaseColor, so this class renders as a " +
                    "black silhouette in the arena. Author a real class colour.");
            }

            foreach (var a in tints)
            foreach (var b in tints)
            {
                if (a.Key >= b.Key) continue;
                float delta = Mathf.Abs(a.Value.r - b.Value.r)
                            + Mathf.Abs(a.Value.g - b.Value.g)
                            + Mathf.Abs(a.Value.b - b.Value.b);
                Assert.Greater(delta, 0.15f,
                    $"{a.Key} and {b.Key} have near-identical tints ({a.Value} vs {b.Value}) — " +
                    "players cannot tell the two classes apart on the field.");
            }
        }

        [Test]
        public void ClassRegistry_TintAlpha_IsReachableBy_MenuUiControllerTintOf()
        {
            // MenuUiController.TintOf gates on `e.TintColor.a > 0f` before using the
            // registry tint, falling back to its own hard-coded palette otherwise.
            // Entry.TintColor is declared [ColorUsage(showAlpha: false)], so the alpha
            // channel cannot be authored in the Inspector at all — if it is 0 in the
            // asset the gate can never open, the registry branch is dead code, and the
            // menu tint permanently disagrees with the in-world tint.
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");
                Assert.Greater(entry.TintColor.a, 0f,
                    $"{cls}'s TintColor alpha is {entry.TintColor.a}. MenuUiController.TintOf requires " +
                    "alpha > 0 to use the registry tint, but [ColorUsage(showAlpha: false)] hides the " +
                    "alpha slider — so the menu silently uses a different colour than the arena does. " +
                    "Fix by either authoring alpha 1 or dropping the `.a > 0f` gate in MenuUiController.");
            }
        }

        [Test]
        public void ClassRegistry_EveryEntry_HasALoreQuote()
        {
            // Character-select shows it under the class name (UI rebuild Stage C).
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.LoreQuote),
                    $"{cls} has no LoreQuote — character-select renders an empty line for it.");
            }
        }

        [Test]
        public void ClassRegistry_EveryEntry_HasItsModelAndGenericAvatar()
        {
            // ChickenController.AttachClassModel instantiates ModelPrefab under the chicken
            // root and hands ModelAvatar to the root Animator. A null ModelPrefab spawns an
            // invisible chicken — there is no placeholder capsule to fall back on any more.
            //
            // The avatar must be GENERIC. Unity maps these rigs to Humanoid without complaint
            // (Mixamo-standard bone names, all 22 resolve, isValid && isHuman), which makes
            // Humanoid look like the right answer. It is not: humanoid retargeting rebuilds the
            // pose in human muscle space every frame. Measured live on 2026-08-07, that stretched
            // the chicken from 1.60m to 1.75m, splayed the limbs, and put the feet 1.04m BELOW the
            // CharacterController capsule — the chicken renders as a heap sunk through the floor.
            // With a Generic avatar the model spans the capsule exactly and its feet sit exactly
            // on the ground. This test is the tripwire for anyone re-importing these .fbx files.
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");

                Assert.IsNotNull(entry.ModelPrefab,
                    $"{cls}'s registry entry has no ModelPrefab — chickens of this class spawn invisible.");
                Assert.IsNotNull(entry.ModelPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true),
                    $"{cls}'s ModelPrefab '{entry.ModelPrefab.name}' has no SkinnedMeshRenderer, so there " +
                    "is nothing for ChickenVisuals to tint or HitFeedback to flash.");

                Assert.IsNotNull(entry.ModelAvatar,
                    $"{cls}'s registry entry has no ModelAvatar — the root Animator cannot bind the rig.");
                Assert.IsTrue(entry.ModelAvatar.isValid,
                    $"{cls}'s ModelAvatar '{entry.ModelAvatar.name}' is invalid — re-import the .fbx with " +
                    "Rig ▸ Animation Type = Generic, Avatar Definition = Create From This Model.");
                Assert.IsFalse(entry.ModelAvatar.isHuman,
                    $"{cls}'s ModelAvatar '{entry.ModelAvatar.name}' is Humanoid. Humanoid retargeting " +
                    "deforms the chicken rig and sinks it ~1m through the floor (see comment above). " +
                    "Re-import the .fbx with Rig ▸ Animation Type = Generic.");
            }
        }

        [Test]
        public void ClassRegistry_EveryEntry_HasAnAuthoredTintStrength()
        {
            // TintStrength was added to Entry after the asset was authored, so it deserialized
            // to 0 on every existing entry until the values were filled in. 0 is a *silent*
            // failure mode: the chickens still render, they just quietly lose the class hue cue
            // entirely, and nothing else in the game complains. The other end is just as bad —
            // near 1 multiplies the class tint into the model's baked albedo atlas (URP/Lit
            // computes _BaseMap x _BaseColor) and every class collapses toward flat mud.
            // This test pins the authored band so a reset field or a re-import cannot regress
            // the models to untinted-or-washed-out without the suite saying so.
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");

                Assert.Greater(entry.TintStrength, 0f,
                    $"{cls}'s TintStrength is {entry.TintStrength}. Zero means no class wash at all — " +
                    "the usual cause is the field being added/reset and left at its default. " +
                    "Author it on ChickenClassRegistry.asset (~0.25).");
                Assert.LessOrEqual(entry.TintStrength, 0.6f,
                    $"{cls}'s TintStrength is {entry.TintStrength}, which washes out the model's baked " +
                    "albedo atlas — the tint and the texture are near-identical in hue, so multiplying " +
                    "them at this strength turns the class art to mud. Keep it at or below 0.6.");
            }
        }

        [Test]
        public void ClassRegistry_EveryModel_ResolvesToATexturedInstancedMaterial()
        {
            // The albedo atlas ships EMBEDDED inside each .fbx and is never surfaced to a usable
            // material on its own — the importer's own material (Material_0) comes through with
            // _BaseMap unset, which is why the chickens rendered untextured for so long. The fix
            // is a material remap in the .fbx importer's externalObjects map pointing at a real
            // URP/Lit material under Assets/_Game/Art/Materials/. That remap lives in the .meta,
            // so a re-import, a meta conflict, or a "clean up unused materials" pass can drop it
            // silently and the chickens go back to untextured grey with nothing logged.
            //
            // _BaseColor must stay white: ChickenVisuals pushes the class tint through a
            // MaterialPropertyBlock, and URP/Lit multiplies _BaseMap x _BaseColor — a non-white
            // base would double-apply the tint on top of the already-class-coloured atlas.
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");
                Assert.IsNotNull(entry.ModelPrefab, $"{cls} has no ModelPrefab.");

                var smr = entry.ModelPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Assert.IsNotNull(smr, $"{cls}'s ModelPrefab has no SkinnedMeshRenderer.");

                var mat = smr.sharedMaterial;
                Assert.IsNotNull(mat,
                    $"{cls}'s model renderer has no material — it renders as magenta/grey.");

                Assert.IsTrue(mat.HasProperty("_BaseMap"),
                    $"{cls}'s material '{mat.name}' has no _BaseMap property (shader is " +
                    $"'{(mat.shader != null ? mat.shader.name : "null")}'). URP/Lit is required.");
                Assert.IsNotNull(mat.GetTexture("_BaseMap"),
                    $"{cls}'s material '{mat.name}' has no _BaseMap texture, so the chicken renders " +
                    "untextured. The .fbx importer material remap has probably been dropped — " +
                    "re-point (Material, Material_0) at the class material in " +
                    "Assets/_Game/Art/Materials/.");

                var baseColor = mat.GetColor("_BaseColor");
                Assert.AreEqual(Color.white, baseColor,
                    $"{cls}'s material '{mat.name}' has _BaseColor {baseColor}, not white. " +
                    "ChickenVisuals applies the class tint per-instance via a MaterialPropertyBlock; " +
                    "a non-white base multiplies the tint in twice.");

                Assert.IsTrue(mat.enableInstancing,
                    $"{cls}'s material '{mat.name}' has GPU instancing disabled. ChickenVisuals " +
                    "documents itself as instancing-friendly precisely because it uses a " +
                    "MaterialPropertyBlock instead of per-chicken material copies — that claim only " +
                    "pays off if the material opts in.");
            }
        }

        // ---- ChickenStatsSO — the per-class tunables ----------------------------

        [Test]
        public void ClassStats_EveryAsset_HasSaneValues()
        {
            var all = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);
            Assert.AreEqual(4, all.Count,
                $"Expected one ChickenStatsSO per class in {TestAssets.ClassesDir}, found {all.Count}.");

            foreach (var s in all)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.DisplayName), $"{s.name}: DisplayName is blank.");
                Assert.Greater(s.MoveSpeed, 0f,    $"{s.name}: MoveSpeed {s.MoveSpeed} — the class cannot move.");
                Assert.Greater(s.TurnSpeed, 0f,    $"{s.name}: TurnSpeed {s.TurnSpeed} — the class cannot turn.");
                Assert.GreaterOrEqual(s.CargoCapacity, 1, $"{s.name}: CargoCapacity {s.CargoCapacity} — cannot carry food, so it can never score.");
                Assert.Greater(s.CollectionRate, 0f, $"{s.name}: CollectionRate {s.CollectionRate} — cannot collect food, so it can never score.");
                Assert.Greater(s.Scale, 0f,        $"{s.name}: Scale {s.Scale} — the mesh collapses to a point.");
                Assert.AreNotEqual(ChickenPassive.None, s.Passive,
                    $"{s.name}: Passive is None. Every playable class has a passive (GDD §5.2); " +
                    "None means the class ships with a missing mechanic and the UI shows no passive copy.");
            }
        }

        [Test]
        public void ClassStats_EveryClass_HasAuthoredPeckValues()
        {
            // PeckAmount/PeckCooldown are what the Balance Oracle now solves SCT against, so
            // an unauthored class silently falls back to the C# defaults (3 per 1.0s) and
            // lands on someone else's clear time without anything going red.
            //
            // Only classes that can actually peck are checked, and "can peck" is read off
            // Peck's own AllowedClasses rather than hardcoded here — one source of truth, so
            // opening or closing foraging to a class updates this test for free.
            var peck = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir)
                .Find(a => a is PeckAbilitySO);
            Assert.IsNotNull(peck, "No PeckAbilitySO asset found — foraging is unequippable.");

            foreach (var s in TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir))
            {
                if (!System.Enum.TryParse<ChickenClass>(s.DisplayName, out var cls) ||
                    !AbilityRegistrySO.IsAllowedFor(peck, cls))
                {
                    continue; // cannot forage; its peck stats are unused by design
                }

                Assert.Greater(s.PeckAmount, 0f,
                    $"{s.name}: PeckAmount {s.PeckAmount} — a press would take no food, so the " +
                    "class can never fill its cargo and the Oracle reports it unreachable.");
                Assert.Greater(s.PeckCooldown, 0f,
                    $"{s.name}: PeckCooldown {s.PeckCooldown} — a zero cooldown makes foraging " +
                    "instantaneous and collapses SCT to pure travel time.");

                // Sanity band, not a balance rule: the Oracle owns the exact value. These
                // bounds only catch an order-of-magnitude authoring slip (a stray 30 or 0.03).
                Assert.LessOrEqual(s.PeckCooldown, 5f,
                    $"{s.name}: PeckCooldown {s.PeckCooldown}s is longer than any solved value " +
                    "has ever been — check for a misplaced decimal.");
                Assert.LessOrEqual(s.PeckAmount, s.CargoCapacity,
                    $"{s.name}: PeckAmount {s.PeckAmount} exceeds CargoCapacity {s.CargoCapacity}, " +
                    "so a single press always overfills and the class fills in one press regardless " +
                    "of its cooldown.");
            }
        }

        // ---- The Peck invariant -------------------------------------------------

        /// <summary>
        /// Drives the real <c>MatchBootstrapper.ResolveLegalLoadout</c> — the single spawn
        /// chokepoint both bots and players pass through — and asserts the one rule the whole
        /// food economy rests on: every class that CAN forage always ends up holding Peck, and
        /// the Assassin never does.
        /// </summary>
        /// <remarks>
        /// Worth reaching through reflection for. A forager that spawns without Peck cannot
        /// score for the entire match, and nothing anywhere logs an error, because "this
        /// ability is not equipped" is a completely ordinary state. It is the exact shape of
        /// bug that only shows up as "why did nobody score" three playtests later.
        ///
        /// The bootstrapper is added to an INACTIVE GameObject so Unity never runs its Awake.
        /// </remarks>
        [Test]
        public void ResolveLegalLoadout_AlwaysEquipsPeck_ForForagers_AndNeverForTheAssassin()
        {
            var reg = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            var peck = TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir).Find(a => a is PeckAbilitySO);
            Assert.IsNotNull(peck, "No PeckAbilitySO asset — nothing can collect food.");

            var go = new GameObject(nameof(ResolveLegalLoadout_AlwaysEquipsPeck_ForForagers_AndNeverForTheAssassin));
            go.SetActive(false); // keeps Awake from running
            try
            {
                var boot = go.AddComponent<MatchBootstrapper>();
                var regField = typeof(MatchBootstrapper).GetField("_abilityRegistry",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(regField, "MatchBootstrapper._abilityRegistry was renamed — update this test.");
                regField.SetValue(boot, reg);

                var method = typeof(MatchBootstrapper).GetMethod("ResolveLegalLoadout",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(method, "MatchBootstrapper.ResolveLegalLoadout was renamed — update this test.");

                foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
                {
                    // Deliberately hostile input: nothing chosen at all. This is what a bot
                    // with no preset, or a player who never opened the picker, sends in.
                    var args = new object[] { cls, null, null, null, null, null, null, null, null, null, null };
                    method.Invoke(boot, args);

                    var resolved = new[] { args[7], args[8], args[9], args[10] };
                    int peckCount = 0;
                    foreach (var slot in resolved) if (ReferenceEquals(slot, peck)) peckCount++;

                    bool canForage = AbilityRegistrySO.IsAllowedFor(peck, cls);
                    if (canForage)
                    {
                        Assert.AreEqual(1, peckCount,
                            $"{cls} can forage but resolved {peckCount} Peck(s) from an empty selection. " +
                            "Exactly one is required: zero means the class can never collect food and " +
                            "will score nothing all match, and two wastes a slot on a duplicate.");
                    }
                    else
                    {
                        Assert.AreEqual(0, peckCount,
                            $"{cls} cannot forage, but ResolveLegalLoadout handed it Peck. Not being able " +
                            "to farm is the mechanical basis of the class being a predator.");
                    }

                    // Whatever the class, the loadout must be full — an empty slot is a dead button.
                    foreach (var slot in resolved)
                        Assert.IsNotNull(slot, $"{cls} resolved a null ability slot from an empty selection.");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClassStats_EveryPassive_IsUsedByExactlyOneClass()
        {
            var all = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir);
            var dupes = all.GroupBy(s => s.Passive).Where(g => g.Count() > 1).ToList();

            Assert.IsEmpty(dupes.Select(g => $"{g.Key} on [{string.Join(", ", g.Select(s => s.name))}]").ToList(),
                "Two classes share a passive — the classes are no longer differentiated.");
        }

        // ---- MatchConfigSO ------------------------------------------------------

        [Test]
        public void MatchConfig_ValuesAreInTheirDocumentedRanges()
        {
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            Assert.GreaterOrEqual(cfg.MatchDurationSeconds, 30f,
                $"MatchDurationSeconds {cfg.MatchDurationSeconds} is below the SO's own [Min(30)].");
            Assert.GreaterOrEqual(cfg.FoodTargetToWin, 1,
                $"FoodTargetToWin {cfg.FoodTargetToWin} — the match would end the instant it starts.");
            Assert.GreaterOrEqual(cfg.DeathStunSeconds, 0f, "DeathStunSeconds cannot be negative.");
            Assert.GreaterOrEqual(cfg.DepositRatePerSecond, 0.5f,
                $"DepositRatePerSecond {cfg.DepositRatePerSecond} is below the SO's own [Min(0.5)]; " +
                "at or near zero, cargo never reaches the base.");
        }

        [Test]
        public void MatchConfig_MaxPlayers_CoversTheFourPlayerDesign()
        {
            // MaxPlayers is passed to StartGameArgs.PlayerCount. If it deserialises to
            // 0 (field added after the asset was last written, and Unity did not apply
            // the C# initialiser) Photon refuses every joiner and the demo is 1-player.
            var cfg = TestAssets.Load<MatchConfigSO>(TestAssets.MatchConfigPath);

            Assert.GreaterOrEqual(cfg.MaxPlayers, 4,
                $"MaxPlayers is {cfg.MaxPlayers}. Cluck Wars is a 4-player FFA — anything lower " +
                "silently caps the room below the design and breaks tools/run-clients.ps1.");
            Assert.LessOrEqual(cfg.MaxPlayers, 16,
                $"MaxPlayers {cfg.MaxPlayers} exceeds the SO's own [Range(1, 16)].");
        }

        // ---- ColorSchemeSO ------------------------------------------------------

        [Test]
        public void ColorScheme_EveryColor_IsAtLeastPartiallyOpaque()
        {
            // Every field here is painted onto a HUD element. Alpha 0 is not "subtle",
            // it is invisible — and it looks identical to a missing asset at runtime.
            var scheme = TestAssets.Load<ColorSchemeSO>(TestAssets.ColorSchemePath);

            var colorFields = typeof(ColorSchemeSO)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(Color))
                .ToList();

            Assert.IsNotEmpty(colorFields, "ColorSchemeSO has no public Color fields — the test is stale.");

            foreach (var f in colorFields)
            {
                var c = (Color)f.GetValue(scheme);
                Assert.Greater(c.a, 0f,
                    $"ColorScheme.{f.Name} has alpha 0 — whatever it paints is fully invisible in game.");
            }
        }

        // ---- PrefabRegistrySO — every runtime spawn goes through it -------------

        [Test]
        public void PrefabRegistry_EveryPrefabSlot_IsAssigned()
        {
            // MapGenerator / MatchBootstrapper / ChickenCargo resolve their spawn
            // prefabs here and only WARN when a slot is null, then skip the spawn.
            // A blank slot therefore means "no bases", "no piles" or "no ability
            // zones" with nothing louder than a log line.
            var reg = TestAssets.Load<PrefabRegistrySO>(TestAssets.PrefabRegistryPath);

            var slots = typeof(PrefabRegistrySO)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(Fusion.NetworkObject))
                .ToList();

            Assert.IsNotEmpty(slots, "PrefabRegistrySO exposes no NetworkObject slots — the test is stale.");

            foreach (var f in slots)
            {
                var value = (Fusion.NetworkObject)f.GetValue(reg);
                Assert.IsNotNull(value,
                    $"PrefabRegistry.{f.Name} is unassigned. Its consumer logs a warning and skips the " +
                    "spawn entirely, so the feature just silently does not exist in the match.");
            }
        }

        [Test]
        public void PrefabRegistry_PointsAtThePrefabsUnderTheConventionalFolder()
        {
            var reg = TestAssets.Load<PrefabRegistrySO>(TestAssets.PrefabRegistryPath);

            var slots = typeof(PrefabRegistrySO)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(Fusion.NetworkObject));

            foreach (var f in slots)
            {
                var value = (Fusion.NetworkObject)f.GetValue(reg);
                if (value == null) continue; // covered by the assignment test above

                string path = UnityEditor.AssetDatabase.GetAssetPath(value);
                Assert.IsTrue(path.StartsWith(TestAssets.PrefabsDir),
                    $"PrefabRegistry.{f.Name} points at '{path}', outside {TestAssets.PrefabsDir}. " +
                    "Spawnable prefabs live in one folder so the Fusion NetworkProjectConfig prefab " +
                    "table stays predictable.");
            }
        }

        // ---- AudioRegistrySO ----------------------------------------------------

        [Test]
        public void AudioRegistry_Exists_AndNoClipIsWiredToTwoDifferentCues()
        {
            // Null clips are legal by design (UnityAudioService no-ops on null), so
            // this deliberately does NOT assert completeness. What it does catch is a
            // copy-paste slip in the Inspector: the same clip dragged into two cue
            // slots, which makes two distinct game events sound identical.
            var reg = TestAssets.Load<AudioRegistrySO>(TestAssets.AudioRegistryPath);

            var assigned = typeof(AudioRegistrySO)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(AudioClip))
                .Select(f => new { f.Name, Clip = (AudioClip)f.GetValue(reg) })
                .Where(x => x.Clip != null)
                .ToList();

            var dupes = assigned
                .GroupBy(x => x.Clip)
                .Where(g => g.Count() > 1)
                .Select(g => $"'{g.Key.name}' on [{string.Join(", ", g.Select(x => x.Name))}]")
                .ToList();

            Assert.IsEmpty(dupes, "The same AudioClip is wired to more than one cue slot.");
        }

        // ---- MatchBootstrapper bot loadouts (scene-authored, not an asset) ------

        /// <summary>
        /// Parses the raw <c>_botLoadouts</c> YAML block out of Game.unity into each
        /// preset's Name + Slot0/Slot1/Slot2 guids (empty string for an unassigned slot).
        /// The component lives on a scene GameObject, not an asset, and
        /// <c>EconomyAndPilesTests.SceneVector2/SceneFloat</c> already established reading
        /// Game.unity as text instead of opening it in an EditMode test — that would
        /// disturb whatever scene the developer currently has open.
        /// </summary>
        private static List<(string Name, string Slot0Guid, string Slot1Guid, string Slot2Guid)> ParseBotLoadoutSlotGuids()
        {
            string path = TestAssets.GameScenePath;
            Assert.IsTrue(System.IO.File.Exists(path), $"{path} not found on disk.");

            var lines = System.IO.File.ReadAllLines(path);
            int start = System.Array.FindIndex(lines, l => l.TrimStart().StartsWith("_botLoadouts:"));
            Assert.GreaterOrEqual(start, 0,
                "MatchBootstrapper._botLoadouts is not serialized in Game.unity. Either the field was " +
                "renamed/removed or the MatchBootstrapper GameObject is gone — check every inspector slot.");

            var result = new List<(string, string, string, string)>();
            string currentName = null;
            string slot0 = null, slot1 = null, slot2 = null;

            void FlushCurrent()
            {
                if (currentName != null)
                    result.Add((currentName, slot0 ?? "", slot1 ?? "", slot2 ?? ""));
            }

            static string ExtractGuid(string trimmed)
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                    @"fileID:\s*(\d+),\s*guid:\s*([0-9a-f]+)");
                return match.Success && match.Groups[1].Value != "0" ? match.Groups[2].Value : null;
            }

            for (int i = start + 1; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();

                if (trimmed.StartsWith("- Name:"))
                {
                    FlushCurrent();
                    currentName = trimmed.Substring("- Name:".Length).Trim();
                    slot0 = slot1 = slot2 = null;
                    continue;
                }

                if (trimmed.StartsWith("Slot0:")) { Assert.IsNotNull(currentName, $"Found a Slot0 line before any preset Name in {path}."); slot0 = ExtractGuid(trimmed); continue; }
                if (trimmed.StartsWith("Slot1:")) { Assert.IsNotNull(currentName, $"Found a Slot1 line before any preset Name in {path}."); slot1 = ExtractGuid(trimmed); continue; }
                if (trimmed.StartsWith("Slot2:")) { Assert.IsNotNull(currentName, $"Found a Slot2 line before any preset Name in {path}."); slot2 = ExtractGuid(trimmed); continue; }

                // AllowedClasses is part of the same preset entry and skipped on purpose;
                // anything else — the next top-level MonoBehaviour field, or a new scene
                // object's "--- !u!" header — ends the block.
                if (trimmed.StartsWith("AllowedClasses:")) continue;

                break;
            }
            FlushCurrent();

            Assert.IsNotEmpty(result,
                $"Parsed zero bot loadout presets out of {path} — the parser or the scene data is stale.");
            return result;
        }

        [Test]
        public void BotLoadoutPresets_EveryAssignedSlot_IsAGenuineClassLegalAbility()
        {
            // 2026-07-27 design directive: the Common slot is no longer mandatory (GDD
            // §7.1-7.2 updated accordingly), so this no longer pins Slot0 to
            // SlotKind.Common — MatchBootstrapper.ResolveLegalLoadout accepts any mix of
            // Common and class-legal Character abilities now. What still must hold: every
            // assigned slot on every scene-authored preset resolves to a real
            // AbilityBaseSO asset, abilities within a preset are distinct, and each is
            // legal for at least one of its own AllowedClasses (or legal for everyone, if
            // AllowedClasses is empty). A preset that fails this would round-trip through
            // ResolveLegalLoadout's sanitiser and get silently replaced — the bot would
            // spawn with a different loadout than the one authored in the Inspector.
            var presets = ParseBotLoadoutSlotGuids();

            foreach (var (name, s0, s1, s2) in presets)
            {
                var abilities = new List<CluckWars.Abilities.AbilityBaseSO>();

                foreach (var (slotLabel, guid) in new[] { ("Slot0", s0), ("Slot1", s1), ("Slot2", s2) })
                {
                    if (string.IsNullOrEmpty(guid)) continue; // unassigned slot is legal

                    string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    Assert.IsFalse(string.IsNullOrEmpty(assetPath),
                        $"Bot loadout preset '{name}' {slotLabel} references guid {guid}, which AssetDatabase " +
                        "cannot resolve to any asset — it was deleted, or the guid in Game.unity is stale.");

                    var ability = UnityEditor.AssetDatabase.LoadAssetAtPath<CluckWars.Abilities.AbilityBaseSO>(assetPath);
                    Assert.IsNotNull(ability,
                        $"Bot loadout preset '{name}' {slotLabel} ({assetPath}) is not an AbilityBaseSO.");

                    bool legalForSomeone = ability.AllowedClasses == CluckWars.Abilities.ChickenClassFlags.None
                        ? false // an ability legal for nobody is an authoring bug, not "legal for all"
                        : true;
                    Assert.IsTrue(legalForSomeone,
                        $"Bot loadout preset '{name}' {slotLabel} is '{ability.name}' with AllowedClasses=None " +
                        "— it is legal for no class and can never be equipped by anyone.");

                    Assert.IsFalse(abilities.Contains(ability),
                        $"Bot loadout preset '{name}' equips '{ability.name}' twice ({slotLabel} duplicates an " +
                        "earlier slot) — ResolveLegalLoadout drops the duplicate and backfills something else, " +
                        "so the bot won't actually spawn with what's authored in the Inspector.");
                    abilities.Add(ability);
                }
            }
        }

        /// <summary>
        /// The baked map draws a "shadow" under each base at
        /// <c>MapGenerator._baseZoneRadius</c>, but the zone chickens actually deposit in
        /// is <c>PlayerBase._depositRadius</c> on the prefab. They are authored in two
        /// different places, so nothing but this test stops them drifting — and a shadow
        /// that lies about where you can deposit is worse than no shadow at all.
        /// </summary>
        [Test]
        public void BaseZoneShadowRadius_MatchesPlayerBaseDepositRadius()
        {
            var basePrefab = TestAssets.Load<GameObject>(TestAssets.PlayerBasePrefabPath);
            Assert.IsNotNull(basePrefab, $"Missing {TestAssets.PlayerBasePrefabPath}.");

            var playerBase = basePrefab.GetComponent<PlayerBase>();
            Assert.IsNotNull(playerBase, "PlayerBase component missing from the prefab.");
            float depositRadius = TestAssets.PrivateField<float>(playerBase, "_depositRadius");

            var mapGenPrefab = new GameObject("TempMapGen");
            try
            {
                var generator = mapGenPrefab.AddComponent<MapGenerator>();
                float shadowRadius = generator.BaseZoneRadius;

                Assert.AreEqual(depositRadius, shadowRadius, 0.001f,
                    $"MapGenerator._baseZoneRadius ({shadowRadius}) disagrees with " +
                    $"PlayerBase._depositRadius ({depositRadius}). The baked base shadow would " +
                    "show a deposit zone that is the wrong size. Update whichever is stale and " +
                    "re-run Cluck Wars/Map/Bake Map Scene.");
            }
            finally
            {
                Object.DestroyImmediate(mapGenPrefab);
            }
        }

        // ---- Skeletal animation wiring ----------------------------------------
        // The clips are bound at spawn through a runtime AnimatorOverrideController built in
        // ChickenController.BindSkeletalAnimation. Nearly every way that wiring can be wrong is
        // SILENT — an unresolved override key or a cross-class clip binds to nothing and the
        // chicken simply stands in bind pose. These tests are the tripwires.

        private const string ControllerPath  = "Assets/_Game/Art/Animations/Chicken.controller";
        private const string PlaceholderDir  = "Assets/_Game/Art/Animations/Placeholders";

        /// <summary>The five state names, which are ALSO the override keys used in code.</summary>
        private static readonly string[] StateNames = { "Idle", "Walk", "Cast", "Hit", "Stunned" };

        /// <summary>Idle/Walk/Stunned are held; Cast/Hit are one-shot reactions.</summary>
        private static readonly Dictionary<string, bool> ShouldLoop = new()
        {
            { "Idle", true }, { "Walk", true }, { "Stunned", true },
            { "Cast", false }, { "Hit", false },
        };

        private static (string State, AnimationClip Clip)[] ClipsOf(ChickenClassRegistrySO.Entry e) =>
            new[]
            {
                ("Idle", e.Clips.Idle), ("Walk", e.Clips.Walk), ("Cast", e.Clips.Cast),
                ("Hit", e.Clips.Hit),   ("Stunned", e.Clips.Stunned),
            };

        [Test]
        public void ClassRegistry_EveryEntry_HasFiveClipsFromItsOwnFbx()
        {
            // Cross-class drift is the specific failure this guards. A clip from another class's
            // .fbx references bone paths that do not exist on this skeleton, so it binds to
            // nothing: the chicken freezes rather than erroring. Same-file identity is the only
            // cheap check that catches it, and it is exactly the shape of the bot-loadout
            // staleness bug this project already shipped once.
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");
                Assert.IsNotNull(entry.ModelPrefab, $"{cls} has no ModelPrefab.");

                string fbxPath = AssetDatabase.GetAssetPath(entry.ModelPrefab);

                foreach (var (state, clip) in ClipsOf(entry))
                {
                    Assert.IsNotNull(clip,
                        $"{cls}'s registry entry has no '{state}' AnimationClip. All five are " +
                        "required — ChickenController treats a partial set as no set and falls back " +
                        "to procedural motion. Run 'Cluck Wars/Animation/Wire Skeletal Clips'.");

                    Assert.AreEqual(fbxPath, AssetDatabase.GetAssetPath(clip),
                        $"{cls}'s '{state}' clip ('{clip.name}') comes from " +
                        $"'{AssetDatabase.GetAssetPath(clip)}' but its ModelPrefab comes from " +
                        $"'{fbxPath}'. A clip from another class's rig binds to no bones and the " +
                        "chicken silently freezes. Re-run 'Cluck Wars/Animation/Wire Skeletal Clips'.");
                }
            }
        }

        [Test]
        public void ClassClips_HaveTheExpectedLoopFlags()
        {
            // The override replaces the placeholder outright, so it is the CLASS clip's loop flag
            // that governs at runtime. A non-looping Idle plays once and leaves the chicken frozen
            // for the rest of the match; a looping Hit never releases the state.
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");
                foreach (var (state, clip) in ClipsOf(entry))
                {
                    if (clip == null) continue; // reported by the completeness test above
                    Assert.AreEqual(ShouldLoop[state], clip.isLooping,
                        $"{cls}'s '{state}' clip ('{clip.name}') has isLooping={clip.isLooping}, " +
                        $"expected {ShouldLoop[state]}. Fix it on the .fbx importer's Animation tab " +
                        "(Loop Time), not in code.");
                }
            }
        }

        [Test]
        public void ClassClips_DoNotKeyTheModelRootTransform()
        {
            // ChickenAnimator writes the model root's own local TRS every LateUpdate; the Animator
            // evaluates before LateUpdate. If a clip also keyed the model root (binding path ""),
            // ChickenAnimator would silently overwrite that curve every frame — the clip would
            // "mostly work" with something subtly wrong. Verified 2026-08-09: every curve targets
            // the armature child ('*_chicken_rig') or a bone below it, so the two never collide.
            // This test keeps that true across future re-exports.
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);

            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.IsTrue(reg.TryGet(cls, out var entry), $"No entry for {cls}.");
                foreach (var (state, clip) in ClipsOf(entry))
                {
                    if (clip == null) continue;
                    var rootBound = AnimationUtility.GetCurveBindings(clip)
                        .Where(b => string.IsNullOrEmpty(b.path))
                        .Select(b => b.propertyName)
                        .Distinct()
                        .ToList();

                    Assert.IsEmpty(rootBound,
                        $"{cls}'s '{state}' clip keys the model ROOT transform " +
                        $"({string.Join(", ", rootBound)}). ChickenAnimator also writes that " +
                        "transform in LateUpdate and would silently overwrite the clip every frame. " +
                        "Either re-export with the motion on the armature child, or interpose an " +
                        "animation-neutral pivot for ChickenAnimator to write instead.");
                }
            }
        }

        [Test]
        public void ChickenController_EveryState_HasAMotionMatchingItsOverrideKey()
        {
            // AnimatorOverrideController keys by the ORIGINAL clip sitting in each Motion slot.
            // A null slot means there is nothing to key against and the override silently resolves
            // to nothing; a renamed placeholder means the same. Both produce a bind-pose chicken.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, $"No AnimatorController at '{ControllerPath}'.");

            var motionByState = controller.layers
                .SelectMany(l => l.stateMachine.states)
                .ToDictionary(s => s.state.name, s => s.state.motion);

            foreach (var state in StateNames)
            {
                Assert.IsTrue(motionByState.ContainsKey(state),
                    $"Chicken.controller has no state named '{state}'.");

                var motion = motionByState[state];
                Assert.IsNotNull(motion,
                    $"Chicken.controller's '{state}' state has a null Motion. The override " +
                    "controller has no key to bind the per-class clip to, so the class clip is " +
                    "silently ignored. Run 'Cluck Wars/Animation/Wire Skeletal Clips'.");

                Assert.AreEqual(state, motion.name,
                    $"Chicken.controller's '{state}' state holds a motion named '{motion.name}'. " +
                    "ChickenController keys its overrides by these names, so they must match the " +
                    "state names exactly.");
            }
        }

        [Test]
        public void PlaceholderClips_ExistAndCarryTheExpectedLoopFlags()
        {
            // The placeholders are both the override keys and the graceful fallback: an empty clip
            // evaluates to the bind pose, so an un-overridden state is a still chicken, not an
            // exception. They must stay empty — a placeholder with curves would leak into any
            // class whose clip failed to bind.
            foreach (var state in StateNames)
            {
                string path = $"{PlaceholderDir}/{state}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                Assert.IsNotNull(clip,
                    $"No placeholder clip at '{path}'. Run 'Cluck Wars/Animation/Wire Skeletal Clips'.");

                Assert.AreEqual(ShouldLoop[state], clip.isLooping,
                    $"Placeholder '{state}' has isLooping={clip.isLooping}, expected {ShouldLoop[state]}.");

                Assert.IsEmpty(AnimationUtility.GetCurveBindings(clip),
                    $"Placeholder '{state}' has animation curves. Placeholders must stay empty so " +
                    "that a state whose class clip failed to bind rests in bind pose.");
            }
        }

        [Test]
        public void ChickenController_Parameters_MatchTheNamesAndTypesHashedInCode()
        {
            // ChickenAnimator caches these as Animator.StringToHash constants. A rename on the
            // controller does not break the build — Unity just logs a per-call warning and the
            // parameter never moves. Pin both ends of the contract here.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, $"No AnimatorController at '{ControllerPath}'.");

            var expected = new Dictionary<string, AnimatorControllerParameterType>
            {
                { "Speed",       AnimatorControllerParameterType.Float   },
                { "AbilityCast", AnimatorControllerParameterType.Trigger },
                { "Hit",         AnimatorControllerParameterType.Trigger },
                { "Stunned",     AnimatorControllerParameterType.Bool    },
            };

            var actual = controller.parameters.ToDictionary(p => p.name, p => p.type);

            foreach (var (name, type) in expected)
            {
                Assert.IsTrue(actual.ContainsKey(name),
                    $"Chicken.controller has no '{name}' parameter, but ChickenAnimator hashes and " +
                    $"writes it every frame. Present: [{string.Join(", ", actual.Keys)}].");
                Assert.AreEqual(type, actual[name],
                    $"Chicken.controller's '{name}' is a {actual[name]}, but ChickenAnimator drives " +
                    $"it as a {type}.");
            }
        }
    }
}
