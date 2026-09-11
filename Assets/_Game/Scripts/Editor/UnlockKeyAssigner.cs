using System.Collections.Generic;
using CluckWars.Abilities;
using CluckWars.Logging;
using UnityEditor;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Fills <see cref="AbilityBaseSO.UnlockKey"/> on every ability asset that has none, then
    /// reports duplicate keys. Safe to re-run at any time: it never overwrites an existing key.
    /// </summary>
    /// <remarks>
    /// <b>It never writes <c>Editor/Tests/UnlockKeyManifest.json</c>.</b> The manifest is a
    /// hand-curated record of the keys players already hold. Regenerating it from the current
    /// assets would make <c>UnlockKeyTests.UnlockKeys_MatchTheCommittedManifest</c> agree with
    /// whatever the assets say — tautological, and blind to exactly the drift it exists to catch.
    /// </remarks>
    public static class UnlockKeyAssigner
    {
        public const string MenuPath = "Cluck Wars/Progression/Assign Missing Unlock Keys";
        private const string Source = "UnlockKeys";

        [MenuItem(MenuPath)]
        private static void AssignMissingFromMenu() => AssignMissing();

        /// <summary>Assigns every missing key, saves, and returns how many were assigned.</summary>
        public static int AssignMissing()
        {
            // Editor tools have no injection; a local ILogService keeps us inside the ILogService-only rule.
            ILogService log = new UnityLogService(LogLevel.Info);

            var abilities = new List<(AbilityBaseSO asset, string path)>();
            foreach (string guid in AssetDatabase.FindAssets("t:AbilityBaseSO", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>(path);
                if (asset != null) abilities.Add((asset, path));
            }

            int assigned = 0;
            foreach (var (asset, path) in abilities)
            {
                if (asset.AssignUnlockKeyIfMissing())
                {
                    EditorUtility.SetDirty(asset);
                    assigned++;
                    log.Info(Source, $"{path} → {asset.UnlockKey}");
                }
                else if (string.IsNullOrEmpty(asset.UnlockKey))
                {
                    log.Error(Source,
                        $"{path}: the asset name '{asset.name}' derives no usable key, so it still has none " +
                        "and cannot be unlocked or saved. Rename it to a word-shaped name (letters/digits) and " +
                        $"re-run {MenuPath}.");
                }
            }
            AssetDatabase.SaveAssets();

            var firstPathByKey = new Dictionary<string, string>(System.StringComparer.Ordinal);
            int duplicates = 0;
            foreach (var (asset, path) in abilities)
            {
                string key = asset.UnlockKey;
                if (string.IsNullOrEmpty(key)) continue;

                if (firstPathByKey.TryGetValue(key, out string firstPath))
                {
                    duplicates++;
                    log.Error(Source,
                        $"Duplicate unlock key '{key}' on {firstPath} and {path}. Duplicating an asset (Ctrl+D) " +
                        "copies its key. Clear the key on the copy (the one NOT listed under this key in " +
                        $"Editor/Tests/UnlockKeyManifest.json), then re-run {MenuPath}.");
                }
                else
                {
                    firstPathByKey[key] = path;
                }
            }

            log.Info(Source,
                $"Assigned {assigned} unlock key(s) across {abilities.Count} ability asset(s); {duplicates} duplicate(s).");
            return assigned;
        }
    }
}
