using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.UI;

namespace CluckWars.Tests
{
    /// <summary>
    /// Locks the state-glyph art chain (feedback batch C2): every <see cref="StateGlyphStyle"/>
    /// state -> a sprite file on disk that loads as a Sprite -> the shared small import profile,
    /// plus no orphan file in <c>Icons/States</c> and unique, well-formed USS classes.
    /// Everything is derived from the map and the folder.
    /// </summary>
    public sealed class StateGlyphArtTests
    {
        private static IEnumerable<StateGlyphStyle.StateGlyph> States => StateGlyphStyle.All;

        [Test]
        public void EveryStateEnumValue_HasAnEntryInAll()
        {
            var enumValues = System.Enum.GetValues(typeof(StateGlyphStyle.StateGlyph)).Cast<StateGlyphStyle.StateGlyph>();
            var missing = enumValues.Except(States).ToList();
            Assert.IsEmpty(missing, "State with no StateGlyphStyle.All entry: " + string.Join(", ", missing));
            Assert.AreEqual(States.Count(), States.Distinct().Count(), "Duplicate state in StateGlyphStyle.All");
        }

        [Test]
        public void EveryState_HasItsSpriteOnDisk_LoadingAsASprite()
        {
            var problems = new List<string>();
            foreach (var s in States)
            {
                string path = StateGlyphStyle.SpritePathFor(s);
                if (!File.Exists(path)) { problems.Add($"{s}: file missing {path}"); continue; }
                if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null) problems.Add($"{s}: {path} does not load as a Sprite");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryStateGlyph_IsImportedAsAnUiSprite_Capped256()
        {
            const int max = 256;
            var problems = new List<string>();
            foreach (var s in States)
            {
                // Marked reuses the shipped skull outside the States folder (its own import rules).
                string path = StateGlyphStyle.SpritePathFor(s);
                if (!path.StartsWith(StateGlyphStyle.GlyphFolder)) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) { problems.Add($"{path}: no TextureImporter"); continue; }
                if (ti.textureType != TextureImporterType.Sprite) problems.Add($"{path}: not a Sprite");
                if (ti.spriteImportMode != SpriteImportMode.Single) problems.Add($"{path}: spriteImportMode {ti.spriteImportMode}");
                if (!Mathf.Approximately(ti.spritePixelsPerUnit, 100f)) problems.Add($"{path}: PPU {ti.spritePixelsPerUnit}");
                if (ti.filterMode != FilterMode.Bilinear) problems.Add($"{path}: filter {ti.filterMode}");
                if (ti.mipmapEnabled) problems.Add($"{path}: mipmaps on");
                if (ti.maxTextureSize != max) problems.Add($"{path}: default maxTextureSize {ti.maxTextureSize}");
                foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
                {
                    var ps = ti.GetPlatformTextureSettings(platform);
                    if (ps.overridden && ps.maxTextureSize != max)
                        problems.Add($"{path}: {platform} override maxTextureSize {ps.maxTextureSize}");
                }
            }
            Assert.IsEmpty(problems,
                "State glyphs must import as 256 px UI sprites (UiSpriteImportSettings stamps them; reimport if this fails):\n"
                + string.Join("\n", problems));
        }

        [Test]
        public void EveryStateGlyph_HasStrokesThatSurviveA9pxOpen()
        {
            // Same stroke contract as the ability icons: a 9 px open at 256 keeps >= 90 % of the ink, so the
            // outline matches the kept set and no hairline detail disappears on a 24-40 px badge.
            var problems = new List<string>();
            foreach (var s in States)
            {
                string path = StateGlyphStyle.SpritePathFor(s);
                if (!path.StartsWith(StateGlyphStyle.GlyphFolder)) continue;   // Marked reuses the shipped skull
                var m = IconStrokeMetrics.Load(path);
                if (m == null) { problems.Add($"{path}: not a readable PNG"); continue; }
                float kept = IconStrokeMetrics.InkKeptByOpen(m);
                if (kept < 0.90f) problems.Add($"{path}: a 9 px open keeps {kept:P0} of the ink");
            }
            Assert.IsEmpty(problems, "Glyph ink strokes too thin for the locked icon style:\n" + string.Join("\n", problems));
        }

