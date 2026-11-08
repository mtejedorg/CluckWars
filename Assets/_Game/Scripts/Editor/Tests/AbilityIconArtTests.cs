using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.Abilities;
using CluckWars.EditorTools;
using CluckWars.Gameplay;
using CluckWars.UI;

namespace CluckWars.Tests
{
    /// <summary>
    /// Locks the ability-icon chain end to end (menu overhaul Phase 2): every ability / perk type
    /// -> an <see cref="AbilityIconStyle"/> entry -> <c>Icons/Abilities/Icon_&lt;TypeName&gt;.png</c> on
    /// disk -> a <c>.cw-hex-icon--*</c> rule in every icon stylesheet whose url points at that file
    /// -> a small import size. Any broken link renders a blank hex (or the monogram safety net) with
    /// at most one log line, so each link is a named failure here.
    /// </summary>
    public sealed class AbilityIconArtTests
    {
        private static IEnumerable<Type> ConcreteAbilityTypes() =>
            typeof(AbilityBaseSO).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(AbilityBaseSO)) && !t.IsAbstract);

        [Test]
        public void EveryAbilityTypeAndRegisteredAbility_HasAnIconEntry()
        {
            var missing = new SortedSet<string>();
            foreach (var t in ConcreteAbilityTypes())
                if (!AbilityIconStyle.Entries.ContainsKey(t.Name)) missing.Add($"{t.Name} (type)");

            var reg = TestAssets.Load<AbilityRegistrySO>(TestAssets.AbilityRegistryPath);
            foreach (var a in reg.All.Where(a => a != null))      // actives AND passives (perks)
                if (AbilityIconStyle.ClassFor(a) == null) missing.Add($"{a.name} ({a.GetType().Name}, registered)");
            foreach (var a in TestAssets.LoadAllIn<AbilityBaseSO>(TestAssets.AbilitiesDir))
                if (AbilityIconStyle.ClassFor(a) == null) missing.Add($"{a.name} ({a.GetType().Name}, asset)");

            Assert.IsEmpty(missing,
                "No AbilityIconStyle entry: these fall back to a monogram disc in the menus and a text label on the HUD. " +
                "Add an entry, Icon_<TypeName>.png and the rule in both stylesheets.\n" + string.Join("\n", missing));
        }

        [Test]
        public void EveryIconEntry_HasItsSpriteOnDisk()
        {
            var missing = AbilityIconStyle.Entries.Keys
                .Select(AbilityIconStyle.SpriteFileFor)
                .Where(p => !File.Exists(p))
                .ToList();
            Assert.IsEmpty(missing, "Icon sprite missing:\n" + string.Join("\n", missing));
        }

        [Test]
        public void NoOrphanSprite_InTheAbilityIconFolder()
        {
            var expected = new HashSet<string>(AbilityIconStyle.Entries.Keys.Select(k => $"Icon_{k}.png"));
            var orphans = Directory.GetFiles(AbilityIconStyle.IconFolder, "Icon_*.png")
                .Select(Path.GetFileName)
                .Where(f => !expected.Contains(f))
                .ToList();
            Assert.IsEmpty(orphans,
                "Icon files with no AbilityIconStyle entry (renamed type? typo?):\n" + string.Join("\n", orphans));
        }

        [Test]
        public void EveryIconClass_IsUnique()
        {
            var dupes = AbilityIconStyle.Entries.GroupBy(kv => kv.Value).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(kv => kv.Key))}").ToList();
            Assert.IsEmpty(dupes, "Two types share one icon class, so they draw the same sprite:\n" + string.Join("\n", dupes));
        }

        [Test]
        public void EveryIconClass_HasARule_PointingAtItsOwnSprite_InEveryStylesheet()
        {
            var problems = new List<string>();
            foreach (var sheetPath in TestAssets.AbilityIconStylesheets)
            {
                Assert.IsTrue(File.Exists(sheetPath), $"Stylesheet not found: {sheetPath}");
                string css = File.ReadAllText(sheetPath);
                foreach (var kv in AbilityIconStyle.Entries)
                {
                    // Whole-selector match (the lookahead stops '.cw-hex-icon--snatch' matching
                    // '.cw-hex-icon--snatchXX'), then the url inside that rule's braces.
                    var m = Regex.Match(css,
                        Regex.Escape("." + kv.Value) + @"(?![A-Za-z0-9_-])\s*\{([^}]*)\}");
                    if (!m.Success) { problems.Add($"{sheetPath}: no rule for .{kv.Value} ({kv.Key})"); continue; }

                    var url = Regex.Match(m.Groups[1].Value, @"url\(""project://database/([^""?#]+)");
                    string expected = AbilityIconStyle.SpriteFileFor(kv.Key);
                    if (!url.Success) problems.Add($"{sheetPath}: .{kv.Value} has no background-image url");
                    else if (url.Groups[1].Value != expected)
                        problems.Add($"{sheetPath}: .{kv.Value} points at {url.Groups[1].Value}, expected {expected}");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryIcon_HasStrokesThatSurviveA9pxOpen()
        {
            // The kept set's outline is ~17 px with no hairlines; the 2026-11-07 redraws shipped 5-7 px
            // outlines and 2 px details that vanished on a 48 px hex. A 9 px open at 256 must keep >= 90 %
            // of the ink (kept set: 0.91-1.00).
            var problems = new List<string>();
            foreach (var typeName in AbilityIconStyle.Entries.Keys)
            {
                string path = AbilityIconStyle.SpriteFileFor(typeName);
                var m = IconStrokeMetrics.Load(path);
                if (m == null) { problems.Add($"{path}: not a readable PNG"); continue; }
                float kept = IconStrokeMetrics.InkKeptByOpen(m);
                if (kept < 0.90f) problems.Add($"{path}: a 9 px open keeps {kept:P0} of the ink");
            }
            Assert.IsEmpty(problems, "Ink strokes too thin for the locked icon style:\n" + string.Join("\n", problems));
        }

        [Test]
        public void JumpIcon_HasNoTrailingSpeedDashes()
        {
            // Spec A6: the Jump icon is a bird over one solid arc. Trailing dashes made it read as the Speed bolt
            // or a running bird; three or more thin horizontal bars behind the bird is that picture again.
            string path = AbilityIconStyle.SpriteFileFor(nameof(ShadowstepAbilitySO));
            var m = IconStrokeMetrics.Load(path);
            Assert.IsNotNull(m, $"{path}: not a readable PNG");
            var bars = IconStrokeMetrics.ThinHorizontalBarsLeftHalf(m);
            Assert.Less(bars.Count, 3, $"{path} has speed dashes behind the bird:\n" + string.Join("\n", bars));
        }

        [Test]
        public void IconTextures_AreImportedSmall_OnEveryPlatform()
        {
            const int limit = 512;
            var problems = new List<string>();
            foreach (var typeName in AbilityIconStyle.Entries.Keys)
            {
                string path = AbilityIconStyle.SpriteFileFor(typeName);
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) { problems.Add($"{path}: no TextureImporter"); continue; }
                if (ti.textureType != TextureImporterType.Sprite) problems.Add($"{path}: not imported as a Sprite");
                if (ti.mipmapEnabled) problems.Add($"{path}: mipmaps on");
                if (ti.maxTextureSize > limit) problems.Add($"{path}: maxTextureSize {ti.maxTextureSize}");
                foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
                {
                    var ps = ti.GetPlatformTextureSettings(platform);
                    if (ps.overridden && ps.maxTextureSize > limit)
                        problems.Add($"{path}: {platform} override maxTextureSize {ps.maxTextureSize}");
                }
            }
            Assert.IsEmpty(problems,
                $"Ability icons must import at <= {limit} px (UiSpriteImportSettings caps them at " +
                $"{UiSpriteImportSettings.AbilityIconMaxSize}; reimport if this fails):\n" + string.Join("\n", problems));
        }
    }
}
