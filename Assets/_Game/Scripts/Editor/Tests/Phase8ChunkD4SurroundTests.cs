using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CluckWars.EditorTools;
using CluckWars.Visuals;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 chunk D item D4 (audit L04 "hedges and the PC surround ground are flat colour slabs"):
    /// <list type="bullet">
    /// <item><c>Surround_Grass.mat</c> (the 260 m plane outside the fence) carries a grass tile derived from the arena's
    /// <c>GrassCalm</c> as a stock URP Lit Detail Albedo x2 map (<c>surround_grass_detail.png</c>, linear, neutral mean), at the
    /// arena tile's world period. The tile sits in the DETAIL slot, not the base map, on purpose: the base colour stays the
    /// field's mean (darker, less saturated than the arena = "outside"), and the Mobile haze baker writes its per-texel ramp
    /// into the hazed copy's base map (it refuses a textured original), while the copy inherits the detail map unchanged.</item>
    /// <item>The hedge leaf tiles are replaced by clumpy 2 m x 1 m tiles (luma std >= 20, the source tile's mean colour) with a
    /// scalloped top row on the sides, and the crown gets a scallop Detail Albedo x2 map that lobes both crown edges.</item>
    /// <item>The Mobile hazed copies (<see cref="SurroundHazeBaker"/>, re-run after the change) use the same textures.</item>
    /// </list>
    /// Match-camera numbers + captures: Captures/env3d_after/D/D4/.
    /// </summary>
    public class Phase8ChunkD4SurroundTests
    {
        internal const string SurroundGrassMat = "Assets/_Game/Art/Materials/Surround_Grass.mat";
        internal const string HedgeSideMat = "Assets/_Game/Art/Materials/Hedge_Side.mat";
        internal const string HedgeTopMat = "Assets/_Game/Art/Materials/Hedge_Top.mat";
        internal const string GrassDetail = "Assets/Generated/Env/Textures/surround_grass_detail.png";
        internal const string SideTile = "Assets/Generated/Env/Textures/hedge_leaf_side_d4.png";
        internal const string TopTile = "Assets/Generated/Env/Textures/hedge_leaf_top_d4.png";
        internal const string CrownScallop = "Assets/Generated/Env/Textures/hedge_crown_scallop.png";
        private const string SideSource = "Assets/Generated/Env/Textures/hedge_leaf_tile.png";
        private const string TopSource = "Assets/Generated/Env/Textures/hedge_leaf_tile_top.png";

        /// <summary>Hedge texture period along the hedge, metres (the HedgeStrip UVs are metres; base tiling = 1 / period).</summary>
        internal const float HedgePeriod = 2f;
        /// <summary>Side/crown seam in the side UVs: only rows v &lt;= this are drawn.</summary>
        private const float SideSeamV = 0.78f;
        private const float MinLeafStd = 20f;
        /// <summary>Surround plane edge and arena floor edge (both Cube primitives: top face UV 0..1), and the arena tiling.</summary>
        private const float SurroundSize = 260f, ArenaSize = 51.3f, ArenaTiling = 5f;
        private static readonly Color SurroundMean = new Color(0.15f, 0.22f, 0.14f, 1f);

        private static Material Load(string p)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            Assert.IsNotNull(m, p);
            return m;
        }

        /// <summary>PNG texels as stored (0..255), row 0 = bottom (v = 0).</summary>
        private static Color32[] Texels(string path, out int w, out int h)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(t, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), path))), path);
                w = t.width; h = t.height;
                return t.GetPixels32();
            }
            finally { Object.DestroyImmediate(t); }
        }

        private static float Luma601(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// <summary>Mean RGB and luma std over the rows with v &lt; <paramref name="maxV"/> (0..255).</summary>
        private static (Vector3 Mean, float LumaStd) Stats(string path, float maxV = 1f)
        {
            var px = Texels(path, out int w, out int h);
            int rows = Mathf.Clamp(Mathf.RoundToInt(maxV * h), 1, h);
            double r = 0, g = 0, b = 0, l = 0, l2 = 0; int n = rows * w;
            for (int i = 0; i < n; i++)
            {
                var c = px[i]; float y = Luma601(c);
                r += c.r; g += c.g; b += c.b; l += y; l2 += y * y;
            }
            double ml = l / n;
            return (new Vector3((float)(r / n), (float)(g / n), (float)(b / n)), (float)System.Math.Sqrt(System.Math.Max(0, l2 / n - ml * ml)));
        }

        private static bool Linear(Texture t) => !((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t))).sRGBTexture;

        // ---- surround ground -------------------------------------------------------------------------------------

        [Test]
        public void SurroundGrass_HasTheGrassTile_AsADetailMap_OnAnUntexturedBase()
        {
            var m = Load(SurroundGrassMat);
            Assert.IsNull(m.GetTexture("_BaseMap"), "the base map stays empty: the Mobile haze copy carries its ramp there");
            Assert.IsTrue(m.IsKeywordEnabled("_DETAIL_MULX2"), "stock URP Lit Detail Albedo x2");
            var d = m.GetTexture("_DetailAlbedoMap");
            Assert.IsNotNull(d, "L04: Surround_Grass had no texture at all");
            Assert.AreEqual(GrassDetail, AssetDatabase.GetAssetPath(d));
            Assert.IsTrue(Linear(d), "a Detail Albedo x2 map must be sRGB off");
            Assert.AreEqual(1f, m.GetFloat("_DetailAlbedoMapScale"), 1e-4f, "scale 1 keeps the cheap _DETAIL_MULX2 path");
        }

        [Test]
        public void SurroundGrass_TilesAtTheArenaGrassPeriod()
        {
            var m = Load(SurroundGrassMat);
            Assert.AreEqual(Vector2.one, m.GetTextureScale("_BaseMap"), "detail UVs ride on the base UVs (the plane's 0..1)");
            var t = m.GetTextureScale("_DetailAlbedoMap");
            Assert.AreEqual(t.x, t.y, 1e-4f, "square tiling");
            float surroundPeriod = SurroundSize / t.x, arenaPeriod = ArenaSize / ArenaTiling;
            Assert.AreEqual(arenaPeriod, surroundPeriod, 0.02f * arenaPeriod, "the same grass at the same world scale as the arena floor");
            Assert.AreEqual(ArenaTiling, Load(Phase8ChunkD8Tests.GrassPath).GetTextureScale("_BaseMap").x, 1e-4f, "arena tiling source (E3)");
        }

        [Test]
        public void SurroundGrass_KeepsItsMeanColour_DarkerAndLessSaturatedThanTheArena()
        {
            var m = Load(SurroundGrassMat);
            var bc = m.GetColor("_BaseColor");
            Assert.AreEqual(SurroundMean.r, bc.r, 1e-3f); Assert.AreEqual(SurroundMean.g, bc.g, 1e-3f); Assert.AreEqual(SurroundMean.b, bc.b, 1e-3f);
            var (mean, std) = Stats(GrassDetail);
            for (int c = 0; c < 3; c++)
                Assert.AreEqual(127.5f, mean[c], 1f, $"detail channel {c} is neutral on average (x2 = 1): the field's mean stays the base colour");
            Assert.Greater(std, 6f, "the tile actually varies (it is not a flat 128)");

            // Material-level albedo means (gamma project: colour x texel as stored). Arena = GrassFloor's tinted tile (E3's macro is neutral).
            double ar = 0, ag = 0, ab = 0; var arena = Phase8ChunkD8Tests.TintedTexels(Load(Phase8ChunkD8Tests.GrassPath));
            foreach (var c in arena) { ar += c.r; ag += c.g; ab += c.b; }
            var arenaMean = new Color((float)(ar / arena.Length), (float)(ag / arena.Length), (float)(ab / arena.Length), 1f);
            float lumaS = Phase8ChunkD8Tests.Luma709(bc), lumaA = Phase8ChunkD8Tests.Luma709(arenaMean);
            Assert.Less(lumaS, 0.6f * lumaA, "outside reads a clear step darker than the playfield");
            var labS = Phase8ChunkD8Tests.ToLab(bc); var labA = Phase8ChunkD8Tests.ToLab(arenaMean);
            Assert.Less(new Vector2(labS.y, labS.z).magnitude, new Vector2(labA.y, labA.z).magnitude, "and less saturated");
        }

        // ---- hedges ----------------------------------------------------------------------------------------------

        [TestCase(SideTile, SideSource, 0.78f)]
        [TestCase(TopTile, TopSource, 1f)]
        public void HedgeLeafTile_HasContrast_AndKeepsTheSourceMeanColour(string tile, string source, float drawnV)
        {
            var (mean, std) = Stats(tile, drawnV);
            var (srcMean, srcStd) = Stats(source);
            Assert.GreaterOrEqual(std, MinLeafStd, $"L04: leaf tile luma std {srcStd:0.0} -> needs >= {MinLeafStd} so the lumps read");
            for (int c = 0; c < 3; c++)
                Assert.AreEqual(srcMean[c], mean[c], 1f, $"channel {c}: the hedge keeps its colour (over the rows the mesh draws)");
        }

        [Test]
        public void HedgeMaterials_TileTwoMetres_AndTheCrownHasTheScallopDetail()
        {
            var side = Load(HedgeSideMat); var top = Load(HedgeTopMat);
            Assert.AreEqual(SideTile, AssetDatabase.GetAssetPath(side.GetTexture("_BaseMap")));
            Assert.AreEqual(TopTile, AssetDatabase.GetAssetPath(top.GetTexture("_BaseMap")));
            foreach (var m in new[] { side, top })
            {
                Assert.AreEqual(new Vector2(1f / HedgePeriod, 1f), m.GetTextureScale("_BaseMap"), m.name);
                Assert.AreEqual(m.GetTexture("_BaseMap"), m.GetTexture("_MainTex"), m.name + ": _MainTex mirrors _BaseMap");
                Assert.AreEqual(m.GetTextureScale("_BaseMap"), m.GetTextureScale("_MainTex"), m.name);
            }
            Assert.IsFalse(side.IsKeywordEnabled("_DETAIL_MULX2"), "the side's scallop row is painted into its tile (v is unique there): no extra sample");
            Assert.IsTrue(top.IsKeywordEnabled("_DETAIL_MULX2"));
            var d = top.GetTexture("_DetailAlbedoMap");
            Assert.AreEqual(CrownScallop, AssetDatabase.GetAssetPath(d));
            Assert.IsTrue(Linear(d));
            // Detail UVs = base UVs x scale: u keeps the 2 m period (lobes line up with the side's), v / 3 maps the crown's planar
            // v 0.47..2.52 into 0..1 exactly once, so each crown edge gets its own lobes.
            Assert.AreEqual(new Vector2(1f, 1f / 3f), top.GetTextureScale("_DetailAlbedoMap"));
            Assert.AreEqual(1f, top.GetFloat("_DetailAlbedoMapScale"), 1e-4f);
        }

        [Test]
        public void CrownScallop_IsNeutralInTheMiddle_AndLobesTheEdges()
        {
            var px = Texels(CrownScallop, out int w, out int h);
            // detail v = crown v / 3, crown y = v - 1.5: the middle (|y| < 0.5) must be exactly neutral, the edges (|y| 0.8..0.98) varied.
            double mid = 0, e = 0, e2 = 0; int nm = 0, ne = 0;
            for (int row = 0; row < h; row++)
            {
                float y = Mathf.Abs(3f * (row + 0.5f) / h - 1.5f);
                for (int x = 0; x < w; x++)
                {
                    float t = px[row * w + x].r;
                    if (y < 0.5f) { mid += Mathf.Abs(t - 127.5f); nm++; }
                    else if (y > 0.8f && y < 0.98f) { e += t; e2 += t * t; ne++; }
                }
            }
            Assert.Less(mid / nm, 1.01, "crown middle untouched (128 = x1)");
            double me = e / ne;
            Assert.Greater(System.Math.Sqrt(e2 / ne - me * me), 25.0, "lobes and notches along both crown edges");
        }

        // ---- Mobile hazed copies follow -----------------------------------------------------------------------------

        [Test]
        public void HazedCopies_UseTheSourceTextures()
        {
            var map = AssetDatabase.LoadAssetAtPath<SurroundHazeMapSO>(SurroundHazeBaker.MapAssetPath);
            Assert.IsNotNull(map, SurroundHazeBaker.MapAssetPath);
            var problems = new List<string>();
            var seen = new HashSet<string>();
            foreach (var c in map.Copies)
            {
                var o = c.Original; var hz = c.Hazed;
                if (o == null || hz == null) { problems.Add("null copy"); continue; }
                seen.Add(AssetDatabase.GetAssetPath(o));
                // The ramp copy (surround grass) owns its base map (the haze ramp); every other copy keeps the original's.
                var props = c.Band == -1 ? new[] { "_DetailAlbedoMap" } : new[] { "_BaseMap", "_DetailAlbedoMap" };
                foreach (var p in props)
                {
                    if (o.GetTexture(p) != hz.GetTexture(p)) problems.Add($"{hz.name}.{p}: {Name(hz.GetTexture(p))} != {o.name}'s {Name(o.GetTexture(p))}");
                    if (o.GetTextureScale(p) != hz.GetTextureScale(p) || o.GetTextureOffset(p) != hz.GetTextureOffset(p)) problems.Add($"{hz.name}.{p}: tiling differs from {o.name}");
                }
                if (o.IsKeywordEnabled("_DETAIL_MULX2") != hz.IsKeywordEnabled("_DETAIL_MULX2")) problems.Add($"{hz.name}: _DETAIL_MULX2 differs from {o.name}");
            }
            CollectionAssert.IsEmpty(problems, "re-run Cluck Wars/Map/Bake Mobile Surround Haze:\n" + string.Join("\n", problems));
            foreach (var p in new[] { SurroundGrassMat, HedgeSideMat, HedgeTopMat })
                Assert.IsTrue(seen.Contains(p), p + " has a hazed copy");
        }

        private static string Name(Texture t) => t == null ? "none" : t.name;
    }
}
