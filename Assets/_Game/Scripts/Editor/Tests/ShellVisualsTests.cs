using System.IO;
using System.Linq;
using System.Reflection;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Phase = CluckWars.Visuals.ChickenShellVisuals.Phase;
using Edge = CluckWars.Visuals.ChickenShellVisuals.Edge;

namespace CluckWars.Tests
{
    /// <summary>
    /// Feedback batch D2 (Egg Shell body): the phase rules derived from replicated state, the class scale cap that keeps every
    /// egg under the nameplate (read from the shipped class assets), the cracks curve, the build on Chicken.prefab, and the
    /// rule that the shell driver never asks any overlay to hide anything (a stun cannot land during the egg).
    /// </summary>
    public sealed class ShellVisualsTests
    {
        private const string SourcePath = "Assets/_Game/Scripts/Visuals/ChickenShellVisuals.cs";

        // ---- Phase from (slot active, control immune) -------------------------------

        [Test]
        public void Phase_FollowsTheSlotAndTheImmunity()
        {
            Assert.AreEqual(Phase.None, ChickenShellVisuals.PhaseOf(false, false));
            Assert.AreEqual(Phase.None, ChickenShellVisuals.PhaseOf(false, true), "immunity without the slot is not a shell phase");
            Assert.AreEqual(Phase.Walk, ChickenShellVisuals.PhaseOf(true, false), "walk = active and not immune");
            Assert.AreEqual(Phase.Egg, ChickenShellVisuals.PhaseOf(true, true), "egg = active and immune");
        }

        [TestCase(Phase.None, Phase.None, Edge.None)]
        [TestCase(Phase.Walk, Phase.Walk, Edge.None)]
        [TestCase(Phase.Egg, Phase.Egg, Edge.None)]
        [TestCase(Phase.None, Phase.Walk, Edge.WalkStart)]
        [TestCase(Phase.Walk, Phase.Egg, Edge.Close)]
        [TestCase(Phase.None, Phase.Egg, Edge.Close)]        // a walk skipped between two observations still closes
        [TestCase(Phase.Egg, Phase.None, Edge.Open)]         // slot over: timeout, second press, another cast
        [TestCase(Phase.Egg, Phase.Walk, Edge.Open)]         // immunity closed while the slot still runs
        [TestCase(Phase.Walk, Phase.None, Edge.WalkEnd)]     // a walk that ends without an egg
        public void Edges_BetweenObservedPhases(Phase prev, Phase cur, Edge expected)
        {
            Assert.AreEqual(expected, ChickenShellVisuals.EdgeOf(prev, cur));
        }

        [Test]
        public void TheEggEnds_ByEitherEdge()
        {
            var viaSlot = ChickenShellVisuals.EdgeOf(ChickenShellVisuals.PhaseOf(true, true), ChickenShellVisuals.PhaseOf(false, false));
            var viaImmunity = ChickenShellVisuals.EdgeOf(ChickenShellVisuals.PhaseOf(true, true), ChickenShellVisuals.PhaseOf(true, false));
            Assert.AreEqual(Edge.Open, viaSlot);
            Assert.AreEqual(Edge.Open, viaImmunity);
        }

        // ---- Scale cap -----------------------------------------------------------------

        private static ChickenStatsSO[] Classes() => TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir).ToArray();

        [Test]
        public void EggTop_StaysBelowTheNameplate_ForEveryShippedClass()
        {
            var classes = Classes();
            Assert.AreEqual(4, classes.Length, "the four shipped class assets");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            float labelY = new SerializedObject(prefab.GetComponent<ChickenNameplate>()).FindProperty("_offset").vector3Value.y;

            foreach (var c in classes)
            {
                float top = ChickenShellVisuals.EggWorldHeight(c.Scale);
                Assert.Less(top, FeedbackTuning.ShellEggTopMaxHeight, c.DisplayName + " egg top " + top + " m reaches the nameplate band");
                Assert.Less(top, labelY, c.DisplayName + " egg must stay under the nameplate label (" + labelY + ")");
                Assert.AreEqual(Mathf.Min(c.Scale, FeedbackTuning.ShellEggScaleCap), ChickenShellVisuals.EggScale(c.Scale), 1e-5f, c.DisplayName);
            }
            // The cap is what makes the largest class fit: uncapped it would break the limit.
            float biggest = classes.Max(c => c.Scale);
            Assert.GreaterOrEqual(FeedbackTuning.ShellEggBaseHeight * biggest, FeedbackTuning.ShellEggTopMaxHeight,
                "if no class needed the cap, the cap would be dead code");
        }

        [Test]
        public void EggSize_StillGrowsWithTheClass_UntilTheCap()
        {
            var ordered = Classes().OrderBy(c => c.Scale).ToArray();
            for (int i = 1; i < ordered.Length; i++)
                Assert.GreaterOrEqual(ChickenShellVisuals.EggWorldHeight(ordered[i].Scale), ChickenShellVisuals.EggWorldHeight(ordered[i - 1].Scale));
        }

