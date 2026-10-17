using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D3 "mound reads as smooth custard" (finding L03): at the match camera the tiled grain averaged out to a flat
    /// colour with no volume. The mound now samples a UNIQUE baked albedo (<see cref="MoundAlbedo"/>): the D1 grain tile
    /// tiled x4 in the same phase as before, multiplied by a warm (amber) darkening in the concave dips and the
    /// ground-occluded base (a Cycles AO bake + a concavity term from the mesh heightfield), a small lift on the untouched
    /// crest / flanks, and a few corn-kernel clusters painted on the crest (domed, no sun direction baked: pile yaws differ).
    /// The same dip term drives a stock URP Lit <c>_OcclusionMap</c> (<see cref="MoundOcclusion"/>, indirect light only),
    /// so the dips sink further on the ambient-lit (shaded) side. Both maps share UV0 with <c>_BaseMap_ST</c> = 0.25:
    /// the mound mesh's UV0 is a planar top-down projection spanning 0..4 (u = 4 (x + 0.5)), so 0.25 maps each map once.
    /// Mesh, footprint, collider and the tint property block are untouched; stock URP Lit, no new shader.
    /// <para>
    /// These checks read the textures under the real mesh (vertex UVs x 0.25), so they fail if the material goes back to a
    /// flat tile, if the maps are flipped / mis-scaled relative to the mesh, or if the kernels leave the crest.
    /// The match-camera render limits (lit top R / hue / clip / grain) stay in <see cref="Phase8ChunkD1Tests"/>.
    /// </para>
    /// </summary>
    public class Phase8ChunkD3Tests
    {
        public const string MoundAlbedo = "Assets/Generated/Env/Textures/food_mound_albedo.png";
        public const string MoundOcclusion = "Assets/Generated/Env/Textures/food_mound_occlusion.png";
        private const string MoundMaterial = "Assets/_Game/Art/Materials/FoodMound.mat";
        private const string MoundFbx = "Assets/Generated/Env/food_mound.fbx";
        private const string EmptyFbx = "Assets/Generated/Env/food_mound_empty.fbx";
        private const string GrainTile = "Assets/Generated/Env/Textures/grain_corn_tile_neutral.png";

        /// <summary>UV0 -> unique-map UV (the material's _BaseMap tiling).</summary>
        public const float UniqueTiling = 0.25f;

        private static readonly Vector3 Rec709 = new Vector3(0.2126f, 0.7152f, 0.0722f);

        private static Mesh FbxMesh(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>().sharedMesh;

        private static Quaternion FbxRotation(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>().transform.localRotation;

        private static Texture2D LoadPng(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Assert.IsTrue(tex.LoadImage(System.IO.File.ReadAllBytes(path)), "could not decode " + path);
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        private static float Luma(Color c) => (Rec709.x * c.r + Rec709.y * c.g + Rec709.z * c.b) * 255f;

        /// <summary>Distinct vertices of the full mound: (height above the base in Unity space, unique-map UV).</summary>
        private static List<(float h, Vector2 uv)> MoundVerts(out float peak)
        {
            var mesh = FbxMesh(MoundFbx); var rot = FbxRotation(MoundFbx);
            var v = mesh.vertices; var uv = mesh.uv;
            var list = new List<(float, Vector2)>();
            for (int i = 0; i < v.Length; i++) list.Add(((rot * v[i]).y, uv[i] * UniqueTiling));
            peak = list.Max(p => p.Item1);
            return list;
        }

        // ---- material ------------------------------------------------------------------------------

        [Test]
        public void FoodMoundMaterial_SamplesTheUniqueAlbedoAndOcclusion_OnceAcrossTheMound()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MoundMaterial);
            Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, "stock URP Lit, no custom shader");
            Assert.AreEqual(MoundAlbedo, AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")));
            Assert.AreEqual(new Vector2(UniqueTiling, UniqueTiling), m.GetTextureScale("_BaseMap"));
            Assert.AreEqual(Vector2.zero, m.GetTextureOffset("_BaseMap"));
            Assert.AreEqual(MoundOcclusion, AssetDatabase.GetAssetPath(m.GetTexture("_OcclusionMap")));
            Assert.IsTrue(m.IsKeywordEnabled("_OCCLUSIONMAP"), "occlusion map keyword (else URP Lit ignores the map)");
            Assert.AreEqual(1f, m.GetFloat("_OcclusionStrength"));

            var occ = (TextureImporter)AssetImporter.GetAtPath(MoundOcclusion);
            Assert.IsFalse(occ.sRGBTexture, "occlusion is data: linear");
            var alb = (TextureImporter)AssetImporter.GetAtPath(MoundAlbedo);
            Assert.IsTrue(alb.sRGBTexture);
            Assert.AreEqual(TextureWrapMode.Clamp, alb.wrapMode, "unique map: clamp, never wrap");
        }

        [TestCase(MoundFbx)]
        [TestCase(EmptyFbx)]
        public void MoundUVs_TimesTheTiling_StayInsideTheUniqueMap(string fbx)
        {
            var uvs = FbxMesh(fbx).uv.Select(u => u * UniqueTiling).ToArray();
            Assert.That(uvs.Min(u => Mathf.Min(u.x, u.y)), Is.GreaterThanOrEqualTo(0f), fbx);
            Assert.That(uvs.Max(u => Mathf.Max(u.x, u.y)), Is.LessThanOrEqualTo(1f), fbx);
        }

        // ---- volume: darker dips / base, crest untouched -----------------------------------------

        /// <summary>
        /// Under the mesh: the base ring (height &lt; 15 % of the peak) samples a darker albedo than the crest (&gt; 85 %), by
        /// at least 7 % (shipped ~13 %; a flat tile gives ~0 %), and a lower occlusion (&gt;= 0.1 below; shipped 1.0 vs ~0.8).
        /// The darkening is warm: its green drops more than its red (amber, not grey).
        /// </summary>
        [Test]
        public void Albedo_IsDarkerAndWarmerAtTheBase_ThanOnTheCrest_AndOcclusionFollows()
        {
            var alb = LoadPng(MoundAlbedo); var occ = LoadPng(MoundOcclusion);
            try
            {
                var verts = MoundVerts(out float peak);
                var crest = verts.Where(p => p.h > 0.85f * peak).ToList();
                var bse = verts.Where(p => p.h < 0.15f * peak).ToList();
                Assert.That(crest.Count, Is.GreaterThan(30)); Assert.That(bse.Count, Is.GreaterThan(30));
                Color Mean(Texture2D t, List<(float h, Vector2 uv)> s)
                {
                    var c = Color.clear; foreach (var p in s) c += t.GetPixelBilinear(p.uv.x, p.uv.y); return c / s.Count;
                }
                var cc = Mean(alb, crest); var cb = Mean(alb, bse);
                float lc = Luma(cc), lb = Luma(cb), oc = Mean(occ, crest).r, ob = Mean(occ, bse).r;
                string info = FormattableString.Invariant($"crest luma {lc:0.0} occ {oc:0.00} RGB {cc}, base luma {lb:0.0} occ {ob:0.00} RGB {cb}");
                TestContext.WriteLine(info);
                Assert.LessOrEqual(lb / lc, 0.93f, "the base / dips must sample a darker albedo than the crest. " + info);
                Assert.LessOrEqual(ob, oc - 0.1f, "the occlusion map must darken the base. " + info);
                Assert.Greater(cc.g / cc.r - cb.g / cb.r, 0.01f, "the dip darkening is amber (green drops more than red). " + info);
            }
            finally { UnityEngine.Object.DestroyImmediate(alb); UnityEngine.Object.DestroyImmediate(occ); }
        }

        // ---- kernel clusters on the crest --------------------------------------------------------

        /// <summary>
        /// Kernels are the only warm texels in the map (R - B &gt; 30 / 255; the neutral grain tile peaks at 9): present
        /// (0.1 % - 3 % of the map) and all within 0.04 (unique-UV) of a vertex above 65 % of the peak, i.e. on the crest.
        /// </summary>
        [Test]
        public void KernelClusters_ArePresent_AndOnlyOnTheCrest()
        {
            var alb = LoadPng(MoundAlbedo); var tile = LoadPng(GrainTile);
            try
            {
                Assert.Less(tile.GetPixels32().Max(c => c.r - c.b), 30, "the grain tile itself must stay neutral (the kernel detector relies on it)");
                var px = alb.GetPixels32(); int w = alb.width, h = alb.height;
                var kernel = new List<Vector2>();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) { var c = px[y * w + x]; if (c.r - c.b > 30) kernel.Add(new Vector2((x + 0.5f) / w, (y + 0.5f) / h)); }
                float frac = kernel.Count / (float)px.Length;
                TestContext.WriteLine(FormattableString.Invariant($"kernel texels {kernel.Count} ({frac:P2})"));
                Assert.That(frac, Is.InRange(0.001f, 0.03f), "a few kernel clusters");
                var crestUv = MoundVerts(out float peak).Where(p => p.h > 0.65f * peak).Select(p => p.uv).ToArray();
                float worst = 0f;
                for (int i = 0; i < kernel.Count; i += 7)
                    worst = Mathf.Max(worst, crestUv.Min(u => (u - kernel[i]).magnitude));
                Assert.Less(worst, 0.04f, "kernel texel off the crest (UV distance to the nearest crest vertex)");
            }
            finally { UnityEngine.Object.DestroyImmediate(alb); UnityEngine.Object.DestroyImmediate(tile); }
        }
    }
}
