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
            // outline matches the kept set and no hairline detail disappears on a 24-40 px badge. Every state,
            // Marked's skull included (I14: it carries the ink ring now, though it lives outside the States folder).
            var problems = new List<string>();
            foreach (var s in States)
            {
                string path = StateGlyphStyle.SpritePathFor(s);
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
        public void PeckGlyph_IsThePeckIconBird_WithTheInkRing()
        {
            // I10: Peck reads as ONE bird. The PECK state glyph is the Peck ability icon (head, open beak, one seed)
            // re-stroked for the badge, so its cream silhouette must match the icon once both are normalised to the
            // same box (redrawn glyph 0.96; the old different bird 0.51; every other glyph 0.34-0.42 -> 0.85 splits them),
            // it must keep the seed as its own cream island the way the icon does, and its ink ring share must sit
            // inside the range the other state glyphs show (derived from the folder, not hand-copied).
            string glyphPath = StateGlyphStyle.SpritePathFor(StateGlyphStyle.StateGlyph.PeckChain);
            string iconPath = AbilityIconStyle.SpriteFileFor(nameof(CluckWars.Abilities.PeckAbilitySO));
            var glyph = IconStrokeMetrics.Load(glyphPath);
            var icon = IconStrokeMetrics.Load(iconPath);
            Assert.IsNotNull(glyph, glyphPath + " unreadable");
            Assert.IsNotNull(icon, iconPath + " unreadable");

            bool[] glyphCream = CreamOf(glyph), iconCream = CreamOf(icon);
            float iou = Iou(Normalise(glyphCream, glyph.W, glyph.H), Normalise(iconCream, icon.W, icon.H));
            Assert.GreaterOrEqual(iou, 0.85f, $"PECK glyph silhouette IoU with the Peck icon {iou:F3}: not the same bird");

            var gIslands = CreamIslands(glyphCream, glyph.W, glyph.H);
            var iIslands = CreamIslands(iconCream, icon.W, icon.H);
            Assert.AreEqual(iIslands.Count, gIslands.Count, "PECK glyph must keep the icon's cream islands (head + seed)");
            Assert.GreaterOrEqual(gIslands.Count, 2, "Peck art lost its seed");
            float gSeed = gIslands[1] / (float)gIslands[0], iSeed = iIslands[1] / (float)iIslands[0];
            Assert.That(gSeed, Is.InRange(iSeed * 0.5f, iSeed * 1.5f), $"PECK seed/head ratio {gSeed:F3} vs icon {iSeed:F3}");

            // Ink ring: every opaque pixel on the outer edge is ink, and the ink share sits in the other glyphs' range.
            Assert.GreaterOrEqual(EdgeInkShare(glyph), 0.95f, "PECK glyph outer edge is not an ink ring");
            var (min, max) = InkShareRangeOfFolderGlyphs(StateGlyphStyle.StateGlyph.PeckChain);
            float peck = InkShare(glyph);
            Assert.That(peck, Is.InRange(min - 0.02f, max + 0.02f), $"PECK ink share {peck:F2} outside the glyph range {min:F2}-{max:F2}");
        }

        [Test]
        public void SkullGlyph_CarriesTheInkRing_WithAnInkShareInTheGlyphRange()
        {
            // I14: the Marked skull shipped as a flat white shape (ink share 0.00 vs 0.38-0.63 on every other state
            // glyph), so it read as a different family on the badge. It now carries the same 17 px ink ring with its
            // eye sockets, nose and tooth gaps as ink: the outer edge is ink and the ink share sits in the range the
            // States-folder glyphs show (derived from the folder at test time, not hand-copied).
            string path = StateGlyphStyle.SpritePathFor(StateGlyphStyle.StateGlyph.Marked);
            var skull = IconStrokeMetrics.Load(path);
            Assert.IsNotNull(skull, path + " unreadable");
            Assert.GreaterOrEqual(EdgeInkShare(skull), 0.95f, "Marked skull outer edge is not an ink ring");
            var (min, max) = InkShareRangeOfFolderGlyphs(StateGlyphStyle.StateGlyph.Marked);
            float share = InkShare(skull);
            Assert.That(share, Is.InRange(min - 0.02f, max + 0.02f), $"Marked skull ink share {share:F2} outside the glyph range {min:F2}-{max:F2}");
        }

        [Test]
        public void SpeedingGlyph_IsTheHexBolt_WithTwoSeparateDashes()
        {
            // J2 (E10a): FAST used to be three dashes plus a chevron (four cream pieces) while the Speed Burst hex
            // shows a lightning bolt. The glyph is now that bolt with two short trailing dashes: after the ink ring
            // is masked off (cream pixels only) it must be exactly three islands (bolt + two dashes), the biggest
            // island must match the hex icon's bolt silhouette once both are normalised to the same box, and the
            // outer edge must stay an ink ring.
            string glyphPath = StateGlyphStyle.SpritePathFor(StateGlyphStyle.StateGlyph.Speeding);
            string iconPath = AbilityIconStyle.SpriteFileFor(nameof(CluckWars.Abilities.SpeedBurstAbilitySO));
            var glyph = IconStrokeMetrics.Load(glyphPath);
            var icon = IconStrokeMetrics.Load(iconPath);
            Assert.IsNotNull(glyph, glyphPath + " unreadable");
            Assert.IsNotNull(icon, iconPath + " unreadable");

            var islands = CreamIslands(CreamOf(glyph), glyph.W, glyph.H);
            Assert.AreEqual(3, islands.Count, "Speeding glyph must be one bolt plus two dash blobs, cream islands: " + string.Join(",", islands));
            Assert.Greater(islands[0], islands[1] * 4, "the bolt must dwarf the dashes");

            bool[] boltOnly = LargestCreamIsland(CreamOf(glyph), glyph.W, glyph.H);
            var iconCream = CreamOf(icon);
            float iou = Iou(Normalise(boltOnly, glyph.W, glyph.H), Normalise(iconCream, icon.W, icon.H));
            Assert.GreaterOrEqual(iou, 0.85f, $"Speeding bolt IoU with the Speed Burst hex bolt {iou:F3}: not the same shape");

            Assert.GreaterOrEqual(EdgeInkShare(glyph), 0.95f, "Speeding glyph outer edge is not an ink ring");
        }

        private static bool[] LargestCreamIsland(bool[] mask, int w, int h)
        {
            var seen = new bool[mask.Length];
            List<int> best = null;
            var stack = new Stack<int>();
            for (int s = 0; s < mask.Length; s++)
            {
                if (!mask[s] || seen[s]) continue;
                var cur = new List<int>();
                seen[s] = true; stack.Push(s);
                while (stack.Count > 0)
                {
                    int p = stack.Pop(); cur.Add(p);
                    int px = p % w, py = p / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (mask[q] && !seen[q]) { seen[q] = true; stack.Push(q); }
                        }
                }
                if (best == null || cur.Count > best.Count) best = cur;
            }
            var res = new bool[mask.Length];
            if (best != null) foreach (var i in best) res[i] = true;
            return res;
        }

        /// <summary>Fraction of the opaque pixels on the glyph's outer edge that are ink (1 = a full ink ring).</summary>
        private static float EdgeInkShare(IconStrokeMetrics.Masks m)
        {
            int edge = 0, edgeInk = 0;
            for (int y = 0; y < m.H; y++)
                for (int x = 0; x < m.W; x++)
                {
                    int i = y * m.W + x;
                    if (!m.Opaque[i]) continue;
                    bool border = x == 0 || y == 0 || x == m.W - 1 || y == m.H - 1
                        || !m.Opaque[i - 1] || !m.Opaque[i + 1] || !m.Opaque[i - m.W] || !m.Opaque[i + m.W];
                    if (!border) continue;
                    edge++;
                    if (m.Ink[i]) edgeInk++;
                }
            Assert.Greater(edge, 0, "glyph has no opaque edge");
            return edgeInk / (float)edge;
        }

        /// <summary>Min and max ink share over the glyphs in the States folder, leaving out <paramref name="except"/>.</summary>
        private static (float min, float max) InkShareRangeOfFolderGlyphs(StateGlyphStyle.StateGlyph except)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var s in States)
            {
                string path = StateGlyphStyle.SpritePathFor(s);
                if (s == except || !path.StartsWith(StateGlyphStyle.GlyphFolder)) continue;
                var m = IconStrokeMetrics.Load(path);
                if (m == null) continue;
                float share = InkShare(m);
                min = Mathf.Min(min, share);
                max = Mathf.Max(max, share);
            }
            Assert.Less(min, max, "need other state glyphs to derive the ink range");
            return (min, max);
        }

        private static bool[] CreamOf(IconStrokeMetrics.Masks m)
        {
            var c = new bool[m.Opaque.Length];
            for (int i = 0; i < c.Length; i++) c[i] = m.Opaque[i] && !m.Ink[i];
            return c;
        }

        private static float InkShare(IconStrokeMetrics.Masks m)
        {
            int op = 0, ink = 0;
            for (int i = 0; i < m.Opaque.Length; i++) { if (m.Opaque[i]) op++; if (m.Ink[i]) ink++; }
            return op == 0 ? 0f : ink / (float)op;
        }

        /// <summary>Samples the mask's own bounding box onto a 64x64 grid (pixel centres), so two drawings of
        /// the same shape at different sizes and canvases compare cell for cell.</summary>
        private static bool[] Normalise(bool[] mask, int w, int h)
        {
            const int n = 64;
            var b = IconStrokeMetrics.BoundsOf(mask, w, h).Value;
            var g = new bool[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int x = b.xMin + (int)((i + 0.5f) * b.width / n);
                    int y = b.yMin + (int)((j + 0.5f) * b.height / n);
                    g[j * n + i] = mask[y * w + x];
                }
            return g;
        }

        private static float Iou(bool[] a, bool[] b)
        {
            int inter = 0, union = 0;
            for (int i = 0; i < a.Length; i++) { if (a[i] && b[i]) inter++; if (a[i] || b[i]) union++; }
            return union == 0 ? 0f : inter / (float)union;
        }

        /// <summary>Sizes of the 8-connected cream islands, largest first.</summary>
        private static List<int> CreamIslands(bool[] mask, int w, int h)
        {
            var seen = new bool[mask.Length];
            var sizes = new List<int>();
            var stack = new Stack<int>();
            for (int s = 0; s < mask.Length; s++)
            {
                if (!mask[s] || seen[s]) continue;
                int size = 0;
                seen[s] = true;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int p = stack.Pop();
                    size++;
                    int px = p % w, py = p / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (mask[q] && !seen[q]) { seen[q] = true; stack.Push(q); }
                        }
                }
                sizes.Add(size);
            }
            sizes.Sort((a, b) => b.CompareTo(a));
            return sizes;
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
