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