        [Test]
        public void EveryStateGlyph_FillsAtLeast80PercentOfItsCanvasHeight()
        {
            // ChickenStatusBadges scales a glyph by its canvas height, so the content must fill that height or the
            // glyph draws small (round-3 H3: four wide glyphs sat on a 256 square at ~55 % of the slot).
            var problems = new List<string>();
            var files = Directory.GetFiles(StateGlyphStyle.GlyphFolder, "*.png");
            Assert.IsNotEmpty(files);
            foreach (var f in files)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(f))) { problems.Add($"{f}: not a readable PNG"); continue; }
                    var px = tex.GetPixels32();
                    int w = tex.width, h = tex.height, minY = h, maxY = -1;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            if (px[y * w + x].a > 8) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
                    if (maxY < 0) { problems.Add($"{f}: empty"); continue; }
                    float ratio = (maxY - minY + 1) / (float)h;
                    if (ratio < 0.80f) problems.Add($"{f}: content {maxY - minY + 1} of {h} px tall ({ratio:P0}), want >= 80 %");
                }
                finally { Object.DestroyImmediate(tex); }
            }
            Assert.IsEmpty(problems, string.Join(System.Environment.NewLine, problems));
        }

        [Test]
        public void WaddleGlyph_IsAnEggWithTwoFeet_NotTheEggGlyph()
        {
            // H4: WADDLE is an egg that walks. Below the egg body the glyph has two separate feet; the EGG glyph is one
            // unbroken shape there. They must also differ as pictures.
            var walk = IconStrokeMetrics.Load(StateGlyphStyle.SpritePathFor(StateGlyphStyle.StateGlyph.ShieldWalk));
            var egg = IconStrokeMetrics.Load(StateGlyphStyle.SpritePathFor(StateGlyphStyle.StateGlyph.ShieldEgg));
            Assert.IsNotNull(walk, "ShieldWalk glyph unreadable");
            Assert.IsNotNull(egg, "ShieldEgg glyph unreadable");
            Assert.AreEqual(egg.W, walk.W);
            Assert.AreEqual(egg.H, walk.H);

            var wb = IconStrokeMetrics.BoundsOf(walk.Opaque, walk.W, walk.H).Value;
            int feetRow = wb.yMin + Mathf.RoundToInt(wb.height * 0.15f);      // texture rows run bottom-up: 15 % above the bottom
            Assert.AreEqual(2, IconStrokeMetrics.OpaqueRunsInRow(walk, feetRow),
                "WADDLE: a row near the bottom must cross exactly two separate feet");

            int diff = 0, union = 0;
            for (int i = 0; i < walk.Opaque.Length; i++)
            {
                if (walk.Opaque[i] || egg.Opaque[i]) union++;
                if (walk.Opaque[i] != egg.Opaque[i] || (walk.Opaque[i] && walk.Ink[i] != egg.Ink[i])) diff++;
            }
            Assert.GreaterOrEqual(diff / (float)union, 0.15f, "WADDLE and EGG must read as different pictures");
        }

        [Test]
        public void EveryUssClass_IsUniqueAndWellFormed()
        {
            var classes = States.Select(StateGlyphStyle.UssClassFor).ToList();
            var dupes = classes.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dupes, "Two states share one USS class: " + string.Join(", ", dupes));
            var bad = classes.Where(c => !Regex.IsMatch(c, @"^cw-status-glyph--[a-z]+(-[a-z]+)*$")).ToList();
            Assert.IsEmpty(bad, "Malformed USS class (want cw-status-glyph--<kebab>): " + string.Join(", ", bad));
        }

        [Test]
        public void EverySpritePath_IsUnique()
        {
            var dupes = States.GroupBy(StateGlyphStyle.SpritePathFor).Where(g => g.Count() > 1)
                .Select(g => g.Key).ToList();
            Assert.IsEmpty(dupes, "Two states share one sprite file: " + string.Join(", ", dupes));
        }

        [Test]
        public void NoOrphanSprite_InTheStateGlyphFolder()
        {
            var referenced = new HashSet<string>(States.Select(StateGlyphStyle.SpritePathFor));
            var orphans = Directory.GetFiles(StateGlyphStyle.GlyphFolder, "*.png")
                .Select(f => f.Replace(Path.DirectorySeparatorChar, '/'))
                .Where(f => !referenced.Contains(f))
                .ToList();
            Assert.IsEmpty(orphans,
                "Glyph files with no StateGlyphStyle entry (renamed state? typo?):\n" + string.Join("\n", orphans));
        }
    }
}
