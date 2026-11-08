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

        /// <summary>The shipped class asset and baked model top (model units) for one class, from the registry.</summary>
        private static (ChickenStatsSO stats, float modelTop) ShippedClass(ChickenClassRegistrySO reg, ChickenClass cls)
        {
            Assert.IsTrue(reg.TryGet(cls, out var e) && e.Stats != null && e.ModelPrefab != null, cls + ": registry entry with stats and model");
            float modelTop = 0f;
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
            return (e.Stats, modelTop);
        }

        private static ChickenClass[] AllClasses() => (ChickenClass[])System.Enum.GetValues(typeof(ChickenClass));

        [Test]
        public void TheBird_FitsInsideTheEgg_HeightWise_ForEveryClass()
        {
            // Class models are 1.6 m tall in model space (checked on the baked mesh); inside the egg the bird wears its class tuck
            // (BodyScaleFor, E1a) and sinks by its class sink, so its top (model units, root-local) is modelTop x tuck - sink / classScale,
            // and may exceed the egg top by at most the comb allowance. (Width is judged on the captures: the rest pose of the prefab
            // is spread out, so its horizontal extent is not the animated idle silhouette.)
            var reg = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsNotNull(reg);
            Assert.AreEqual(4, AllClasses().Length);
            foreach (var cls in AllClasses())
            {
                var (c, modelTop) = ShippedClass(reg, cls);
                Assert.AreEqual(FeedbackTuning.ShellModelHeight, modelTop, 0.05f, c.DisplayName + ": authored model height changed, retune ShellModelHeight");
                float tuck = ChickenShellVisuals.BodyScaleFor(c.Scale, cls);
                float sink = FeedbackTuning.ShellBodySinkFor(cls);
                Assert.GreaterOrEqual(sink, 0f, c.DisplayName + ": the sink lowers the bird, never lifts it");
                float eggTopModelUnits = ChickenShellVisuals.EggWorldHeight(c.Scale) / c.Scale;
                float birdTop = modelTop * tuck - sink / c.Scale;
                Assert.LessOrEqual(birdTop, eggTopModelUnits + FeedbackTuning.ShellBodyTopAllowance + 1e-3f, c.DisplayName + " pokes out of the egg");
                Assert.LessOrEqual(tuck, FeedbackTuning.ShellBodyTuckFor(cls) + 1e-6f, c.DisplayName + ": the cap only ever tucks deeper");
                Assert.Greater(tuck, 0.5f, c.DisplayName + ": a tuck this deep would read as a different bird");
            }
        }

        [Test]
        public void TheEggTuckTable_IsTheE1aRuling()
        {
            // E1a (2026-11-08): only the Warrior changes (0.82 -> 0.70 plus a 0.06 m sink); the other three keep exactly what they wore.
            Assert.AreEqual(FeedbackTuning.ShellBodyTuckWarrior, FeedbackTuning.ShellBodyTuckFor(ChickenClass.Warrior), 1e-6f);
            Assert.That(FeedbackTuning.ShellBodyTuckWarrior, Is.InRange(0.64f, 0.70f), "E1a: 0.70, lowered in 0.02 steps on captures, never below 0.64");
            Assert.AreEqual(0.06f, FeedbackTuning.ShellBodySinkFor(ChickenClass.Warrior), 1e-6f);
            foreach (var cls in new[] { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin })
            {
                Assert.AreEqual(FeedbackTuning.ShellBodyScale, FeedbackTuning.ShellBodyTuckFor(cls), 1e-6f, cls + " keeps the default tuck");
                Assert.AreEqual(0f, FeedbackTuning.ShellBodySinkFor(cls), 1e-6f, cls + " does not sink");
            }
            Assert.AreEqual(0.82f, FeedbackTuning.ShellBodyScale, 1e-6f, "the default tuck");

            // Effective values from the shipped class scales: the Warrior wears 0.70, the Fatty stays capped below 0.82 as before.
            var reg = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            foreach (var cls in AllClasses())
            {
                Assert.IsTrue(reg.TryGet(cls, out var e) && e.Stats != null);
                float before = Mathf.Min(FeedbackTuning.ShellBodyScale,
                    (ChickenShellVisuals.EggWorldHeight(e.Stats.Scale) / e.Stats.Scale + FeedbackTuning.ShellBodyTopAllowance) / FeedbackTuning.ShellModelHeight);
                float now = ChickenShellVisuals.BodyScaleFor(e.Stats.Scale, cls);
                if (cls == ChickenClass.Warrior) Assert.AreEqual(FeedbackTuning.ShellBodyTuckWarrior, now, 1e-5f, "the Warrior wears the ruled tuck");
                else Assert.AreEqual(before, now, 1e-6f, cls + ": unchanged by E1a");
            }
        }

        [Test]
        public void TheWarrior_SitsDeeper_AndItsCombMayPeekButNoMore()
        {
            var reg = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            var (w, modelTop) = ShippedClass(reg, ChickenClass.Warrior);
            float eggTopWorld = ChickenShellVisuals.EggWorldHeight(w.Scale);
            float topBefore = w.Scale * modelTop * FeedbackTuning.ShellBodyScale;
            float topNow = w.Scale * modelTop * ChickenShellVisuals.BodyScaleFor(w.Scale, ChickenClass.Warrior) - FeedbackTuning.ShellBodySinkFor(ChickenClass.Warrior);
            Assert.Less(topNow, topBefore - 0.15f, "E1a lowers the Warrior top by the tuck and the sink (about 0.25 m)");
            Assert.LessOrEqual(topNow, eggTopWorld + FeedbackTuning.ShellBodyTopAllowance * w.Scale + 1e-3f, "at most the comb tip above the egg");
        }

        [Test]
        public void TheEggCapAndTop_AreUnchangedByTheTuck()
        {
            // E1a keeps the egg itself: scale cap 1.27, the capped egg top 1.22 x 1.27 = 1.55 m (strictly under ShellEggTopMaxHeight).
            Assert.AreEqual(1.27f, FeedbackTuning.ShellEggScaleCap, 1e-6f);
            Assert.AreEqual(1.55f, FeedbackTuning.ShellEggTopMaxHeight, 1e-6f);
            Assert.AreEqual(1.22f, FeedbackTuning.ShellEggBaseHeight, 1e-6f);
            float biggest = Classes().Max(c => c.Scale);
            Assert.AreEqual(FeedbackTuning.ShellEggBaseHeight * FeedbackTuning.ShellEggScaleCap, ChickenShellVisuals.EggWorldHeight(biggest), 1e-5f,
                "the largest shipped class wears the capped egg");
            Assert.Less(ChickenShellVisuals.EggWorldHeight(biggest), FeedbackTuning.ShellEggTopMaxHeight);
        }

        [Test]
        public void ExtraYOffset_ComposesIntoTheSingleWrite_InWorldMetres_AndReturnsToRest()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath));
            root.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                root.transform.localScale = Vector3.one * 1.35f;   // the root carries the class scale
                var anim = root.GetComponent<ChickenAnimator>();
                Assert.IsNotNull(anim);
                var model = new GameObject("model").transform;
                model.SetParent(root.transform, false);
                model.localPosition = new Vector3(0f, -0.8f, 0f);
                anim.SetModelRoot(model);
                anim.SetSkeletalActive(true);
                Assert.AreEqual(0f, anim.ExtraYOffset, "rest value");
                var apply = typeof(ChickenAnimator).GetMethod("ApplySkeletalOffsets", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(apply);

                apply.Invoke(anim, null);
                Vector3 restWorld = model.position;
                anim.ExtraYOffset = -0.06f;
                apply.Invoke(anim, null);
                Assert.AreEqual(restWorld.y - 0.06f, model.position.y, 1e-4f, "the sink is world metres whatever the class scale");
                Assert.AreEqual(restWorld.x, model.position.x, 1e-5f);
                anim.ExtraYOffset = 0f;
                apply.Invoke(anim, null);
                Assert.AreEqual(restWorld, model.position, "writing 0 returns the model exactly to rest");
            }
            finally { Object.DestroyImmediate(root); }

            string shell = File.ReadAllText(SourcePath);
            Assert.IsTrue(shell.Contains("_animator.ExtraYOffset = -_bodySink"), "the shell writes the eased sink");
            Assert.GreaterOrEqual(System.Text.RegularExpressions.Regex.Matches(shell, @"_animator\.ExtraYOffset = 0f").Count, 2,
                "OnDisable and GoOff clear the sink");
        }

        // ---- Lateral fit (E1a follow-up): the triangle-cut egg profile vs the baked, animated bird --------------------------

        /// <summary>The egg's radius about its axis at height <paramref name="y"/> in direction <paramref name="dir"/>: the farthest
        /// point where a triangle edge crosses that horizontal plane, inside a 14 degree cone around the direction.</summary>
        private static float EggRadiusAt(System.Collections.Generic.List<Vector3[]> tris, float y, Vector2 dir)
        {
            float best = 0f;
            foreach (var tri in tris)
                for (int k = 0; k < 3; k++)
                {
                    Vector3 a = tri[k], b = tri[(k + 1) % 3];
                    if ((a.y - y) * (b.y - y) > 0f || a.y == b.y) continue;
                    var p = Vector3.Lerp(a, b, (y - a.y) / (b.y - a.y));
                    var d = new Vector2(p.x, p.z);
                    if (d.sqrMagnitude < 1e-8f || Vector2.Dot(d.normalized, dir) < 0.97f) continue;
                    best = Mathf.Max(best, d.magnitude);
                }
            return best;
        }

        /// <summary>
        /// The worst amount (world m) the class's body pokes past the seated idle egg per direction (+X, -X, +Z front, -Z back), over
        /// the Idle clip and the Shield's cast clip, in the egg phase pose (class tuck, squeeze, sink) at the shipped class scale. Band:
        /// from a quarter of the egg's height (above it is the feet, which stand on the ground through the egg's pointed bottom) up to
        /// the seam + 0.04 m for the back and sides, and up to the seam for the front: above the seam the cap narrows and the head and
        /// beak peek out, ACCEPTED by E1a (the comb tip may peek), so the front above the seam is never asserted.
        /// </summary>
        private (float px, float nx, float pz, float nz, string where) EggOvershoot(ChickenClass cls)
        {
            var reg = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsTrue(reg.TryGet(cls, out var e) && e.Stats != null && e.ModelPrefab != null && e.Clips.Idle != null, cls + ": registry entry");
            var turtle = AssetDatabase.LoadAssetAtPath<CluckWars.Abilities.AbilityBaseSO>("Assets/_Game/Data/Abilities/TurtleMode.asset");
            Assert.IsNotNull(turtle, "the shipped Shield (TurtleMode.asset)");
            float cs = e.Stats.Scale;

            var shell = Wake();
            var root = _chicken.transform;
            root.position = Vector3.zero; root.rotation = Quaternion.identity; root.localScale = Vector3.one * cs;
            var cc = _chicken.GetComponent<CharacterController>();
            // The seated idle egg exactly as Apply poses it: cup risen, cap on the seam, no squash or breath.
            var shellRoot = root.Find("EggShell");
            shellRoot.gameObject.SetActive(true);
            shellRoot.localPosition = new Vector3(0f, ChickenGround.LocalFeetY(cc) - cc.skinWidth / cs, 0f);
            shellRoot.localRotation = Quaternion.identity;
            shellRoot.localScale = Vector3.one * (ChickenShellVisuals.EggWorldHeight(cs) / cs);
            var cap = shellRoot.Find("Cap");
            float seamLocal = (float)typeof(ChickenShellVisuals).GetField("_seamY", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shell);
            shellRoot.Find("Cup").localPosition = Vector3.zero;
            cap.localPosition = new Vector3(0f, seamLocal, 0f);
            var tris = new System.Collections.Generic.List<Vector3[]>();
            float eggMin = float.MaxValue, eggMax = float.MinValue;
            foreach (var mf in shellRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                var mx = mf.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices; var ts = mf.sharedMesh.triangles;
                foreach (var v in vs) { float y = mx.MultiplyPoint3x4(v).y; eggMin = Mathf.Min(eggMin, y); eggMax = Mathf.Max(eggMax, y); }
                for (int i = 0; i < ts.Length; i += 3)
                    tris.Add(new[] { mx.MultiplyPoint3x4(vs[ts[i]]), mx.MultiplyPoint3x4(vs[ts[i + 1]]), mx.MultiplyPoint3x4(vs[ts[i + 2]]) });
            }
            float seam = cap.position.y, bandLo = eggMin + 0.25f * (eggMax - eggMin);

            // The bird in the egg pose, the way ChickenAnimator composes it on the model root.
            var model = Object.Instantiate(e.ModelPrefab, root, false);
            model.hideFlags = HideFlags.HideAndDontSave;
            var nested = model.GetComponent<Animator>();
            if (nested != null) Object.DestroyImmediate(nested);
            float tuck = ChickenShellVisuals.BodyScaleFor(cs, cls);
            model.transform.localPosition = new Vector3(0f, ChickenGround.LocalFeetY(cc) - FeedbackTuning.ShellBodySinkFor(cls) / cs, 0f);
            model.transform.localScale = Vector3.Scale(e.ResolvedModelScale * tuck, new Vector3(1f, 1f, FeedbackTuning.ShellBodySqueezeZFor(cls)));

            var dirs = new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) };
            var worst = new[] { -9f, -9f, -9f, -9f };
            string where = "";
            var poses = new System.Collections.Generic.List<(AnimationClip clip, float t)>();
            for (int i = 0; i <= 8; i++) poses.Add((e.Clips.Idle, e.Clips.Idle.length * i / 8f));
            var cast = e.Clips.CastFor(turtle.CastMotion);
            if (cast != null) for (int i = 0; i <= 8; i++) poses.Add((cast, cast.length * i / 8f));
            try
            {
                foreach (var (clip, t) in poses)
                {
                    clip.SampleAnimation(model, t);
                    var pts = new System.Collections.Generic.List<Vector3>();
                    foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var m = new Mesh();
                        smr.BakeMesh(m);   // the bones already carry the model-root scale: place with position + rotation only
                        var mx = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        foreach (var v in m.vertices) pts.Add(mx.MultiplyPoint3x4(v));
                        Object.DestroyImmediate(m);
                    }
                    for (float y = bandLo; y <= seam + 0.04f; y += 0.02f)
                        for (int d = 0; d < 4; d++)
                        {
                            if (d == 2 && y > seam) continue;   // the front above the seam: the accepted head/beak peek (E1a)
                            float body = 0f;
                            foreach (var p in pts)
                            {
                                if (Mathf.Abs(p.y - y) >= 0.01f) continue;
                                var q = new Vector2(p.x, p.z);
                                if (q.sqrMagnitude > 1e-8f && Vector2.Dot(q.normalized, dirs[d]) >= 0.97f) body = Mathf.Max(body, q.magnitude);
                            }
                            if (body == 0f) continue;
                            float over = body - EggRadiusAt(tris, y, dirs[d]);
                            if (over > worst[d]) { worst[d] = over; if (d == 3) where = clip.name + " t=" + t.ToString("F2") + " y=" + y.ToString("F2"); }
                        }
                }
            }
            finally { Object.DestroyImmediate(model); }
            return (worst[0], worst[1], worst[2], worst[3], where);
        }

        [Test]
        public void TheWarrior_FitsTheEgg_AtTheBackAndSides_InEveryIdleAndShieldCastPose()
        {
            // E1a follow-up: the tail and folded wing tips at the seam band poked through the back of the shell (captures G1*).
            // The front-back squeeze takes them in; the sides already fit, so x stays unsqueezed. The front is asserted only up to
            // the seam (above it the head/beak peek out where the cap narrows, accepted by E1a).
            var (px, nx, pz, nz, where) = EggOvershoot(ChickenClass.Warrior);
            Assert.LessOrEqual(nz, 0f, "the Warrior back pokes " + nz.ToString("F3") + " m out of the egg at " + where);
            Assert.LessOrEqual(px, 0f, "the Warrior right side pokes out by " + px.ToString("F3") + " m");
            Assert.LessOrEqual(nx, 0f, "the Warrior left side pokes out by " + nx.ToString("F3") + " m");
            Assert.LessOrEqual(pz, 0.02f, "the Warrior breast below the seam pokes out by " + pz.ToString("F3") + " m");
        }

        [Test]
        public void TheOtherClasses_EggFit_IsMeasured_NotAsserted()
        {
            // E1a only rules the Warrior: the other three keep their tuck and wear no squeeze, so their measured overshoot is reported
            // here (test output) for a later ruling instead of being pinned. Only the shape of the table is asserted.
            foreach (var cls in new[] { ChickenClass.Speedy, ChickenClass.Fatty, ChickenClass.Assassin })
            {
                Assert.AreEqual(1f, FeedbackTuning.ShellBodySqueezeZFor(cls), 1e-6f, cls + " wears no squeeze");
                var (px, nx, pz, nz, where) = EggOvershoot(cls);
                TestContext.WriteLine(cls + " egg overshoot (m): +X " + px.ToString("F3") + "  -X " + nx.ToString("F3") +
                                      "  front " + pz.ToString("F3") + "  back " + nz.ToString("F3") + " (" + where + ")");
                if (_chicken != null) { Object.DestroyImmediate(_chicken); _chicken = null; }
            }
        }

        [Test]
        public void TheWarriorSqueeze_IsOnlyFrontBack_AndInTheRuledRange()
        {
            float sq = FeedbackTuning.ShellBodySqueezeZWarrior;
            Assert.AreEqual(sq, FeedbackTuning.ShellBodySqueezeZFor(ChickenClass.Warrior), 1e-6f);
            Assert.GreaterOrEqual(sq, 0.76f, "E1a floor: deeper reads as a different bird");
            Assert.LessOrEqual(sq, 0.85f, "E1a: at 0.85 the back still pokes out in the Shield cast pose");
        }

        [Test]
        public void ExtraZScale_ComposesIntoTheScaleWrite_AndReturnsToRest()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath));
            root.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var anim = root.GetComponent<ChickenAnimator>();
                var model = new GameObject("model").transform;
                model.SetParent(root.transform, false);
                model.localScale = new Vector3(2f, 3f, 4f);
                anim.SetModelRoot(model);
                anim.SetSkeletalActive(true);
                Assert.AreEqual(1f, anim.ExtraZScale, "rest value");
                var apply = typeof(ChickenAnimator).GetMethod("ApplySkeletalOffsets", BindingFlags.Instance | BindingFlags.NonPublic);
                anim.ExtraUniformScale = 0.7f;
                anim.ExtraZScale = 0.8f;
                apply.Invoke(anim, null);
                Assert.AreEqual(2f * 0.7f, model.localScale.x, 1e-4f, "x untouched by the squeeze");
                Assert.AreEqual(3f * 0.7f, model.localScale.y, 1e-4f);
                Assert.AreEqual(4f * 0.7f * 0.8f, model.localScale.z, 1e-4f, "z = rest x uniform x squeeze");
                anim.ExtraUniformScale = 1f;
                anim.ExtraZScale = 1f;
                apply.Invoke(anim, null);
                Assert.AreEqual(new Vector3(2f, 3f, 4f), model.localScale, "the squeeze leaves nothing behind");
            }
            finally { Object.DestroyImmediate(root); }

            string shell = File.ReadAllText(SourcePath);
            Assert.IsTrue(shell.Contains("_animator.ExtraZScale = _bodySqueezeZ"), "the shell writes the eased squeeze");
            Assert.GreaterOrEqual(System.Text.RegularExpressions.Regex.Matches(shell, @"_animator\.ExtraZScale = 1f").Count, 2,
                "OnDisable and GoOff clear the squeeze");
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
            var shared = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Egg_Shell.mat");
            Assert.IsNotNull(shared);
            Assert.AreEqual("Cluck Wars/Egg Shell", shared.shader.name);
            foreach (var r in renderers)
            {
                Assert.AreSame(shared, r.sharedMaterial, r.name + ": shared material, never an instance");
                Assert.IsNotNull(r.GetComponent<MeshFilter>().sharedMesh);
            }
            CollectionAssert.AreEquivalent(new[] { "EggShell_Top", "EggShell_Bottom" }, renderers.Select(r => r.name).ToArray());
            Assert.IsFalse(shellRoot.gameObject.activeSelf, "off until a Shield phase is observed");
            Assert.IsFalse(_chicken.GetComponentsInChildren<Transform>(true).Any(t => t.name == "EggShell_Whole"));
        }

        private static void Invoke(ChickenShellVisuals shell, string method, params object[] args) =>
            typeof(ChickenShellVisuals).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(shell, args);

        private static void SetField(ChickenShellVisuals shell, string field, object value) =>
            typeof(ChickenShellVisuals).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(shell, value);

        private float BlockCrack(string rendererName)
        {
            var r = _chicken.transform.Find("EggShell").GetComponentsInChildren<MeshRenderer>(true).Single(x => x.name == rendererName);
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return block.GetFloat("_Crack");
        }

        [Test]
        public void AWalkAfterAnEarlyEndedEgg_ShowsAnUncrackedCup()
        {
            // storm_1B_a (F2): an egg ended early snaps the cracks to ShellCrackOnEarlyEnd and pushes them to both renderers
            // (EnterOpen); the hatch then runs out (GoOff) and the next cast starts a walk. The cup must come back clean.
            var shell = Wake();
            SetField(shell, "_crack", FeedbackTuning.ShellCrackOnEarlyEnd);
            Invoke(shell, "PushBlock");
            Assert.AreEqual(FeedbackTuning.ShellCrackOnEarlyEnd, BlockCrack("EggShell_Bottom"), 1e-5f, "precondition: the early end cracked the cup");

            Invoke(shell, "GoOff");
            Assert.AreEqual(0f, BlockCrack("EggShell_Bottom"), 1e-6f, "an Off shell carries no stale cracks");
            Assert.AreEqual(0f, BlockCrack("EggShell_Top"), 1e-6f);

            // Even if something dirtied the block while off, the walk itself pushes a clean one.
            SetField(shell, "_crack", 1f);
            Invoke(shell, "PushBlock");
            Invoke(shell, "EnterWalk", false);
            Assert.AreEqual(0f, BlockCrack("EggShell_Bottom"), 1e-6f, "the re-cast walk's cup block has _Crack 0");
        }

        [Test]
        public void TheHatchDisc_ScalesWithTheClassLikeTheEgg()
        {
            foreach (float classScale in new[] { 0.8f, 1f, 1.2f, 1.5f })
                Assert.AreEqual(FeedbackTuning.ShellBurstRadius / FeedbackTuning.ShellEggBaseHeight,
                    ChickenShellVisuals.BurstRadius(classScale) / ChickenShellVisuals.EggWorldHeight(classScale), 1e-5f,
                    "burst radius / egg height is one constant at class scale " + classScale);
            Assert.Greater(FeedbackTuning.ShellBurstLift, 0f, "the debris rises off the seam");
            Assert.Less(FeedbackTuning.ShellBurstGravity, 1f, "a lighter-than-g fall, so it clears the outline first");
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

