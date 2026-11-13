using System.IO;
using System.Linq;
using System.Reflection;
using CluckWars.Gameplay;
using CluckWars.Visuals;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CluckWars.Tests
{
    /// <summary>
    /// Maestro 2026-11-13: the egg stays per-class off the size stat (and so stays smaller than the bigger birds), and instead
    /// the bird itself disappears into it and reappears out of the hatch, both with an animation. These pin the BEHAVIOUR:
    /// the body is fully hidden for the whole sealed stretch on a local and on a remote chicken, hidden at once for a peer that
    /// starts observing mid-egg, fully visible again once the hatch finishes, the fade never outlasts its phase for any class or
    /// motion preference, and the Invisibility floor for a player's own bird is exactly what it was.
    /// </summary>
    public sealed class ShellBodyHiddenTests
    {
        private const float Close = FeedbackTuning.ShellCloseSeconds;
        private const float CloseReduced = FeedbackTuning.ShellCloseReducedSeconds;
        private const float Open = FeedbackTuning.ShellOpenSeconds;
        private const float OpenReduced = FeedbackTuning.ShellOpenReducedSeconds;

        private static readonly float[] Phases = { Close, CloseReduced, Open, OpenReduced };

        // ---- The close: gone by the time the shell seals --------------------------------------------

        [Test]
        public void TheBirdFadesOutOverTheClose_AndIsGoneWhenTheShellSeals()
        {
            foreach (float dur in new[] { Close, CloseReduced })
            {
                Assert.AreEqual(0f, ChickenShellVisuals.CloseConceal01(0f, dur), 1e-5f, "visible when the shell starts closing");
                float seal = dur * FeedbackTuning.ShellBodyFadeOutFraction;
                Assert.AreEqual(1f, ChickenShellVisuals.CloseConceal01(seal, dur), 1e-5f, "fully gone the moment the shell seals");
                Assert.AreEqual(1f, ChickenShellVisuals.CloseConceal01(dur, dur), 1e-5f, "and stays gone for the rest of the close");

                // A ramp, not a cut: strictly rising across the fade, and partway through it is partway hidden.
                float prev = -1f;
                for (int i = 0; i <= 10; i++)
                {
                    float c = ChickenShellVisuals.CloseConceal01(seal * i / 10f, dur);
                    Assert.GreaterOrEqual(c, prev, "the conceal never goes backwards during the close");
                    prev = c;
                }
                float mid = ChickenShellVisuals.CloseConceal01(seal * 0.5f, dur);
                Assert.That(mid, Is.InRange(0.2f, 0.8f), "halfway through the fade the bird is half there, not popped");
            }
        }

        [Test]
        public void TheCloseFade_EndsExactlyWhenTheCapSeats()
        {
            // The fraction is not a free number: it is the cap drop over the whole close, so the fade and the seal coincide.
            Assert.AreEqual(FeedbackTuning.ShellCloseDropSeconds / FeedbackTuning.ShellCloseSeconds,
                            FeedbackTuning.ShellBodyFadeOutFraction, 1e-5f,
                            "the body must be gone when the cap seats, not before or after");
        }

        // ---- The hatch: out of the shards, never before them ----------------------------------------

        [Test]
        public void TheBirdFadesBackInOverTheOpen_AfterTheShellHasBroken()
        {
            foreach (float dur in new[] { Open, OpenReduced })
            {
                float delay = dur * FeedbackTuning.ShellBodyFadeInDelayFraction;
                Assert.AreEqual(1f, ChickenShellVisuals.OpenConceal01(0f, dur), 1e-5f, "the hatch starts with no bird in sight");
                Assert.AreEqual(1f, ChickenShellVisuals.OpenConceal01(delay * 0.99f, dur), 1e-5f,
                                "still hidden while the cap pops and the shards, yolk and cap feathers leave");
                float back = delay + dur * FeedbackTuning.ShellBodyFadeInFraction;
                Assert.AreEqual(0f, ChickenShellVisuals.OpenConceal01(back, dur), 1e-5f, "whole again before the phase ends");
                Assert.AreEqual(0f, ChickenShellVisuals.OpenConceal01(dur, dur), 1e-5f, "and still whole at the end of the phase");

                float prev = 2f;
                for (int i = 0; i <= 10; i++)
                {
                    float c = ChickenShellVisuals.OpenConceal01(delay + (back - delay) * i / 10f, dur);
                    Assert.LessOrEqual(c, prev, "the conceal never goes backwards during the hatch");
                    prev = c;
                }
                float mid = ChickenShellVisuals.OpenConceal01((delay + back) * 0.5f, dur);
                Assert.That(mid, Is.InRange(0.2f, 0.8f), "halfway through the fade-in the bird is half there, not popped");
            }
        }

        [Test]
        public void TheHatchFadeIn_StartsAfterTheShellHasVisiblyBroken()
        {
            float delay = Open * FeedbackTuning.ShellBodyFadeInDelayFraction;
            Assert.Greater(delay, 0f, "the bird must not start appearing on the same frame as the cap");
            // By the time it starts, the cap has already travelled most of its pop arc (1-(1-u)^2 of ShellCapPopHeight).
            float u = Mathf.Clamp01(delay / FeedbackTuning.ShellCapPopSeconds);
            Assert.Greater(1f - (1f - u) * (1f - u), 0.4f, "the shell must be visibly open before the bird shows");
        }

        // ---- The fade can never outlast its phase, for any class or preference ----------------------

        [Test]
        public void TheFractions_KeepEveryFadeInsideItsOwnPhase()
        {
            Assert.That(FeedbackTuning.ShellBodyFadeOutFraction, Is.InRange(0f, 1f));
            Assert.That(FeedbackTuning.ShellBodyFadeInDelayFraction + FeedbackTuning.ShellBodyFadeInFraction, Is.LessThan(1f),
                        "the hatch fade-in must finish before the open phase hands back to GoOff");
            foreach (float dur in Phases)
            {
                Assert.Greater(dur, 0f);
                Assert.AreEqual(1f, ChickenShellVisuals.CloseConceal01(dur, dur), 1e-5f);
                Assert.AreEqual(0f, ChickenShellVisuals.OpenConceal01(dur, dur), 1e-5f);
            }
        }

        [Test]
        public void TheRamps_AreTheSameForEveryClass_BecauseTheyAreFractionsOfThePhase()
        {
            // Nothing in the conceal reads a class, a scale or an egg size, which is exactly why Maestro's "no more size
            // changes" ruling is safe here: the Fatty's capped egg and the Speedy's small one hide their bird identically.
            var reg = AssetDatabase.LoadAssetAtPath<ChickenClassRegistrySO>(TestAssets.ChickenClassRegistryPath);
            Assert.IsNotNull(reg);
            foreach (ChickenClass cls in System.Enum.GetValues(typeof(ChickenClass)))
            {
                if (!reg.TryGet(cls, out var e) || e.Stats == null) continue;
                Assert.AreEqual(1f, ChickenShellVisuals.CloseConceal01(Close, Close), 1e-5f, cls + " sealed in");
                Assert.AreEqual(0f, ChickenShellVisuals.OpenConceal01(Open, Open), 1e-5f, cls + " out again");
                // And the egg sizing it has to cover is still the shipped, per-class one (untouched by this fix).
                Assert.AreEqual(Mathf.Min(e.Stats.Scale, FeedbackTuning.ShellEggScaleCap),
                                ChickenShellVisuals.EggScale(e.Stats.Scale), 1e-5f, cls + ": egg scale unchanged");
            }
        }

        [Test]
        public void TheShellDegenerateDurations_FailClosed()
        {
            Assert.AreEqual(1f, ChickenShellVisuals.CloseConceal01(0f, 0f), "a zero-length close hides immediately, never half-way");
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
            // The egg does not weaken the floor: with no conceal, the body alpha is exactly the A1 rule, hidden or not.
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
        public void HalfWayThroughTheClose_YourOwnBirdIsAlreadyFadingOut_NotHeldAtTheFloor()
        {
            float conceal = ChickenShellVisuals.CloseConceal01(Close * FeedbackTuning.ShellBodyFadeOutFraction * 0.5f, Close);
            float own = ChickenVisuals.BodyAlpha(1f, true, conceal);
            Assert.Less(own, 1f, "your own bird fades with the shell");
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
                Assert.IsFalse(visuals.IsBodyHidden, "mid-fade the ghost carries it; the renderers stay on");
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

        [Test]
        public void TheSealThreshold_IsOnlyTheSealedStretch_NotTheFade()
        {
            Assert.That(FeedbackTuning.ShellBodySealedConceal, Is.InRange(0.9f, 1f));
            Assert.Less(ChickenShellVisuals.CloseConceal01(Close * FeedbackTuning.ShellBodyFadeOutFraction * 0.9f, Close),
                        FeedbackTuning.ShellBodySealedConceal, "the renderers only go off once the fade has finished");
            Assert.GreaterOrEqual(ChickenShellVisuals.CloseConceal01(Close, Close), FeedbackTuning.ShellBodySealedConceal);
            Assert.GreaterOrEqual(ChickenShellVisuals.OpenConceal01(0f, Open), FeedbackTuning.ShellBodySealedConceal,
                                  "and are still off when the hatch begins");
        }

        // ---- Wiring: the shell drives it, off the same replicated phase, on every peer ---------------

        private const string ShellPath = "Assets/_Game/Scripts/Visuals/ChickenShellVisuals.cs";
        private const string VisualsPath = "Assets/_Game/Scripts/Visuals/ChickenVisuals.cs";

        private static string Src(string rel) =>
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), rel));

        [Test]
        public void APeerThatStartsObservingMidEgg_HidesTheBirdImmediately()
        {
            // EnterIdle is the seeding path (Observe's first observation with the egg already running) AND it is the only
            // place that can be reached without a Close to fade through, so it must seal on the spot.
            string shell = Src(ShellPath);
            int idle = shell.IndexOf("private void EnterIdle(", System.StringComparison.Ordinal);
            Assert.Greater(idle, 0, "EnterIdle still exists");
            int end = shell.IndexOf("\n        }", idle, System.StringComparison.Ordinal);
            string body = shell.Substring(idle, end - idle);
            StringAssert.Contains("SetBodyConceal(1f)", body, "a mid-egg observer must see a hidden bird at once, not a fade from visible");
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
        }

        [Test]
        public void TheConcealIsLocalPresentation_DerivedFromTheSameReplicatedPhaseAsTheShell()
        {
            string shell = Src(ShellPath);
            // No RPC and no networked field is introduced: the phase still comes from the polled slot mask + control immunity.
            StringAssert.DoesNotContain("[Rpc", shell);
            StringAssert.DoesNotContain("[Networked]", shell);
            StringAssert.Contains("Phase cur = PhaseOf(true, _controller.IsControlImmune);", shell);
            // And the push goes through ChickenVisuals, which owns the one ghost/opacity path, not a second way to hide a bird.
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
