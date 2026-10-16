using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluckWars.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CluckWars.EditorTools
{
    /// <summary>
    /// Phase 8 C F1 stage 2: bakes the Mobile-tier surround haze into material COPIES. For every renderer under
    /// <c>Map.unity/MapSurroundings</c> whose whole footprint is outside the fence it makes (or updates in place, so
    /// GUIDs and scene references survive a re-run) a hazed copy of each of its materials in <see cref="HazedFolder"/>,
    /// then writes the renderer -> copies mapping onto a <see cref="SurroundHazeSet"/> on the <c>MapSurroundings</c>
    /// root and the copy manifest to <see cref="MapAssetPath"/>. The renderers KEEP their original materials in the
    /// scene: the copies are applied at runtime on the Mobile tier only. Re-runnable; owns exactly the
    /// <see cref="HazedFolder"/> contents, the manifest asset and the <see cref="SurroundHazeSet"/> component.
    /// </summary>
    /// <remarks>
    /// <para><b>Numbers.</b> Everything comes from <see cref="HazeSettings"/> (colour, square-distance rule, ramp,
    /// max), the same source the PC pass reads, so the two tiers cannot drift apart: re-run this after changing it.</para>
    /// <para><b>Colour match.</b> A copy has <c>_BaseColor = original * (1 - a)</c> and
    /// <c>_EmissionColor = haze * a</c>, which URP Lit renders as <c>lit * (1 - a) + haze * a</c>, the PC
    /// pass's <c>lerp(lit, haze, a)</c>. Both are written in colour-buffer space: the haze is the exact vector the PC
    /// shader blends with (<see cref="HazeSettings.ShaderColour"/>) and material colours are converted only when the
    /// project is in Linear space (it is in Gamma today), so the match holds in either colour space. Unlike an albedo lerp it needs no guess of the lighting, hazes shadowed and
    /// sunlit sides alike, and works on textured materials (a tint can only darken a texture). Residual differences:
    /// the dielectric specular (smoothness 0 to 0.08 here, negligible) is not scaled, and PC tonemaps after the haze
    /// while Mobile has no post.</para>
    /// <para><b>Banding.</b> Objects: <see cref="BandCount"/> bands of <see cref="BandWidth"/> m of square distance from
    /// <see cref="HazeSettings.RampStart"/>; a renderer's band is that of its bounds centre and the band's amount is
    /// <see cref="HazeSettings.AmountAt"/> at the band's middle (the last band, from 57 m, runs to infinity and sits at
    /// the 60 m ramp end, i.e. the max). Error vs the PC per-pixel ramp: at most half a band, 3 m = 0.055 amount.
    /// Renderers before the ramp start (fence-hedge gap vignettes) get no copy: their amount is 0 on PC too.</para>
    /// <para><b>Surround grass.</b> One 260 m plane spans the whole ramp, so one flat amount would either haze the
    /// strip next to the hedge (0 on PC) or leave the far field dark. Its copy instead carries the ramp per texel in
    /// two small linear textures on the stock Lit slots (<c>_BaseMap</c> = 1 - a, <c>_EmissionMap</c> = a / max,
    /// <c>_EmissionColor</c> = haze * max), sampled through the plane's own top-face UVs: same mesh, same shader,
    /// no vertex colours (stock URP Lit ignores them) and no extra geometry.</para>
    /// </remarks>
    public static class SurroundHazeBaker
    {
        public const string MapScenePath = "Assets/_Game/Scenes/Map.unity";
        public const string SurroundingsName = "MapSurroundings";
        public const string GrassName = "SurroundGrass";
        public const string HazedFolder = "Assets/_Game/Art/Materials/Hazed";
        public const string MapAssetPath = "Assets/_Game/Data/SurroundHazeMap.asset";
        public const string GrassBaseRampPath = HazedFolder + "/SurroundGrass_HazeBase.png";
        public const string GrassEmissionRampPath = HazedFolder + "/SurroundGrass_HazeEmission.png";
        public const string GrassCopySuffix = "_HzRamp";

        /// <summary>Width of one distance band (metres of square distance).</summary>
        public const float BandWidth = 6f;
        /// <summary>Bands from <see cref="HazeSettings.RampStart"/>; the last one is open-ended.</summary>
        public const int BandCount = 6;
        /// <summary>Ramp texture size: 256 texels over the 260 m plane, ~1 m per texel.</summary>
        public const int RampTextureSize = 256;

        private static readonly string[] BaseColourProps = { "_BaseColor", "_Color" };
        private const string EmissionColour = "_EmissionColor";
        private const string EmissionKeyword = "_EMISSION";

        /// <summary>Band of a square distance from the arena centre; -1 before the ramp starts (no haze).</summary>
        public static int BandOf(float squareDistance)
        {
            if (squareDistance < HazeSettings.RampStart) return -1;
            return Mathf.Min(Mathf.FloorToInt((squareDistance - HazeSettings.RampStart) / BandWidth), BandCount - 1);
        }

        /// <summary>Inner edge of band <paramref name="band"/> (square distance, m).</summary>
        public static float BandEdge(int band) => HazeSettings.RampStart + band * BandWidth;

        /// <summary>Amount baked into band <paramref name="band"/>: <see cref="HazeSettings.AmountAt"/> at its middle.</summary>
        public static float BandAmount(int band) =>
            HazeSettings.AmountAt(new Vector3(HazeSettings.ArenaCentre.x + BandEdge(band) + 0.5f * BandWidth, 0f, HazeSettings.ArenaCentre.y));

        /// <summary>
        /// Smallest square distance from the arena centre of any point of a world AABB's XZ footprint. Larger than
        /// <see cref="HazeSettings.FenceHalfSize"/> = entirely outside the fence.
        /// </summary>
        public static float NearestSquareDistance(Bounds b)
        {
            var c = HazeSettings.ArenaCentre;
            float NearestAbs(float lo, float hi) => lo <= 0f && hi >= 0f ? 0f : Mathf.Min(Mathf.Abs(lo), Mathf.Abs(hi));
            return Mathf.Max(NearestAbs(b.min.x - c.x, b.max.x - c.x), NearestAbs(b.min.z - c.y, b.max.z - c.y));
        }

        // ---- menu -------------------------------------------------------------------------------------------------

        [MenuItem("Cluck Wars/Map/Bake Mobile Surround Haze (in place)", priority = 25)]
        public static void BakeIntoMapScene()
        {
            Scene map = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).path == MapScenePath) map = SceneManager.GetSceneAt(i);
            bool openedHere = !map.IsValid() || !map.isLoaded;
            if (openedHere) map = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
            try
            {
                string report = Bake(map);
                EditorSceneManager.MarkSceneDirty(map);
                if (!EditorSceneManager.SaveScene(map)) Debug.LogError($"[SurroundHazeBaker] Failed to save {MapScenePath}.");
                Debug.Log("[SurroundHazeBaker] " + report);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(map, removeScene: true);
            }
        }

        /// <summary>Builds / updates every copy, the manifest and the scene set in <paramref name="map"/>; returns a summary.</summary>
        public static string Bake(Scene map)
        {
            var surroundings = map.GetRootGameObjects().FirstOrDefault(g => g.name == SurroundingsName);
            if (surroundings == null) throw new System.InvalidOperationException($"{MapScenePath} has no {SurroundingsName} root.");
            EnsureFolder(HazedFolder);

            var produced = new HashSet<string>();
            var copies = new Dictionary<string, (Material orig, Material hazed, int band, float amount, int uses)>();
            var entries = new List<SurroundHazeSet.Entry>();
            var errors = new List<string>();
            int skippedGap = 0;
            var perBand = new int[BandCount];

            foreach (var r in surroundings.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                string where = PathOf(r.transform);
                var originals = r.sharedMaterials;
                if (originals.Length == 0 || originals.Any(m => m == null)) { errors.Add($"{where}: missing material"); continue; }
                if (originals.Any(m => !m.shader.name.StartsWith("Universal Render Pipeline/")))
                { errors.Add($"{where}: not a URP shader ({string.Join(", ", originals.Select(m => m.shader.name))})"); continue; }
                if (originals.Any(m => m.IsKeywordEnabled(EmissionKeyword)))
                { errors.Add($"{where}: original already uses emission; the emission haze would overwrite it"); continue; }

                bool isGrass = r.transform.parent == surroundings.transform && r.name == GrassName;
                if (isGrass)
                {
                    var hazed = new Material[originals.Length];
                    for (int s = 0; s < originals.Length; s++)
                    {
                        string key = originals[s].name + GrassCopySuffix;
                        if (!copies.TryGetValue(key, out var c))
                        {
                            BuildRampTextures(r, out var baseRamp, out var emissionRamp);
                            produced.Add(GrassBaseRampPath); produced.Add(GrassEmissionRampPath);
                            string path = $"{HazedFolder}/{key}.mat";
                            var m = WriteCopy(originals[s], path, x => HazeRamp(x, baseRamp, emissionRamp));
                            produced.Add(path);
                            c = (originals[s], m, -1, HazeSettings.MaxAmount, 0);
                        }
                        c.uses++; copies[key] = c; hazed[s] = c.hazed;
                    }
                    entries.Add(new SurroundHazeSet.Entry(r, originals, hazed, HazeSettings.MaxAmount, -1));
                    continue;
                }

                var bounds = r.bounds;
                int band = BandOf(HazeSettings.SquareDistance(bounds.center));
                // Before the ramp (fence-hedge gap vignettes; a corner flower decal even tucks 0.1 m under the fence line):
                // amount 0 on PC as well, so no copy.
                if (band < 0) { skippedGap++; continue; }
                if (NearestSquareDistance(bounds) <= HazeSettings.FenceHalfSize)
                { errors.Add($"{where}: footprint reaches inside the fence (nearest square distance {NearestSquareDistance(bounds):0.00} m), left out"); continue; }
                float amount = BandAmount(band);
                var hz = new Material[originals.Length];
                for (int s = 0; s < originals.Length; s++)
                {
                    string key = $"{originals[s].name}_Hz{band + 1}";
                    if (!copies.TryGetValue(key, out var c))
                    {
                        string path = $"{HazedFolder}/{key}.mat";
                        var m = WriteCopy(originals[s], path, x => HazeFlat(x, amount));
                        produced.Add(path);
                        c = (originals[s], m, band, amount, 0);
                    }
                    c.uses++; copies[key] = c; hz[s] = c.hazed;
                }
                perBand[band]++;
                entries.Add(new SurroundHazeSet.Entry(r, originals, hz, amount, band));
            }

            // Stale copies from an earlier bake (renamed / removed originals) go; everything this bake owns is in `produced`.
            int removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { HazedFolder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(p) || produced.Contains(p)) continue;
                if (AssetDatabase.DeleteAsset(p)) removed++;
            }

            var mapAsset = AssetDatabase.LoadAssetAtPath<SurroundHazeMapSO>(MapAssetPath);
            if (mapAsset == null)
            {
                mapAsset = ScriptableObject.CreateInstance<SurroundHazeMapSO>();
                AssetDatabase.CreateAsset(mapAsset, MapAssetPath);
            }
            var edges = Enumerable.Range(0, BandCount).Select(BandEdge).ToArray();
            var amounts = Enumerable.Range(0, BandCount).Select(BandAmount).ToArray();
            mapAsset.EditorSet(edges, amounts,
                copies.OrderBy(kv => kv.Key, System.StringComparer.Ordinal)
                      .Select(kv => new SurroundHazeMapSO.Copy(kv.Value.orig, kv.Value.hazed, kv.Value.band, kv.Value.amount, kv.Value.uses)).ToList(),
                entries.Count);
            EditorUtility.SetDirty(mapAsset);

            var set = surroundings.GetComponent<SurroundHazeSet>();
            if (set == null) set = surroundings.AddComponent<SurroundHazeSet>();
            set.EditorSetEntries(entries, mapAsset);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            // The URP postprocessor re-validates new / changed materials after the save; check the keyword survived it.
            foreach (var c in copies.Values)
                if (!c.hazed.IsKeywordEnabled(EmissionKeyword))
                    errors.Add($"{c.hazed.name}: _EMISSION is off after import (URP validation); the copy would only darken");

            foreach (var e in errors) Debug.LogError("[SurroundHazeBaker] " + e);
            return $"renderers {entries.Count} (per band {string.Join("/", perBand)} + grass ramp), copies {copies.Count}, " +
                   $"gap renderers without haze {skippedGap}, stale assets removed {removed}, errors {errors.Count}.";
        }

        // ---- materials -------------------------------------------------------------------------------------------

        /// <summary>Copy of <paramref name="original"/> configured by <paramref name="haze"/>, written over the asset at
        /// <paramref name="path"/> when it exists (keeps its GUID, so scene references survive a re-bake).</summary>
        private static Material WriteCopy(Material original, string path, System.Action<Material> haze)
        {
            var m = new Material(original) { name = Path.GetFileNameWithoutExtension(path) };
            haze(m);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(m, path);
                return m;
            }
            EditorUtility.CopySerialized(m, existing);
            existing.name = m.name;
            Object.DestroyImmediate(m);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>Flat amount: base * (1 - a), emission = haze * a (colour-buffer space).</summary>
        private static void HazeFlat(Material m, float a)
        {
            foreach (var p in BaseColourProps)
            {
                if (!m.HasProperty(p)) continue;
                var c = m.GetColor(p);
                var g = FromShader(ToShader(c) * (1f - a));
                g.a = c.a;
                m.SetColor(p, g);
            }
            SetEmission(m, ShaderHaze * a);
        }

        /// <summary>Per-texel ramp (surround grass): the factors ride in the base / emission maps, the colours stay flat.</summary>
        private static void HazeRamp(Material m, Texture2D baseRamp, Texture2D emissionRamp)
        {
            foreach (var p in new[] { "_BaseMap", "_MainTex" })
            {
                if (!m.HasProperty(p)) continue;
                if (m.GetTexture(p) != null)
                    throw new System.InvalidOperationException($"{m.name}: the ramp copy needs an untextured original ({p} is set).");
                m.SetTexture(p, baseRamp);
                m.SetTextureScale(p, Vector2.one);
                m.SetTextureOffset(p, Vector2.zero);
            }
            m.SetTexture("_EmissionMap", emissionRamp);
            SetEmission(m, ShaderHaze * HazeSettings.MaxAmount);
        }

        /// <summary>
        /// The haze colour exactly as the PC pass blends it into the colour buffer: <see cref="HazeSettings.ShaderColour"/>
        /// (a vector, so Unity applies no colour-space conversion to it).
        /// </summary>
        private static Color ShaderHaze
        {
            get { var h = HazeSettings.ShaderColour; return new Color(h.x, h.y, h.z, 1f); }
        }

        private static bool LinearSpace => QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>Material colour as the value the shader receives (Unity linearises material colours only in Linear space).</summary>
        private static Color ToShader(Color materialColour) => LinearSpace ? materialColour.linear : materialColour;

        /// <summary>Inverse of <see cref="ToShader"/>: the material colour that makes the shader receive <paramref name="shaderValue"/>.</summary>
        private static Color FromShader(Color shaderValue) => LinearSpace ? shaderValue.gamma : shaderValue;

        /// <param name="shaderValue">Emission as the shader should receive it (colour-buffer space).</param>
        private static void SetEmission(Material m, Color shaderValue)
        {
            var g = FromShader(shaderValue);
            g.a = 1f;
            m.SetColor(EmissionColour, g);
            m.EnableKeyword(EmissionKeyword);
            // URP's material validation (BaseShaderGUI.SetMaterialKeywords, run by its MaterialPostprocessor right after
            // import and by the inspector) sets _EMISSION = (globalIlluminationFlags & AnyEmissive) != 0: with None or
            // EmissiveIsBlack it silently switches the keyword back OFF in memory. BakedEmissive is what the URP inspector
            // writes for "Emission" on; it only matters to a lightmap bake, and these copies are never on a renderer then.
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }

        // ---- grass ramp textures -----------------------------------------------------------------------------------

        /// <summary>
        /// Writes the two ramp textures for the surround grass plane, texel (i, j) holding the amount at the world point
        /// the plane's top face maps that UV to (affine fit of the top-face vertices; any UV orientation works).
        /// </summary>
        private static void BuildRampTextures(Renderer grass, out Texture2D baseRamp, out Texture2D emissionRamp)
        {
            var mf = grass.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) throw new System.InvalidOperationException($"{GrassName} has no mesh.");
            var verts = mesh.vertices; var normals = mesh.normals; var uvs = mesh.uv;
            var l2w = grass.transform.localToWorldMatrix;
            var top = Enumerable.Range(0, verts.Length).Where(i => l2w.MultiplyVector(normals[i]).normalized.y > 0.9f).ToList();
            if (top.Count < 3) throw new System.InvalidOperationException($"{GrassName}: no upward face found.");

            Vector3 W(int i) => l2w.MultiplyPoint3x4(verts[i]);
            int i0 = top[0];
            int i1 = top.First(i => (uvs[i] - uvs[i0]).sqrMagnitude > 0.01f);
            Vector2 d1 = uvs[i1] - uvs[i0];
            int i2 = top.First(i => { var d = uvs[i] - uvs[i0]; return Mathf.Abs(d1.x * d.y - d1.y * d.x) > 0.01f; });
            Vector2 d2 = uvs[i2] - uvs[i0];
            Vector3 p0 = W(i0), e1 = W(i1) - p0, e2 = W(i2) - p0;
            float det = d1.x * d2.y - d2.x * d1.y;
            Vector3 du = (d2.y * e1 - d1.y * e2) / det;   // world per unit u
            Vector3 dv = (-d2.x * e1 + d1.x * e2) / det;  // world per unit v
            Vector3 At(Vector2 uv) => p0 + (uv.x - uvs[i0].x) * du + (uv.y - uvs[i0].y) * dv;
            foreach (var i in top)
                if ((At(uvs[i]) - W(i)).sqrMagnitude > 1e-4f)
                    throw new System.InvalidOperationException($"{GrassName}: top face UVs are not an affine map of the face; the ramp would be wrong.");

            int n = RampTextureSize;
            var basePx = new Color32[n * n];
            var emisPx = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = HazeSettings.AmountAt(At(new Vector2((x + 0.5f) / n, (y + 0.5f) / n)));
                    byte b = (byte)Mathf.RoundToInt(255f * (1f - a));
                    byte e = (byte)Mathf.RoundToInt(255f * a / HazeSettings.MaxAmount);
                    basePx[y * n + x] = new Color32(b, b, b, 255);
                    emisPx[y * n + x] = new Color32(e, e, e, 255);
                }
            baseRamp = WriteRamp(GrassBaseRampPath, basePx, n);
            emissionRamp = WriteRamp(GrassEmissionRampPath, emisPx, n);
        }

        private static Texture2D WriteRamp(string path, Color32[] px, int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false, linear: true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;           // raw factors, not colours
            ti.mipmapEnabled = false;         // smooth ramp: nothing to alias
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.isReadable = false;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        private static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
