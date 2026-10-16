using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CluckWars.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 decision E4 (a): after the D1 gold mound, the all-yellow Fatty read as part of the food pile (match camera hue gap 2-3 deg),
    /// so its albedo atlas was recoloured to a buff hen: CREAM breast / face, ORANGE-BROWN back, wings and tail, with the comb, wattle,
    /// beak, eyes and feet left as they were. Fatty <c>TintStrength</c> 0.25 -> 0.1 (TintColor #F5D75A unchanged: it is also the rival
    /// chevron / UI class colour) so the yellow wash does not turn the cream back into yellow.
    /// </summary>
    /// <remarks>
    /// The test reads the shipped mesh (rest pose, mesh space: Z up, forward = from the body centre toward the Beak-bone vertices) and samples
    /// the atlas at TRIANGLE CENTROIDS (vertex UVs sit on island borders, which carry dark seam colour), times the class wash. It asserts the
    /// features, not one bounding number: the breast is lighter and far less saturated than the back, the back is orange-brown and not yellow,
    /// and the comb and feet are still their own colours. The old yellow atlas fails the breast-chroma and back-hue checks.
    /// Rendered numbers (match camera, both tiers): Captures/env3d_after/D/E4/ and STATE.md E4.
    /// </remarks>
    public class Phase8ChunkE4Tests
    {
        private struct Sample { public Vector3 P, N; public Color C; }

        private static ChickenClassRegistrySO.Entry Fatty()
        {
            var reg = TestAssets.Load<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsTrue(reg.TryGet(ChickenClass.Fatty, out var e), "Fatty missing from the registry");
            return e;
        }

        private static List<Sample> Samples(out Vector3 fwd)
        {
            var e = Fatty();
            var smr = e.ModelPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(smr, "Fatty model has no SkinnedMeshRenderer");
            var mesh = smr.sharedMesh;
            var tex = smr.sharedMaterial.GetTexture("_BaseMap");
            Assert.IsNotNull(tex, "Fatty material has no _BaseMap");
            string path = AssetDatabase.GetAssetPath(tex);
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(decoded, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), path))), path);
                var wash = Color.Lerp(Color.white, e.TintColor, Mathf.Clamp01(e.TintStrength));
                var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var bw = mesh.boneWeights; var tri = mesh.triangles;
                int beak = Array.FindIndex(smr.bones, b => b.name == "Beak");
                Assert.GreaterOrEqual(beak, 0, "no Beak bone");
                Vector3 all = Vector3.zero, bk = Vector3.zero; int nb = 0;
                for (int i = 0; i < v.Length; i++) { all += v[i]; if (bw[i].boneIndex0 == beak) { bk += v[i]; nb++; } }
                Assert.Greater(nb, 0, "no vertices weighted to the Beak bone");
                fwd = bk / nb - all / v.Length; fwd.z = 0f; fwd.Normalize();
                var list = new List<Sample>(tri.Length / 3);
                for (int t = 0; t < tri.Length; t += 3)
                {
                    int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                    var u = (uv[a] + uv[b] + uv[c]) / 3f;
                    var col = decoded.GetPixelBilinear(u.x, u.y);
                    list.Add(new Sample { P = (v[a] + v[b] + v[c]) / 3f, N = (n[a] + n[b] + n[c]).normalized, C = new Color(col.r * wash.r, col.g * wash.g, col.b * wash.b, 1f) });
                }
                return list;
            }
            finally { Object.DestroyImmediate(decoded); }
        }

        private static bool IsRedOrDark(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float val);
            return ((h * 360f < 14f || h * 360f > 335f) && s > 0.55f) || val < 0.2f;
        }

        private static Vector3 MedianLab(IEnumerable<Sample> s)
        {
            var labs = s.Select(x => Phase8ChunkD8Tests.ToLab(x.C)).ToArray();
            Assert.Greater(labs.Length, 100, "too few samples in the region");
            float Med(Func<Vector3, float> f) { var a = labs.Select(f).OrderBy(x => x).ToArray(); return a[a.Length / 2]; }
            return new Vector3(Med(l => l.x), Med(l => l.y), Med(l => l.z));
        }

        private static float Chroma(Vector3 lab) => Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z);
        private static float HueAb(Vector3 lab) => (Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg + 360f) % 360f;

        [Test]
        public void Fatty_IsACreamBreastedHen_WithAnOrangeBrownBack()
        {
            var s = Samples(out var fwd);
            var body = s.Where(x => !IsRedOrDark(x.C)).ToList();
            // chest front (faces forward, mid height) / top of the back (faces up, below the head) / rump (faces backward)
            var breast = MedianLab(body.Where(x => Vector3.Dot(x.N, fwd) > 0.6f && x.P.z > 0.3f && x.P.z < 0.9f));
            var backTop = MedianLab(body.Where(x => x.N.z > 0.6f && x.P.z > 0.6f && x.P.z < 1.0f));
            var rear = MedianLab(body.Where(x => Vector3.Dot(x.N, fwd) < -0.6f && x.P.z > 0.4f && x.P.z < 0.9f));
            TestContext.Out.WriteLine($"breast L {breast.x:0.0} C {Chroma(breast):0.0} h {HueAb(breast):0} | back top L {backTop.x:0.0} C {Chroma(backTop):0.0} h {HueAb(backTop):0} | rear L {rear.x:0.0} C {Chroma(rear):0.0} h {HueAb(rear):0}");

            Assert.Less(Chroma(breast), 28f, "breast must be cream (low chroma), not yellow");
            Assert.Greater(breast.x, backTop.x + 5f, "cream breast lighter than the back");
            Assert.Less(Chroma(breast), 0.65f * Chroma(backTop), "breast clearly less saturated than the back");
            foreach (var (name, lab) in new[] { ("back top", backTop), ("rear", rear) })
            {
                Assert.That(HueAb(lab), Is.InRange(40f, 66f), $"{name} must be orange-brown (CIELAB hue 40-66; the old yellow was ~76-85)");
                Assert.That(Chroma(lab), Is.InRange(25f, 55f), $"{name} chroma: a brown, not a saturated orange or a grey");
                Assert.Less(lab.x, 50f, $"{name} must be darker than the breast reads (brown)");
            }
        }

        [Test]
        public void Fatty_Comb_AndFeet_KeepTheirOwnColours()
        {
            var s = Samples(out _);
            var comb = s.Where(x => x.P.z > 1.42f).Select(x => x.C).ToArray();       // the comb is the only geometry above the crown
            var feet = s.Where(x => x.P.z < 0.05f).Select(x => x.C).ToArray();       // toes
            int combRed = comb.Count(c => { Color.RGBToHSV(c, out var h, out var sat, out _); return (h * 360f < 15f || h * 360f > 340f) && sat > 0.8f; });
            int feetWarm = feet.Count(c => { Color.RGBToHSV(c, out var h, out var sat, out _); return (h * 360f < 40f || h * 360f > 340f) && sat > 0.8f; });
            Assert.Greater(combRed, comb.Length * 0.8f, $"comb red {combRed}/{comb.Length}: the recolour must not touch the comb");
            Assert.Greater(feetWarm, feet.Length * 0.8f, $"feet orange {feetWarm}/{feet.Length}: the recolour must not touch the feet");
        }

        [Test]
        public void Fatty_Wash_IsLight_AndTheClassColourIsUnchanged()
        {
            var e = Fatty();
            Assert.AreEqual(0.1f, e.TintStrength, 1e-4f, "E4: a light wash, so #F5D75A does not yellow the cream breast");
            Assert.AreEqual(0.9607843f, e.TintColor.r, 1e-4f, "ART.md Fatty #F5D75A (UI / rival chevron class colour) unchanged");
            Assert.AreEqual(0.8431373f, e.TintColor.g, 1e-4f);
            Assert.AreEqual(0.3529412f, e.TintColor.b, 1e-4f);
        }
    }
}
