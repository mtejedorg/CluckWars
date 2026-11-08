using System.IO;
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

        // ---- F2: the two identity stripes on the CLOSED egg -------------------------------------

        private struct HalfSpan { public float MinV, MaxV, MinY, MaxY; }

        /// <summary>
        /// The closed egg exactly as ChickenShellVisuals seats it (the cup at the shell origin, the cap at its authored seam
        /// position, i.e. the FBX layout): per half, the uv.v and FBX-root height spans; and the largest |v - y / eggHeight|.
        /// </summary>
        private static (HalfSpan cup, HalfSpan cap, float worstVvsHeight) MeasureClosedEgg()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Meshes/Fx/EggShell.fbx");
            Assert.IsNotNull(fbx);
            var spans = new System.Collections.Generic.Dictionary<string, HalfSpan>();
            var points = new System.Collections.Generic.List<Vector2>();   // (height, v)
            foreach (var mf in fbx.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name != "EggShell_Top" && mf.name != "EggShell_Bottom") continue;
                var s = new HalfSpan { MinV = 9, MaxV = -9, MinY = 9, MaxY = -9 };
                var verts = mf.sharedMesh.vertices;
                var uvs = mf.sharedMesh.uv;
                for (int i = 0; i < verts.Length; i++)
                {
                    float y = fbx.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[i])).y;
                    s.MinV = Mathf.Min(s.MinV, uvs[i].y); s.MaxV = Mathf.Max(s.MaxV, uvs[i].y);
                    s.MinY = Mathf.Min(s.MinY, y); s.MaxY = Mathf.Max(s.MaxY, y);
                    points.Add(new Vector2(y, uvs[i].y));
                }
                spans[mf.name] = s;
            }
            Assert.IsTrue(spans.ContainsKey("EggShell_Bottom") && spans.ContainsKey("EggShell_Top"));
            var cup = spans["EggShell_Bottom"];
            var cap = spans["EggShell_Top"];
            float h0 = cup.MinY, h = cap.MaxY - cup.MinY;
            float worst = points.Max(p => Mathf.Abs(p.y - (p.x - h0) / h));
            return (cup, cap, worst);
        }

        [Test]
        public void BothStripeCentres_FallOnTheClosedEgg_AtAboutThirtyAndSeventyPercent_ClearOfTheSeam()
        {
            var (cup, cap, worst) = MeasureClosedEgg();
            // v is the closed egg's normalised height (inner-wall vertices carry the outer v, hence the shell-thickness slack),
            // so a v centre IS a fraction of the closed egg's height.
            Assert.Less(worst, 0.035f, "EggShell.fbx uv.v must be the closed egg's normalised height");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Vector4 centres = mat.GetVector("_StripeCenters");
            float half = mat.GetFloat("_StripeHalfHeight");
            float lower = Mathf.Min(centres.x, centres.y), upper = Mathf.Max(centres.x, centres.y);

            // The seam zone is where the two halves overlap in v (the zigzag): a band there is cut in two by the seam.
            float seamLo = cap.MinV, seamHi = cup.MaxV;
            Assert.Less(seamLo, seamHi, "the halves overlap along a zigzag seam");

            Assert.That(lower - half, Is.GreaterThan(cup.MinV), "the lower band lies on the cup");
            Assert.That(lower + half, Is.LessThan(seamLo), "the lower band is wholly below the seam (one band, on the cup)");
            Assert.That(upper - half, Is.GreaterThan(seamHi), "the upper band is wholly above the seam (one band, on the cap)");
            Assert.That(upper + half, Is.LessThan(cap.MaxV), "the upper band lies on the cap");

            Assert.AreEqual(0.30f, lower, 0.08f, "the lower band reads at about 30 % of the closed egg");
            Assert.AreEqual(0.70f, upper, 0.08f, "the upper band reads at about 70 % of the closed egg");

            var shaderDefault = Shader.Find(ShaderName);
            int idx = shaderDefault.FindPropertyIndex("_StripeCenters");
            Assert.Less(Vector4.Distance(centres, shaderDefault.GetPropertyDefaultVectorValue(idx)), 1e-5f, "the shader default and the shipped material agree");
        }

        [Test]
        public void Cracks_AreAntiAliasedWithSmoothstep_NotAHardStep()
        {
            string path = AssetDatabase.GetAssetPath(Shader.Find(ShaderName));
            string src = File.ReadAllText(path);
            string code = string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
            StringAssert.Contains("smoothstep(_Crack - ", code, "the crack edge is anti-aliased over one pixel of the mask");
            StringAssert.Contains("fwidth(mask)", code);
            Assert.IsFalse(code.Contains("step(mask, _Crack)"), "the aliased hard step on the crack mask is gone");
        }
    }
}
