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

        // ---- H4: Steal and Jump icon polish --------------------------------------------------------------------------------------

        [Test]
        public void StealIcon_FillsTheBox_AndItsInteriorLinesAreAtLeast11px()
        {
            // Three fat toes grip one oversized cob: the glyph fills the box (the claw alone spans >= 65 % of it), and the
            // kernel and toe seams are >= 12 px at 256, so they survive an 11 px open (the old hand had 2-5 px detail that
            // vanished on a 48 px hex).
            string path = AbilityIconStyle.SpriteFileFor(nameof(SnatchAbilitySO));
            var m = IconStrokeMetrics.Load(path);
            Assert.IsNotNull(m, $"{path}: not a readable PNG");
            var box = IconStrokeMetrics.BoundsOf(m.Opaque, m.W, m.H);
            Assert.IsTrue(box.HasValue, $"{path}: empty");
            int larger = Mathf.Max(box.Value.width, box.Value.height);
            Assert.GreaterOrEqual(larger / (float)m.W, 0.65f, $"{path}: the glyph spans {larger} of {m.W} px, want >= 65 %");
            float kept = IconStrokeMetrics.InkKeptByOpen(m, 11);
            Assert.GreaterOrEqual(kept, 0.97f, $"{path}: an 11 px open keeps {kept:P0} of the ink; interior lines must be >= 12 px");
        }

        [Test]
        public void JumpIcon_BirdIsAtLeast55PercentOfTheHeight_AndTheArcIsThinnerThanItsOutline()
        {
            // The bird outline (17 px) survives a 13 px open, the arc stroke (12 px ink) does not: so the 13 px opened ink
            // box is the bird alone, and the arc must still reach below it (it survives the 9 px open).
            string path = AbilityIconStyle.SpriteFileFor(nameof(ShadowstepAbilitySO));
            var m = IconStrokeMetrics.Load(path);
            Assert.IsNotNull(m, $"{path}: not a readable PNG");
            var bird = IconStrokeMetrics.BoundsOf(IconStrokeMetrics.OpenedInk(m, 13), m.W, m.H);
            var all = IconStrokeMetrics.BoundsOf(IconStrokeMetrics.OpenedInk(m, 9), m.W, m.H);
            Assert.IsTrue(bird.HasValue && all.HasValue, $"{path}: no ink");
            Assert.GreaterOrEqual(bird.Value.height / (float)m.H, 0.55f,
                $"{path}: the bird is {bird.Value.height} of {m.H} px tall, want >= 55 %");
            // (texture rows run bottom-up: the arc reaches below the bird = lower y)
            Assert.GreaterOrEqual(bird.Value.yMin - all.Value.yMin, 20 * m.H / 256,
                $"{path}: the arc stroke must be thinner than the bird outline (gone at a 13 px open) and reach below the bird");
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

        // ---- H6: the Speed streak train must read as one line --------------------------------------------------------------------

        private const string SpeedLinePath = "Assets/_Game/Art/UI/Fx/Fx_SpeedLine.png";

        private static Texture2D LoadSpeedLine()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(ImageConversion.LoadImage(tex, File.ReadAllBytes(SpeedLinePath)), "Fx_SpeedLine.png must decode");
            return tex;
        }

        /// <summary>Mean alpha of one column (the streak length runs along X: the sprite is the wide axis).</summary>
        private static float ColumnAlpha(Texture2D t, int x)
        {
            float sum = 0f;
            for (int y = 0; y < t.height; y++) sum += t.GetPixel(x, y).a;
            return sum / t.height;
        }

        [Test]
        public void SpeedLine_ShortEndsRampToZero_SoNeighbouringStreaksMerge()
        {
            var t = LoadSpeedLine();
            try
            {
                Assert.Greater(t.width, t.height, "the streak length is the wide (X) axis");
                float centre = ColumnAlpha(t, t.width / 2);
                Assert.Greater(centre, 0.5f, "the body keeps its alpha");
                Assert.Less(ColumnAlpha(t, 0), 0.15f * centre, "first column ramps to ~0");
                Assert.Less(ColumnAlpha(t, t.width - 1), 0.15f * centre, "last column ramps to ~0");
                int ramp = Mathf.RoundToInt(t.width * 0.2f);
                // monotone rise over the first 20 % and fall over the last 20 % (the ramp, not a hard edge)
                for (int x = 1; x < ramp; x++)
                {
                    Assert.GreaterOrEqual(ColumnAlpha(t, x) + 0.01f, ColumnAlpha(t, x - 1), $"alpha rises at column {x}");
                    int m = t.width - 1 - x;
                    Assert.GreaterOrEqual(ColumnAlpha(t, m) + 0.01f, ColumnAlpha(t, m + 1), $"alpha falls at column {m}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }

        [Test]
        public void SpeedLine_CentreKeepsItsInkEdgeOnTheLongSides()
        {
            var t = LoadSpeedLine();
            try
            {
                int x = t.width / 2;
                int dark = 0;
                for (int y = 0; y < t.height; y++)
                {
                    var c = t.GetPixel(x, y);
                    if (c.a > 0.3f && c.r < 0.3f && c.g < 0.3f && c.b < 0.3f) dark++;
                }
                Assert.GreaterOrEqual(dark, 2, "ink (dark opaque) pixels on the long sides at the centre column");
                Assert.Less(t.GetPixel(x, 1).r, 0.3f, "one long side is ink");
                Assert.IsTrue(t.GetPixel(x, t.height / 2).r > 0.9f, "the cream body is between the ink edges");
            }
            finally { UnityEngine.Object.DestroyImmediate(t); }
        }
    }
}
