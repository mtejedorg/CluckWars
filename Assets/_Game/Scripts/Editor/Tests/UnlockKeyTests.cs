using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CluckWars.Abilities;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using CluckWars.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Guards the stable unlock keys progression saves against: every ability has one, none
    /// collide, and none drift from the committed manifest.
    /// </summary>
    /// <remarks>
    /// <b>A key is a promise to a player.</b> Once a build ships, a player's save holds
    /// <c>"ability.mark_kill"</c>; if that asset's key ever changes, their unlock silently points
    /// at nothing. Nothing at runtime can notice — the lookup just misses — so this fixture is
    /// the only place the break is visible.
    /// <para>
    /// <b>The manifest is hand-edited, never regenerated.</b> A manifest rebuilt from the
    /// current assets would agree with any drift by construction. The menu tool deliberately
    /// does not write it.
    /// </para>
    /// </remarks>
    public sealed class UnlockKeyTests
    {
        private static readonly Regex AbilityKeyShape = new Regex(@"^(ability|passive)\.[a-z0-9]+(_[a-z0-9]+)*$");

        [Serializable]
        private sealed class Manifest
        {
            public ManifestEntry[] Entries;
        }

        [Serializable]
        private sealed class ManifestEntry
        {
            public string Key;
            public string Path;
        }

        /// <summary>Every AbilityBaseSO in the project with its asset path, ordered by path.</summary>
        private static List<(AbilityBaseSO asset, string path)> AllAbilities()
        {
            var abilities = TestAssets.LoadAllIn<AbilityBaseSO>("Assets")
                .Select(a => (asset: a, path: AssetDatabase.GetAssetPath(a)))
                .OrderBy(t => t.path, StringComparer.Ordinal)
                .ToList();
            Assert.IsNotEmpty(abilities,
                "Found no AbilityBaseSO assets under Assets/ at all. The search is broken (type renamed? " +
                "assets moved outside Assets/?) — nothing below means anything until it finds them again.");
            return abilities;
        }

        private static Manifest LoadManifest()
        {
            string path = TestAssets.UnlockKeyManifestPath;
            Assert.IsTrue(File.Exists(path),
                $"Unlock key manifest not found at '{path}'. It is the committed record of every key players " +
                "can hold; restore it from git. Do NOT regenerate it from the current assets — that would make " +
                "this test agree with whatever drift it exists to catch.");

            string json = File.ReadAllText(path);
            var manifest = JsonUtility.FromJson<Manifest>(json);
            Assert.IsNotNull(manifest?.Entries,
                $"'{path}' did not parse as {{\"Entries\":[{{\"Key\":…,\"Path\":…}}, …]}}. Fix the JSON by hand.");
            Assert.IsNotEmpty(manifest.Entries, $"'{path}' has no entries. Restore it from git.");
            return manifest;
        }

        // ------------------------------------------------------------------ keys on assets

        [Test]
        public void EveryAbilityAsset_HasANonEmptyWellFormedKey()
        {
            var offenders = new List<string>();
            foreach (var (asset, path) in AllAbilities())
            {
                string key = asset.UnlockKey;
                bool isPassive = asset is PassiveAbilitySO;
                string prefix = isPassive ? "passive." : "ability.";

                if (string.IsNullOrEmpty(key))
                    offenders.Add($"{path}: has no key");
                else if (!AbilityKeyShape.IsMatch(key))
                    offenders.Add($"{path}: '{key}' is not ability./passive. + lower_snake_case");
                else if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    offenders.Add($"{path}: '{key}' must start with '{prefix}' because it " +
                                  (isPassive ? "IS" : "is NOT") + " a PassiveAbilitySO");
            }

            Assert.IsEmpty(offenders,
                "Every ability asset needs a stable, well-formed unlock key (progression saves unlocks against it).\n" +
                $"  - Missing key: run '{UnlockKeyAssigner.MenuPath}'. It only fills empty keys, never overwrites.\n" +
                "  - Malformed or wrong-prefix key: if the asset is NOT yet in UnlockKeyManifest.json, clear the key " +
                "and re-run the menu. If it IS in the manifest, players may already hold that key — changing it costs " +
                "them their unlock, so talk to the progression owner first.\n" +
                "Offenders:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void UnlockKeys_AreUnique()
        {
            var ownerByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new List<string>();

            void Claim(string key, string owner)
            {
                if (ownerByKey.TryGetValue(key, out string first))
                    duplicates.Add($"'{key}' is on both {first} and {owner}");
                else
                    ownerByKey[key] = owner;
            }

            foreach (var (asset, path) in AllAbilities())
            {
                if (string.IsNullOrEmpty(asset.UnlockKey)) continue; // reported by EveryAbilityAsset_HasANonEmptyWellFormedKey
                Claim(asset.UnlockKey, path);
            }
            foreach (ChickenClass role in Enum.GetValues(typeof(ChickenClass)))
                Claim(UnlockKeyTable.RoleKey(role), $"UnlockKeyTable.RoleKey({role})");

            Assert.IsEmpty(duplicates,
                "Two owners share an unlock key, so an unlock of one would silently unlock (or overwrite) the other.\n" +
                "The usual cause is duplicating an asset with Ctrl+D, which copies the key along with everything else. " +
                $"Fix: clear the key on the COPY (the path not listed under that key in UnlockKeyManifest.json), then run " +
                $"'{UnlockKeyAssigner.MenuPath}' to give it its own.\n" +
                "Duplicates:\n  " + string.Join("\n  ", duplicates));
        }

        [Test]
        public void UnlockKeys_MatchTheCommittedManifest()
        {
            var manifest = LoadManifest();

            // The manifest itself must be a clean key <-> path bijection.
            var manifestProblems = new List<string>();
            var pathByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            var keyByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in manifest.Entries)
            {
                if (string.IsNullOrEmpty(entry?.Key) || string.IsNullOrEmpty(entry.Path))
                {
                    manifestProblems.Add($"an entry has an empty Key or Path ({entry?.Key ?? "null"} → {entry?.Path ?? "null"})");
                    continue;
                }
                if (!pathByKey.TryAdd(entry.Key, entry.Path))
                    manifestProblems.Add($"key '{entry.Key}' is listed twice ({pathByKey[entry.Key]} and {entry.Path})");
                if (!keyByPath.TryAdd(entry.Path, entry.Key))
                    manifestProblems.Add($"path '{entry.Path}' is listed twice ({keyByPath[entry.Path]} and {entry.Key})");
            }
            Assert.IsEmpty(manifestProblems,
                $"{TestAssets.UnlockKeyManifestPath} is inconsistent with itself. Each key and each path must appear " +
                "exactly once. Fix it by hand against git history — never regenerate it.\n  " +
                string.Join("\n  ", manifestProblems));

            var abilities = AllAbilities();
            var assetKeyByPath = abilities.ToDictionary(t => t.path, t => t.asset.UnlockKey ?? "", StringComparer.Ordinal);
            var problems = new List<string>();

            // Direction 1: every asset's (key, path) pair is in the manifest.
            foreach (var (asset, path) in abilities)
            {
                string key = asset.UnlockKey;
                if (keyByPath.TryGetValue(path, out string manifestKey))
                {
                    if (string.IsNullOrEmpty(key))
                        problems.Add($"KEY CLEARED on {path}: the manifest says it is '{manifestKey}'. Restore exactly that " +
                                     $"string by hand — do NOT run '{UnlockKeyAssigner.MenuPath}', which would derive from the current name.");
                    else if (key != manifestKey)
                        problems.Add($"KEY CHANGED on {path}: it is '{key}' but players hold '{manifestKey}'. Revert the " +
                                     "key to the manifest value. A changed key costs every player their unlock.");
                }
                else if (!string.IsNullOrEmpty(key) && pathByKey.TryGetValue(key, out string oldPath))
                {
                    problems.Add($"PATH CHANGED (rename or move) for '{key}': the manifest says {oldPath}, the asset is " +
                                 $"now at {path}. Update the Path in the manifest and NEVER the key.");
                }
                else
                {
                    problems.Add($"NEW ASSET not in the manifest: {path} ('{key}'). Add " +
                                 $"{{\"Key\":\"{key}\",\"Path\":\"{path}\"}} to the manifest, sorted by Key." +
                                 (string.IsNullOrEmpty(key) ? $" It has no key yet: run '{UnlockKeyAssigner.MenuPath}' first." : ""));
                }
            }

            // Direction 2: every manifest entry resolves to an asset at that path carrying that key.
            // Mismatches at an existing path were reported above; here we catch entries left dangling.
            foreach (var entry in manifest.Entries.Where(e => !string.IsNullOrEmpty(e?.Key) && !string.IsNullOrEmpty(e.Path)))
            {
                if (assetKeyByPath.ContainsKey(entry.Path)) continue;
                bool movedElsewhere = abilities.Any(t => t.asset.UnlockKey == entry.Key);
                if (movedElsewhere) continue; // reported as PATH CHANGED above

                problems.Add($"MISSING ASSET for '{entry.Key}': nothing at {entry.Path} and no asset carries that key. " +
                             "If the asset was renamed or moved AND its key was cleared, restore the key by hand and " +
                             "update the Path. If it was deliberately deleted, remove the entry knowingly — players " +
                             "who unlocked it keep a dangling key.");
            }

            Assert.IsEmpty(problems,
                $"Ability unlock keys have drifted from {TestAssets.UnlockKeyManifestPath}.\n" +
                "The manifest records the keys players can hold. Paths may change; keys may not.\n  " +
                string.Join("\n  ", problems));
        }

        // ------------------------------------------------------------------ role keys

        [Test]
        public void RoleKeys_ArePinnedAndCoverEveryRole()
        {
            foreach (ChickenClass role in Enum.GetValues(typeof(ChickenClass)))
            {
                Assert.DoesNotThrow(() => UnlockKeyTable.RoleKey(role),
                    $"ChickenClass.{role} has no row in UnlockKeyTable.RoleKey. Add one with a hand-written key " +
                    "(\"class.<name>\") — never derive it from the enum name, or a rename changes a saved key.");
            }

            // Literal on purpose: these strings are in players' saves. If one of these asserts
            // fails, the table changed — revert it, don't update the test.
            Assert.AreEqual("class.warrior",  UnlockKeyTable.RoleKey(ChickenClass.Warrior),  "Saved role key changed — revert UnlockKeyTable.");
            Assert.AreEqual("class.speedy",   UnlockKeyTable.RoleKey(ChickenClass.Speedy),   "Saved role key changed — revert UnlockKeyTable.");
            Assert.AreEqual("class.fatty",    UnlockKeyTable.RoleKey(ChickenClass.Fatty),    "Saved role key changed — revert UnlockKeyTable.");
            Assert.AreEqual("class.assassin", UnlockKeyTable.RoleKey(ChickenClass.Assassin), "Saved role key changed — revert UnlockKeyTable.");

            Assert.Throws<ArgumentOutOfRangeException>(() => UnlockKeyTable.RoleKey((ChickenClass)250),
                "An unmapped role must throw. Falling back to a derived or empty key would save something no " +
                "future build can promise to honour.");
        }

        // ------------------------------------------------------------------ derivation

        [Test]
        public void DeriveUnlockKey_ProducesStableSnakeCase()
        {
            var vectors = new (string name, bool isPassive, string expected)[]
            {
                ("Headbutt",     false, "ability.headbutt"),
                ("MarkKill",     false, "ability.mark_kill"),
                ("SneakySteal",  false, "ability.sneaky_steal"),
                ("CluckShock",   false, "ability.cluck_shock"),
                ("Feather Trap", false, "ability.feather_trap"),
                ("Bully",        true,  "passive.bully"),
                ("",             false, null),
                (null,           false, null),
            };

            var mismatches = vectors
                .Select(v => (v, actual: AbilityBaseSO.DeriveUnlockKey(v.name, v.isPassive)))
                .Where(r => r.actual != r.v.expected)
                .Select(r => $"DeriveUnlockKey({(r.v.name == null ? "null" : $"\"{r.v.name}\"")}, {r.v.isPassive}) = " +
                             $"{r.actual ?? "null"}, expected {r.v.expected ?? "null"}")
                .ToList();

            Assert.IsEmpty(mismatches,
                "Key derivation changed. It only ever runs for assets that have no key yet, so existing keys are safe — " +
                "but a new asset would now get a different key than the same name got before. Restore the old " +
                "behaviour unless the change is deliberate.\n  " + string.Join("\n  ", mismatches));
        }

        [Test]
        public void AssignUnlockKeyIfMissing_NeverOverwrites_SoARenameKeepsTheKey()
        {
            AssertFillOnce<HeadbuttAbilitySO>("Headbutt", "ability.headbutt");
            AssertFillOnce<BullyPassiveSO>("Bully", "passive.bully");
        }

        private static void AssertFillOnce<T>(string assetName, string expectedKey) where T : AbilityBaseSO
        {
            var ability = ScriptableObject.CreateInstance<T>();
            try
            {
                ability.name = assetName;
                Assert.IsTrue(ability.AssignUnlockKeyIfMissing(),
                    $"A fresh {typeof(T).Name} named '{assetName}' with no key should have been assigned one.");
                Assert.AreEqual(expectedKey, ability.UnlockKey, $"{typeof(T).Name} '{assetName}' derived the wrong key.");

                ability.name = "SomethingElse";
                Assert.IsFalse(ability.AssignUnlockKeyIfMissing(),
                    $"AssignUnlockKeyIfMissing reassigned a key after a rename. It must be fill-once: a renamed asset " +
                    "keeps its key, or every player who unlocked it loses the unlock.");
                Assert.AreEqual(expectedKey, ability.UnlockKey,
                    $"Renaming {typeof(T).Name} changed its unlock key. Keys must survive renames.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ability);
            }
        }
    }
}