        [Test]
        public void TheBird_FitsInsideTheEgg_HeightWise_ForEveryClass()
        {
            // Class models are 1.6 m tall in model space (checked on the baked mesh below); the bird wears BodyScaleFor inside the egg,
            // so its top may exceed the egg top by at most the comb allowance. (Width is judged on the captures: the rest pose of the
            // prefab is spread out, so its horizontal extent is not the animated idle silhouette.)
            var reg = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsNotNull(reg);
            foreach (var c in Classes())
            {
                float modelTop = 0f;
                foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
                {
                    if (!reg.TryGet(cls, out var e) || e.Stats != c || e.ModelPrefab == null) continue;
                    var go = Object.Instantiate(e.ModelPrefab);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            var m = new Mesh();
                            smr.BakeMesh(m);
                            var mx = smr.transform.localToWorldMatrix;
                            foreach (var v in m.vertices) modelTop = Mathf.Max(modelTop, mx.MultiplyPoint3x4(v).y);
                            Object.DestroyImmediate(m);
                        }
                    }
                    finally { Object.DestroyImmediate(go); }
                }
                Assert.AreEqual(FeedbackTuning.ShellModelHeight, modelTop, 0.05f, c.DisplayName + ": authored model height changed, retune ShellModelHeight");
                float eggTopModelUnits = ChickenShellVisuals.EggWorldHeight(c.Scale) / c.Scale;
                float birdTop = modelTop * ChickenShellVisuals.BodyScaleFor(c.Scale);
                Assert.LessOrEqual(birdTop, eggTopModelUnits + FeedbackTuning.ShellBodyTopAllowance + 1e-3f, c.DisplayName + " pokes out of the egg");
                Assert.LessOrEqual(ChickenShellVisuals.BodyScaleFor(c.Scale), FeedbackTuning.ShellBodyScale + 1e-6f);
            }
            Assert.AreEqual(FeedbackTuning.ShellBodyScale, ChickenShellVisuals.BodyScaleFor(1f), 0.01f, "the Warrior wears the ruled 0.82");
        }

        [Test]
        public void TheEggMesh_StaysUnderTheTriangleBudget()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Meshes/Fx/EggShell.fbx");
            int tris = 0;
            foreach (var mf in fbx.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.EndsWith("_Top") || mf.name.EndsWith("_Bottom")) tris += mf.sharedMesh.triangles.Length / 3;
            Assert.Greater(tris, 0);
            Assert.LessOrEqual(tris, 600, "vfx.md 0.2: the egg is at most 600 tris (top + bottom)");
        }

        // ---- Curves -----------------------------------------------------------------------

        [Test]
        public void Crack_IsASmoothstepFromFortyToNinetyPercent()
        {
            Assert.AreEqual(0f, ChickenShellVisuals.Crack01(0f), 1e-6f);
            Assert.AreEqual(0f, ChickenShellVisuals.Crack01(FeedbackTuning.ShellCrackStart), 1e-6f);
            Assert.AreEqual(0.5f, ChickenShellVisuals.Crack01((FeedbackTuning.ShellCrackStart + FeedbackTuning.ShellCrackEnd) * 0.5f), 1e-5f);
            Assert.AreEqual(1f, ChickenShellVisuals.Crack01(FeedbackTuning.ShellCrackEnd), 1e-6f);
            Assert.AreEqual(1f, ChickenShellVisuals.Crack01(1f), 1e-6f);
            float last = -1f;
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                float c = ChickenShellVisuals.Crack01(t);
                Assert.GreaterOrEqual(c, last, "cracks never heal");
                last = c;
            }
            Assert.Greater(FeedbackTuning.ShellCrackOnEarlyEnd, 0f);
            Assert.Less(FeedbackTuning.ShellCrackOnEarlyEnd, 1f);
        }

        [Test]
        public void EggElapsed_RunsFromZeroToOneOverTheEggSeconds()
        {
            Assert.AreEqual(0f, ChickenShellVisuals.EggElapsed01(2f, 2f), 1e-6f);
            Assert.AreEqual(0.5f, ChickenShellVisuals.EggElapsed01(1f, 2f), 1e-6f);
            Assert.AreEqual(1f, ChickenShellVisuals.EggElapsed01(0f, 2f), 1e-6f);
            Assert.AreEqual(0f, ChickenShellVisuals.EggElapsed01(5f, 2f), 1e-6f, "a walk-length remaining is not past the start");
        }

        [Test]
        public void WalkRise_OvershootsBySixPercentThenSettles()
        {
            float peak = 0f;
            for (float t = 0f; t <= 1f; t += 0.002f) peak = Mathf.Max(peak, ChickenShellVisuals.EaseOutBack(t, FeedbackTuning.ShellWalkRiseOvershoot));
            Assert.AreEqual(FeedbackTuning.ShellWalkRiseOvershoot, peak, 0.004f);
            Assert.AreEqual(1f, ChickenShellVisuals.EaseOutBack(1f, FeedbackTuning.ShellWalkRiseOvershoot), 1e-5f);
            Assert.AreEqual(0f, ChickenShellVisuals.EaseOutBack(0f, FeedbackTuning.ShellWalkRiseOvershoot), 1e-5f);
        }

        // ---- The build on Chicken.prefab ---------------------------------------------------

        private GameObject _chicken;

        [TearDown]
        public void Cleanup()
        {
            if (_chicken != null) Object.DestroyImmediate(_chicken);
        }

        private ChickenShellVisuals Wake()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            _chicken = Object.Instantiate(prefab);
            _chicken.hideFlags = HideFlags.HideAndDontSave;
            var shell = _chicken.GetComponent<ChickenShellVisuals>();
            Assert.IsNotNull(shell, "ChickenShellVisuals missing on Chicken.prefab");
            typeof(ChickenShellVisuals).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(shell, null);
            return shell;
        }

        [Test]
        public void Prefab_BuildsTwoShellMeshes_OnTheRoot_WithTheSharedMaterial()
        {
            Wake();
            var shellRoot = _chicken.transform.Find("EggShell");
            Assert.IsNotNull(shellRoot, "the shell hangs off the chicken ROOT, not the animated model");
            var renderers = shellRoot.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(2, renderers.Length, "cup and cap only (the whole egg is discarded)");
            var shared = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Egg_Shell_Placeholder.mat");
            Assert.IsNotNull(shared);
            Assert.AreEqual("Universal Render Pipeline/Lit", shared.shader.name);
            foreach (var r in renderers)
            {
                Assert.AreSame(shared, r.sharedMaterial, r.name + ": shared material, never an instance");
                Assert.IsNotNull(r.GetComponent<MeshFilter>().sharedMesh);
            }
            CollectionAssert.AreEquivalent(new[] { "EggShell_Top", "EggShell_Bottom" }, renderers.Select(r => r.name).ToArray());
            Assert.IsFalse(shellRoot.gameObject.activeSelf, "off until a Shield phase is observed");
            Assert.IsFalse(_chicken.GetComponentsInChildren<Transform>(true).Any(t => t.name == "EggShell_Whole"));
        }

        [Test]
        public void ShadowsAreOnlyOnThePcTier()
        {
            var pc = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            Assert.IsNotNull(pc, "PC_RPAsset missing");
            Assert.IsTrue(RenderTierPolicy.ShellShadowsFor(pc));
            Assert.IsFalse(RenderTierPolicy.ShellShadowsFor(null), "an unknown asset takes the cheap path");
            var mobile = AssetDatabase.FindAssets("t:RenderPipelineAsset Mobile_RPAsset")
                .Select(g => AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(a => a != null);
            Assert.IsNotNull(mobile, "Mobile_RPAsset missing");
            Assert.IsFalse(RenderTierPolicy.ShellShadowsFor(mobile), "no shadow casting on the Mobile tier");
        }

        // ---- Rules: the shell never hides an overlay ----------------------------------------

        private static readonly string[] OverlayTypes =
        {
            "ChickenStateOverlays", "ChickenStatusBadges", "ChickenNameplate", "AbilityZoneVisuals",   // ChickenWorldBars is read-only here (IdentityColor)
        };

        [Test]
        public void TheShellDriver_NeverReachesForTheOverlays()
        {
            // A stun cannot land during the egg (IsControlImmune), so the stars and status ring have nothing to be hidden for;
            // the shell driver must not hold, and so cannot disable, any overlay.
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var t = typeof(ChickenShellVisuals);
            var used = t.GetFields(all).Select(f => f.FieldType)
                .Concat(t.GetProperties(all).Select(p => p.PropertyType))
                .Concat(t.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));
            foreach (var type in used)
                CollectionAssert.DoesNotContain(OverlayTypes, type.Name, "ChickenShellVisuals must not hold or take " + type.Name);

            string src = File.ReadAllText(SourcePath);
            foreach (string name in OverlayTypes)
            {
                Assert.IsFalse(src.Contains(name + "."), "the shell driver references " + name);
                Assert.IsFalse(src.Contains("<" + name + ">"), "the shell driver looks up " + name);
            }
            // Every SetActive in the driver acts on its own shell root.
            foreach (var line in src.Split('\n').Where(l => l.Contains("SetActive(")))
                StringAssert.Contains("_shellRoot", line, "SetActive must only toggle the shell root: " + line.Trim());
        }

        [Test]
        public void TheEggPhase_IsExactlyTheControlImmuneWindow()
        {
            // Stun, root and shove are refused while IsControlImmune, so Egg is the only phase in which no stun can be on the bird.
            Assert.AreEqual(Phase.Egg, ChickenShellVisuals.PhaseOf(true, true));
            Assert.AreNotEqual(Phase.Egg, ChickenShellVisuals.PhaseOf(true, false), "the walk is not stun-proof: its overlays must stay free to draw");
        }
    }
}

