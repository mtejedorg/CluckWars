using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CluckWars.UI;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CluckWars.Tests
{
    /// <summary>
    /// Phase 8 D8 (audit L08): with the body-sized R3 rings, the part of a foot ring that runs under a food-pile mound
    /// was hidden, so a chicken on the far side of a pile showed a sliver of its colour (match camera: 46 % of the local
    /// Warrior's identity ring visible behind the home pile, the rival ring 70 %). Each ring now has a ghost child
    /// (<c>IdentityRingGhost</c> / <c>RivalRingGhost</c>, <c>RingGhost.mat</c>) that draws the hidden arc at 40 % alpha:
    /// ZTest Greater (only behind something) AND stencil bit 3, which only the mound's second material
    /// (<c>MoundStencilMark.mat</c>, the last opaque queue) writes where the mound is the front-most surface, so the
    /// ghost never shows through chicken bodies, fences or props. Visual only.
    /// </summary>
    public class Phase8ChunkD8RingGhostTests
    {
        private const string GhostMatPath = "Assets/_Game/Art/Materials/RingGhost.mat";
        private const string MarkMatPath = "Assets/_Game/Art/Materials/MoundStencilMark.mat";
        private const string MoundMatPath = "Assets/_Game/Art/Materials/FoodMound.mat";
        private const string GhostShaderPath = "Assets/_Game/Art/Shaders/RingGhost.shader";
        private const string MarkShaderPath = "Assets/_Game/Art/Shaders/MoundStencilMark.shader";

        private static Material Load(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(m, path + " missing");
            return m;
        }

        private static T Private<T>(object target, string field) =>
            (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        // ── Materials + shaders ───────────────────────────────────────────────────

        [Test]
        public void GhostMaterial_IsTransparent_ZTestGreater_StencilEqualOnTheMoundBit_At40Percent()
        {
            var g = Load(GhostMatPath);
            Assert.AreEqual("Cluck Wars/Ring Ghost", g.shader.name);
            Assert.AreEqual(GroundRingStyle.GhostAlpha, g.GetFloat("_GhostAlpha"), 1e-4f, "ghost alpha");
            Assert.AreEqual(0.4f, GroundRingStyle.GhostAlpha, 0.05f, "audit L08 asks for a ~40 % ghost");
            Assert.AreEqual((float)CompareFunction.Greater, g.GetFloat("_ZTest"), "only ring pixels something is in front of");
            Assert.GreaterOrEqual(g.renderQueue, (int)RenderQueue.Transparent, "ghost must draw after every opaque (the mound)");
            Assert.AreEqual(Load(MarkMatPath).GetFloat("_StencilRef"), g.GetFloat("_StencilRef"), "ghost reads the bit the mound writes");
            Assert.IsFalse(ShaderUtil.ShaderHasError(g.shader), "Ring Ghost shader has errors");

            string src = File.ReadAllText(GhostShaderPath);
            StringAssert.Contains("ZWrite Off", src);
            StringAssert.Contains("Comp Equal", src);
            StringAssert.Contains("ReadMask [_StencilRef]", src);
            StringAssert.Contains("unity_SpriteColor", src, "SpriteRenderer.color arrives in unity_SpriteColor in Unity 6, not the vertex colour");
            StringAssert.DoesNotContain("multi_compile", src, "keep the ghost to one variant (warm-up list)");
        }

        [Test]
        public void MoundStencilMark_IsTheLastOpaqueQueue_ColourlessAndShadowless_OnAUserStencilBit()
        {
            var m = Load(MarkMatPath);
            Assert.AreEqual("Cluck Wars/Mound Stencil Mark", m.shader.name);
            Assert.AreEqual((int)RenderQueue.GeometryLast, m.renderQueue,
                "must run after chickens / fences / props (so their pixels in front of the mound keep the bit clear) and still in the opaque pass");
            Assert.IsFalse(ShaderUtil.ShaderHasError(m.shader), "Mound Stencil Mark shader has errors");
            int bit = Mathf.RoundToInt(m.GetFloat("_StencilRef"));
            Assert.That(bit, Is.EqualTo(1).Or.EqualTo(2).Or.EqualTo(4).Or.EqualTo(8), "one bit inside URP's user stencil mask (bits 0-3)");

            string src = File.ReadAllText(MarkShaderPath);
            StringAssert.Contains("ColorMask 0", src);
            StringAssert.Contains("ZWrite Off", src);
            StringAssert.Contains("ZTest LEqual", src);
            StringAssert.Contains("Pass Replace", src);
            // Exactly one pass, unlit: no ShadowCaster (extra shadow), no DepthOnly / DepthNormals (prepass cost).
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(src, "\"LightMode\"").Count, "one LightMode tag");
            StringAssert.Contains("\"LightMode\" = \"SRPDefaultUnlit\"", src);
            Assert.AreEqual(1, m.shader.passCount, "one pass");
            StringAssert.DoesNotContain("multi_compile", src, "one variant (warm-up list)");
        }

        [Test]
        public void FoodPilePrefab_MoundRenderer_DrawsTheStencilMarkAfterItsMatteMaterial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.FoodPilePrefabPath);
            var t = prefab.transform.Find("Mound/MoundMesh");
            Assert.IsNotNull(t, "FoodPile.prefab has no Mound/MoundMesh");
            var r = t.GetComponent<MeshRenderer>();
            var mats = r.sharedMaterials;
            Assert.AreEqual(2, mats.Length, "MoundMesh: [FoodMound, MoundStencilMark]");
            Assert.AreEqual(MoundMatPath, AssetDatabase.GetAssetPath(mats[0]));
            Assert.AreEqual(MarkMatPath, AssetDatabase.GetAssetPath(mats[1]));
            Assert.AreEqual(1, t.GetComponent<MeshFilter>().sharedMesh.subMeshCount,
                "a single-submesh mesh: the extra material re-draws the whole mound (also after the empty-mesh swap)");
            var visuals = prefab.GetComponent<FoodPileVisuals>();
            foreach (var mesh in new[] { Private<Mesh>(visuals, "_fullMesh"), Private<Mesh>(visuals, "_emptyMesh") })
                if (mesh != null) Assert.AreEqual(1, mesh.subMeshCount, mesh.name);
        }

        [Test]
        public void ChickenPrefab_WiresTheGhostMaterial_OnBothRingOwners()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            var g = Load(GhostMatPath);
            Assert.AreSame(g, Private<Material>(prefab.GetComponent<ChickenWorldBars>(), "_ringGhostMaterial"), "ChickenWorldBars");
            Assert.AreSame(g, Private<Material>(prefab.GetComponent<RivalIndicator>(), "_ringGhostMaterial"), "RivalIndicator");
        }

        // ── Runtime wiring on the real prefab ────────────────────────────────────

        private static void AssertGhostMirrors(SpriteRenderer ring, SpriteRenderer ghost, string what)
        {
            Assert.IsNotNull(ghost, what + ": no ghost");
            Assert.AreEqual(ring.name + GroundRingStyle.GhostSuffix, ghost.name, what);
            Assert.AreSame(ring.transform, ghost.transform.parent, what + ": ghost must be a child of the ring (follows scale / position / active)");
            Assert.AreEqual(Vector3.zero, ghost.transform.localPosition, what);
            Assert.AreEqual(Vector3.one, ghost.transform.localScale, what);
            Assert.AreEqual(Quaternion.identity, ghost.transform.localRotation, what);
            Assert.AreSame(ring.sprite, ghost.sprite, what + ": ghost must draw the ring's own annulus");
            Assert.AreSame(Load(GhostMatPath), ghost.sharedMaterial, what);
            Assert.AreEqual(ring.sortingOrder + GroundRingStyle.GhostSortingOffset, ghost.sortingOrder, what + ": ghosts sort as one run below rings + edges");
        }

        [Test]
        public void IdentityRing_HasAGhost_ThatFollowsRadiusColourAndVisibility()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var bars = go.GetComponent<ChickenWorldBars>();
                Invoke(bars, "Awake");
                var ring = Private<SpriteRenderer>(bars, "_ring");
                var ghost = Private<SpriteRenderer>(bars, "_ringGhost");
                AssertGhostMirrors(ring, ghost, "identity");
                Assert.IsNull(Private<SpriteRenderer>(bars, "_ringEdge").transform.Find("IdentityRingEdge" + GroundRingStyle.GhostSuffix),
                    "the ink / cream edge has no ghost (the ghost is colour-only, Phase 8 D9)");

                bars.SetBodyFootprint(0.9f); // a Fatty-sized body: new radius, new annulus sprite
                AssertGhostMirrors(ring, ghost, "identity after SetBodyFootprint");

                // The ring half of LateUpdate (the bar half needs a spawned ChickenCombat): unstamped corner -> neutral at 0.5,
                // then the same SyncGhost call LateUpdate makes; the ghost takes the RGB and the alpha relative to the stamped 0.75.
                typeof(ChickenWorldBars).GetMethod("RefreshRing", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bars, new object[] { false });
                GroundRingStyle.SyncGhost(ring, ghost, ChickenWorldBars.RingAlpha);
                Assert.AreEqual(ring.color.r, ghost.color.r, 1e-4f);
                Assert.AreEqual(ring.color.g, ghost.color.g, 1e-4f);
                Assert.AreEqual(ring.color.b, ghost.color.b, 1e-4f);
                Assert.AreEqual(ring.color.a / ChickenWorldBars.RingAlpha, ghost.color.a, 1e-4f, "ghost alpha follows the ring's");

                ring.color = WithA(PlayerPalette.ForCorner(1), ChickenWorldBars.RingAlpha);
                GroundRingStyle.SyncGhost(ring, ghost, ChickenWorldBars.RingAlpha);
                Assert.AreEqual(1f, ghost.color.a, 1e-4f, "a ring at its nominal alpha gets the full 40 % ghost (material multiplies)");
                Assert.AreEqual(PlayerPalette.ForCorner(1).r, ghost.color.r, 1e-4f);

                ring.gameObject.SetActive(false);
                Assert.IsFalse(ghost.gameObject.activeInHierarchy, "a hidden ring hides its ghost");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void RivalRing_HasAGhost_ThatFollowsRadiusPlayerColourFadeAndVisibility()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var ind = go.GetComponent<RivalIndicator>();
            try
            {
                Invoke(ind, "Awake");
                var ring = Private<SpriteRenderer>(ind, "_ring");
                var ghost = Private<SpriteRenderer>(ind, "_ringGhost");
                AssertGhostMirrors(ring, ghost, "rival");

                ind.SetBodyFootprint(0.9f);
                AssertGhostMirrors(ring, ghost, "rival after SetBodyFootprint");

                ind.ShowRingForTests(2, 1f);
                var want = PlayerPalette.ForCorner(2);
                Assert.AreEqual(want.r, ghost.color.r, 1e-4f); Assert.AreEqual(want.g, ghost.color.g, 1e-4f); Assert.AreEqual(want.b, ghost.color.b, 1e-4f);
                Assert.AreEqual(1f, ghost.color.a, 1e-4f, "full-opacity rival -> full 40 % ghost");
                Assert.IsTrue(ghost.gameObject.activeInHierarchy);

                ind.ShowRingForTests(2, 0.95f);
                Assert.AreEqual(0.95f, ghost.color.a, 1e-3f, "the ghost fades with the rival ring");

                Invoke(ind, "Hide"); // what OnDisable / an off-screen or cloaked rival does
                Assert.IsFalse(ghost.gameObject.activeInHierarchy, "a hidden rival ring hides its ghost");
            }
            finally
            {
                if (ind != null) UnityEngine.Object.DestroyImmediate(ind);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ── Real render: the ghost shows behind a mound and nowhere else ─────────

        private string _scenePath;

        [SetUp]
        public void Remember() => _scenePath = SceneManager.GetActiveScene().path;

        [TearDown]
        public void Restore()
        {
            if (string.IsNullOrEmpty(_scenePath)) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// A flat red ring on the ground, two identical boxes standing in front of it (camera side) covering opposite arcs:
        /// the left one is a "mound" (FoodMound + the stencil mark, like the prefab), the right one is any other opaque object
        /// (FoodMound only: a chicken body, a fence). Rendered by URP with the ghost on and off: the ghost must tint the
        /// left box where the ring is behind it, leave the right box untouched, and add nothing where the ring is in view.
        /// </summary>
        [Test]
        public void Render_GhostShowsBehindTheMoundOnly_NotBehindOtherOpaques_NotOverTheVisibleRing()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            var ringGo = new GameObject("IdentityRing");
            ringGo.transform.SetPositionAndRotation(new Vector3(0f, 0.02f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var ring = ringGo.AddComponent<SpriteRenderer>();
            ring.color = new Color(1f, 0f, 0f, 1f);
            SpriteRenderer edge = null, ghost = null;
            GroundRingStyle.Apply(ring, ref edge, 1.5f, 0.3f, 0.02f, 0.01f, 0f);
            Assert.IsTrue(GroundRingStyle.ApplyGhost(ring, ref ghost, Load(GhostMatPath)));
            GroundRingStyle.SyncGhost(ring, ghost, 1f);

            var mound = Box("Mound", new Vector3(-1.4f, 0.5f, -0.5f), Load(MoundMatPath), Load(MarkMatPath));
            var other = Box("Other", new Vector3(1.4f, 0.5f, -0.5f), Load(MoundMatPath));

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            cam.orthographic = true;
            cam.orthographicSize = 2.6f;
            cam.transform.position = new Vector3(0f, 6f, -10.4f);
            cam.transform.LookAt(Vector3.zero);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;

            const int W = 320, H = 240;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            try
            {
                var on = Render(cam, rt);
                ghost.enabled = false;
                var off = Render(cam, rt);

                // Arcs of the ring: behind the mound box, behind the other box, and in plain view (camera side, middle).
                float behindMound = RedGain(cam, on, off, new Vector3(-1.35f, 0.02f, 0f), W, H);
                float behindOther = RedGain(cam, on, off, new Vector3(1.35f, 0.02f, 0f), W, H);
                float inView = RedGain(cam, on, off, new Vector3(0f, 0.02f, -1.35f), W, H);
                TestContext.WriteLine(FormattableString.Invariant($"red gain from the ghost: behind mound {behindMound:0.0}, behind other {behindOther:0.0}, ring in view {inView:0.0}"));

                Assert.Greater(behindMound, 12f, "the arc hidden by the mound must show as a ghost");
                Assert.Less(Mathf.Abs(behindOther), 2f, "no ghost through a non-mound opaque (chicken body, fence, prop)");
                Assert.Less(Mathf.Abs(inView), 2f, "the ghost must not double the visible ring");

                // Sanity: the boxes really hide the ring (the left box is not just transparent).
                Assert.Less(Sample(cam, off, new Vector3(-1.35f, 0.02f, 0f), W, H).x - Sample(cam, off, new Vector3(1.35f, 0.02f, 0f), W, H).x, 3f);
                Assert.Greater(Sample(cam, off, new Vector3(0f, 0.02f, -1.35f), W, H).x, 150f, "ring in view should render red");
            }
            finally
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(mound);
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        private static GameObject Box(string name, Vector3 pos, params Material[] mats)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos;
            go.transform.localScale = new Vector3(1.2f, 1f, 1f);
            go.GetComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        private static Color32[] Render(Camera cam, RenderTexture rt)
        {
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        /// <summary>Mean colour of a 5x5 patch round the screen position of <paramref name="world"/>.</summary>
        private static Vector3 Sample(Camera cam, Color32[] px, Vector3 world, int w, int h)
        {
            cam.aspect = (float)w / h;
            var v = cam.WorldToViewportPoint(world);
            int cx = Mathf.RoundToInt(v.x * w), cy = Mathf.RoundToInt(v.y * h);
            var s = Vector3.zero; int n = 0;
            for (int y = cy - 2; y <= cy + 2; y++)
            for (int x = cx - 2; x <= cx + 2; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                var c = px[y * w + x]; s += new Vector3(c.r, c.g, c.b); n++;
            }
            return n > 0 ? s / n : Vector3.zero;
        }

        private static float RedGain(Camera cam, Color32[] on, Color32[] off, Vector3 world, int w, int h)
        {
            var a = Sample(cam, on, world, w, h);
            var b = Sample(cam, off, world, w, h);
            return (a.x - b.x) - 0.5f * ((a.y - b.y) + (a.z - b.z));
        }
    }
}
