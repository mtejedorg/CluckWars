using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
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
    }
}
