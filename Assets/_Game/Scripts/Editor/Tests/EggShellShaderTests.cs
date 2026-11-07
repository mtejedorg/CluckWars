using System.Linq;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Feedback batch D3 (Egg Shell shader): the shared material uses the hand-written URP shader, the shader carries the
    /// properties <see cref="ChickenShellVisuals"/> and the spec rely on, it compiles, and the material defaults match the
    /// shipped palette ink and the spec cream/smoothness. Derived from the shipped assets, not copied fixtures.
    /// </summary>
    public sealed class EggShellShaderTests
    {
        private const string MaterialPath = "Assets/_Game/Art/Materials/Egg_Shell.mat";
        private const string ShaderName = "Cluck Wars/Egg Shell";

        private static readonly string[] Required = { "_BaseColor", "_SpeckleTex", "_StripeColor", "_CrackTex", "_Crack" };

        [Test]
        public void Material_UsesTheEggShellShader()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.IsNotNull(mat, MaterialPath + " missing");
            Assert.IsNotNull(mat.shader);
            Assert.AreEqual(ShaderName, mat.shader.name);
        }

        [Test]
        public void Shader_HasTheFiveSpecProperties_AndCompiles()
        {
            var shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, ShaderName + " not found");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "the Egg Shell shader has compile errors");
            Assert.IsTrue(shader.isSupported, "the Egg Shell shader is not supported on this graphics API");
            var names = Enumerable.Range(0, shader.GetPropertyCount()).Select(shader.GetPropertyName).ToList();
            foreach (var n in Required) CollectionAssert.Contains(names, n, "shader property " + n);
        }

        [Test]
        public void Material_HasTheTexturesAndTheSpecValues()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.AreEqual("Fx_EggSpeckle", mat.GetTexture("_SpeckleTex")?.name);
            Assert.AreEqual("Fx_EggCrackMask", mat.GetTexture("_CrackTex")?.name);
            Assert.AreEqual(2f, mat.GetFloat("_SpeckleTiling"), 1e-4f, "speckle tiled 2x");
            Assert.AreEqual(0.35f, mat.GetFloat("_Smoothness"), 1e-4f);
            var cream = mat.GetColor("_BaseColor");
            Assert.AreEqual(0.98f, cream.r, 1e-3f); Assert.AreEqual(0.94f, cream.g, 1e-3f); Assert.AreEqual(0.86f, cream.b, 1e-3f);
            var ink = FeedbackPalette.Current.InkColor;
            var m = mat.GetColor("_InkColor");
            Assert.AreEqual(ink.r, m.r, 1e-3f); Assert.AreEqual(ink.g, m.g, 1e-3f); Assert.AreEqual(ink.b, m.b, 1e-3f, "ink default = FeedbackPalette InkColor");
            Assert.AreEqual(0f, mat.GetFloat("_Crack"), "no crack by default; the MPB drives it");
        }

        [Test]
        public void EggShellMesh_MapsUvVToWholeEggHeight_SoTheStripeBandsNeedNoPerHalfOffset()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Meshes/Fx/EggShell.fbx");
            Assert.IsNotNull(fbx);
            float topMin = 9, topMax = -9, botMin = 9, botMax = -9;
            foreach (var mf in fbx.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name != "EggShell_Top" && mf.name != "EggShell_Bottom") continue;
                bool top = mf.name == "EggShell_Top";
                foreach (var uv in mf.sharedMesh.uv)
                {
                    if (top) { topMin = Mathf.Min(topMin, uv.y); topMax = Mathf.Max(topMax, uv.y); }
                    else { botMin = Mathf.Min(botMin, uv.y); botMax = Mathf.Max(botMax, uv.y); }
                }
            }
            Assert.That(botMin, Is.LessThan(0.02f), "the cup starts at the bottom of the whole egg");
            Assert.That(topMax, Is.GreaterThan(0.98f), "the cap ends at the top of the whole egg");
            Assert.That(topMin, Is.GreaterThan(0.45f).And.LessThan(botMax), "the halves meet near the seam in the SAME v space");
            Assert.That(botMax, Is.GreaterThan(0.5f).And.LessThan(0.9f));
        }
    }
}
