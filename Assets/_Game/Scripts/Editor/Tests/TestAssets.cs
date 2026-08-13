using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Shared asset-loading helpers for the EditMode suite. Every category that
    /// asserts against the REAL shipped assets goes through here, so a moved or
    /// renamed asset produces one clear "not found at path" failure instead of a
    /// scatter of NullReferenceExceptions.
    /// </summary>
    /// <remarks>
    /// Paths are hard-coded on purpose. <c>docs/CONVENTIONS.md</c> pins SO assets to
    /// <c>Assets/_Game/Data/</c> and prefabs to <c>Assets/_Game/Prefabs/</c>; a test
    /// that globbed for "wherever the registry happens to live now" would silently
    /// keep passing after someone moved it out from under Zenject's inspector slots.
    /// </remarks>
    internal static class TestAssets
    {
        public const string DataRoot     = "Assets/_Game/Data";
        public const string AbilitiesDir = "Assets/_Game/Data/Abilities";
        public const string ClassesDir   = "Assets/_Game/Data/Classes";
        public const string PrefabsDir   = "Assets/_Game/Prefabs";

        public const string AbilityRegistryPath      = DataRoot + "/AbilityRegistry.asset";
        public const string ChickenClassRegistryPath = DataRoot + "/ChickenClassRegistry.asset";
        public const string MatchConfigPath          = DataRoot + "/MatchConfig.asset";
        public const string ColorSchemePath          = DataRoot + "/ColorScheme.asset";
        public const string PrefabRegistryPath       = DataRoot + "/PrefabRegistry.asset";
        public const string AudioRegistryPath        = DataRoot + "/AudioRegistry.asset";

        public const string ChickenPrefabPath      = PrefabsDir + "/Chicken.prefab";
        public const string DoppelgangerPrefabPath = PrefabsDir + "/Doppelganger.prefab";
        public const string FoodPilePrefabPath     = PrefabsDir + "/FoodPile.prefab";
        public const string FoodPickupPrefabPath   = PrefabsDir + "/FoodPickup.prefab";
        public const string PlayerBasePrefabPath   = PrefabsDir + "/PlayerBase.prefab";

        public const string BootstrapScenePath = "Assets/_Game/Scenes/Bootstrap.unity";
        public const string GameScenePath      = "Assets/_Game/Scenes/Game.unity";

        /// <summary>
        /// Every stylesheet that paints ability icons. A <c>.cw-hex-icon--*</c> rule must
        /// exist in <b>all</b> of them: the touch HUD and the menu front-end load different
        /// sheets, so a rule present in only one paints an icon on one screen and a blank
        /// hex on the other.
        /// </summary>
        public static readonly string[] AbilityIconStylesheets =
        {
            "Assets/UI/Styles/TouchControls.uss",
            "Assets/UI/Styles/CluckWarsTheme.uss",
        };

        /// <summary>Load a required asset, failing the test (not throwing an NRE) when it is missing.</summary>
        public static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset,
                $"Required {typeof(T).Name} asset not found at '{path}'. " +
                "It is referenced by an inspector slot (ProjectInstaller / a prefab), so a move " +
                "or rename breaks the game at runtime, not just this test.");
            return asset;
        }

        /// <summary>Every asset of type <typeparamref name="T"/> under <paramref name="folder"/>.</summary>
        public static List<T> LoadAllIn<T>(string folder) where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .ToList();
        }

        /// <summary>
        /// Reads a serialized scalar field off the <see cref="MapGenerator"/> in
        /// <c>Game.unity</c>, straight from the scene YAML.
        /// </summary>
        /// <remarks>
        /// The component lives in a scene, not on a prefab, so there is no asset to load —
        /// and opening the scene from an EditMode test would disturb whatever the developer
        /// has open. Reading the YAML also catches <b>scene-level overrides</b>, which is the
        /// point: MapGenerator's C# field initializers are routinely overridden in
        /// <c>Game.unity</c>, so a test that read the code defaults would be asserting against
        /// values the game never uses. (Verified 2026-08-13: the pile footprints were
        /// overridden in-scene, so editing the C# defaults alone changed nothing at runtime.)
        /// </remarks>
        public static float SceneFloat(string fieldName)
        {
            string path = GameScenePath;
            Assert.IsTrue(System.IO.File.Exists(path), $"{path} not found on disk.");

            string key = fieldName + ":";
            foreach (var line in System.IO.File.ReadLines(path))
            {
                int idx = line.IndexOf(key, System.StringComparison.Ordinal);
                if (idx < 0) continue;

                var raw = line.Substring(idx + key.Length).Trim();
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                if (float.TryParse(raw, System.Globalization.NumberStyles.Float, culture, out var value))
                    return value;
            }

            Assert.Fail($"MapGenerator.{fieldName} is not serialized in {path}. Either the MapGenerator was " +
                        "removed from the scene or the field was renamed — check every inspector slot too.");
            return 0f;
        }

        /// <summary>Same approach as <see cref="SceneFloat"/>, for a serialized <c>Vector2</c>.</summary>
        public static Vector2 SceneVector2(string fieldName)
        {
            string path = GameScenePath;
            Assert.IsTrue(System.IO.File.Exists(path), $"{path} not found on disk.");

            string key = fieldName + ":";
            foreach (var line in System.IO.File.ReadLines(path))
            {
                int idx = line.IndexOf(key, System.StringComparison.Ordinal);
                if (idx < 0) continue;

                var raw = line.Substring(idx + key.Length).Trim();          // "{x: 7, y: 4}"
                var match = System.Text.RegularExpressions.Regex.Match(raw,
                    @"x:\s*(-?[\d.eE+]+),\s*y:\s*(-?[\d.eE+]+)");
                Assert.IsTrue(match.Success, $"Could not parse '{fieldName}: {raw}' in {path} as a Vector2.");

                var culture = System.Globalization.CultureInfo.InvariantCulture;
                return new Vector2(
                    float.Parse(match.Groups[1].Value, culture),
                    float.Parse(match.Groups[2].Value, culture));
            }

            Assert.Fail($"MapGenerator.{fieldName} is not serialized in {path}. Either the MapGenerator was " +
                        "removed from the scene or the field was renamed — check every inspector slot too.");
            return Vector2.zero;
        }

        /// <summary>
        /// Reads a private serialized field off a live component instance. Used to
        /// assert against tunables that have no public accessor (FoodPile's blocker
        /// radius, MapGenerator's pile amounts). Reflection sees the value Unity
        /// actually deserialized, which is what ships — unlike parsing the YAML,
        /// which misses fields that fall back to their C# initializer.
        /// </summary>
        public static T PrivateField<T>(object instance, string fieldName)
        {
            var f = instance.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(f,
                $"{instance.GetType().Name} has no private field '{fieldName}'. " +
                "It was renamed or removed — update the test AND check every inspector slot that referenced it.");
            return (T)f.GetValue(instance);
        }
    }
}
