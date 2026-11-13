using System.IO;
using System.Linq;
using System.Reflection;
using CluckWars.Abilities;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro 2026-11-13: the egg stays per-class off the size stat (and so stays smaller than the bigger birds), and instead
    /// the bird itself disappears into it and reappears out of the hatch, both with an animation.
    ///
    /// <para><b>Third pass.</b> Passes one and two faded during the CLOSE and both failed on a 60 fps capture for the same
    /// reason: the close opens with the cap already a large object right over the bird and the cup still low around its legs,
    /// so a bird larger than its egg has nowhere inside the close to fade without being drawn over the shell. The vanish now
    /// lives in the tail of the WALK, where no cap exists. The invariant these pin is therefore absolute rather than tuned:
    /// <b>nothing of the body is drawn from the first frame any cap geometry exists.</b></para>
    /// </summary>
    public sealed class ShellBodyHiddenTests
    {
        private const float Open = FeedbackTuning.ShellOpenSeconds;
        private const float Span = FeedbackTuning.ShellBodyVanishSeconds;
        private const float Lead = FeedbackTuning.ShellBodyVanishLeadSeconds;

        private const string ShellPath = "Assets/_Game/Scripts/Visuals/ChickenShellVisuals.cs";
        private const string VisualsPath = "Assets/_Game/Scripts/Visuals/ChickenVisuals.cs";

        private static string Src(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        /// <summary>The shipped Shield, which is where the walk and egg lengths actually come from.</summary>
        private static TurtleModeAbilitySO Shield()
        {
            var so = AssetDatabase.LoadAssetAtPath<AbilityBaseSO>("Assets/_Game/Data/Abilities/TurtleMode.asset") as TurtleModeAbilitySO;
            Assert.IsNotNull(so, "the shipped Shield (TurtleMode.asset)");
            return so;
        }

        /// <summary>The conceal at a point in the walk, expressed the way the runtime sees it: seconds of run left.</summary>
        private static float ConcealAtUntilCap(float untilCap, float eggSeconds) =>
            ChickenShellVisuals.WalkConceal01(untilCap + eggSeconds, eggSeconds);

        // ---- The vanish lives in the walk, and is over before the cap exists -------------------------

        [Test]
        public void TheVanish_LivesInTheWalk_AndIsOverBeforeTheCapAppears()
        {
            float egg = Shield().EggSeconds;

            Assert.AreEqual(0f, ConcealAtUntilCap(Span + Lead, egg), 1e-5f, "the waddle starts with the bird plainly there");
            Assert.AreEqual(0f, ConcealAtUntilCap(Span + Lead + 0.2f, egg), 1e-5f, "and stays there before the vanish window");
            Assert.AreEqual(1f, ConcealAtUntilCap(Lead, egg), 1e-5f,
                            "fully gone " + Lead + " s BEFORE the cap appears, not as it comes down");
            Assert.AreEqual(1f, ConcealAtUntilCap(0f, egg), 1e-5f, "and still gone at the instant the egg starts");

            // A ramp, not a cut: strictly rising across the window, and half way through it is half gone.
            float prev = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float c = ConcealAtUntilCap(Span + Lead - Span * i / 20f, egg);
                Assert.GreaterOrEqual(c, prev, "the vanish never goes backwards");
                prev = c;
            }
            Assert.That(ConcealAtUntilCap(Lead + Span * 0.5f, egg), Is.InRange(0.2f, 0.8f),
                        "half way through the vanish the bird is half there, not popped");
            Assert.GreaterOrEqual(Span, 10f / 60f, "a vanish this short would read as a pop");
        }

        [Test]
        public void TheVanishWindow_FitsInsideTheShippedWalk_WithTheWaddleStillReadable()
        {
            var shield = Shield();
            Assert.Less(Span + Lead, shield.WalkSeconds,
                        "the whole vanish must fit inside the walk, or it would spill into the close");
            Assert.Greater(shield.WalkSeconds - (Span + Lead), 0.5f * shield.WalkSeconds,
                           "over half the walk is still the bird plainly waddling in its cup");
            Assert.Greater(Lead, 0f, "and it finishes strictly before the cap, not on the same frame");
            Assert.Less(Lead, 0.15f, "but not so early that an empty cup waddles for a readable stretch");
        }

        [Test]
        public void TheWalkEnd_IsTakenFromTheSameRuleTheAbilityUses()
        {
            // WalkConceal01 is timed off (remaining - eggSeconds) because ShieldRules.EggStartsNow starts the egg on the first
            // tick the run has eggSeconds or less left. If that rule ever changes, the vanish would aim at the wrong moment.
            var shield = Shield();
            float egg = shield.EggSeconds;
            Assert.IsFalse(ShieldRules.EggStartsNow(egg + 0.01f, egg, eggRunning: false), "still walking");
            Assert.IsTrue(ShieldRules.EggStartsNow(egg, egg, eggRunning: false), "the egg starts exactly here");
            Assert.AreEqual(egg, shield.FollowUpPhaseSeconds, 1e-6f, "which is what the shell reads as eggSeconds");
            // So at that instant the conceal is already 1, by the same arithmetic.
            Assert.AreEqual(1f, ChickenShellVisuals.WalkConceal01(egg, egg), 1e-5f);
        }

        [Test]
        public void AWalkThatIsNotGoingToBecomeAnEgg_KeepsItsBird()
        {
            float egg = Shield().EggSeconds;
            // Another cast during the walk truncates the run to the walk and drops the pending egg
            // (ShieldRules.RunAfterOtherCast), which drives remaining below eggSeconds. The bird must NOT vanish.
            float truncated = ShieldRules.RunAfterOtherCast(egg + 0.8f, egg, eggRunning: false);
            Assert.Greater(truncated, 0f, "the walk runs on");
            Assert.AreEqual(0f, ChickenShellVisuals.WalkConceal01(truncated, egg), 1e-5f,
                            "a walk whose egg was cancelled keeps its bird, whatever is left on the clock");
            Assert.AreEqual(0f, ChickenShellVisuals.WalkConceal01(0.05f, egg), 1e-5f);
            Assert.AreEqual(0f, ChickenShellVisuals.WalkConceal01(5f, 0f), 1e-5f, "an ability with no follow-up phase never vanishes its bird");
        }

        // ---- THE invariant: nothing is drawn once any cap geometry exists ---------------------------

        [Test]
        public void TheCapIsAlreadyALargeObjectOverTheBird_OnTheFirstFrameOfTheClose()
        {
            // Why the vanish cannot live in the close at all, stated as a measurement rather than an opinion: at stateT 0 the
            // cap is already only (ShellCapDropFromScale - 1) of a seam height above its seat and descending.
            Assert.AreEqual(FeedbackTuning.ShellCapDropFromScale, ChickenShellVisuals.CapSeatFactor(0f), 1e-6f);
            Assert.AreEqual(0f, ChickenShellVisuals.CapTravel01(0f), 1e-6f, "it has travelled nothing yet - it simply exists");
            Assert.Less(FeedbackTuning.ShellCapDropFromScale - 1f, 1f,
                        "the cap starts less than one seam height up: there is no 'cap still far away' moment to fade in");
        }

        [Test]
        public void NothingOfTheBody_IsDrawnOnceAnyCapGeometryExists()
        {
            string shell = Src(ShellPath);

            // 1. The close edge itself hides outright, before any Tick runs. This is the guarantee for every path that did not
            //    get a full walk: a close straight from Phase.None, a walk cut short, a frame-rate hitch.
            int enter = shell.IndexOf("private void EnterClose()", System.StringComparison.Ordinal);
            Assert.Greater(enter, 0, "EnterClose still exists");
            int end = shell.IndexOf("\n        }", enter, System.StringComparison.Ordinal);
            string body = shell.Substring(enter, end - enter);
            StringAssert.Contains("SetBodyConceal(1f)", body,
                                  "the cap exists from this frame on, so the body must be gone from this frame on");
            StringAssert.Contains("_capVisible = true;", body, "and the cap is drawn from this frame on");

            // 2. Every subsequent frame of the close and of the idle is a flat 1 - not a ramp that could be partway.
            StringAssert.Contains("case State.Close: conceal = 1f; break;", shell);
            StringAssert.Contains("case State.Idle:  conceal = 1f; break;", shell);

            // 3. And a flat 1 is at or above the threshold that switches the renderers off outright.
            Assert.GreaterOrEqual(1f, FeedbackTuning.ShellBodySealedConceal);
            Assert.That(FeedbackTuning.ShellBodySealedConceal, Is.InRange(0.9f, 1f));

            // 4. The close no longer has a conceal ramp of its own at all.
            Assert.IsFalse(shell.Contains("CloseConceal01"), "the close must not re-grow a fade of its own");
        }

        [Test]
        public void ByTheEndOfTheVanish_TheWholeBodyIsBelowTheGround_ForEveryClass()
        {
            // Belt and braces for the last frames, independent of alpha: the shrink and the sink together put every shipped
            // class entirely under the feet plane before the vanish completes, so even a stray opaque pixel has nowhere to be.
            var reg = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsNotNull(reg);
            int classes = 0;
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                if (!reg.TryGet(cls, out var e) || e.Stats == null) continue;
                classes++;
                float scale = e.Stats.Scale;
                // During the walk the class tuck is not applied (tuck01 is 0): the body wears the vanish scale alone.
                float top = FeedbackTuning.ShellModelHeight * scale * ChickenShellVisuals.BodyVanishScale(1f)
                            - ChickenShellVisuals.BodyVanishSink(1f, scale);
                Assert.Less(top, 0f, cls + ": at full conceal the body top is " + top +
                                     " m - it must be under the ground, not resting on it");
            }
            Assert.AreEqual(4, classes, "the four shipped classes");
        }

        [Test]
        public void TheShrinkAndTheSink_RunTogether_AndLeadTheFade()
        {
            Assert.AreEqual(1f, ChickenShellVisuals.BodyVanishScale(0f), 1e-6f, "a visible bird wears no extra shrink");
            Assert.AreEqual(FeedbackTuning.ShellBodyVanishScale, ChickenShellVisuals.BodyVanishScale(1f), 1e-6f);
            Assert.AreEqual(0f, ChickenShellVisuals.BodyVanishSink(0f, 1f), 1e-6f, "and no extra sink");
            Assert.That(FeedbackTuning.ShellBodyVanishLead, Is.LessThan(1f), "below 1 is what makes them LEAD the alpha");

            float prevScale = 2f, prevSink = -1f;
            for (float c = 0.05f; c < 1f; c += 0.05f)
            {
                float v = ChickenShellVisuals.BodyVanishScale(c);
                float k = ChickenShellVisuals.BodyVanishSink(c, 1f);
                Assert.Less(v, Mathf.Lerp(1f, FeedbackTuning.ShellBodyVanishScale, c) + 1e-5f, "the shrink leads the fade at " + c);
                Assert.Greater(k, Mathf.Lerp(0f, FeedbackTuning.ShellBodyVanishSink, c) - 1e-5f, "the sink leads the fade at " + c);
                Assert.Less(v, prevScale, "the shrink never grows back mid-vanish");
                Assert.Greater(k, prevSink, "the sink never rises back mid-vanish");
                prevScale = v; prevSink = k;
                // The two ride one curve, so they always agree about how far along the vanish is.
                Assert.AreEqual(ChickenShellVisuals.VanishRamp01(c), k / FeedbackTuning.ShellBodyVanishSink, 1e-5f);
            }
        }

        [Test]
        public void TheSink_ScalesWithTheEggLikeEverythingElseAboutTheShell()
        {
            var reg = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                if (!reg.TryGet(cls, out var e) || e.Stats == null) continue;
                Assert.AreEqual(FeedbackTuning.ShellBodyVanishSink * ChickenShellVisuals.EggScale(e.Stats.Scale),
                                ChickenShellVisuals.BodyVanishSink(1f, e.Stats.Scale), 1e-5f, cls.ToString());
            }
        }

        [Test]
        public void TheRuntime_ComposesTheVanishIntoTheOneScaleAndTheOneOffsetWrite()
        {
            string shell = Src(ShellPath);
            StringAssert.Contains("* BodyVanishScale(conceal);", shell, "the shrink multiplies the single scale write");
            StringAssert.Contains("+ BodyVanishSink(conceal, transform.localScale.x);", shell, "the sink adds to the single offset write");
            StringAssert.Contains("_animator.ExtraYOffset = -_bodySink", shell);
            StringAssert.Contains("case State.Walk:  conceal = _reduced ? 0f : WalkConceal01(remaining, eggSeconds); break;", shell);
        }

        // ---- The hatch: back out over bare ground, with the shell on its way out --------------------

        [Test]
        public void TheBirdBurstsBackIn_OverTheOpen_OnlyOnceTheShellIsLeaving()
        {
            float delay = Open * FeedbackTuning.ShellBodyFadeInDelayFraction;
            Assert.AreEqual(1f, ChickenShellVisuals.OpenConceal01(0f, Open), 1e-5f, "the hatch starts with no bird in sight");
            Assert.AreEqual(1f, ChickenShellVisuals.OpenConceal01(delay * 0.99f, Open), 1e-5f,
                            "still hidden while the cap pops and the shards, yolk and cap feathers leave");
            float back = delay + Open * FeedbackTuning.ShellBodyFadeInFraction;
            Assert.AreEqual(0f, ChickenShellVisuals.OpenConceal01(back, Open), 1e-5f, "whole again before the phase ends");
            Assert.AreEqual(0f, ChickenShellVisuals.OpenConceal01(Open, Open), 1e-5f, "and still whole at the end of the phase");

            float prev = 2f;
            for (int i = 0; i <= 10; i++)
            {
                float c = ChickenShellVisuals.OpenConceal01(delay + (back - delay) * i / 10f, Open);
                Assert.LessOrEqual(c, prev, "the return never goes backwards");
                prev = c;
            }
        }

        [Test]
        public void TheHatchDelay_IsTheLaterOfTheCupBeingGoneAndTheCapLeaving()
        {
            // Derived from the two curves TickOpen actually flies, not a hand-picked delay.
            float delay = Open * FeedbackTuning.ShellBodyFadeInDelayFraction;
            Assert.AreEqual(0f, ChickenShellVisuals.CupSinkYScale(delay), 1e-5f, "the cup has fully sunk before the bird returns");
            Assert.GreaterOrEqual(delay, Open - FeedbackTuning.ShellCapScaleOutSeconds - 1e-5f,
                                  "and the cap has begun scaling out for good before the bird returns");
            // The cap is also at the top of its pop arc by then, as far from the bird as it ever gets.
            float u = Mathf.Clamp01(delay / FeedbackTuning.ShellCapPopSeconds);
            Assert.Greater(1f - (1f - u) * (1f - u), 0.95f, "the cap is at its apex and leaving before the bird shows");
        }

        [Test]
        public void ASunkCupAndAScaledOutCap_StopBeingDrawnAtAll()
        {
            // The returning bird used to stand on the cup's flat white remnant disc, because a cup at scale ~0 kept rendering.
            string shell = Src(ShellPath);
            StringAssert.Contains("_cupVisible = _cupYScale > FeedbackTuning.ShellHalfGoneScale;", shell);
            StringAssert.Contains("_capVisible = _capScale > FeedbackTuning.ShellHalfGoneScale;", shell);
            StringAssert.Contains("if (_cupRenderer != null && _cupRenderer.enabled != _cupVisible) _cupRenderer.enabled = _cupVisible;", shell);

            float delay = Open * FeedbackTuning.ShellBodyFadeInDelayFraction;
            Assert.LessOrEqual(ChickenShellVisuals.CupSinkYScale(delay), FeedbackTuning.ShellHalfGoneScale,
                               "so the cup is already not drawn by the time the bird returns");
            Assert.That(FeedbackTuning.ShellHalfGoneScale, Is.InRange(0f, 0.1f), "a threshold, not a visible size");
        }

        [Test]
        public void EveryFade_StaysInsideItsOwnPhase()
        {
            Assert.That(FeedbackTuning.ShellBodyFadeInDelayFraction + FeedbackTuning.ShellBodyFadeInFraction, Is.LessThan(1f),
                        "the hatch return must finish before the open phase hands back to GoOff");
            Assert.That(FeedbackTuning.ShellBodyFadeInDelayFraction, Is.InRange(0f, 1f));
            foreach (float dur in new[] { Open, FeedbackTuning.ShellOpenReducedSeconds })
                Assert.AreEqual(0f, ChickenShellVisuals.OpenConceal01(dur, dur), 1e-5f);
            Assert.AreEqual(0f, ChickenShellVisuals.OpenConceal01(0f, 0f), "a zero-length open shows the bird, never leaves it gone");
        }

        // ---- Local and remote alike, and the Invisibility floor is untouched -------------------------

        [TestCase(true)]
        [TestCase(false)]
        public void TheSealedBird_DrawsNothing_OnItsOwnerAndOnARemotePeer(bool isLocal)
        {
            Assert.AreEqual(0f, ChickenVisuals.BodyAlpha(1f, isLocal, 1f), 1e-6f, "sealed in the egg");
            Assert.AreEqual(0f, ChickenVisuals.BodyAlpha(0f, isLocal, 1f), 1e-6f, "sealed, opacity not yet replicated");
            Assert.AreEqual(0f, ChickenVisuals.BodyAlpha(0.2f, isLocal, 1f), 1e-6f, "sealed while also hidden by Invisibility");
            Assert.AreEqual(1f, ChickenVisuals.BodyAlpha(1f, isLocal, 0f), 1e-6f, "and whole again once the conceal is released");
        }

        [Test]
        public void TheInvisibilityFloor_ForYourOwnBird_IsUnchanged()
        {
            foreach (float op in new[] { 0f, 0.2f, 0.45f, 0.6f, 1f })
            {
                float expectedLocal = ControlStateVFX.IsFadedOpacity(op)
                    ? HiddenVisibilityRules.AlphaFor(op, isLocal: true)
                    : (op > 0f ? op : 1f);
                Assert.AreEqual(expectedLocal, ChickenVisuals.BodyAlpha(op, true, 0f), 1e-6f, "local body alpha at " + op);
                Assert.AreEqual(op > 0f ? op : 1f, ChickenVisuals.BodyAlpha(op, false, 0f), 1e-6f, "remote body alpha at " + op);
            }
            Assert.AreEqual(0.45f, FeedbackTuning.LocalHiddenOpacityFloor, 1e-6f, "the A1 floor itself");
            Assert.AreEqual(FeedbackTuning.LocalHiddenOpacityFloor, HiddenVisibilityRules.AlphaFor(0.2f, true), 1e-6f);
            Assert.AreEqual(0f, HiddenVisibilityRules.AlphaFor(0.2f, false), 1e-6f);
        }

        [Test]
        public void MidVanish_YourOwnBirdIsFading_NotHeldAtTheFloor()
        {
            float conceal = ConcealAtUntilCap(Lead + Span * 0.5f, Shield().EggSeconds);
            float own = ChickenVisuals.BodyAlpha(1f, true, conceal);
            Assert.Less(own, 1f, "your own bird fades as it climbs into the egg");
            Assert.Greater(own, 0f, "but is not cut out");
        }

        // ---- The sealed stretch switches the renderers off ------------------------------------------

        [Test]
        public void SealedIn_TheBodyRenderersAreOff_AndComeBackExactlyAsTheyWere()
        {
            var go = new GameObject("visuals") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var visuals = go.AddComponent<ChickenVisuals>();
                var shown = new GameObject("shown").AddComponent<MeshRenderer>();
                var alreadyOff = new GameObject("alreadyOff").AddComponent<MeshRenderer>();
                shown.transform.SetParent(go.transform, false);
                alreadyOff.transform.SetParent(go.transform, false);
                alreadyOff.enabled = false;
                typeof(ChickenVisuals).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(visuals, null);

                Assert.IsFalse(visuals.IsBodyHidden, "visible to start with");
                visuals.SetShellConcealment(0.5f);
                Assert.IsFalse(visuals.IsBodyHidden, "mid-vanish the ghost carries it; the renderers stay on");
                Assert.IsTrue(shown.enabled);

                visuals.SetShellConcealment(1f);
                Assert.IsTrue(visuals.IsBodyHidden, "sealed in the egg: nothing of the body is drawn at all");
                Assert.IsFalse(shown.enabled);
                Assert.IsFalse(alreadyOff.enabled);

                visuals.SetShellConcealment(0f);
                Assert.IsFalse(visuals.IsBodyHidden);
                Assert.IsTrue(shown.enabled, "the hatched bird draws again");
                Assert.IsFalse(alreadyOff.enabled, "a renderer that was off for its own reasons stays off");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---- Wiring: the shell drives it, off the same replicated phase, on every peer ---------------

        [Test]
        public void APeerThatStartsObservingMidEgg_HidesTheBirdImmediately()
        {
            string shell = Src(ShellPath);
            int idle = shell.IndexOf("private void EnterIdle(", System.StringComparison.Ordinal);
            Assert.Greater(idle, 0, "EnterIdle still exists");
            int end = shell.IndexOf("\n        }", idle, System.StringComparison.Ordinal);
            StringAssert.Contains("SetBodyConceal(1f)", shell.Substring(idle, end - idle),
                                  "a mid-egg observer must see a hidden bird at once, not a fade from visible");
            StringAssert.Contains("if (cur == Phase.Walk) EnterWalk(seeded: true);", shell);
            StringAssert.Contains("else if (cur == Phase.Egg) EnterIdle(remaining, eggSeconds);", shell);
        }

        [Test]
        public void TheShellReleasesTheBird_WheneverItStops()
        {
            string shell = Src(ShellPath);
            foreach (var where in new[] { "private void OnDisable()", "private void GoOff()" })
            {
                int i = shell.IndexOf(where, System.StringComparison.Ordinal);
                Assert.Greater(i, 0, where + " still exists");
                int end = shell.IndexOf("\n        }", i, System.StringComparison.Ordinal);
                StringAssert.Contains("SetBodyConceal(0f)", shell.Substring(i, end - i),
                                      where + " must hand the bird back; a chicken left concealed would be invisible for the rest of the match");
            }
            // A walk that ends without an egg (Fold) keeps its bird too.
            StringAssert.Contains("default:          conceal = 0f; break;", shell);
        }

        [Test]
        public void ReducedMotion_HidesTheBirdOutright_WithNoPartialAlphaOverAClosedEgg()
        {
            // The Reduced close never drops a cap: it grows the already-closed egg from ShellReducedGrowFrom, and the Reduced
            // open shrinks that whole egg away. Any partial alpha there could only be drawn OVER the egg, so there is none.
            string shell = Src(ShellPath);
            StringAssert.Contains("case State.Walk:  conceal = _reduced ? 0f : WalkConceal01(remaining, eggSeconds); break;", shell);
            StringAssert.Contains("case State.Open:  conceal = _reduced ? 1f : OpenConceal01(_stateT, FeedbackTuning.ShellOpenSeconds); break;", shell);
        }

        [Test]
        public void TheConcealIsLocalPresentation_DerivedFromTheSameReplicatedPhaseAsTheShell()
        {
            string shell = Src(ShellPath);
            StringAssert.DoesNotContain("[Rpc", shell);
            StringAssert.DoesNotContain("[Networked]", shell);
            StringAssert.Contains("Phase cur = PhaseOf(true, _controller.IsControlImmune);", shell);
            StringAssert.Contains("_visuals.SetShellConcealment(c);", shell);
            string visuals = Src(VisualsPath);
            StringAssert.Contains("HiddenVisibilityRules.AlphaFor(raw, isLocal: true)", visuals);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(visuals, @"_shellConceal\s*=\s*c;").Count,
                            "one writer for the conceal");
        }

        [Test]
        public void ThePrefabCarriesBothComponents_SoTheConcealCanBeDelivered()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestAssets.ChickenPrefabPath);
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent<ChickenShellVisuals>(), "the shell driver");
            Assert.IsNotNull(prefab.GetComponent<ChickenVisuals>(), "the body opacity owner the shell pushes the conceal to");
        }

        [Test]
        public void NothingAboutTheEggSizing_Moved()
        {
            // Maestro ruled the sizes out of scope: the cap, the base height and the per-class tuck table are what they were.
            Assert.AreEqual(1.27f, FeedbackTuning.ShellEggScaleCap, 1e-6f);
            Assert.AreEqual(1.22f, FeedbackTuning.ShellEggBaseHeight, 1e-6f);
            Assert.AreEqual(0.82f, FeedbackTuning.ShellBodyScale, 1e-6f);
            Assert.AreEqual(0.70f, FeedbackTuning.ShellBodyTuckWarrior, 1e-6f);
            Assert.AreEqual(0.30f, FeedbackTuning.ShellCloseSeconds, 1e-6f);
            Assert.AreEqual(0.40f, FeedbackTuning.ShellOpenSeconds, 1e-6f);
            var classes = TestAssets.LoadAllIn<ChickenStatsSO>(TestAssets.ClassesDir).ToArray();
            Assert.AreEqual(4, classes.Length);
            Assert.Greater(classes.Max(c => ChickenShellVisuals.EggWorldHeight(c.Scale)),
                           classes.Min(c => ChickenShellVisuals.EggWorldHeight(c.Scale)),
                           "the egg is still different per class, off the size stat");
        }
    }
}
