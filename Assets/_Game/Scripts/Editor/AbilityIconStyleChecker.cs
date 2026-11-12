using UnityEditor;
using UnityEngine;
using CluckWars.Gameplay;
using CluckWars.Logging;

namespace CluckWars.UI.Editor
{
    public static class AbilityIconStyleChecker
    {
        [InitializeOnLoadMethod]
        private static void CheckAbilityIcons()
        {
            var guids = AssetDatabase.FindAssets("t:AbilityRegistrySO");
            if (guids.Length == 0) return;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var registry = AssetDatabase.LoadAssetAtPath<AbilityRegistrySO>(path);
            if (registry == null || registry.All == null) return;

            int missingCount = 0;
            var logger = new UnityLogService(LogLevel.Warn);

            foreach (var ability in registry.All)
            {
                if (ability == null) continue;
                string cls = AbilityIconStyle.ClassFor(ability);
                if (string.IsNullOrEmpty(cls))
                {
                    logger.Error("AbilityIconStyle", $"Missing icon mapping for ability '{ability.name}' (type {ability.GetType().Name}) in AbilityIconStyle.cs. It will silently fall back to no icon.");
                    missingCount++;
                }
            }

            if (missingCount > 0)
            {
                logger.Error("AbilityIconStyle", $"Found {missingCount} abilities without an icon mapping. See above errors.");
            }
        }
    }
}
